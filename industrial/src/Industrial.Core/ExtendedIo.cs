using System.Text;
using System.Text.Json;
using ExcelDataReader;
using Industrial.Contracts;
using MySqlConnector;
using Npgsql;

namespace Industrial.Core;

/// <summary>Excel (.xlsx) reader using ExcelDataReader. Same RecordEnvelope
/// contract as CSV; no workflow or registry core changes needed to add it.</summary>
public sealed class ExcelRecordReader : IRecordReader
{
    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(file.FullPath);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        if (!reader.Read()) yield break;
        var headers = new List<string>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            headers.Add(reader.GetString(index));
        }

        var row = 0;
        while (await Task.Run(() => reader.Read(), ct))
        {
            ct.ThrowIfCancellationRequested();
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count && index < reader.FieldCount; index++)
            {
                values[headers[index]] = reader.IsDBNull(index) ? null : reader.GetValue(index).ToString();
            }

            values["record_id"] = values.GetValueOrDefault("record_id")?.ToString() ?? $"{file.Identity.Key}#{row}";
            values["record_time"] = values.GetValueOrDefault("record_time")?.ToString()
                                    ?? file.SourceDate.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            yield return new RecordEnvelope(values, file.Identity, file.SourceDate);
            row++;
        }
    }
}

/// <summary>PostgreSQL target with record_id primary key and ON CONFLICT DO
/// NOTHING. Npgsql owns pooling and protocol handling.</summary>
public sealed class PostgreSqlRecordSink : IRecordSink
{
    private readonly string _connectionString;

    public PostgreSqlRecordSink(string connectionString) => _connectionString = connectionString;

    public string TargetId => "postgresql";

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS industrial_records (
                record_id text PRIMARY KEY, source_identity text NOT NULL,
                record_time timestamptz NOT NULL, payload jsonb NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(ct);
        var count = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var id = record.Values.GetValueOrDefault("record_id")?.ToString()
                     ?? throw new InvalidOperationException("record_id is required");
            command.CommandText = """
                INSERT INTO industrial_records(record_id, source_identity, record_time, payload)
                VALUES (@id, @source, @time, CAST(@payload AS jsonb))
                ON CONFLICT (record_id) DO NOTHING;
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
        }

        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>MySQL target with INSERT IGNORE on the record_id unique key.</summary>
public sealed class MySqlRecordSink : IRecordSink
{
    private readonly string _connectionString;

    public MySqlRecordSink(string connectionString) => _connectionString = connectionString;

    public string TargetId => "mysql";

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey, CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS industrial_records (
                record_id VARCHAR(256) PRIMARY KEY, source_identity VARCHAR(512) NOT NULL,
                record_time DATETIME(6) NOT NULL, payload JSON NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(ct);
        var count = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var id = record.Values.GetValueOrDefault("record_id")?.ToString()
                     ?? throw new InvalidOperationException("record_id is required");
            command.CommandText = """
                INSERT IGNORE INTO industrial_records(record_id, source_identity, record_time, payload)
                VALUES (@id, @source, @time, @payload);
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime.UtcDateTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
        }

        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>Selects CSV or Excel readers by file extension without changing any
/// workflow, registry or loader code. New readers plug into this one contract.</summary>
public sealed class RoutingRecordReader : IRecordReader
{
    public IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, CancellationToken ct = default)
    {
        var reader = Path.GetExtension(file.FullPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? (IRecordReader)new ExcelRecordReader()
            : new CsvRecordReader();
        return reader.ReadAsync(file, ct);
    }
}

/// <summary>Routes local directories and UNC shares through their respective
/// implementations. SMB sessions are coordinated by UncSessionCache.</summary>
public sealed class RoutingFileSource : IFileSource, IAsyncDisposable
{
    private readonly IResourceRegistry _resources;
    private readonly ICredentialVault? _vault;
    private readonly LocalFileSource _local;
    private readonly UncFileSource? _unc;

    public RoutingFileSource(IResourceRegistry resources, ICredentialVault? vault = null)
    {
        _resources = resources;
        _vault = vault;
        _local = new LocalFileSource(resources);
        if (OperatingSystem.IsWindows())
        {
            _unc = new UncFileSource(resources, vault);
        }
    }

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value) ? value?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(root) && root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (_unc is null)
            {
                throw new PlatformNotSupportedException("UNC sources require a Windows host");
            }

            return await _unc.DiscoverAsync(resourceId, range, ct);
        }

        return await _local.DiscoverAsync(resourceId, range, ct);
    }

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        _local.IsStableAsync(file, quietPeriod, ct);

    public ValueTask DisposeAsync()
    {
        UncFileSource.ReleaseAll();
        return ValueTask.CompletedTask;
    }
}
