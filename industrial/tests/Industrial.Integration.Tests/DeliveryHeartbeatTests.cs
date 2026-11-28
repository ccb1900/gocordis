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
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Delivery leases are kept alive on a bounded TIME interval, not on a
/// record count. Both record sinks and archive delivery use the same heartbeat;
/// a renewal that fails fences the claim and aborts the slow operation.
/// </summary>
public sealed class DeliveryHeartbeatTests
{
    [Fact]
    public async Task HeartbeatRenewsOnIntervalAndCompletesSlowOperation()
    {
        var renewals = 0;
        var cancelled = false;
        var result = await DeliveryLeaseHeartbeat.RunAsync(
            "delivery-slow",
            async token =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(220), token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    throw;
                }
                return 42;
            },
            _ =>
            {
                Interlocked.Increment(ref renewals);
                return ValueTask.FromResult(true);
            },
            TimeSpan.FromMilliseconds(60));

        Assert.Equal(42, result);
        Assert.True(renewals >= 2);
        Assert.False(cancelled);
    }

    [Fact]
    public async Task FailedRenewalCancelsTheOperationAndSurfacesTheLoss()
    {
        var cancelled = false;
        async Task Run()
        {
            await DeliveryLeaseHeartbeat.RunAsync(
                "delivery-fenced",
                async token =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        throw;
                    }
                    return 0;
                },
                _ => ValueTask.FromResult(false),
                TimeSpan.FromMilliseconds(50));
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        Assert.True(cancelled, "a lost lease must cancel the in-flight delivery");
        Assert.Contains("lost its lease", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationStillPropagatesAsCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await DeliveryLeaseHeartbeat.RunAsync(
                "delivery-cancel",
                async token =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    return 0;
                },
                _ => ValueTask.FromResult(true),
                TimeSpan.FromMilliseconds(20), cts.Token));
    }

    [Fact]
    public async Task SlowSinkDeliveryKeepsItsLeaseValidAndCompletes()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        FileDelivery? first = null;
        FileDelivery? renewed = null;
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            id => new DelegateSink(id, async (_, records, key, token) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2000), token);
                var count = 0;
                await foreach (var record in records.WithCancellation(token)) count++;
                return new DeliveryResultRef(key, id, count);
            }), leaseHeartbeatSeconds: 1);
        await SaveAndPublishAsync(api, SinkTemplate("heartbeat-template"));
        await RegisterTargetAsync(api, "target-1");
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-heartbeat,Heartbeat,machine-1,heartbeat-template,1,p,source-1,target-1,manual,,\n"));

        var run = api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "b-heartbeat",
        }).AsTask();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline && renewed is null)
        {
            var current = (await ledger.ListAllDeliveriesAsync())
                .FirstOrDefault(delivery => delivery.Status == CollectionStatus.InProgress);
            if (current is not null)
            {
                first ??= current;
                if (current.LeaseExpiresAt > first.LeaseExpiresAt) renewed = current;
            }
            await Task.Delay(50);
        }
        Assert.True(await run);
        Assert.NotNull(first);
        Assert.NotNull(renewed);
        var delivery = Assert.Single(await ledger.ListAllDeliveriesAsync());
        Assert.Equal(CollectionStatus.Succeeded, delivery.Status);
        Assert.Equal(2, delivery.Records);
    }

    [Fact]
    public async Task FailedRenewalAbortsTheSlowSinkAndLeavesTheClaimFenced()
    {
        using var temp = new TempDir();
        var sourceRoot = PrepareSource(temp.Root);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = CreateRuntime(temp.Root, sourceRoot, out var api, out var ledger,
            id => new DelegateSink(id, async (_, records, key, token) =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }
                var count = 0;
                await foreach (var record in records.WithCancellation(token)) count++;
                return new DeliveryResultRef(key, id, count);
            }), leaseHeartbeatSeconds: 1);
        await SaveAndPublishAsync(api, SinkTemplate("fenced-template"));
        await RegisterTargetAsync(api, "target-1");
        Assert.True(await ImportBindingAsync(api,
            "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
            "targetResourceIds,triggerKind,cron,parameters\n" +
            "b-fenced,Fenced,machine-1,fenced-template,1,p,source-1,target-1,manual,,\n"));

        var run = api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "b-fenced",
        }).AsTask();
        FileDelivery? held = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline && held is null)
        {
            held = (await ledger.ListAllDeliveriesAsync())
                .FirstOrDefault(delivery => delivery.Status == CollectionStatus.InProgress);
            if (held is null) await Task.Delay(50);
        }
        Assert.NotNull(held);

        // Simulate a fenced-out writer: the claim is no longer held by this
        // token, so the next heartbeat must fail and abort the slow sink.
        await using (var db = IndustrialStore.Create(Path.Combine(temp.Root, "industrial-ledger.db")))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE FileDeliveries SET Status = {0} WHERE Id = {1}",
                CollectionStatus.Failed, held.Id);
        }
        Assert.True(await run);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var after = await ledger.GetDeliveryAsync(held.Id);
        Assert.NotNull(after);
        Assert.Equal(CollectionStatus.Failed, after!.Status);
        // A failed heartbeat fences the writer and interrupts the run; it must
        // never be reported as a successful delivery.
        Assert.Equal(CollectionStatus.Interrupted, Assert.Single(await ledger.ListRunsAsync()).Status);
    }

    private static string PrepareSource(string root)
    {
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var file = Path.Combine(sourceRoot, $"{yesterday:yyyyMMdd}.csv");
        File.WriteAllText(file, "record_id,reading\n1,42\n2,43\n");
        File.SetLastWriteTimeUtc(file, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        return sourceRoot;
    }

    private static WorkflowDefinition SinkTemplate(string id) => new(
        id, "Sink template", 1,
        [
            new WorkflowNode("src", "industrial.filesystem.local",
                new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0),
            new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
            new WorkflowNode("store", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "{{TargetResource}}" }, 200, 0),
        ],
        [
            new WorkflowEdge("src", "files", "read", "input"),
            new WorkflowEdge("read", "records", "store", "records"),
        ], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["{{SourceResource}}"], new Dictionary<string, object?>());

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

    private static Runtime CreateRuntime(
        string root, string sourceRoot, out ApiGateway api, out ICollectionLedger ledger,
        Func<string, IRecordSink> createSink, int leaseHeartbeatSeconds = 60)
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
            ["leaseHeartbeatSeconds"] = leaseHeartbeatSeconds,
        });
        registry.RegisterComponentFactory("test.sinks", _ =>
            new SinkFactoriesComponent([new TestSinkFactory(createSink)]));
        var types = IndustrialComponents.Types.ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.sinks");
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
        public override string Name => "test.heartbeat-sinks";
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
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-heartbeat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
