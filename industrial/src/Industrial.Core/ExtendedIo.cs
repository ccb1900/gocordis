using System.Text;
using System.Text.Json;
using ExcelDataReader;
using Gocordis.Extensions;
using Industrial.Contracts;
using MySqlConnector;
using Npgsql;

namespace Industrial.Core;

/// <summary>Excel (.xlsx) reader using ExcelDataReader. Same RecordEnvelope
/// contract as CSV; no workflow or registry core changes needed to add it.</summary>
public sealed class ExcelRecordReader : IRecordReader
{
    private readonly IReadOnlyDictionary<string, object?> _config;

    public ExcelRecordReader(IReadOnlyDictionary<string, object?>? config = null) =>
        _config = config ?? new Dictionary<string, object?>();

    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DiscoveredFile file, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = new NodeConfigReader(_config);
        var headerRow = Math.Max(1, config.GetInt("headerRow", 1));
        await using var stream = File.OpenRead(file.FullPath);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
        for (var skipped = 1; skipped < headerRow; skipped++)
        {
            if (!reader.Read()) yield break;
        }
        if (!reader.Read()) yield break;
        var headers = new List<string>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            headers.Add(reader.IsDBNull(index) ? $"Column{index + 1}" : reader.GetString(index));
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

/// <summary>PostgreSQL target with record_id primary key. Write mode and
/// business key come from the shared SinkWriteOptions contract.</summary>
public sealed class PostgreSqlRecordSink : IRecordSink
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly SinkWriteOptions _options;

    public PostgreSqlRecordSink(string connectionString, string? targetId = null,
        SinkWriteOptions? options = null, string? table = null)
    {
        _connectionString = connectionString;
        _table = SqlIdentifier.QuotePostgreSqlTable(table ?? "industrial_records");
        TargetId = targetId ?? "postgresql";
        _options = options ?? SinkWriteOptions.From(null);
    }

    public string TargetId { get; }

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = _options.WriteMode == "replace"
            ? await connection.BeginTransactionAsync(ct)
            : null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {_table} (
                record_id text PRIMARY KEY, source_identity text NOT NULL,
                record_time timestamptz NOT NULL, payload jsonb NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(ct);

        var count = 0;
        var replacedSources = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var record in records.WithCancellation(ct))
        {
            var id = _options.ResolveId(record);
            if (_options.WriteMode == "replace" && replacedSources.Add(record.Source.Key))
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@source", record.Source.Key);
                command.CommandText = $"DELETE FROM {_table} WHERE source_identity = @source;";
                await command.ExecuteNonQueryAsync(ct);
            }
            command.CommandText = _options.WriteMode switch
            {
                "upsert" => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, CAST(@payload AS jsonb))
                    ON CONFLICT (record_id) DO UPDATE SET
                        source_identity = EXCLUDED.source_identity,
                        record_time = EXCLUDED.record_time, payload = EXCLUDED.payload;
                    """,
                "replace" => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, CAST(@payload AS jsonb));
                    """,
                _ => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, CAST(@payload AS jsonb))
                    ON CONFLICT (record_id) DO NOTHING;
                    """,
            };
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new DeliveryResultRef(idempotencyKey, TargetId, count);
    }
}

/// <summary>MySQL target. Write mode and business key follow the same contract
/// as SQL Server and PostgreSQL.</summary>
public sealed class MySqlRecordSink : IRecordSink
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly SinkWriteOptions _options;

    public MySqlRecordSink(string connectionString, string? targetId = null,
        SinkWriteOptions? options = null, string? table = null)
    {
        _connectionString = connectionString;
        _table = SqlIdentifier.QuoteMySqlTable(table ?? "industrial_records");
        TargetId = targetId ?? "mysql";
        _options = options ?? SinkWriteOptions.From(null);
    }

    public string TargetId { get; }

    public async ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey, CancellationToken ct = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = _options.WriteMode == "replace"
            ? await connection.BeginTransactionAsync(ct)
            : null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {_table} (
                record_id VARCHAR(256) PRIMARY KEY, source_identity VARCHAR(512) NOT NULL,
                record_time DATETIME(6) NOT NULL, payload JSON NOT NULL);
            """;
        await command.ExecuteNonQueryAsync(ct);

        var count = 0;
        var replacedSources = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var record in records.WithCancellation(ct))
        {
            var id = _options.ResolveId(record);
            if (_options.WriteMode == "replace" && replacedSources.Add(record.Source.Key))
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@source", record.Source.Key);
                command.CommandText = $"DELETE FROM {_table} WHERE source_identity = @source;";
                await command.ExecuteNonQueryAsync(ct);
            }
            command.CommandText = _options.WriteMode switch
            {
                "upsert" => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, @payload)
                    ON DUPLICATE KEY UPDATE source_identity = VALUES(source_identity),
                        record_time = VALUES(record_time), payload = VALUES(payload);
                    """,
                "replace" => $"""
                    INSERT INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, @payload);
                    """,
                _ => $"""
                    INSERT IGNORE INTO {_table}(record_id, source_identity, record_time, payload)
                    VALUES (@id, @source, @time, @payload);
                    """,
            };
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@source", record.Source.Key);
            command.Parameters.AddWithValue("@time", record.RecordTime.UtcDateTime);
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(record.Values));
            count += await command.ExecuteNonQueryAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
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

    public RoutingFileSource(
        IResourceRegistry resources,
        ICredentialVault? vault = null,
        Broker<IRecordReaderProvider>? readers = null)
    {
        _resources = resources;
        _vault = vault;
        Func<IReadOnlyList<string>> extensions = () =>
            readers?.Providers.Select(provider => provider.FileExtension).ToArray()
            ?? [".csv", ".xlsx"];
        _local = new LocalFileSource(resources, extensions);
        if (OperatingSystem.IsWindows())
        {
            _unc = new UncFileSource(resources, vault, extensions);
        }
    }

    public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default) =>
        DiscoverAsync(resourceId, range, null, null, ct);

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, FileProfileDefinition? profile,
        IReadOnlySet<DateOnly>? businessDates, CancellationToken ct = default)
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

            return await _unc.DiscoverAsync(resourceId, range, profile, businessDates, ct);
        }

        return await _local.DiscoverAsync(resourceId, range, profile, businessDates, ct);
    }

    /// <summary>One session scope for the whole workflow execution, regardless
    /// of whether the injected source is local or UNC.</summary>
    public async ValueTask<IAsyncDisposable?> LeaseScopeAsync(
        string resourceId, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value) ? value?.ToString() : null;
        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        if (_unc is null)
            throw new PlatformNotSupportedException("UNC sources require a Windows host");
        return await _unc.LeaseScopeAsync(resourceId, ct);
    }

    public async ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default)
    {
        if (_unc is not null && file.FullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return await _unc.IsStableAsync(file, quietPeriod, ct);
        return await _local.IsStableAsync(file, quietPeriod, ct);
    }

    public ValueTask DisposeAsync()
    {
        UncFileSource.ReleaseAll();
        return ValueTask.CompletedTask;
    }
}
