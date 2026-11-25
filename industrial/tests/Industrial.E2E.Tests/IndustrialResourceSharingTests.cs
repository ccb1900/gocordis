using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Elsa;
using Industrial.Persistence;
using Industrial.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Scenario B (docs/collector.md §15): one UNC/SQL-style credential is
/// referenced by several target Resources. Workflows carry only resource ids;
/// the sink factory resolves the secret from the vault.</summary>
public sealed class IndustrialResourceSharingTests
{
    [Fact]
    public async Task SharedCredentialIsResolvedByResourceAndWorkflowsCarryNoSecret()
    {
        const string secret = "{\"username\":\"sa\",\"password\":\"S3cret!\"}";
        var root = Path.Combine(Path.GetTempPath(), "industrial-resources-" + Guid.NewGuid().ToString("N"));
        var sourceA = Path.Combine(root, "source-a");
        var sourceB = Path.Combine(root, "source-b");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        await WriteCsv(Path.Combine(sourceA, "recent-a.csv"), "a-1");
        await WriteCsv(Path.Combine(sourceB, "recent-b.csv"), "b-1");

        try
        {
            await using var runtime = new Runtime();
            var console = new ConsoleHost(runtime);
            await using var registry = new PluginRegistry(runtime);
            console.AttachRegistry(registry);
            registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
            registry.ExternalCapabilities.Add(ApiGateway.Key);
            await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

            registry.RegisterComponentFactory("industrial.domain", manifest =>
                new IndustrialDomainComponent(manifest.Config));
            registry.RegisterComponentFactory("industrial.nodes", _ => new IndustrialNodesComponent());
            registry.RegisterComponentFactory("industrial.elsa", _ => new IndustrialElsaComponent());
            registry.RegisterComponentFactory("industrial.ui", _ => new IndustrialUiComponent());
            await registry.ApplyAsync([
                Decl("industrial.domain", new Dictionary<string, object?>
                {
                    ["root"] = root, ["allowDevelopmentKey"] = "true",
                }),
                Decl("industrial.nodes"), Decl("industrial.elsa"), Decl("industrial.ui"),
            ]);

            var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
            var vault = runtime.RequireCapability(IndustrialKeys.CredentialVault);
            var workflows = runtime.RequireCapability(IndustrialKeys.WorkflowRegistry);
            await vault.PutAsync("sql-credential", "Plant SQL", "sqlserver", secret);

            foreach (var (id, sourceRoot, targetId) in new[]
                     {
                         ("source-a", sourceA, "target-a"),
                         ("source-b", sourceB, "target-b"),
                     })
            {
                await resources.RegisterAsync(Resource(id, "filesystem.local", sourceRoot, null));
                await resources.RegisterAsync(Resource(
                    targetId, "sqlite", root, null));
            }
            await resources.RegisterAsync(Resource(
                "sql-a", "sqlserver", "sql-a.dbhost", "sql-credential",
                ("database", "plant_a")));
            await resources.RegisterAsync(Resource(
                "sql-b", "sqlserver", "sql-b.dbhost", "sql-credential",
                ("database", "plant_b")));

            var sqlA = await resources.GetAsync("sql-a");
            var sqlB = await resources.GetAsync("sql-b");
            Assert.Equal("sql-credential", sqlA!.CredentialId);
            Assert.Equal("sql-credential", sqlB!.CredentialId);
            var connectionA = await SqlServerSinkFactory.BuildConnectionStringAsync(sqlA, vault);
            var connectionB = await SqlServerSinkFactory.BuildConnectionStringAsync(sqlB, vault);
            Assert.Contains("S3cret!", connectionA);
            Assert.Contains("sa", connectionA);
            Assert.Contains("S3cret!", connectionB);
            Assert.Contains("plant_b", connectionB);

            foreach (var (workflowId, resourceId, targetId) in new[]
                     {
                         ("flow-a", "source-a", "target-a"),
                         ("flow-b", "source-b", "target-b"),
                     })
            {
                var definition = Definition(workflowId, resourceId, targetId);
                Assert.True(await console.Api.ExecuteAsync("industrial/save-workflow",
                    JsonSerializer.SerializeToNode(definition) as JsonObject));
                Assert.True(await console.Api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = workflowId }));
                Assert.True(await console.Api.ExecuteAsync("industrial/run-workflow",
                    new JsonObject { ["workflowId"] = workflowId, ["resourceId"] = resourceId }));

                var saved = await workflows.GetAsync(workflowId);
                Assert.NotNull(saved);
                Assert.DoesNotContain("S3cret!", JsonSerializer.Serialize(saved));
                var sinkNode = saved!.Nodes.Single(node => node.Type == "industrial.sink.sqlite");
                Assert.DoesNotContain("S3cret!", JsonSerializer.Serialize(sinkNode.Config));
                Assert.Equal(targetId, sinkNode.Config["targetResourceId"]?.ToString());
            }

            await using var ledgerDb = IndustrialStore.Create(Path.Combine(root, "industrial-ledger.db"));
            var targets = await ledgerDb.FileDeliveries
                .Select(delivery => delivery.TargetId).ToListAsync();
            Assert.Contains("target-a", targets);
            Assert.Contains("target-b", targets);
            var statuses = await ledgerDb.FileDeliveries
                .Select(delivery => delivery.Status).ToListAsync();
            Assert.All(statuses, status => Assert.Equal("Succeeded", status));

            // The console query is the UI's view of the SAME ledger rows.
            var deliveriesWire = Assert.IsType<JsonArray>(console.Api.Query("industrial/deliveries"));
            var wireTargets = deliveriesWire.Select(delivery =>
                delivery?["targetId"]?.GetValue<string>()).ToArray();
            Assert.Contains("target-a", wireTargets);
            Assert.Contains("target-b", wireTargets);
            Assert.All(deliveriesWire, delivery =>
                Assert.Equal("Succeeded", delivery?["status"]?.GetValue<string>()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WriteCsv(string path, string recordId)
    {
        File.WriteAllText(path, $"record_id,reading\n{recordId},42\n");
        File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        await Task.CompletedTask;
    }

    private static ResourceDefinition Resource(
        string id, string type, string root, string? credentialId,
        params (string Key, string Value)[] extra)
    {
        var config = new Dictionary<string, object?>
        {
            [type == "sqlserver" ? "host" : "root"] = root,
        };
        foreach (var pair in extra) config[pair.Key] = pair.Value;
        return new ResourceDefinition(id, type, id, type, config, credentialId,
            0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private static WorkflowDefinition Definition(string workflowId, string sourceId, string targetId) =>
        new(workflowId, workflowId, 1,
            [
                new WorkflowNode("source", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = sourceId }, 0, 0),
                new WorkflowNode("reader", "industrial.reader.csv",
                    new Dictionary<string, object?> { ["resourceId"] = sourceId }, 100, 0),
                new WorkflowNode("sink", "industrial.sink.sqlite",
                    new Dictionary<string, object?>
                    {
                        ["resourceId"] = sourceId, ["targetResourceId"] = targetId,
                    }, 200, 0),
            ],
            [
                new WorkflowEdge("source", "files", "reader", "input"),
                new WorkflowEdge("reader", "records", "sink", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            [sourceId], new Dictionary<string, object?>());

    private static PluginManifest Decl(string id, IReadOnlyDictionary<string, object?>? config = null) =>
        new(id, id, true, config ?? new Dictionary<string, object?>(), [], []);
}
