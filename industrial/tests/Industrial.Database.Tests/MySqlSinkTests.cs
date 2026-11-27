using Industrial.Contracts;
using Industrial.Core;
using MySqlConnector;

namespace Industrial.Database.Tests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOCORDIS_MYSQL_CONNECTION")))
            Skip = "Set GOCORDIS_MYSQL_CONNECTION to a real MySQL instance";
    }
}

/// <summary>Opt-in real MySQL integration; SQLite is never substituted.</summary>
public sealed class MySqlSinkTests
{
    [MySqlFact]
    public async Task MySqlSinkIsIdempotentAgainstARealServer()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOCORDIS_MYSQL_CONNECTION")!;
        var sink = new MySqlRecordSink(connectionString, "mysql",
            SinkWriteOptions.From(new Dictionary<string, object?> { ["keyScope"] = "global" }));
        var records = new[] { Record("mysql-1"), Record("mysql-2") };
        var first = await sink.WriteAsync("file-1", Stream(records), "key-1");
        var replay = await sink.WriteAsync("file-1", Stream(records), "key-1");
        Assert.Equal(2, first.Records);
        Assert.Equal(0, replay.Records);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT count(*) FROM industrial_records WHERE record_id IN ('mysql-1','mysql-2')", connection);
        Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static RecordEnvelope Record(string id) => new(
        new Dictionary<string, object?> { ["record_id"] = id, ["reading"] = "42" },
        new FileIdentity("mysql-source|recent.csv|1|HASH", "recent.csv", 1,
            DateTimeOffset.UtcNow, "HASH", "mysql-source"),
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
