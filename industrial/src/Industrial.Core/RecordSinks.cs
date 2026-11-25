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

    public SqliteRecordSink(Func<IndustrialTargetDbContext> db, string? targetId = null)
    {
        _db = db;
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
        var count = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var recordId = record.Values.GetValueOrDefault("record_id")?.ToString()
                           ?? throw new InvalidOperationException("record_id is required for idempotent delivery");
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
        }

        await db.SaveChangesAsync(ct);
        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>SQL Server target with a stable record_id unique key. Mature
/// Microsoft.Data.SqlClient driver; no home-grown connection pool.</summary>
public sealed class SqlServerRecordSink : IRecordSink
{
    private readonly string _connectionString;
    private readonly string _table;

    public SqlServerRecordSink(string connectionString, string table = "IndustrialRecords", string? targetId = null)
    {
        _connectionString = connectionString;
        _table = table;
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
        var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'dbo.{_table}', N'U') IS NULL
            CREATE TABLE dbo.{_table} (
                record_id NVARCHAR(256) NOT NULL PRIMARY KEY,
                source_identity NVARCHAR(512) NOT NULL,
                record_time DATETIMEOFFSET NOT NULL,
                payload NVARCHAR(MAX) NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);

        var count = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var recordId = record.Values.GetValueOrDefault("record_id")?.ToString()
                           ?? throw new InvalidOperationException("record_id is required for idempotent delivery");
            command.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM dbo.{_table} WHERE record_id = @id)
                INSERT INTO dbo.{_table}(record_id, source_identity, record_time, payload)
                VALUES (@id, @source, @time, @payload);
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", recordId);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
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
