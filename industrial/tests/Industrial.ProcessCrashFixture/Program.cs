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
using Microsoft.EntityFrameworkCore;

namespace Industrial.ProcessCrashFixture;

/// <summary>Child process for the real OS-kill acceptance test. It writes one
/// row to target B, signals a marker file, then waits to be killed before any
/// ledger claim or Elsa activity can complete.</summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("root directory required");
        var root = Path.GetFullPath(args[0]);
        var targetA = Path.Combine(root, "target-a.db");
        var targetB = Path.Combine(root, "target-b.db");
        var marker = Path.Combine(root, "crash-ready");

        await using var runtime = new Runtime();
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
        registry.RegisterComponentFactory("crash.sinks",
            _ => new SinkFactoriesComponent(targetA, targetB, marker));

        var types = IndustrialComponents.Types
            .Where(type => type is not ("industrial.ui" or "industrial.reader.json"))
            .ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "crash.sinks");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());

        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var now = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "target-a", "target-b" })
        {
            await resources.RegisterAsync(new ResourceDefinition(
                id, id, id, "fixture", new Dictionary<string, object?>(), null,
                0, true, now, now));
        }

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

        if (!await console.Api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)))
        {
            throw new InvalidOperationException("fixture failed to save workflow");
        }
        if (!await console.Api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "two-target-flow" }))
        {
            throw new InvalidOperationException("fixture failed to publish workflow");
        }
        await console.Api.ExecuteAsync("industrial/run-workflow", new JsonObject
        {
            ["workflowId"] = "two-target-flow",
            ["resourceId"] = "source-1",
            ["instanceId"] = "two-target-instance",
        });
        Console.WriteLine("fixture completed without being killed");
    }

    private sealed class SinkFactoriesComponent(
        string targetA, string targetB, string marker) : Component
    {
        public override string Name => "crash.sinks";
        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.RecordSinkFactories];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.RecordSinkFactories);
            var handles = new List<IRecordSinkFactory>
            {
                new TargetFactory("target-a", () => new SqliteRecordSink(
                    () => IndustrialStore.CreateTarget(targetA), "target-a")),
                new TargetFactory("target-b", () => new PartialThenWaitSink(
                    () => IndustrialStore.CreateTarget(targetB), "target-b", marker)),
            }.Select(factory => broker.Register(factory)).ToArray();
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

    private sealed class TargetFactory(string resourceType, Func<IRecordSink> create) : IRecordSinkFactory
    {
        public string ResourceType => resourceType;
        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null,
            CancellationToken ct = default) => ValueTask.FromResult(create());
    }

    private sealed class PartialThenWaitSink(
        Func<IndustrialTargetDbContext> db, string targetId, string marker) : IRecordSink
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
                if (written == 1)
                {
                    await File.WriteAllTextAsync(marker, "ready", ct);
                    await Task.Delay(Timeout.Infinite, ct);
                }
            }
            return new DeliveryResultRef(idempotencyKey, targetId, written);
        }
    }
}
