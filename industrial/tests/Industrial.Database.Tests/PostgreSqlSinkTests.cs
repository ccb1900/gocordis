using Industrial.Contracts;
using Industrial.Core;
using Npgsql;

namespace Industrial.Database.Tests;

/// <summary>Opt-in real PostgreSQL integration test. The connection string is
/// supplied by industrial/e2e-database.sh; no SQLite substitution is accepted.</summary>
public sealed class PostgreSqlSinkTests
{
    [Fact]
    public async Task PostgreSqlSinkIsIdempotentAgainstARealServer()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOCORDIS_PG_CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "GOCORDIS_PG_CONNECTION is required for real database integration");
        var root = Path.Combine(Path.GetTempPath(), "industrial-pg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "recent.csv");
            await File.WriteAllTextAsync(path, "record_id,reading\npg-1,42\npg-2,43\n");
            var identity = FileIdentityFactory.Create(path, root, "pg-source");
            var file = new DiscoveredFile(identity, path, DateTimeOffset.UtcNow, "pg-source");
            var reader = new CsvRecordReader();
            var records = new List<RecordEnvelope>();
            await foreach (var record in reader.ReadAsync(file)) records.Add(record);
            Assert.Equal(2, records.Count);
            Assert.True(records[0].Values.ContainsKey("record_id"),
                "CSV header must carry record_id: " + string.Join(",", records[0].Values.Keys));
            var sink = new PostgreSqlRecordSink(connectionString, "postgresql",
                SinkWriteOptions.From(new Dictionary<string, object?> { ["keyScope"] = "global" }));

            var first = await sink.WriteAsync("file-1", Stream(records), "key-1");
            var second = await sink.WriteAsync("file-1", Stream(records), "key-1");
            Assert.Equal(2, first.Records);
            Assert.True(second.Records == 0, "ON CONFLICT DO NOTHING keeps the replay idempotent");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM industrial_records WHERE record_id IN ('pg-1','pg-2')";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.Equal(2, count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async IAsyncEnumerable<RecordEnvelope> Stream(IEnumerable<RecordEnvelope> records)
    {
        foreach (var record in records)
        {
            await ValueTask.CompletedTask;
            yield return record;
        }
    }
}
