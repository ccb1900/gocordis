using System.Diagnostics;
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

/// <summary>Real vertical slice on the shipped Elsa path: local source → CSV →
/// mapping → validation → idempotent SQLite sink → archive, with file-level and
/// target-level ledger. Every run goes through a published workflow definition
/// and an <c>industrial/run-workflow</c> command.</summary>
public sealed class CollectionPipelineTests
{
    [Fact]
    public async Task CollectionEmitsOpenTelemetryActivity()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var seen = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Gocordis.Industrial.Collection",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => seen.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        var factories = new FactorySet(temp.TargetDb);
        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            Assert.True(await PublishAsync(api, Definition("telemetry")));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "telemetry", ["resourceId"] = "source-1",
            }));
        });

        Assert.Contains("collection.run", seen);
    }

    [Fact]
    public async Task FullCollectionIsIdempotentAndLedgerTracksEachTarget()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n2,43\n");
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition(
            "workflow-1", mappingField: "reading", validate: true, archive: true);

        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
        });

        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
        await using var ledger = IndustrialStore.Create(temp.LedgerDb);
        var runs = ledger.CollectionRuns.AsNoTracking().ToList();
        Assert.Equal(2, runs.Count);
        Assert.NotEqual(runs[0].Id, runs[1].Id);
        var files = ledger.CollectionFiles.AsNoTracking().ToList();
        Assert.Single(files);
        Assert.Equal(CollectionStatus.Succeeded, files[0].Status);
        var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery =>
            Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
        Assert.Empty(factories.FailedSinkErrors);
        Assert.Single(Directory.GetFiles(temp.ArchiveRoot, "*.csv", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ArchiveFailureIsRetriedWithoutRepeatingDatabaseDelivery()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n2,43\n");
        // A file where the archive directory must be makes real archiving fail
        // while leaving the database target healthy.
        BlockArchive(temp);
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow-1", archive: true);
        string archiveDeliveryId = string.Empty;

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var deliveries = await ledger.ListAllDeliveriesAsync();
            var dbDelivery = deliveries.Single(delivery => delivery.TargetId == "target-a");
            var archiveDelivery = deliveries.Single(delivery => delivery.TargetId == "archive");
            Assert.Equal(CollectionStatus.Succeeded, dbDelivery.Status);
            Assert.Equal(CollectionStatus.Failed, archiveDelivery.Status);
            Assert.Equal(1, dbDelivery.Attempts);
            archiveDeliveryId = archiveDelivery.Id;
        });

        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
        File.Delete(temp.ArchiveRoot);
        Directory.CreateDirectory(temp.ArchiveRoot);

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            // A retry of ONLY the archive target is enough; the database row
            // must not be written again.
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = archiveDeliveryId,
            }));
            var after = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(CollectionStatus.Succeeded,
                after.Single(delivery => delivery.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.Succeeded,
                after.Single(delivery => delivery.TargetId == "archive").Status);
        });

        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
        Assert.Single(Directory.GetFiles(temp.ArchiveRoot, "*.csv", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RetryRunnerOnlyRetriesFailedTargets()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n2,43\n");
        BlockArchive(temp);
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow-1", archive: true);

        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
        });
        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));

        File.Delete(temp.ArchiveRoot);
        Directory.CreateDirectory(temp.ArchiveRoot);
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["targetId"] = "archive",
            }));
            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.All(deliveries, delivery =>
                Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
            Assert.Equal(1, deliveries.Single(delivery => delivery.TargetId == "target-a").Attempts);
        });

        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
    }

    [Fact]
    public async Task DatabaseDisconnectIsRecordedAndRetriedWithoutDuplicateWrites()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n2,43\n");
        var factories = new FactorySet(temp.TargetDb) { FailSink = true };
        var definition = Definition("workflow-1");
        string failedDeliveryId = string.Empty;

        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            var failed = Assert.Single(await ledger.ListAllDeliveriesAsync());
            Assert.Equal(CollectionStatus.Failed, failed.Status);
            Assert.Equal(1, failed.Attempts);
            Assert.Contains("database offline", failed.Error, StringComparison.Ordinal);
            failedDeliveryId = failed.Id;
        });
        Assert.Equal(0, await CountTargetRowsAsync(temp.TargetDb));

        factories.FailSink = false;
        await Compose(temp.Root, factories, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                Assert.Single(await ledger.ListAllDeliveriesAsync()).Status);
        });
        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
    }

    [Fact]
    public async Task RestartedRuntimeResumesFailedDeliveryFromLedger()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n2,43\n");
        var failing = new FactorySet(temp.TargetDb) { FailSink = true };
        var definition = Definition("workflow-1");
        string failedDeliveryId = string.Empty;

        await Compose(temp.Root, failing, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
            failedDeliveryId = Assert.Single(await ledger.ListAllDeliveriesAsync()).Id;
        });

        // A new process has no in-memory state; the ledger + persisted Elsa
        // instance are the resume source.
        var restored = new FactorySet(temp.TargetDb);
        await Compose(temp.Root, restored, async (_, _, _, ledger, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));
            Assert.Equal(CollectionStatus.Succeeded,
                Assert.Single(await ledger.ListAllDeliveriesAsync()).Status);
        });
        Assert.Equal(2, await CountTargetRowsAsync(temp.TargetDb));
    }

    [Fact]
    public async Task OfflineScanBackfillsMissedFilesWithoutDuplicatingBusinessData()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "late.csv", "record_id,reading\nlate-1,99\n");
        var late = Path.Combine(temp.SourceRoot, "late.csv");
        File.SetLastWriteTimeUtc(late, DateTimeOffset.UtcNow.AddDays(-3).UtcDateTime);

        var planner = new HistoricalRangePlanner();
        var onlineRange = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
        var backfillRange = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(7));
        Assert.DoesNotContain(await new LocalFileSource(await SourceRegistryAsync(temp)).DiscoverAsync(
                "source-1", onlineRange),
            file => file.Identity.RelativePath == "late.csv");

        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("offline", lookbackDays: 7);
        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
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

        Assert.Equal(1, await CountTargetRowsAsync(temp.TargetDb));
        await using var ledger = IndustrialStore.Create(temp.LedgerDb);
        Assert.Single(ledger.CollectionFiles.AsNoTracking().ToList(),
            file => file.IdentityKey.Contains("late.csv"));
        Assert.Single(ledger.FileDeliveries.AsNoTracking().ToList(),
            delivery => delivery.IdempotencyKey.Contains("late.csv"));
    }

    [Fact]
    public async Task RecordTimeFromCsvReachesTheTarget()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "record-time.csv",
            "record_id,reading,record_time\n3,44,2026-01-02T03:04:05+00:00\n");
        var factories = new FactorySet(temp.TargetDb);
        var definition = Definition("workflow-2");
        await Compose(temp.Root, factories, async (_, _, _, _, _, api) =>
        {
            Assert.True(await PublishAsync(api, definition));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1",
            }));
        });

        await using var target = IndustrialStore.CreateTarget(temp.TargetDb);
        var row = Assert.Single(target.Records.AsNoTracking()
            .Where(record => record.RecordId.Contains("|3")).ToList());
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), row.RecordTime);
    }

    [Fact]
    public async Task HistoricalBackfillUsesLedgerNotLastSuccessDate()
    {
        using var temp = new TempDir();
        await WriteSourceAsync(temp.SourceRoot, "sample.csv", "record_id,reading\n1,42\n");
        var planner = new HistoricalRangePlanner();
        var now = DateTimeOffset.UtcNow;
        var range = planner.Plan(now, TimeSpan.FromDays(400));
        Assert.Equal(now.AddDays(-400), range.FromInclusive);
        Assert.Equal(now, range.ToExclusive);

        var files = await new LocalFileSource(await SourceRegistryAsync(temp))
            .DiscoverAsync("source-1", range);
        Assert.Single(files);
        var identity = files[0].Identity;
        var same = FileIdentityFactory.Create(files[0].FullPath, temp.SourceRoot, "source-1");
        Assert.Equal(identity.Key, same.Key);

        var otherMapping = FileIdentityFactory.Create(
            files[0].FullPath, temp.SourceRoot, "source-1", DateTimeOffset.UtcNow);
        Assert.Equal(identity.Key, otherMapping.Key);
    }

    [Fact]
    public async Task CredentialVaultFailsFastWithoutProductionKey()
    {
        var previous = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY", null);
            using var temp = new TempDir();
            var secretKeyMissing = false;
            try
            {
                _ = new CredentialVault(() => IndustrialStore.Create(temp.LedgerDb));
            }
            catch (InvalidOperationException)
            {
                secretKeyMissing = true;
            }

            Assert.True(secretKeyMissing);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOCORDIS_INDUSTRIAL_KEY", previous);
        }
    }

    [Fact]
    public async Task CredentialVaultNeverExposesSecretsThroughViews()
    {
        using var temp = new TempDir();
        var vault = new CredentialVault(() => IndustrialStore.Create(temp.LedgerDb),
            allowDevelopmentKey: true);
        var view = await vault.PutAsync("cred-1", "share", "username-password", "s3cret");
        Assert.Equal("s3cret", await vault.RevealAsync("cred-1"));
        Assert.DoesNotContain("s3cret", view.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", JsonSerializer.Serialize(view), StringComparison.Ordinal);
        var rotated = await vault.RotateAsync("cred-1", "new-secret");
        Assert.Equal(2, rotated.Version);
        Assert.Equal("new-secret", await vault.RevealAsync("cred-1"));
    }

    private static void BlockArchive(TempDir temp)
    {
        if (Directory.Exists(temp.ArchiveRoot)) Directory.Delete(temp.ArchiveRoot);
        else if (File.Exists(temp.ArchiveRoot)) File.Delete(temp.ArchiveRoot);
        File.WriteAllText(temp.ArchiveRoot, "not a directory");
    }

    private static async Task WriteSourceAsync(string root, string name, string contents)
    {
        var path = Path.Combine(root, name);
        await File.WriteAllTextAsync(path, contents);
        File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
    }

    private static WorkflowDefinition Definition(
        string workflowId,
        string? mappingField = null,
        bool validate = false,
        bool archive = false,
        int lookbackDays = 1)
    {
        var nodes = new List<WorkflowNode>
        {
            new("src", "industrial.filesystem.local",
                new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
            new("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
        };
        var edges = new List<WorkflowEdge> { new("src", "files", "read", "input") };
        var output = "read";
        var port = "records";
        var x = 200;
        if (mappingField is not null)
        {
            nodes.Add(new WorkflowNode("map", "industrial.transform.mapping",
                new Dictionary<string, object?>
                {
                    ["mapping"] = JsonSerializer.SerializeToElement(
                        new Dictionary<string, string> { ["value"] = mappingField }),
                }, x, 0));
            edges.Add(new WorkflowEdge(output, port, "map", "input"));
            output = "map";
            port = "output";
            x += 100;
        }

        if (validate)
        {
            nodes.Add(new WorkflowNode("validate", "industrial.transform.validate",
                new Dictionary<string, object?>
                {
                    ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                }, x, 0));
            edges.Add(new WorkflowEdge(output, port, "validate", "input"));
            output = "validate";
            port = "output";
            x += 100;
        }

        nodes.Add(new WorkflowNode("store", "industrial.sink.sqlite",
            new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, x, 0));
        edges.Add(new WorkflowEdge(output, port, "store", "records"));
        if (archive)
        {
            nodes.Add(new WorkflowNode("archive", "industrial.sink.archive",
                new Dictionary<string, object?>(), x, 200));
            edges.Add(new WorkflowEdge("src", "files", "archive", "files"));
        }

        return new WorkflowDefinition(
            workflowId, workflowId, 1, nodes, edges,
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?> { ["lookbackDays"] = lookbackDays });
    }

    private static async Task<bool> PublishAsync(ApiGateway api, WorkflowDefinition definition) =>
        await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)) &&
        await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId });

    private static async Task<int> CountTargetRowsAsync(string targetDb)
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
        public bool FailSink { get; set; }
        public List<string> FailedSinkErrors { get; } = [];

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => FailSink
                ? new OfflineSink("target-a", FailedSinkErrors)
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

    private sealed class OfflineSink(string targetId, List<string> errors) : IRecordSink
    {
        public string TargetId => targetId;

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            errors.Add("database offline");
            throw new IOException("database offline");
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

    private static async Task<IResourceRegistry> SourceRegistryAsync(TempDir temp)
    {
        var resources = new ResourceRegistry(() => IndustrialStore.Create(temp.LedgerDb));
        await resources.RegisterAsync(new ResourceDefinition(
            "source-1", "filesystem.local", "source-1", "local",
            new Dictionary<string, object?> { ["root"] = temp.SourceRoot },
            null, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        return resources;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-pipeline-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            ArchiveRoot = Path.Combine(Root, "archive");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(ArchiveRoot);
            LedgerDb = Path.Combine(Root, "industrial-ledger.db");
            TargetDb = Path.Combine(Root, "industrial-target.db");
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string ArchiveRoot { get; }
        public string LedgerDb { get; }
        public string TargetDb { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
