using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>A plugin can contribute an exact node type and its execution
/// handler without modifying the designer, Elsa converter or runtime core.
/// Delivery handlers participate in the same Run completion lifecycle as
/// built-in sinks.</summary>
public sealed class CustomNodeHandlerTests
{
    [Fact]
    public async Task ContributedHandlerExecutesItsExactNodeType()
    {
        using var temp = new TempDir();
        await Compose(temp.Root, new MarkerHandler(Path.Combine(temp.Root, "handler-ran.txt")),
            async (api, workflows, ledger) =>
            {
                var definition = Flow("custom-flow", "test.custom.validate", "src");
                Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                    JsonSerializer.SerializeToNode(definition)));
                Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "custom-flow" }));
                Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = "custom-flow", ["resourceId"] = "source-1",
                }));
                Assert.True(File.Exists(Path.Combine(temp.Root, "handler-ran.txt")));
            });
    }

    [Fact]
    public async Task CustomSinkAloneEndsTheRun()
    {
        using var temp = new TempDir();
        await Compose(temp.Root, new DeliveryHandler(fail: false), async (api, workflows, ledger) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Flow("custom-sink-flow", "test.custom.sink", "target-a"))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "custom-sink-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "custom-sink-flow", ["resourceId"] = "source-1",
            }));
            var run = (await ledger.ListRunsAsync()).Single();
            Assert.Equal(CollectionStatus.Succeeded, run.Status);
        });
    }

    [Fact]
    public async Task FailedCustomSinkMakesTheRunFailed()
    {
        using var temp = new TempDir();
        await Compose(temp.Root, new DeliveryHandler(fail: true), async (api, workflows, ledger) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Flow("failing-sink-flow", "test.custom.sink", "target-a"))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "failing-sink-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "failing-sink-flow", ["resourceId"] = "source-1",
            }));
            var run = (await ledger.ListRunsAsync()).Single();
            Assert.Equal(CollectionStatus.Failed, run.Status);
        });
    }

    [Fact]
    public async Task CustomSinkAndBuiltInSinkBothCompleteTheRun()
    {
        using var temp = new TempDir();
        await Compose(temp.Root, new DeliveryHandler(fail: false), async (api, workflows, ledger) =>
        {
            var definition = new WorkflowDefinition(
                "mixed-sink-flow", "Mixed sink flow", 1,
                [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("custom", "test.custom.sink",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
                    new WorkflowNode("store", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 100),
                ],
                [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "custom", "input"),
                    new WorkflowEdge("read", "records", "store", "records"),
                ],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["source-1", "target-a"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "mixed-sink-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "mixed-sink-flow", ["resourceId"] = "source-1",
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                (await ledger.ListRunsAsync()).Single().Status);
            Assert.Equal(2, (await ledger.ListAllDeliveriesAsync()).Count(delivery =>
                delivery.Status == CollectionStatus.Succeeded));
        });
    }

    [Fact]
    public async Task CustomSinkRetryDoesNotEndTheRunBeforeItsDeliverySucceeds()
    {
        using var temp = new TempDir();
        await Compose(temp.Root, new FlakyDeliveryHandler(), async (api, workflows, ledger) =>
        {
            var definition = Flow("retry-sink-flow", "test.custom.sink", "target-a");
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "retry-sink-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "retry-sink-flow", ["resourceId"] = "source-1",
            }));
            var failed = (await ledger.ListAllDeliveriesAsync()).Single(delivery =>
                delivery.TargetId == "custom-sink" && delivery.Status == CollectionStatus.Failed);
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failed.Id,
            }));
            var retried = (await ledger.GetDeliveryAsync(failed.Id))!;
            Assert.Equal(CollectionStatus.Succeeded, retried.Status);
            Assert.Equal(2, retried.Attempts);
        });
    }

    private static WorkflowDefinition Flow(string id, string nodeType, string configValue)
    {
        var config = nodeType.EndsWith("sink", StringComparison.Ordinal)
            ? new Dictionary<string, object?> { ["targetResourceId"] = configValue }
            : new Dictionary<string, object?>();
        return new WorkflowDefinition(
            id, id, 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("custom", nodeType, config, 200, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "custom", "input"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1", "target-a"], new Dictionary<string, object?>());
    }

    private static async Task Compose(string root, IIndustrialNodeHandler handler,
        Func<ApiGateway, IWorkflowRegistry, ICollectionLedger, Task> act)
    {
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var file = Path.Combine(sourceRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd}.csv");
        await File.WriteAllTextAsync(file, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(file,
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());
        registry.RegisterComponentFactory("test.handler", _ => new HandlerComponent(handler));
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true", ["root"] = root,
            ["seedResource"] = "true", ["sourceRoot"] = sourceRoot,
            ["resourceId"] = "source-1",
        });
        var types = IndustrialComponents.Types.ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.handler");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());
        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        await resources.RegisterAsync(new ResourceDefinition(
            "target-a", "sqlite", "target-a", "fixture",
            new Dictionary<string, object?> { ["database"] = Path.Combine(root, "target.db") },
            null, 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await runtime.RequireCapability(IndustrialKeys.NodeCatalog).RegisterAsync(new NodeDescriptor(
            handler.NodeType, "Custom", "Custom", "transform", "Custom", "1.0.0",
            [new NodePort("input", PortType.RecordSet, true)], [],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []));
        await act(console.Api, runtime.RequireCapability(IndustrialKeys.WorkflowRegistry),
            runtime.RequireCapability(IndustrialKeys.CollectionLedger));
    }

    private sealed class HandlerComponent(IIndustrialNodeHandler handler) : Component
    {
        public override string Name => "test.node-handler";
        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.NodeExecutionHandlers];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.NodeExecutionHandlers);
            IDisposable? handle = null;
            await context.EffectAsync(
                () =>
                {
                    handle = broker.Register(handler.NodeType, handler);
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    handle?.Dispose();
                    return ValueTask.CompletedTask;
                });
            return null;
        }
    }

    private sealed class FlakyDeliveryHandler : IIndustrialNodeHandler
    {
        private int _attempts;
        public string NodeType => "test.custom.sink";
        public IndustrialNodeCompletion Completion => IndustrialNodeCompletion.Delivery;

        public async ValueTask ExecuteAsync(IndustrialNodeExecutionContext context, CancellationToken ct = default)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            var file = (await context.Ledger.ListAllFilesAsync(ct)).LastOrDefault()
                       ?? throw new InvalidOperationException("no source file");
            var delivery = await context.Ledger.StartDeliveryAsync(
                file.Id, "custom-sink", $"{file.Id}->custom-sink", "record", context.RunId, ct);
            var claim = await context.Ledger.TryClaimDeliveryAsync(
                context.RunId!, delivery.Id, TimeSpan.FromMinutes(5), ct)
                ?? throw new InvalidOperationException("custom delivery was not claimed");
            if (attempt == 1)
                await context.Ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, "temporary outage", ct);
            else
                await context.Ledger.CompleteDeliveryAsync(delivery.Id, claim.ClaimToken, 1, ct);
            context.Complete(1, []);
        }
    }

    private sealed class MarkerHandler(string marker) : IIndustrialNodeHandler
    {
        public string NodeType => "test.custom.validate";

        public async ValueTask ExecuteAsync(IndustrialNodeExecutionContext context, CancellationToken ct = default)
        {
            var count = 0;
            await foreach (var _ in context.Records(context.Node.Id, ct).WithCancellation(ct)) count++;
            await File.WriteAllTextAsync(marker, count.ToString(), ct);
            context.Complete(count, [$"custom:{count}"]);
        }
    }

    private sealed class DeliveryHandler(bool fail) : IIndustrialNodeHandler
    {
        public string NodeType => "test.custom.sink";
        public IndustrialNodeCompletion Completion => IndustrialNodeCompletion.Delivery;

        public async ValueTask ExecuteAsync(IndustrialNodeExecutionContext context, CancellationToken ct = default)
        {
            var file = (await context.Ledger.ListAllFilesAsync(ct)).LastOrDefault()
                       ?? throw new InvalidOperationException("no source file");
            var delivery = await context.Ledger.StartDeliveryAsync(
                file.Id, "custom-sink", $"{file.Id}->custom-sink", "record", context.RunId, ct);
            var claim = await context.Ledger.TryClaimDeliveryAsync(
                context.RunId!, delivery.Id, TimeSpan.FromMinutes(5), ct)
                ?? throw new InvalidOperationException("custom delivery was not claimed");
            if (fail)
                await context.Ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, "custom sink unavailable", ct);
            else
                await context.Ledger.CompleteDeliveryAsync(delivery.Id, claim.ClaimToken, 1, ct);
            context.Complete(1, []);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"industrial-handler-{Guid.NewGuid():N}");
        public TempDir() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }
}
