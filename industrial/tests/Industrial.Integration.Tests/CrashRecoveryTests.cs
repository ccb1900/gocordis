using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>The product acceptance scenario: one CSV, one Reader and Mapping,
/// two database targets. A hard exit after the first partial write must resume
/// the original Elsa instance and retry only the incomplete delivery.</summary>
public sealed class CrashRecoveryTests
{
    [Fact]
    public async Task TwoTargetMidWriteCrashResumesAndRetriesOnlyTheIncompleteTarget()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "20261126.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, "20261126.csv"),
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
        await Compose(temp.Root, factories, async (catalog, workflows, resources, api) =>
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
        await Compose(temp.Root, factories, async (_, _, _, api) =>
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

    private static async Task Compose(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ApiGateway, Task> act)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

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
            console.Api);
    }

    private sealed class FactorySet(string targetA, string targetB)
    {
        public bool CrashTargetB { get; set; }

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => new SqliteRecordSink(
                () => IndustrialStore.CreateTarget(targetA), "target-a")),
            new TargetFactory("target-b", () => CrashTargetB
                ? new PartialThenExitSink(() => IndustrialStore.CreateTarget(targetB), "target-b", 1)
                : new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetB), "target-b")),
        ];
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
                var recordId = record.Values.GetValueOrDefault("record_id")?.ToString()
                               ?? throw new InvalidOperationException("record_id is required");
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
