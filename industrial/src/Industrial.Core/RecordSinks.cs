using System.Text.Json;
using System.Text.Json.Nodes;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Idempotent SQLite target used for the real end-to-end slice and
/// failure-recovery tests. The record_id unique key makes retries safe.</summary>
public sealed class SqliteRecordSink : IRecordSink
{
    private readonly Func<IndustrialTargetDbContext> _db;

    private readonly SinkWriteOptions _options;

    public SqliteRecordSink(Func<IndustrialTargetDbContext> db, string? targetId = null,
        SinkWriteOptions? options = null)
    {
        _db = db;
        _options = options ?? SinkWriteOptions.From(null);
        TargetId = targetId ?? "sqlite";
    }

    public string TargetId { get; }

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId,
        IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        await using var db = _db();
        const int batchSize = 500;
        var count = 0;
        var pending = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var recordId = _options.ResolveId(record);
            if (await db.Records.AnyAsync(row => row.RecordId == recordId, ct))
            {
                continue;
            }

            db.Records.Add(new TargetRecordEntity
            {
                RecordId = recordId,
                SourceIdentityKey = record.Source.Key,
                RecordTime = record.RecordTime,
                PayloadJson = JsonSerializer.Serialize(record.Values),
            });
            count++;
            if (++pending >= batchSize)
            {
                await db.SaveChangesAsync(ct);
                // Release change-tracked entities so live memory stays bounded
                // regardless of input size.
                db.ChangeTracker.Clear();
                pending = 0;
            }
        }

        if (pending > 0) await db.SaveChangesAsync(ct);
        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>SQL Server target with a stable record_id unique key. Mature
/// Microsoft.Data.SqlClient driver; no home-grown connection pool.</summary>
public sealed class SqlServerRecordSink : IRecordSink
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly string _writeMode;
    private readonly SinkWriteOptions _options;

    public SqlServerRecordSink(string connectionString, string table = "IndustrialRecords",
        string? targetId = null, string writeMode = "append", string? businessKey = null,
        SinkWriteOptions? options = null)
    {
        _connectionString = connectionString;
        _table = SqlIdentifier.QuoteTable(table);
        _options = options ?? SinkWriteOptions.From(new Dictionary<string, object?>
        {
            ["writeMode"] = writeMode, ["businessKey"] = businessKey,
        });
        _writeMode = _options.WriteMode;
        TargetId = targetId ?? "sqlserver";
    }

    public string TargetId { get; }

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId,
        IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using System.Data.Common.DbTransaction? transaction = _writeMode == "replace"
            ? await connection.BeginTransactionAsync(ct)
            : null;
        var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction?)transaction;
        command.CommandText = $"""
            IF OBJECT_ID(N'{_table}', N'U') IS NULL
            CREATE TABLE {_table} (
                record_id NVARCHAR(256) NOT NULL PRIMARY KEY,
                source_identity NVARCHAR(512) NOT NULL,
                record_time DATETIMEOFFSET NOT NULL,
                payload NVARCHAR(MAX) NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);

        var count = 0;
        var replacedSources = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var record in records.WithCancellation(ct))
        {
            var recordId = _options.ResolveId(record);

            if (_writeMode == "replace" && replacedSources.Add(record.Source.Key))
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@source", record.Source.Key);
                command.CommandText = $"DELETE FROM {_table} WHERE source_identity = @source;";
                await command.ExecuteNonQueryAsync(ct);
            }

            command.CommandText = _writeMode switch
            {
                "upsert" => $"""
                    MERGE {_table} AS target
                    USING (SELECT @id AS record_id) AS source
                    ON target.record_id = source.record_id
                    WHEN MATCHED THEN UPDATE SET source_identity = @source,
                        record_time = @time, payload = @payload
                    WHEN NOT MATCHED THEN INSERT(record_id, source_identity, record_time, payload)
                        VALUES (@id, @source, @time, @payload);
                    """,
                "replace" => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, @payload);
                    """,
                _ => $"""
                    IF NOT EXISTS (SELECT 1 FROM {_table} WHERE record_id = @id)
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, @payload);
                    """,
            };
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", recordId);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>Archive via temp file + checksum + atomic rename. Retries of the
/// same idempotency key cannot duplicate or truncate an archived source.</summary>
public sealed class FileArchiveSink : IArchiveSink
{
    private readonly string _archiveRoot;

    public FileArchiveSink(string archiveRoot, string? targetId = null)
    {
        _archiveRoot = archiveRoot;
        TargetId = targetId ?? "archive";
    }

    public string TargetId { get; }

    public async ValueTask ArchiveAsync(
        DiscoveredFile file, string idempotencyKey, CancellationToken ct = default)
    {
        var dateFolder = file.SourceDate.ToString("yyyyMMdd");
        var directory = Path.Combine(_archiveRoot, dateFolder);
        Directory.CreateDirectory(directory);
        var safeKey = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(idempotencyKey)))[..16];
        var target = Path.Combine(directory, $"{safeKey}{Path.GetExtension(file.FullPath)}");
        if (File.Exists(target))
        {
            return;
        }

        var temporary = target + ".partial";
        await using (var source = File.OpenRead(file.FullPath))
        await using (var destination = File.Create(temporary))
        {
            await source.CopyToAsync(destination, ct);
        }

        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(temporary, ct)));
        if (!string.Equals(actual, file.Identity.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            throw new InvalidOperationException($"archive checksum mismatch for '{file.Identity.RelativePath}'");
        }

        File.Move(temporary, target, overwrite: false);
    }
}
