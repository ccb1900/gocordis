using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Industrial.Tests;

/// <summary>The product acceptance scenario: one CSV, one Reader and Mapping,
/// two database targets. A hard exit after the first partial write must resume
/// the original Elsa instance and retry only the incomplete delivery.</summary>
public sealed class CrashRecoveryTests
{
    private sealed class RealMultiDatabaseFactAttribute : FactAttribute
    {
        public RealMultiDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOCORDIS_SQLSERVER_CONNECTION")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOCORDIS_MYSQL_CONNECTION")))
                Skip = "Set GOCORDIS_SQLSERVER_CONNECTION and GOCORDIS_MYSQL_CONNECTION to real servers";
        }
    }

    [RealMultiDatabaseFact]
    public async Task MultiTargetPartialFailureRetriesOnlyTheFailedRealDatabase()
    {
        // Use the databases already provisioned by the evidence workflow and
        // isolate each run with unique table names instead of creating a DB.
        var sqlConnectionString = Environment.GetEnvironmentVariable("GOCORDIS_SQLSERVER_CONNECTION")!;
        var mySqlConnectionString = Environment.GetEnvironmentVariable("GOCORDIS_MYSQL_CONNECTION")!;
        var sqlServer = new SqlConnectionStringBuilder(sqlConnectionString);
        var mySql = new MySqlConnectionStringBuilder(mySqlConnectionString);
        var sqlTable = $"IndustrialRecords_{Guid.NewGuid():N}";
        var mysqlTable = $"industrial_records_{Guid.NewGuid():N}".Replace('-', '_');

        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 3).Select(index => $"{index},{index * 10}")));
        var sourceFile = Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv");
        await File.WriteAllTextAsync(sourceFile, csv);
        File.SetLastWriteTimeUtc(sourceFile, now.AddHours(-1).UtcDateTime);

        var factories = new FactorySet(
            Path.Combine(temp.Root, "unused.db"), Path.Combine(temp.Root, "unused-target.db"));
        var definition = new WorkflowDefinition(
            "real-multi-target-flow", "Real multi target flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("storeSql", "industrial.sink.sqlserver",
                    new Dictionary<string, object?> { ["targetResourceId"] = "real-sqlserver" }, 300, 0),
                new WorkflowNode("storeMySql", "industrial.sink.mysql",
                    new Dictionary<string, object?> { ["targetResourceId"] = "real-mysql" }, 300, 100),
                new WorkflowNode("storeArchive", "industrial.sink.archive",
                    new Dictionary<string, object?>(), 300, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "storeSql", "records"),
                new WorkflowEdge("map", "output", "storeMySql", "records"),
                new WorkflowEdge("src", "files", "storeArchive", "files"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, vault, api) =>
        {
            await vault.PutAsync("real-sql-cred", "Real SQL Server credential", "username-password",
                JsonSerializer.Serialize(new { username = sqlServer.UserID, password = sqlServer.Password }));
            await vault.PutAsync("real-mysql-cred", "Real MySQL credential", "username-password",
                JsonSerializer.Serialize(new { username = mySql.UserID, password = mySql.Password }));
            var now = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "real-sqlserver", "sqlserver", "Real SQL Server", "sqlserver",
                new Dictionary<string, object?>
                {
                    ["server"] = sqlServer.DataSource,
                    ["database"] = sqlServer.InitialCatalog,
                    ["table"] = sqlTable,
                }, "real-sql-cred", 1, true, now, now));
            // Intentionally unreachable on the first run: the other targets
            // must still succeed and the failed target must be independently
            // recoverable without re-running them.
            await resources.RegisterAsync(new ResourceDefinition(
                "real-mysql", "mysql", "Real MySQL", "mysql",
                new Dictionary<string, object?>
                {
                    ["host"] = "127.0.0.1", ["port"] = "1",
                    ["database"] = mySql.Database, ["table"] = mysqlTable,
                }, "real-mysql-cred", 1, true, now, now));
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
            }));

            var sqlDelivery = (await ledger.ListAllDeliveriesAsync()).Single(
                delivery => delivery.TargetId == "real-sqlserver");
            var archiveDelivery = (await ledger.ListAllDeliveriesAsync()).Single(
                delivery => delivery.TargetId == "archive");
            var mysqlDelivery = (await ledger.ListAllDeliveriesAsync()).Single(
                delivery => delivery.TargetId == "real-mysql");
            Assert.Equal(CollectionStatus.Succeeded, sqlDelivery.Status);
            Assert.Equal(CollectionStatus.Succeeded, archiveDelivery.Status);
            Assert.Equal(CollectionStatus.Failed, mysqlDelivery.Status);
            Assert.Equal(1, sqlDelivery.Attempts);
            Assert.Equal(1, archiveDelivery.Attempts);
        });

        Assert.Equal(3, await CountSqlServerRowsAsync(sqlConnectionString, sqlTable));
        var archiveFiles = Directory.GetFiles(Path.Combine(temp.Root, "archive"), "*.csv",
            SearchOption.AllDirectories);
        Assert.Single(archiveFiles);

        // Repair only the failed target's connection, then retry its delivery.
        await Compose(temp.Root, factories, async (_, _, resources, ledger, _, api) =>
        {
            var existing = await resources.GetAsync("real-mysql")
                           ?? throw new InvalidOperationException("mysql resource disappeared");
            var valid = new MySqlConnectionStringBuilder(mySqlConnectionString)
            {
                Database = mySql.Database,
            };
            await resources.RegisterAsync(existing with
            {
                Version = existing.Version,
                Configuration = new Dictionary<string, object?>
                {
                    ["host"] = valid.Server,
                    ["port"] = valid.Port.ToString(),
                    ["database"] = valid.Database,
                    ["table"] = mysqlTable,
                },
            });
            var failed = (await ledger.ListAllDeliveriesAsync()).Single(
                delivery => delivery.TargetId == "real-mysql" &&
                            delivery.Status == CollectionStatus.Failed);
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failed.Id,
            }));
        });

        Assert.Equal(3, await CountMySqlRowsAsync(mySqlConnectionString, mysqlTable));
        Assert.Single(archiveFiles);
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, _) =>
        {
            var deliveries = await ledger.ListAllDeliveriesAsync();
            var sql = deliveries.Single(delivery => delivery.TargetId == "real-sqlserver");
            var archive = deliveries.Single(delivery => delivery.TargetId == "archive");
            var mysql = deliveries.Single(delivery => delivery.TargetId == "real-mysql");
            Assert.Equal(CollectionStatus.Succeeded, sql.Status);
            Assert.Equal(CollectionStatus.Succeeded, archive.Status);
            Assert.Equal(CollectionStatus.Succeeded, mysql.Status);
            Assert.Equal(1, sql.Attempts);
            Assert.Equal(1, archive.Attempts);
            Assert.Equal(2, mysql.Attempts);
        });
    }

    [Fact]
    public async Task SingleDeliveryRetryDoesNotCommitTheWholeSinkNodeAsComplete()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file-1.csv"),
            "record_id,reading\n1,10\n2,20\n");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file-2.csv"),
            "record_id,reading\n3,30\n4,40\n");
        foreach (var path in Directory.GetFiles(sourceRoot))
            File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetDb = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(Path.Combine(temp.Root, "target-a.db"), targetDb)
        {
            FailTargetBOnce = true,
        };
        var definition = new WorkflowDefinition(
            "single-retry-flow", "Single retry flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("sink", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "sink", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        string instanceId = string.Empty;
        string retriedDeliveryId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
                ["instanceId"] = "single-retry-instance",
            }));

            var failed = (await ledger.ListAllDeliveriesAsync())
                .Where(delivery => delivery.TargetId == "target-b" &&
                                   delivery.Status == CollectionStatus.Failed)
                .ToList();
            Assert.Equal(2, failed.Count);
            retriedDeliveryId = failed[0].Id;
            instanceId = (await ledger.ListRunsAsync()).Single().InstanceId
                         ?? "single-retry-instance";
        });

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = retriedDeliveryId,
            }));
            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(delivery => delivery.Id == retriedDeliveryId).Status);
            Assert.Equal(CollectionStatus.Failed,
                deliveries.Single(delivery => delivery.Id != retriedDeliveryId).Status);
            Assert.Equal(CollectionStatus.Failed,
                (await ledger.ListRunsAsync()).Single().Status);
            var execution = Assert.IsType<JsonObject>(api.Query("industrial/execution"));
            var nodes = execution["nodes"]!.AsArray()
                .Select(node => (JsonObject)node!)
                .ToDictionary(node => node["nodeId"]!.GetValue<string>());
            Assert.NotEqual("Reused", nodes["sink"]["status"]!.GetValue<string>());
        });

        await ComposeCore(temp.Root, factories, async (_, _, _, _, _, _, stage) =>
        {
            var fingerprint = Fingerprint(definition, "sink");
            Assert.Null(await stage.TryGetAsync<DeliveryResultRef>(
                instanceId, "sink", fingerprint));
        });
    }

    [Fact]
    public async Task MultipleFilesKeepIndependentDeliveryStatesAcrossTwoTargets()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "a.csv"),
            "record_id,reading\na1,1\na2,2\n");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "b.csv"),
            "record_id,reading\nb1,3\nb2,4\n");
        foreach (var path in Directory.GetFiles(sourceRoot))
            File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB) { FailTargetBOnce = true };
        var definition = TwoTargetDefinition();

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            await RegisterTargetAsync(resources, "target-a", DateTimeOffset.UtcNow);
            await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                ["instanceId"] = "multi-file-instance",
            }));

            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(4, deliveries.Count);
            Assert.Equal(2, deliveries.Count(d => d.TargetId == "target-a" &&
                                                  d.Status == CollectionStatus.Succeeded));
            Assert.Equal(2, deliveries.Count(d => d.TargetId == "target-b" &&
                                                  d.Status == CollectionStatus.Failed));
        });

        Assert.Equal(4, await CountRowsAsync(targetA));
        Assert.Equal(0, await CountRowsAsync(targetB));

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            var failed = (await ledger.ListAllDeliveriesAsync())
                .Where(d => d.Status == CollectionStatus.Failed).ToList();
            Assert.Equal(2, failed.Count);
            foreach (var delivery in failed)
            {
                Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
                {
                    ["deliveryId"] = delivery.Id,
                }));
            }
            Assert.All(await ledger.ListAllDeliveriesAsync(),
                delivery => Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
        });

        Assert.Equal(4, await CountRowsAsync(targetA));
        Assert.Equal(4, await CountRowsAsync(targetB));
        Assert.Equal(4, await DistinctRowCountAsync(targetB));
    }

    [Fact]
    public async Task RetryThatCrashesAgainIsRecoveredByTheNextStart()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB) { CrashTargetB = true };
        var definition = TwoTargetDefinition();

        await Compose(temp.Root, factories, async (_, workflows, resources, _, _, api) =>
        {
            await RegisterTargetAsync(resources, "target-a", DateTimeOffset.UtcNow);
            await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            try
            {
                await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                    ["instanceId"] = "crash-twice-instance",
                });
            }
            catch (OperationCanceledException) { }
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(1, await CountRowsAsync(targetB));

        // Startup recovery retries the incomplete target, but the sink dies
        // again after one row. The next process must still see recoverable work.
        try
        {
            await Compose(temp.Root, factories, async (_, _, _, _, _, _) => { });
        }
        catch (OperationCanceledException) { }
        Assert.Equal(2, await CountRowsAsync(targetB));

        factories.CrashTargetB = false;
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.True(recovery["recovered"]?.GetValue<int>() >= 1);
            Assert.All(await ledger.ListAllDeliveriesAsync(),
                delivery => Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(6, await CountRowsAsync(targetB));
        Assert.Equal(6, await DistinctRowCountAsync(targetB));
    }

    [Fact]
    public async Task TargetIdRetryDrainsFailedDeliveriesAcrossInstances()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB);
        var definition = TwoTargetDefinition();

        for (var index = 1; index <= 2; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"file-{index}.csv"),
                $"record_id,reading\n{index},10\n");
            File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"file-{index}.csv"),
                DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
            factories.FailTargetBOnce = true;
            await Compose(temp.Root, factories, async (_, workflows, resources, _, _, api) =>
            {
                if (index == 1)
                {
                    await RegisterTargetAsync(resources, "target-a", DateTimeOffset.UtcNow);
                    await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
                    Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                        JsonSerializer.SerializeToNode(definition)));
                    Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                        new JsonObject { ["workflowId"] = definition.WorkflowId }));
                }
                Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                    ["instanceId"] = $"separate-instance-{index}",
                }));
            });
        }

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.Equal(2, (await ledger.ListAllDeliveriesAsync())
                .Count(d => d.TargetId == "target-b" && d.Status == CollectionStatus.Failed));
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (!(await ledger.GetRetriableDeliveriesAsync("target-b")).Any()) break;
                Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
                {
                    ["targetId"] = "target-b",
                }));
            }
            Assert.Empty(await ledger.GetRetriableDeliveriesAsync("target-b"));
            Assert.All((await ledger.ListAllDeliveriesAsync()).Where(d => d.TargetId == "target-b"),
                delivery => Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
        });

        Assert.Equal(2, await CountRowsAsync(targetB));
    }

    private static WorkflowDefinition TwoTargetDefinition() => new(
        "two-target-flow", "Two target flow", 1,
        [
            new WorkflowNode("src", "industrial.filesystem.local",
                new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
            new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
            new WorkflowNode("map", "industrial.transform.mapping",
                new Dictionary<string, object?>
                {
                    ["mapping"] = JsonSerializer.SerializeToElement(
                        new Dictionary<string, string> { ["value"] = "reading" }),
                }, 200, 0),
            new WorkflowNode("storeA", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 300, 0),
            new WorkflowNode("storeB", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 200),
        ],
        [
            new WorkflowEdge("src", "files", "read", "input"),
            new WorkflowEdge("read", "records", "map", "input"),
            new WorkflowEdge("map", "output", "storeA", "records"),
            new WorkflowEdge("map", "output", "storeB", "records"),
        ],
        new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["source-1"], new Dictionary<string, object?>());

    private static string Fingerprint(WorkflowDefinition definition, string nodeId)
    {
        var node = definition.Nodes.Single(candidate => candidate.Id == nodeId);
        var material = string.Join("|",
            $"{definition.WorkflowId}:{definition.Version}", node.Id, node.Type,
            JsonSerializer.Serialize(node.Config));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static async Task<int> CountSqlServerRowsAsync(string connectionString, string table)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM [{table}]";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountMySqlRowsAsync(string connectionString, string table)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM `{table}`";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task TwoTargetMidWriteCrashResumesAndRetriesOnlyTheIncompleteTarget()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var ledgerPath = Path.Combine(temp.Root, "industrial-ledger.db");
        var factories = new FactorySet(targetA, targetB) { CrashTargetB = true };

        var definition = new WorkflowDefinition(
            "two-target-flow", "Two target flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv",
                    new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("storeA", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 300, 0),
                new WorkflowNode("storeB", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "storeA", "records"),
                new WorkflowEdge("map", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        // First process: the second sink commits one row and then the process
        // vanishes before the delivery or the Elsa activity can complete.
        await Compose(temp.Root, factories, async (catalog, workflows, resources, _, _, api) =>
        {
            await RegisterTargetAsync(resources, "target-a", DateTimeOffset.UtcNow);
            await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "two-target-flow" }));
            try
            {
                await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = "two-target-flow",
                    ["resourceId"] = "source-1",
                    ["instanceId"] = "two-target-instance",
                });
            }
            catch (OperationCanceledException)
            {
                // The simulated hard exit deliberately aborts this call.
            }
            Assert.NotNull(await workflows.GetPublishedAsync("two-target-flow"));
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(1, await CountRowsAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.True(run.Status is CollectionStatus.Running or CollectionStatus.Interrupted);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.Equal(2, deliveries.Count);
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(delivery => delivery.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.InProgress,
                deliveries.Single(delivery => delivery.TargetId == "target-b").Status);
        }

        // Second process: recovery runs as an effect of loading the same plugin,
        // not as a manual API call. Only target-b had an incomplete delivery.
        factories.CrashTargetB = false;
        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.Equal(1, recovery["recovered"]?.GetValue<int>());
            Assert.Empty(recovery["failures"]!.AsArray());
            var execution = Assert.IsType<JsonObject>(api.Query("industrial/execution"));
            Assert.Equal("Finished", execution["status"]!.GetValue<string>());
            Assert.Equal(new[] { "src", "read", "map", "storeA", "storeB" },
                execution["executed"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
            var nodes = execution["nodes"]!.AsArray()
                .Select(node => (JsonObject)node!)
                .ToDictionary(node => node["nodeId"]!.GetValue<string>());
            Assert.Equal("Reused", nodes["storeA"]["status"]!.GetValue<string>());
            Assert.Equal("Succeeded", nodes["storeB"]["status"]!.GetValue<string>());
            Assert.Equal(5, nodes["storeB"]["records"]!.GetValue<int>());
            Assert.Equal(3, nodes["storeB"]["samples"]!.AsArray().Count);
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(6, await CountRowsAsync(targetB));
        Assert.Equal(6, await DistinctRowCountAsync(targetA));
        Assert.Equal(6, await DistinctRowCountAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.Equal(CollectionStatus.Succeeded, run.Status);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.All(deliveries, delivery =>
                Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
            Assert.Equal(6, deliveries.Single(delivery => delivery.TargetId == "target-a").Records);
            Assert.Equal(5, deliveries.Single(delivery => delivery.TargetId == "target-b").Records);
        }
    }

    [Fact]
    public async Task RealProcessKillRecoversOnlyTheIncompleteDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var ledgerPath = Path.Combine(temp.Root, "industrial-ledger.db");
        var marker = Path.Combine(temp.Root, "crash-ready");
        var fixtureProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../Industrial.ProcessCrashFixture/Industrial.ProcessCrashFixture.csproj"));

        var start = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{fixtureProject}\" --no-build -- \"{temp.Root}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var child = Process.Start(start)
                           ?? throw new InvalidOperationException("failed to start crash fixture");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            // Do not await stdout/stderr here: those tasks only complete after
            // the child exits, and this child is intentionally still alive.
            Assert.True(await WaitForMarkerAsync(marker, TimeSpan.FromSeconds(60)),
                "crash fixture never reached its partial-write marker");
        }
        finally
        {
            if (!child.HasExited)
            {
                // Real OS process termination, not an exception injected into
                // the workflow: no ledger completion, effect unwind or EOF.
                child.Kill(entireProcessTree: true);
            }
            await child.WaitForExitAsync();
        }
        _ = await stdout;
        _ = await stderr;

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(1, await CountRowsAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.True(run.Status is CollectionStatus.Running or CollectionStatus.Interrupted);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(delivery => delivery.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.InProgress,
                deliveries.Single(delivery => delivery.TargetId == "target-b").Status);
        }

        // New process: startup recovery sees the orphaned Elsa instance and
        // resumes it from committed stages; only target-b still needs work.
        var factories = new FactorySet(targetA, targetB);
        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.Equal(1, recovery["recovered"]?.GetValue<int>());
            Assert.Empty(recovery["failures"]!.AsArray());
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(6, await CountRowsAsync(targetB));
        Assert.Equal(6, await DistinctRowCountAsync(targetA));
        Assert.Equal(6, await DistinctRowCountAsync(targetB));
        await using var finalLedger = IndustrialStore.Create(ledgerPath);
        Assert.Equal(CollectionStatus.Succeeded,
            Assert.Single(finalLedger.CollectionRuns.AsNoTracking().ToList()).Status);
    }

    private static async Task<bool> WaitForMarkerAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(50);
        }
        return File.Exists(path);
    }

    private static async Task<int> CountRowsAsync(string path)
    {
        await using var db = IndustrialStore.CreateTarget(path);
        return await db.Records.AsNoTracking().CountAsync();
    }

    private static async Task<int> DistinctRowCountAsync(string path)
    {
        await using var db = IndustrialStore.CreateTarget(path);
        return await db.Records.AsNoTracking().Select(row => row.RecordId).Distinct().CountAsync();
    }

    private static async Task RegisterTargetAsync(
        IResourceRegistry resources, string id, DateTimeOffset now) =>
        await resources.RegisterAsync(new ResourceDefinition(
            id, id, id, "test", new Dictionary<string, object?>(), null,
            0, true, now, now));

    private static Task Compose(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ICollectionLedger,
            ICredentialVault, ApiGateway, Task> act) =>
        ComposeCore(root, factories,
            (nodes, workflows, resources, ledger, vault, api, _) =>
                act(nodes, workflows, resources, ledger, vault, api));

    private static async Task ComposeCore(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ICollectionLedger,
            ICredentialVault, ApiGateway, IStageOutputStore, Task> act)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());

        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
            ["root"] = root,
            ["seedResource"] = "true",
            ["sourceRoot"] = Path.Combine(root, "source"),
            ["resourceId"] = "source-1",
        });
        registry.RegisterComponentFactory("test.sinks", _ => new SinkFactoriesComponent(factories.Factories));
        var types = IndustrialComponents.Types
            .Where(type => type is not ("industrial.ui" or "industrial.reader.json"))
            .ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.sinks");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());

        await act(
            runtime.RequireCapability(IndustrialKeys.NodeCatalog),
            runtime.RequireCapability(IndustrialKeys.WorkflowRegistry),
            runtime.RequireCapability(IndustrialKeys.ResourceRegistry),
            runtime.RequireCapability(IndustrialKeys.CollectionLedger),
            runtime.RequireCapability(IndustrialKeys.CredentialVault),
            console.Api,
            runtime.RequireCapability(IndustrialKeys.StageOutputStore));
    }

    [Fact]
    public async Task DeliveryRetryReusesPublishedMappingAndValidation()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB) { FailTargetBOnce = true };

        var definition = new WorkflowDefinition(
            "retry-flow", "Retry flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv",
                    new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("validate", "industrial.transform.validate",
                    new Dictionary<string, object?>
                    {
                        ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                    }, 300, 0),
                new WorkflowNode("storeA", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 400, 0),
                new WorkflowNode("storeB", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 400, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "validate", "input"),
                new WorkflowEdge("validate", "output", "storeA", "records"),
                new WorkflowEdge("validate", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        string failedDeliveryId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
            }));

            var allDeliveries = await ledger.ListAllDeliveriesAsync();
            var delivery = allDeliveries.Single(candidate =>
                candidate.TargetId == "target-b" && candidate.Status == "Failed");
            failedDeliveryId = delivery.Id;
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        await using var target = IndustrialStore.CreateTarget(targetB);
        var rows = target.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"reading\"", row.PayloadJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeliveryRetryResumesTheRunThatOwnsTheFailedDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var firstFile = $"{now:yyyyMMdd}.csv";
        var secondFile = $"{now.AddDays(-25):yyyyMMdd}.csv";
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, firstFile),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, firstFile), now.AddHours(-1).UtcDateTime);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, secondFile),
            "record_id,reading\n4,77\n5,88\n6,99\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, secondFile), now.AddDays(-25).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB);

        WorkflowDefinition Definition(int version) => new(
            "multi-run-retry-flow", "Multi run retry flow", version,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                version == 1
                    ? new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 300, 0)
                    : new WorkflowNode("storeB", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                version == 1
                    ? new WorkflowEdge("map", "output", "storeA", "records")
                    : new WorkflowEdge("map", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?> { ["lookbackDays"] = version == 1 ? 1 : 30 });

        string failedDeliveryId = string.Empty;
        string firstDeliveryId = string.Empty;
        string firstRunId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(1))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "multi-run-retry-flow" }));
            // This id sorts ABOVE the failing run's id: the old string-sort
            // heuristic would resume this unrelated execution.
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "multi-run-retry-flow", ["version"] = 1,
                ["resourceId"] = "source-1", ["instanceId"] = "zzz-unrelated-instance",
            }));
            var firstDelivery = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.TargetId == "target-a" && candidate.Status == CollectionStatus.Succeeded);
            firstDeliveryId = firstDelivery.Id;
            firstRunId = (await ledger.GetRunIdsByDeliveryAsync(firstDelivery.Id)).Single();
            File.Delete(Path.Combine(sourceRoot, firstFile));

            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(2))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "multi-run-retry-flow" }));
            factories.FailTargetBOnce = true;
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "multi-run-retry-flow", ["version"] = 2,
                ["resourceId"] = "source-1", ["instanceId"] = "aaa-owning-instance",
            }));

            var delivery = (await ledger.ListAllDeliveriesAsync()).Single(candidate =>
                candidate.TargetId == "target-b" && candidate.Status == CollectionStatus.Failed);
            failedDeliveryId = delivery.Id;
            var runIds = await ledger.GetRunIdsByDeliveryAsync(delivery.Id);
            var owner = await ledger.GetRunAsync(runIds.Single());
            Assert.Equal("aaa-owning-instance", owner!.InstanceId);
            var unrelated = (await ledger.ListRunsAsync()).Single(run => run.InstanceId == "zzz-unrelated-instance");
            Assert.NotEqual(owner.Id, unrelated.Id);

            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));

            // The first run's successful delivery is untouched by the retry.
            var firstAfter = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.Id == firstDeliveryId);
            Assert.Equal(CollectionStatus.Succeeded, firstAfter.Status);
            Assert.Equal(firstRunId, (await ledger.GetRunIdsByDeliveryAsync(firstDeliveryId)).Single());
            Assert.Single(await ledger.ListAllDeliveriesAsync(), candidate => candidate.TargetId == "target-a");
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        await using var firstTarget = IndustrialStore.CreateTarget(targetA);
        var firstRows = firstTarget.Records.AsNoTracking().ToList();
        Assert.Equal(3, firstRows.Count);
        Assert.All(firstRows, row => Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal));
        Assert.Equal(new[] { "11", "22", "33" }, ValuesOf(firstRows));
        await using var target = IndustrialStore.CreateTarget(targetB);
        var rows = target.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"reading\"", row.PayloadJson, StringComparison.Ordinal);
        });
        Assert.Equal(new[] { "77", "88", "99" }, ValuesOf(rows));
    }

    [Fact]
    public async Task RetryUsesTheLastRunThatAttemptedTheSameDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv"),
            "record_id,reading,reading2\n1,11,101\n2,22,202\n3,33,303\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv"),
            now.AddHours(-1).UtcDateTime);
        var target = Path.Combine(temp.Root, "target.db");
        var factories = new FactorySet(Path.Combine(temp.Root, "unused.db"), target);

        WorkflowDefinition Definition(int version, string sourceField) => new(
            "last-attempt-flow", "Last attempt flow", version,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = sourceField }),
                    }, 200, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "store", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        string deliveryId = string.Empty;
        string lastRunId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var registered = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "target-b", "target-b", "target-b", "fixture",
                new Dictionary<string, object?> { ["database"] = target }, null, 0, true, registered, registered));

            foreach (var (version, sourceField, instanceId) in new[]
                     {
                         (1, "reading", "aaa-old-mapping"),
                         (2, "reading2", "zzz-new-mapping"),
                     })
            {
                Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                    JsonSerializer.SerializeToNode(Definition(version, sourceField))));
                Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "last-attempt-flow" }));
                factories.FailTargetBOnce = true;
                Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = "last-attempt-flow", ["version"] = version,
                    ["resourceId"] = "source-1", ["instanceId"] = instanceId,
                }));
                var failed = (await ledger.ListAllDeliveriesAsync()).Single(candidate =>
                    candidate.TargetId == "target-b" && candidate.Status == CollectionStatus.Failed);
                deliveryId = failed.Id;
                lastRunId = failed.LastAttemptRunId
                            ?? throw new InvalidOperationException("delivery has no last attempt run");
                Assert.Equal(instanceId, await ledger.GetRunInstanceAsync(lastRunId));
                Assert.Equal(lastRunId, failed.LastAttemptRunId);
            }

            Assert.Single(await ledger.ListAllDeliveriesAsync());
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = deliveryId,
            }));
        });

        await using var db = IndustrialStore.CreateTarget(target);
        var rows = db.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain("reading", row.PayloadJson, StringComparison.Ordinal);
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
        });
        Assert.Equal(new[] { "101", "202", "303" }, rows
            .Select(row => JsonDocument.Parse(row.PayloadJson).RootElement
                .GetProperty("value").GetString() ?? string.Empty)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(lastRunId));
    }

    [Fact]
    public async Task StreamingPipelineKeepsLiveHeapBoundedOnLargeInput()
    {
        const int rowCount = 100_000;
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv");
        await using (var writer = new StreamWriter(file))
        {
            await writer.WriteLineAsync("record_id,reading");
            for (var i = 0; i < rowCount; i++) await writer.WriteLineAsync($"{i},{i}");
        }
        File.SetLastWriteTimeUtc(file, now.AddHours(-1).UtcDateTime);
        var target = Path.Combine(temp.Root, "target.db");
        var factories = new FactorySet(target, Path.Combine(temp.Root, "unused.db"));
        var definition = new WorkflowDefinition(
            "memory-flow", "Memory flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "store", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var baseline = GC.GetTotalMemory(true);
        using var sampling = new CancellationTokenSource();
        var peak = baseline;
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                // Force collection so we measure LIVE heap, not uncollected garbage.
                var live = GC.GetTotalMemory(true);
                if (live > peak) peak = live;
                await Task.Delay(50, sampling.Token);
            }
        });
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var registered = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "target-a", "target-a", "target-a", "fixture",
                new Dictionary<string, object?> { ["database"] = target }, null, 0, true, registered, registered));
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var delivery = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.TargetId == "target-a");
            Assert.Equal(rowCount, delivery.Records);
        });
        sampling.Cancel();
        try { await sampler; } catch (OperationCanceledException) { }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var after = GC.GetTotalMemory(true);
        // A full batch of 100k RecordEnvelopes would be tens of MB; streaming
        // through disk-backed stage datasets must stay far below that. Check
        // PEAK live heap during execution, not only post-GC survivor heap.
        Assert.True(peak - baseline < 64L * 1024 * 1024,
            $"peak live heap grew by {(peak - baseline) / (1024 * 1024)} MB");
        Assert.True(after - baseline < 64L * 1024 * 1024,
            $"surviving heap grew by {(after - baseline) / (1024 * 1024)} MB");
        Assert.Equal(rowCount, await CountRowsAsync(target));
    }

    [Fact]
    public async Task LargeFileStreamsThroughReaderMappingValidationAndSink()
    {
        const int rowCount = 20_000;
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv");
        await using (var writer = new StreamWriter(file))
        {
            await writer.WriteLineAsync("record_id,reading");
            for (var i = 1; i <= rowCount; i++)
                await writer.WriteLineAsync($"{i},{i * 3}");
        }
        File.SetLastWriteTimeUtc(file, now.AddHours(-1).UtcDateTime);
        var target = Path.Combine(temp.Root, "target.db");
        var factories = new FactorySet(target, Path.Combine(temp.Root, "unused.db"));

        var definition = new WorkflowDefinition(
            "streaming-flow", "Streaming flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("validate", "industrial.transform.validate",
                    new Dictionary<string, object?>
                    {
                        ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                    }, 300, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 400, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "validate", "input"),
                new WorkflowEdge("validate", "output", "store", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var registered = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "target-a", "target-a", "target-a", "fixture",
                new Dictionary<string, object?> { ["database"] = target }, null, 0, true, registered, registered));
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
            }));
            var delivery = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.TargetId == "target-a");
            Assert.Equal(CollectionStatus.Succeeded, delivery.Status);
            Assert.Equal(rowCount, delivery.Records);
        });

        Assert.Equal(rowCount, await CountRowsAsync(target));
        Assert.Equal(rowCount, await DistinctRowCountAsync(target));
    }

    [Fact]
    public async Task PublishANewSinkBackfillsOnlyThatTargetForHistoricalFiles()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB);

        WorkflowDefinition Definition(int version, bool includeSecondSink) => new(
            "backfill-flow", "Backfill flow", version,
            includeSecondSink
                ? [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
                    new WorkflowNode("storeB", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 200, 200),
                ]
                : [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
                ],
            includeSecondSink
                ? [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "storeA", "records"),
                    new WorkflowEdge("read", "records", "storeB", "records"),
                ]
                : [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "storeA", "records"),
                ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, _, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(1, false))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "backfill-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "backfill-flow", ["version"] = 1, ["resourceId"] = "source-1",
            }));
            Assert.Equal(3, await CountRowsAsync(targetA));
            Assert.False(File.Exists(targetB));

            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(2, true))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "backfill-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "backfill-flow", ["version"] = 2, ["resourceId"] = "source-1",
            }));

            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(2, deliveries.Count(delivery => delivery.Status == CollectionStatus.Succeeded));
            Assert.Single(deliveries, delivery => delivery.TargetId == "target-a");
            Assert.Single(deliveries, delivery => delivery.TargetId == "target-b");
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        Assert.Equal(3, await CountRowsAsync(targetB));
    }

    private static string[] ValuesOf(IEnumerable<TargetRecordEntity> rows) => rows
        .Select(row => JsonDocument.Parse(row.PayloadJson).RootElement
            .GetProperty("value").GetString() ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private sealed class FactorySet(string targetA, string targetB)
    {
        public bool CrashTargetB { get; set; }
        public bool FailTargetBOnce { get; set; }

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => new SqliteRecordSink(
                () => IndustrialStore.CreateTarget(targetA), "target-a")),
            new TargetFactory("target-b", () =>
            {
                if (CrashTargetB)
                {
                    return new PartialThenExitSink(() => IndustrialStore.CreateTarget(targetB), "target-b", 1);
                }
                if (FailTargetBOnce)
                {
                    FailTargetBOnce = false;
                    return new FailOnceSink(() => IndustrialStore.CreateTarget(targetB), "target-b");
                }
                return new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetB), "target-b");
            }),
        ];
    }

    private sealed class FailOnceSink : IRecordSink
    {
        private readonly string _targetId;

        public FailOnceSink(Func<IndustrialTargetDbContext> _, string targetId) => _targetId = targetId;

        public string TargetId => _targetId;

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            throw new IOException("connection refused");
    }

    private sealed class TargetFactory(string resourceType, Func<IRecordSink> create) : IRecordSinkFactory
    {
        public string ResourceType => resourceType;

        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null,
            CancellationToken ct = default) => ValueTask.FromResult(create());
    }

    /// <summary>Commits one real row, then dies like a killed process: the
    /// ledger claim stays held and the Elsa activity never completes.</summary>
    private sealed class PartialThenExitSink(
        Func<IndustrialTargetDbContext> db, string targetId, int rowsBeforeExit) : IRecordSink
    {
        public string TargetId => targetId;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            await using var context = db();
            var written = 0;
            await foreach (var record in records.WithCancellation(ct))
            {
                var recordId = new SinkWriteOptions("append", null).ResolveId(record);
                if (!await context.Records.AnyAsync(row => row.RecordId == recordId, ct))
                {
                    context.Records.Add(new TargetRecordEntity
                    {
                        RecordId = recordId,
                        SourceIdentityKey = record.Source.Key,
                        RecordTime = record.RecordTime,
                        PayloadJson = JsonSerializer.Serialize(record.Values),
                    });
                    written++;
                }
                await context.SaveChangesAsync(ct);
                if (written == rowsBeforeExit)
                {
                    throw new OperationCanceledException("simulated process termination");
                }
            }
            return new DeliveryResultRef(idempotencyKey, targetId, written);
        }
    }

    private sealed class SinkFactoriesComponent(IReadOnlyList<IRecordSinkFactory> factories) : Component
    {
        public override string Name => "test.sink-factories";

        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.RecordSinkFactories];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.RecordSinkFactories);
            var handles = factories.Select(factory => broker.Register(factory)).ToArray();
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () =>
                {
                    foreach (var handle in handles.Reverse()) handle.Dispose();
                    return ValueTask.CompletedTask;
                });
            return null;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "industrial-crash-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
