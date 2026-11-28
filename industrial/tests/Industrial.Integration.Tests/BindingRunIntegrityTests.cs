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

/// <summary>Per-run binding integrity: identity parameters, bound recovery
/// snapshots, delivery-aware business dates and a scheduler that survives a
/// top-level tick failure.</summary>
public sealed class BindingRunIntegrityTests
{
    [Fact]
    public async Task ReservedParametersCannotShadowBindingIdentity()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out _);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("reserved-template"));

        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-reserved,Reserved,machine-7,reserved-template,1,p-identity,source-1," +
            "target-1,manual,," +
            "shift=night;sourceResource=evil;Binding=evil;Machine=evil;Profile=evil;" +
            "TargetResource=evil;TargetResource2=evil;BusinessDates=1999-01-01\n"));
        Assert.True(await api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-reserved", ["lookbackDays"] = 1 }));

        var values = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal("b-reserved", values["Binding"]!.GetValue<string>());
        Assert.Equal("machine-7", values["Machine"]!.GetValue<string>());
        Assert.Equal("p-identity", values["Profile"]!.GetValue<string>());
        Assert.Equal("p-identity", values["FileProfile"]!.GetValue<string>());
        Assert.Equal("source-1", values["SourceResource"]!.GetValue<string>());
        Assert.Equal("target-1", values["TargetResource"]!.GetValue<string>());
        Assert.Equal("night", values["shift"]!.GetValue<string>());
        Assert.DoesNotContain("evil", values.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunCollectionCommandIsRemovedAndOnlyElsaRunsACollection()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        var recent = Path.Combine(sourceRoot, "recent.csv");
        await File.WriteAllTextAsync(recent, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(recent, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger);
        // A direct Elsa run uses the published definition as-is (no binding
        // substitution), so its source resource id must be literal.
        await SaveAndPublishAsync(api, SourceOnlyTemplate("elsa-entry-template") with
        {
            Nodes = [new WorkflowNode("src", "industrial.filesystem.local",
                new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0)],
        });

        Assert.DoesNotContain("industrial/run-collection", api.Commands);
        var ui = runtime.RequireCapability(UiContributionRegistry.Key);
        Assert.All(ui.Pages(), page => Assert.All(page.Actions ?? [], action =>
            Assert.NotEqual("industrial/run-collection", action.Command)));
        Assert.Contains(ui.Pages().SelectMany(page => page.Actions ?? []),
            action => action.Command == "industrial/run-workflow");
        Assert.False(await api.ExecuteAsync("industrial/run-collection", new JsonObject
        {
            ["workflowId"] = "elsa-entry-template", ["resourceId"] = "source-1",
        }));
        Assert.Empty(await ledger.ListRunsAsync());

        Assert.Contains("industrial/run-workflow", api.Commands);
        Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
        {
            ["workflowId"] = "elsa-entry-template", ["resourceId"] = "source-1",
        }));
        var run = Assert.Single(await ledger.ListRunsAsync());
        // A source-only definition has no delivery node, so the new
        // finalization rules must report the discovered file instead of
        // silently calling the run successful.
        Assert.Equal(CollectionStatus.Failed, run.Status);
        Assert.Contains("no delivery", run.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(await ledger.ListAllFilesAsync());
    }

    [Fact]
    public async Task CatchUpDaysZeroDoesNotSynthesizeDatesAndOutOfRangeIsRejected()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("catchup-template"));
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-zero,Zero,machine-1,catchup-template,1,p-zero,source-1,target-1,manual,,catchUpDays=0\n" +
            "b-huge,Huge,machine-1,catchup-template,1,p-zero,source-1,target-1,manual,,catchUpDays=4000\n"));

        Assert.True(await api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-zero", ["lookbackDays"] = 1 }));
        Assert.DoesNotContain(await ledger.ListDatesAsync(),
            date => string.Equals(date.BindingId, "b-zero", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await api.ExecuteAsync("industrial/run-binding",
                new JsonObject { ["bindingId"] = "b-huge", ["lookbackDays"] = 1 }));
        Assert.Contains("catchUpDays", error.Message, StringComparison.Ordinal);
        Assert.Contains("366", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await ledger.ListDatesAsync(),
            date => string.Equals(date.BindingId, "b-huge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BindingRunsKeepTheirOwnOutcomeWhenExecutedConcurrently()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            id => id == "target-bad"
                ? new DelegateSink(id, (_, records, key, ct) => throw new IOException("delivery refused"))
                : SuccessSink(id));
        await SaveAndPublishAsync(api, SinkTemplate("isolation-template"));
        await RegisterTargetAsync(api, "target-good");
        await RegisterTargetAsync(api, "target-bad");
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-good,Good,machine-1,isolation-template,1,p-iso,source-1,target-good,manual,,shift=day\n" +
            "b-bad,Bad,machine-2,isolation-template,1,p-iso,source-1,target-bad,manual,,shift=night\n"));

        var good = api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-good", ["lookbackDays"] = 1 }).AsTask();
        var bad = api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-bad", ["lookbackDays"] = 1 }).AsTask();
        foreach (var ok in await Task.WhenAll(good, bad)) Assert.True(ok);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var dates = await ledger.ListDatesAsync();
        Assert.Equal(CollectionDateStatus.Succeeded,
            Assert.Single(dates, date => date.BindingId == "b-good" && date.BusinessDate == yesterday).Status);
        var failed = Assert.Single(dates, date => date.BindingId == "b-bad" && date.BusinessDate == yesterday);
        Assert.Equal(CollectionDateStatus.Failed, failed.Status);
        Assert.NotNull(failed.Error);
        Assert.Contains("delivery refused", failed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDeliveryNeverMarksABusinessDateSucceeded()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            id => new DelegateSink(id, (_, _, _, _) => throw new IOException("sink is down")));
        await SaveAndPublishAsync(api, SinkTemplate("date-failure-template"));
        await RegisterTargetAsync(api, "target-1");
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-date,Date,machine-1,date-failure-template,1,p-date,source-1,target-1,manual,,catchUpDays=1\n"));

        // Elsa itself finishes; only the delivery failed. The business date
        // must still be Failed, not Succeeded with files > 0.
        Assert.True(await api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-date", ["lookbackDays"] = 1 }));
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var date = Assert.Single(await ledger.ListDatesAsync());
        Assert.Equal(yesterday, date.BusinessDate);
        Assert.Equal(CollectionDateStatus.Failed, date.Status);
        Assert.NotNull(date.Error);
        Assert.Contains("sink is down", date.Error!, StringComparison.Ordinal);
        var run = Assert.Single(await ledger.ListRunsAsync(), item => item.Status == CollectionStatus.Failed);
        Assert.Equal(CollectionStatus.Failed, run.Status);
    }

    [Fact]
    public async Task RecoveryKeepsTheExactBoundRunSnapshot()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        var crashed = true;
        Func<string, IRecordSink> sink = id => crashed
            ? new DelegateSink(id, (_, _, _, _) => throw new OperationCanceledException("simulated hard exit"))
            : SuccessSink(id);

        await using (var first = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger, sink))
        {
            await SaveAndPublishAsync(api, SinkTemplate("bound-template"));
            await RegisterTargetAsync(api, "target-1");
            Assert.True(await ImportBindingAsync(api,
                "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
                "targetResourceIds,triggerKind,cron,parameters\n" +
                "b-bound,Bound,machine-bound,bound-template,1,p-bound,source-1,target-1,manual,,shift=bound-value\n"));
            // A simulated hard exit is recorded as a resumable incident rather
            // than propagated through the API; the ledger must keep the run
            // Running with its exact bound snapshot.
            await api.ExecuteAsync("industrial/run-binding",
                new JsonObject { ["bindingId"] = "b-bound", ["lookbackDays"] = 1 });
            var run = Assert.Single(await ledger.ListRunsAsync());
            var snapshot = await ledger.GetRunDefinitionAsync(run.Id);
            Assert.NotNull(snapshot);
            Assert.Contains("bound-value", snapshot!, StringComparison.Ordinal);
            Assert.Equal(CollectionStatus.Running, run.Status);
        }

        crashed = false;
        await using (var second = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger, sink))
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.Equal(1, recovery["recovered"]!.GetValue<int>());
            Assert.Empty(recovery["failures"]!.AsArray());
            var run = Assert.Single(await ledger.ListRunsAsync());
            var snapshot = await ledger.GetRunDefinitionAsync(run.Id);
            Assert.NotNull(snapshot);
            Assert.Contains("bound-value", snapshot!, StringComparison.Ordinal);
            Assert.Equal(CollectionStatus.Succeeded, run.Status);

            var instance = Assert.Single(await ElsaPersistedExecution.ListInstancesAsync(
                Path.Combine(temp.Root, "industrial-elsa.db")));
            Assert.Equal(snapshot, instance.DefinitionJson);
            Assert.Equal(ElsaPersistedExecution.DefinitionHash(snapshot!), instance.DefinitionHash);
        }
    }

    [Fact]
    public async Task TopLevelBindingSchedulerFailureIsRecordedButDoesNotEscape()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out _);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("schedule-template"));
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron\n" +
            "b-tick,Tick,machine-1,schedule-template,1,p-tick,source-1,target-1,schedule,0 0 1 1 *\n"));

        // Break only the ledger store: the tick's binding scan succeeds, then
        // the fire scan throws at the top level of the one scheduler loop.
        foreach (var path in Directory.GetFiles(temp.Root, "industrial-ledger.db*")) File.Delete(path);
        Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
        var status = Assert.IsType<JsonObject>(api.Query("industrial/binding-scheduler-status"));
        Assert.False(string.IsNullOrWhiteSpace(status["lastError"]?.GetValue<string>()));
        // The manual tick and the timer use the same path; a second tick must
        // still be accepted rather than seeing a dead scheduler.
        Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
    }

    [Fact]
    public async Task RetryAfterOriginalFileDeletedUsesTheImmutableSnapshot()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        var original = Directory.GetFiles(sourceRoot, "*.csv").Single();
        var failTarget = true;
        Func<string, IRecordSink> sinkFactory = id => new DelegateSink(id,
            async (_, records, key, ct) =>
            {
                if (failTarget) throw new IOException("target is down");
                return await DrainAsync(id, records, key, ct);
            });

        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            sinkFactory);
        await SaveAndPublishAsync(api, SinkTemplate("snapshot-retry-template"));
        await RegisterTargetAsync(api, "target-1");
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-snapshot,Snapshot,machine-1,snapshot-retry-template,1,p-basic,source-1," +
            "target-1,manual,,shift=day\n"));
        await api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-snapshot", ["lookbackDays"] = 1 });

        var failed = Assert.Single(await ledger.ListAllDeliveriesAsync(),
            delivery => delivery.Status == CollectionStatus.Failed);
        File.Delete(original);
        failTarget = false;

        Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
        {
            ["deliveryId"] = failed.Id, ["targetId"] = failed.TargetId,
        }));
        var retried = Assert.Single(await ledger.ListAllDeliveriesAsync(),
            delivery => delivery.Id == failed.Id);
        Assert.Equal(CollectionStatus.Succeeded, retried.Status);
        Assert.True(retried.Records > 0);
    }

    [Fact]
    public async Task LateFileInDatedDirectoryIsDiscoveredThroughTheRealSourcePath()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var datedDirectory = Path.Combine(sourceRoot, businessDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(datedDirectory);
        var file = Path.Combine(datedDirectory, "readings.csv");
        await File.WriteAllTextAsync(file, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            id => SuccessSink(id));
        await SaveAndPublishAsync(api, SinkTemplate("late-file-template"));
        await RegisterTargetAsync(api, "target-1");
        Assert.True(await api.ExecuteAsync("industrial/save-file-profile", new JsonObject
        {
            ["id"] = "p-dated", ["name"] = "Dated directory", ["fileNamePattern"] = "*.csv",
            ["format"] = "csv", ["delimiter"] = ",", ["hasHeader"] = true,
            ["encoding"] = "utf-8", ["mapping"] = new JsonObject { ["value"] = "reading" },
            ["required"] = new JsonArray("value"), ["directoryPattern"] = "YYYY-MM-DD",
            ["datePattern"] = string.Empty, ["flat"] = false, ["metadataRules"] = new JsonArray(),
        }));
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-late,Late,machine-1,late-file-template,1,p-dated,source-1,target-1,manual,," +
            "catchUpDays=7;shift=late\n"));

        Assert.True(await api.ExecuteAsync("industrial/run-binding",
            new JsonObject { ["bindingId"] = "b-late", ["lookbackDays"] = 1 }));

        var collected = Assert.Single(await ledger.ListAllFilesAsync());
        Assert.Equal(businessDate, DateOnly.FromDateTime(collected.SourceDate.UtcDateTime));
        var date = Assert.Single(await ledger.ListDatesAsync(),
            row => row.BusinessDate == businessDate);
        Assert.Equal(CollectionDateStatus.Succeeded, date.Status);
        Assert.Equal(1, date.Files);

        Assert.False(string.IsNullOrWhiteSpace(collected.SnapshotPath));
        Assert.True(File.Exists(collected.SnapshotPath!));
        Assert.True(await api.ExecuteAsync("industrial/maintain-snapshots", new JsonObject()));
        var status = Assert.IsType<JsonObject>(api.Query("industrial/snapshot-status"));
        Assert.Null(status["error"]);
        var report = Assert.IsType<JsonObject>(status["report"]);
        Assert.True(report["deleted"]!.GetValue<int>() >= 1);
        Assert.False(File.Exists(collected.SnapshotPath!));
    }

    private static WorkflowDefinition SourceOnlyTemplate(string id) => new(
        id, "Source template", 1,
        [new WorkflowNode("src", "industrial.filesystem.local", new Dictionary<string, object?>
        {
            ["resourceId"] = "{{SourceResource}}", ["shift"] = "{{shift}}",
        }, 0, 0)],
        [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["{{SourceResource}}"], new Dictionary<string, object?>());

    private static WorkflowDefinition SinkTemplate(string id) => new(
        id, "Sink template", 1,
        [
            new WorkflowNode("src", "industrial.filesystem.local",
                new Dictionary<string, object?>
                {
                    ["resourceId"] = "{{SourceResource}}", ["shift"] = "{{shift}}",
                }, 0, 0),
            new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
            new WorkflowNode("store", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "{{TargetResource}}" }, 200, 0),
        ],
        [
            new WorkflowEdge("src", "files", "read", "input"),
            new WorkflowEdge("read", "records", "store", "records"),
        ], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["{{SourceResource}}"], new Dictionary<string, object?>());

    private static string PrepareSource(string root)
    {
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        // A completed business date, never UTC "today": the source node's
        // planned-date filter rejects files whose production window is open.
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var file = Path.Combine(sourceRoot, $"{yesterday:yyyyMMdd}.csv");
        File.WriteAllText(file, "record_id,reading\n1,42\n2,43\n");
        File.SetLastWriteTimeUtc(file, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        return sourceRoot;
    }

    private static async Task SaveAndPublishAsync(ApiGateway api, WorkflowDefinition definition)
    {
        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId }));
    }

    private static Task<bool> ImportBindingAsync(ApiGateway api, string csv) =>
        api.ExecuteAsync("industrial/import-bindings", new JsonObject { ["csv"] = csv }).AsTask();

    private static async Task RegisterTargetAsync(ApiGateway api, string id) =>
        Assert.True(await api.ExecuteAsync("industrial/save-resource", new JsonObject
        {
            ["id"] = id, ["name"] = id, ["type"] = "test-target", ["provider"] = "local",
            ["config"] = new JsonObject(), ["enabled"] = true,
        }));

    private static IRecordSink SuccessSink(string id) =>
        new DelegateSink(id, (_, records, key, ct) => DrainAsync(id, records, key, ct));

    private static async ValueTask<DeliveryResultRef> DrainAsync(
        string id, IAsyncEnumerable<RecordEnvelope> records, string key, CancellationToken ct)
    {
        var count = 0;
        await foreach (var _ in records.WithCancellation(ct)) count++;
        return new DeliveryResultRef(key, id, count);
    }

    private static Runtime CreateRuntime(
        string root, string sourceRoot, out ApiGateway api, out ICollectionLedger ledger,
        Func<string, IRecordSink>? createSink = null)
    {
        var runtime = new Runtime();
        var console = new ConsoleHost(runtime);
        var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        runtime.LoadAsync(new ConsoleCapabilitiesComponent(console)).GetAwaiter().GetResult();
        runtime.LoadAsync(new TimerExtensionComponent()).GetAwaiter().GetResult();
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "true",
            ["sourceRoot"] = sourceRoot, ["resourceId"] = "source-1",
            ["schedulerIntervalSeconds"] = "3600",
        });
        if (createSink is not null)
        {
            registry.RegisterComponentFactory("test.sinks", _ =>
                new SinkFactoriesComponent([new TestSinkFactory(createSink)]));
        }
        var types = IndustrialComponents.Types.ToList();
        if (createSink is not null) types.Insert(types.IndexOf("industrial.elsa"), "test.sinks");
        registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
        return runtime;
    }

    private sealed class TestSinkFactory(Func<string, IRecordSink> create) : IRecordSinkFactory
    {
        public string ResourceType => "test-target";

        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default) =>
            ValueTask.FromResult(create(resource.Id));
    }

    private sealed class DelegateSink(
        string targetId,
        Func<string, IAsyncEnumerable<RecordEnvelope>, string, CancellationToken,
            ValueTask<DeliveryResultRef>> write) : IRecordSink
    {
        public string TargetId => targetId;

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            write(collectionFileId, records, idempotencyKey, ct);
    }

    private sealed class SinkFactoriesComponent(IReadOnlyList<IRecordSinkFactory> factories) : Component
    {
        public override string Name => "test.binding-integrity-sinks";

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
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "industrial-run-integrity-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
