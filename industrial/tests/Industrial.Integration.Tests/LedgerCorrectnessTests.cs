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

/// <summary>Behavior gates for the ledger on the shipped Elsa path:
/// resource-scoped identity, terminal run state, target-scoped retry and a
/// single in-flight claim.</summary>
public sealed class LedgerCorrectnessTests
{
    [Fact]
    public async Task IdenticalFilesInTwoResourcesHaveIndependentLedgerEntries()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        await temp.WriteSourceAsync(temp.SecondSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow");

        await Compose(temp.Root, factories, async (_, _, resources, ledger, _, api) =>
        {
            await RegisterSourceAsync(resources, "source-2", temp.SecondSourceRoot);
            Assert.True(await PublishAsync(api, definition));
            foreach (var resourceId in new[] { "source-1", "source-2" })
            {
                Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = definition.WorkflowId, ["resourceId"] = resourceId,
                }));
            }

            using var db = IndustrialStore.Create(temp.LedgerDb);
            var files = db.CollectionFiles.AsNoTracking().ToList();
            Assert.Equal(2, files.Count);
            Assert.Equal(["source-1", "source-2"],
                files.Select(file => file.ResourceId).OrderBy(id => id));
            Assert.Equal(2, db.FileDeliveries.AsNoTracking().Count());
            Assert.Equal(2, factories.TargetAInvocations);
            var first = await new LocalFileSource(resources)
                .DiscoverAsync("source-1", temp.Range);
            Assert.NotNull(await ledger.GetFileByIdentityAsync("source-1", first[0].Identity.Key));
        });
    }

    [Fact]
    public async Task RunReachesTerminalStateWithFinishedAt()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow");
        string runId = string.Empty;

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var runs = await ledger.ListRunsAsync();
            runId = Assert.Single(runs).Id;
        });

        await using var db = IndustrialStore.Create(temp.LedgerDb);
        var run = Assert.Single(db.CollectionRuns.AsNoTracking().ToList());
        Assert.Equal(runId, run.Id);
        Assert.Equal(CollectionStatus.Succeeded, run.Status);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task RunFailureIsRecordedForRecovery()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        // A file where the snapshot directory must be makes the real source
        // node fail after the ledger run has started.
        File.WriteAllText(Path.Combine(temp.Root, "industrial-snapshots"), "not a directory");
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow");

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            });
            var execution = Assert.IsType<JsonObject>(api.Query("industrial/execution"));
            Assert.False(string.IsNullOrWhiteSpace(execution["error"]?.ToString()),
                "the source-stage failure must surface to the caller");
            var run = Assert.Single(await ledger.ListRunsAsync());
            Assert.Null(run.FinishedAt);
        });

        // No business data may be written by a failed source stage, and the
        // unfinished run remains the durable record of the failed attempt.
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, _) =>
        {
            Assert.Empty(await ledger.ListAllDeliveriesAsync());
        });
        await using var target = IndustrialStore.CreateTarget(temp.TargetDb);
        Assert.Empty(target.Records.AsNoTracking().ToList());
    }

    [Fact]
    public async Task NewlyConfiguredTargetReceivesAlreadySucceededHistoricalFile()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var factories = new FactorySet(temp.TargetDb);
        var firstDefinition = Definition("workflow", secondTarget: false);
        var secondDefinition = Definition("workflow", secondTarget: true);

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, firstDefinition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = firstDefinition.WorkflowId, ["resourceId"] = "source-1",
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                Assert.Single(await ledger.ListAllFilesAsync()).Status);

            Assert.True(await PublishAsync(api, secondDefinition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = secondDefinition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(2, deliveries.Count);
            Assert.All(deliveries, delivery =>
                Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
        });

        Assert.Equal(1, factories.TargetAInvocations);
        Assert.Equal(1, factories.TargetBInvocations);
    }

    [Fact]
    public async Task RetryCanTargetOneFailedDelivery()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var factories = new FactorySet(temp.TargetDb) { FailTargetA = true, FailTargetB = true };
        var definition = Definition("workflow", secondTarget: true);

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var failed = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(2, failed.Count);
            Assert.All(failed, delivery => Assert.Equal(CollectionStatus.Failed, delivery.Status));
        });

        factories.FailTargetA = false;
        factories.FailTargetB = false;
        var invocationsAfterFirstRun = (factories.TargetAInvocations, factories.TargetBInvocations);
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["targetId"] = "target-b",
            }));
            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(CollectionStatus.Failed,
                deliveries.Single(row => row.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(row => row.TargetId == "target-b").Status);
        });

        Assert.Equal(invocationsAfterFirstRun.TargetAInvocations, factories.TargetAInvocations);
        Assert.Equal(invocationsAfterFirstRun.TargetBInvocations + 1, factories.TargetBInvocations);
    }

    [Fact]
    public async Task SuccessfulRetryRollsUpFileStatus()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var factories = new FactorySet(temp.TargetDb) { FailTargetA = true };
        var definition = Definition("workflow");

        string deliveryId = string.Empty;
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            Assert.Equal(CollectionStatus.PartialFailure,
                Assert.Single(await ledger.ListAllFilesAsync()).Status);
            deliveryId = Assert.Single(await ledger.ListAllDeliveriesAsync()).Id;
        });

        factories.FailTargetA = false;
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = deliveryId,
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                Assert.Single(await ledger.ListAllFilesAsync()).Status);
        });
    }

    [Fact]
    public async Task ConcurrentRunsClaimOneDeliveryPerFileAndTarget()
    {
        using var temp = new TempDir();
        await temp.WriteSourceAsync(temp.FirstSourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factories = new FactorySet(temp.TargetDb)
        {
            GatedSink = new GatedSink("target-a", entered, gate.Task),
        };
        var definition = Definition("workflow");

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            var first = api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                ["instanceId"] = "first-instance",
            }).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var second = api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
                ["instanceId"] = "second-instance",
            }).AsTask();
            gate.SetResult();
            await Task.WhenAll(first, second);

            using var db = IndustrialStore.Create(temp.LedgerDb);
            Assert.Single(db.CollectionFiles.AsNoTracking().ToList());
            Assert.Single(db.FileDeliveries.AsNoTracking().ToList());
        });

        Assert.Equal(1, factories.TargetAInvocations);
    }

    private static async Task RegisterSourceAsync(
        IResourceRegistry resources, string id, string root) =>
        await resources.RegisterAsync(new ResourceDefinition(
            id, "filesystem.local", id, "local",
            new Dictionary<string, object?> { ["root"] = root },
            null, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

    private static WorkflowDefinition Definition(string workflowId, bool secondTarget = false)
    {
        var nodes = new List<WorkflowNode>
        {
            new("src", "industrial.filesystem.local", new Dictionary<string, object?>(), 0, 0),
            new("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
            new("storeA", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
        };
        var edges = new List<WorkflowEdge>
        {
            new("src", "files", "read", "input"),
            new("read", "records", "storeA", "records"),
        };
        if (secondTarget)
        {
            nodes.Add(new WorkflowNode("storeB", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 200, 200));
            edges.Add(new WorkflowEdge("read", "records", "storeB", "records"));
        }

        return new WorkflowDefinition(
            workflowId, workflowId, 1, nodes, edges,
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?> { ["lookbackDays"] = 1 });
    }

    private static async Task<bool> PublishAsync(ApiGateway api, WorkflowDefinition definition) =>
        await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)) &&
        await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId });

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
            ["sourceRoot"] = Path.Combine(root, "source-1"),
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
        public bool FailTargetA { get; set; }
        public bool FailTargetB { get; set; }
        public GatedSink? GatedSink { get; set; }
        public int TargetAInvocations;
        public int TargetBInvocations;

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => Counting(
                "target-a",
                GatedSink is not null
                    ? GatedSink
                    : FailTargetA
                        ? new OfflineSink("target-a")
                        : new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetDb), "target-a"),
                () => Interlocked.Increment(ref TargetAInvocations))),
            new TargetFactory("target-b", () => Counting(
                "target-b",
                FailTargetB
                    ? new OfflineSink("target-b")
                    : new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetDb), "target-b"),
                () => Interlocked.Increment(ref TargetBInvocations))),
        ];

        private static IRecordSink Counting(string id, IRecordSink inner, Action onWrite) =>
            new CountingSink(id, inner, onWrite);
    }

    private sealed class TargetFactory(string resourceType, Func<IRecordSink> create) : IRecordSinkFactory
    {
        public string ResourceType => resourceType;

        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null,
            CancellationToken ct = default) => ValueTask.FromResult(create());
    }

    private sealed class CountingSink(string targetId, IRecordSink inner, Action onWrite) : IRecordSink
    {
        public string TargetId => targetId;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            onWrite();
            return await inner.WriteAsync(collectionFileId, records, idempotencyKey, ct);
        }
    }

    private sealed class OfflineSink(string targetId) : IRecordSink
    {
        public string TargetId => targetId;

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            throw new IOException("target offline");
    }

    private sealed class GatedSink(string targetId, TaskCompletionSource entered, Task gate) : IRecordSink
    {
        public string TargetId => targetId;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            entered.TrySetResult();
            await gate.WaitAsync(ct);
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct)) count++;
            return new DeliveryResultRef(idempotencyKey, TargetId, count);
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
            Root = Path.Combine(Path.GetTempPath(), "industrial-ledger-" + Guid.NewGuid().ToString("N"));
            FirstSourceRoot = Path.Combine(Root, "source-1");
            SecondSourceRoot = Path.Combine(Root, "source-2");
            Directory.CreateDirectory(FirstSourceRoot);
            Directory.CreateDirectory(SecondSourceRoot);
            LedgerDb = Path.Combine(Root, "industrial-ledger.db");
            TargetDb = Path.Combine(Root, "industrial-target.db");
            Range = new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow);
        }

        public string Root { get; }
        public string FirstSourceRoot { get; }
        public string SecondSourceRoot { get; }
        public string LedgerDb { get; }
        public string TargetDb { get; }
        public SourceDateRange Range { get; }

        public Task WriteSourceAsync(string root, string name, string contents)
        {
            var path = Path.Combine(root, name);
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
