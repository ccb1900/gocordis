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

namespace Industrial.Tests;

/// <summary>Cancellation on the shipped Elsa path: the delivery's lease and
/// claim must be released and the failure must remain visible on the ledger.</summary>
public sealed class DeliveryCancellationTests
{
    [Fact]
    public async Task CancellationInsideADeliveryReleasesTheClaimAndStillSurfacesFailure()
    {
        using var temp = new TempDir();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factories = new FactorySet(new CancellingSink(entered, release.Task));
        var definition = Definition("workflow");

        Task? run = null;
        await Compose(temp.Root, factories, async (_, _, _, _, _, api, runtime) =>
        {
            Assert.True(await PublishAsync(api, definition));
            run = api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                ["instanceId"] = "cancelled-instance",
            }).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            // Cancel the component context that owns the in-flight Elsa
            // command, exactly as a host shutdown would.
            await runtime.DisposeAsync();
        });

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run!);
        Assert.False(string.IsNullOrWhiteSpace(cancellation.Message));

        // A cancelled delivery is a hard exit: its claim is intentionally kept
        // in-flight until the next process starts. Startup recovery releases
        // that claim and keeps the failure visible; it must never silently
        // turn the interrupted delivery into a success.
        await Compose(temp.Root, factories, async (_, _, _, _, _, api, _) =>
        {
            Assert.NotNull(api.Query("industrial/execution"));
        });

        await using var db = IndustrialStore.Create(temp.LedgerDb);
        var delivery = Assert.Single(await db.FileDeliveries.AsNoTracking().ToListAsync());
        Assert.Equal(CollectionStatus.Failed, delivery.Status);
        Assert.Null(delivery.ClaimToken);
        Assert.Equal(0, delivery.LeaseExpiresAtUnixMs);
        Assert.False(string.IsNullOrWhiteSpace(delivery.Error));
        var runRow = Assert.Single(await db.CollectionRuns.AsNoTracking().ToListAsync());
        Assert.True(CollectionStatus.IsTerminal(runRow.Status),
            $"run must be terminal after recovery, was {runRow.Status}");
    }

    private static WorkflowDefinition Definition(string workflowId) => new(
        workflowId, workflowId, 1,
        [
            new WorkflowNode("src", "industrial.filesystem.local",
                new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
            new WorkflowNode("read", "industrial.reader.csv",
                new Dictionary<string, object?>(), 100, 0),
            new WorkflowNode("store", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
        ],
        [
            new WorkflowEdge("src", "files", "read", "input"),
            new WorkflowEdge("read", "records", "store", "records"),
        ],
        new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["source-1"], new Dictionary<string, object?> { ["lookbackDays"] = 1 });

    private static async Task<bool> PublishAsync(ApiGateway api, WorkflowDefinition definition) =>
        await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)) &&
        await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId });

    private static async Task Compose(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ICollectionLedger,
            ICredentialVault, ApiGateway, Gocordis.Core.Runtime, Task> act)
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
            ["sourceRoot"] = root,
            ["resourceId"] = "source-1",
        });
        registry.RegisterComponentFactory("test.sinks", _ => new SinkFactoriesComponent(factories));
        var types = IndustrialComponents.Types
            .Where(type => type is not ("industrial.ui" or "industrial.reader.json"))
            .ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.sinks");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());

        var targets = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var registered = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "target-a", "target-b" })
        {
            if (await targets.GetAsync(id) is null)
            {
                await targets.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, registered, registered));
            }
        }

        await act(
            runtime.RequireCapability(IndustrialKeys.NodeCatalog),
            runtime.RequireCapability(IndustrialKeys.WorkflowRegistry),
            runtime.RequireCapability(IndustrialKeys.ResourceRegistry),
            runtime.RequireCapability(IndustrialKeys.CollectionLedger),
            runtime.RequireCapability(IndustrialKeys.CredentialVault),
            console.Api,
            runtime);
    }

    private sealed class FactorySet(IRecordSink sink) : IRecordSinkFactory
    {
        public string ResourceType => "target-a";

        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null,
            CancellationToken ct = default) => ValueTask.FromResult(sink);
    }

    private sealed class CancellingSink(TaskCompletionSource entered, Task release) : IRecordSink
    {
        private int _writes;

        public string TargetId => "target-a";

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _writes) > 1)
            {
                // Recovery must still fail this delivery; it must not look as
                // though the cancelled attempt quietly completed.
                throw new IOException("delivery still unavailable after cancellation");
            }

            entered.TrySetResult();
            await release.WaitAsync(ct);
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct)) count++;
            return new DeliveryResultRef(idempotencyKey, TargetId, count);
        }
    }

    private sealed class SinkFactoriesComponent(IRecordSinkFactory factory) : Component
    {
        public override string Name => "test.sink-factories";

        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.RecordSinkFactories];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.RecordSinkFactories);
            var handle = broker.Register(factory);
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () =>
                {
                    handle.Dispose();
                    return ValueTask.CompletedTask;
                });
            return null;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "sample.csv"), "record_id,reading\n1,42\n");
            File.SetLastWriteTimeUtc(Path.Combine(Root, "sample.csv"),
                DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
            LedgerDb = Path.Combine(Root, "industrial-ledger.db");
        }

        public string Root { get; }
        public string LedgerDb { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
