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
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Reliability gates on the shipped Elsa path: a partially failed
/// delivery and a large file must both converge to exact, duplicate-free
/// business data.</summary>
public sealed class RecoveryTests
{
    [Fact]
    public async Task MidStreamFailureIsRetriedWithoutDuplicateRows()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync("sample.csv", "record_id,reading\n1,1\n2,2\n");
        var factories = new FactorySet(temp.TargetDb) { FailMidStream = true };
        var definition = Definition("workflow");
        string failedDeliveryId = string.Empty;

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var retriable = await ledger.GetRetriableDeliveriesAsync();
            failedDeliveryId = Assert.Single(retriable).Id;
            Assert.Equal(0, await CountRowsAsync(temp.TargetDb));
        });

        factories.FailMidStream = false;
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                Assert.Single(await ledger.ListAllDeliveriesAsync()).Status);
        });

        await using var target = IndustrialStore.CreateTarget(temp.TargetDb);
        Assert.Equal(2, await target.Records.AsNoTracking().CountAsync());
        Assert.Equal(2, await target.Records.AsNoTracking()
            .Select(row => row.RecordId).Distinct().CountAsync());
    }

    [Fact]
    public async Task LargeFileCountsAreExactAndReplayDoesNotDuplicate()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync("sample.csv", "record_id,reading\n1,1\n2,2\n");
        var builder = new StringBuilder("record_id,reading\n");
        for (var i = 0; i < 20_000; i++) builder.Append(i).Append(',').Append(i).Append('\n');
        await temp.WriteSourceAsync("large.csv", builder.ToString());
        var definition = Definition("workflow");

        await Compose(temp.Root, new FactorySet(temp.TargetDb),
            async (_, _, _, _, _, api) =>
            {
                Assert.True(await PublishAsync(api, definition));
                foreach (var _ in Enumerable.Range(0, 2))
                {
                    Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                    {
                        ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                    }));
                }
            });

        // The fixture also contains the small 20261125.csv, so two additional
        // source-scoped identities are expected; replay must not duplicate any.
        await using var target = IndustrialStore.CreateTarget(temp.TargetDb);
        Assert.Equal(20_002, await target.Records.AsNoTracking().CountAsync());
        Assert.Equal(20_002, await target.Records.AsNoTracking()
            .Select(row => row.RecordId).Distinct().CountAsync());
    }

    [Fact]
    public async Task ReusingAStagedSourceAfterOverwriteIsRejected()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync("sample.csv", "record_id,reading\n1,1\n2,2\n");
        var path = Path.Combine(temp.SourceRoot, "sample.csv");
        var staged = FileIdentityFactory.Create(path, temp.SourceRoot, "source-1");
        var refFile = new BatchFileRef(
            staged.Key, "source-1", staged.Key, staged.RelativePath,
            staged.ContentHash, temp.Range.FromInclusive, staged.Length, staged.LastWriteUtc);

        await File.WriteAllTextAsync(path, "record_id,reading\n1,999\n");
        var changed = FileIdentityFactory.Create(path, temp.SourceRoot, "source-1");
        var error = Assert.Throws<InvalidOperationException>(() => StagedFileIdentity.Verify(refFile, changed));
        Assert.Contains("changed on disk", error.Message, StringComparison.Ordinal);
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

    private static async Task<int> CountRowsAsync(string targetDb)
    {
        await using var db = IndustrialStore.CreateTarget(targetDb);
        return await db.Records.AsNoTracking().CountAsync();
    }

    private static async Task Compose(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ICollectionLedger,
            ICredentialVault, ApiGateway, Task> act)
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
            console.Api);
    }

    private sealed class FactorySet(string targetDb)
    {
        public bool FailMidStream { get; set; }

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => FailMidStream
                ? new MidStreamFailureSink()
                : new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetDb), "target-a")),
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

    /// <summary>Fails mid-stream before writing anything, so recovery has no
    /// partial business rows to clean up.</summary>
    private sealed class MidStreamFailureSink : IRecordSink
    {
        public string TargetId => "target-a";

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct))
            {
                count++;
                if (count == 2) throw new IOException("mid-stream disconnect");
            }

            return new DeliveryResultRef("partial", TargetId, count);
        }
    }

    private sealed class SinkFactoriesComponent(FactorySet factories) : Component
    {
        public override string Name => "test.sink-factories";

        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.RecordSinkFactories];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.RecordSinkFactories);
            var handles = factories.Factories.Select(factory => broker.Register(factory)).ToArray();
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
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-recovery-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            Directory.CreateDirectory(SourceRoot);
            LedgerDb = Path.Combine(Root, "industrial-ledger.db");
            TargetDb = Path.Combine(Root, "industrial-target.db");
            Range = new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string LedgerDb { get; }
        public string TargetDb { get; }
        public SourceDateRange Range { get; }

        public Task WriteSourceAsync(string name, string contents)
        {
            var path = Path.Combine(SourceRoot, name);
            File.WriteAllText(path, contents);
            File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
