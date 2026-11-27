using Industrial.Contracts;
using Industrial.Core;
using Microsoft.Data.SqlClient;

namespace Industrial.Database.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOCORDIS_SQLSERVER_CONNECTION")))
            Skip = "Set GOCORDIS_SQLSERVER_CONNECTION to a real SQL Server instance";
    }
}

/// <summary>Opt-in real SQL Server integration; SQLite is never substituted.</summary>
public sealed class SqlServerSinkTests
{
    [SqlServerFact]
    public async Task SqlServerSinkIsIdempotentAgainstARealServer()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOCORDIS_SQLSERVER_CONNECTION")!;
        var sink = new SqlServerRecordSink(connectionString, "IndustrialRecords", "sqlserver",
            "append", null, SinkWriteOptions.From(new Dictionary<string, object?>
            {
                ["keyScope"] = "global",
            }));
        var records = new[]
        {
            Record("sqlserver-1"), Record("sqlserver-2"),
        };
        var first = await sink.WriteAsync("file-1", Stream(records), "key-1");
        var replay = await sink.WriteAsync("file-1", Stream(records), "key-1");
        Assert.Equal(2, first.Records);
        Assert.Equal(0, replay.Records);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM IndustrialRecords WHERE record_id IN ('sqlserver-1','sqlserver-2')";
        Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static RecordEnvelope Record(string id) => new(
        new Dictionary<string, object?> { ["record_id"] = id, ["reading"] = "42" },
        new FileIdentity("sqlserver-source|recent.csv|1|HASH", "recent.csv", 1,
            DateTimeOffset.UtcNow, "HASH", "sqlserver-source"),
        DateTimeOffset.UtcNow);

    private static async IAsyncEnumerable<RecordEnvelope> Stream(IEnumerable<RecordEnvelope> records)
    {
        foreach (var record in records)
        {
            await ValueTask.CompletedTask;
            yield return record;
        }
    }
}
