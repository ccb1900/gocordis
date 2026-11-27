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

/// <summary>Scale gate for the single binding scheduler: 800 independently
/// scheduled bindings, a hard concurrency ceiling, and one bad binding that
/// must fail without stopping the other 799.</summary>
public sealed class BindingSchedulerTests
{
    [Fact]
    public async Task EightHundredBindingsScheduleWithBoundedConcurrencyAndIsolatedFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-binding-scheduler-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "recent.csv"),
            "record_id,reading\n1,42\n");
        try
        {
            await using var runtime = CreateRuntime(root, sourceRoot, concurrency: 16,
                out var api, out var nodes, out var ledger, out var bindings);
            await nodes.RegisterAsync(new NodeDescriptor(
                "sched.source", "Source", "Source", "source", "Folder", "1.0.0", [],
                [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));

            var definition = new WorkflowDefinition(
                "sched-template", "Scheduler template", 1,
                [new WorkflowNode("src", "sched.source",
                    new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0)],
                [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "sched-template" }));

            Assert.True(await api.ExecuteAsync("industrial/import-file-profiles", new JsonObject
            {
                ["csv"] = "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n" +
                          "p-sched,Profile,*.csv,csv,,true,utf-8,value=reading,value\n",
            }));
            const string failingBinding = "b-sched-417";
            var csv = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                      "sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                      string.Join("\n", Enumerable.Range(0, 800).Select(index =>
                          $"b-sched-{index},Binding {index},machine-{index},sched-template,1," +
                          $"p-sched,{(index == 417 ? "missing-source" : "source-1")}," +
                          "target-1,schedule,*/1 * * * *")) + "\n";
            Assert.True(await api.ExecuteAsync("industrial/import-bindings",
                new JsonObject { ["csv"] = csv }));
            Assert.Equal(800, (await bindings.ListAsync()).Count);

            // First tick only installs scan positions; it must not replay history.
            Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
            var initial = Assert.IsType<JsonObject>(api.Query("industrial/binding-scheduler-status"));
            Assert.Equal(800, initial["scanned"]!.GetValue<int>());
            Assert.Equal(0, initial["fired"]!.GetValue<int>());

            // Move every scan position just before the latest minute boundary:
            // exactly one occurrence becomes due for every binding.
            var now = DateTimeOffset.UtcNow;
            var lastOccurrence = new DateTimeOffset(now.Year, now.Month, now.Day,
                now.Hour, now.Minute, 0, TimeSpan.Zero);
            foreach (var binding in await bindings.ListAsync())
            {
                await ledger.SetScheduleCheckpointAsync(
                    BindingSchedule.FireWorkflowId(binding.Id), BindingSchedule.FireVersion,
                    lastOccurrence.AddSeconds(-1), 0);
            }

            Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
            var tick = Assert.IsType<JsonObject>(api.Query("industrial/binding-scheduler-status"));
            Assert.Equal(800, tick["scanned"]!.GetValue<int>());
            Assert.Equal(800, tick["fired"]!.GetValue<int>());
            Assert.Equal(799, tick["succeeded"]!.GetValue<int>());
            Assert.Equal(1, tick["failed"]!.GetValue<int>());
            var peak = tick["peakInFlight"]!.GetValue<int>();
            Assert.InRange(peak, 2, 16);

            var fires = (await ledger.ListScheduleFiresAsync())
                .Where(fire => BindingSchedule.BindingIdFromFireWorkflowId(fire.WorkflowId) is not null)
                .ToArray();
            Assert.Equal(800, fires.Length);
            Assert.Equal(799, fires.Count(fire => fire.Status == ScheduleFireStatus.Succeeded));
            var failed = Assert.Single(fires, fire => fire.Status == ScheduleFireStatus.Failed);
            Assert.Equal(failingBinding,
                BindingSchedule.BindingIdFromFireWorkflowId(failed.WorkflowId));
            Assert.NotNull(failed.Error);
            Assert.Contains("missing-source", failed.Error!);

            var status = Assert.IsType<JsonObject>(api.Query("industrial/binding-status"));
            var reported = status["scheduledFires"]!.AsArray()
                .Single(node => node!["bindingId"]!.GetValue<string>() == failingBinding)!;
            Assert.Equal(ScheduleFireStatus.Failed, reported["status"]!.GetValue<string>());
            Assert.Contains("missing-source", reported["error"]!.GetValue<string>());

            // Pausing a binding removes only that binding from the next tick.
            Assert.True(await api.ExecuteAsync("industrial/set-binding-enabled",
                new JsonObject { ["id"] = "b-sched-0", ["enabled"] = false }));
            Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
            tick = Assert.IsType<JsonObject>(api.Query("industrial/binding-scheduler-status"));
            Assert.Equal(799, tick["scanned"]!.GetValue<int>());

            Assert.True(await api.ExecuteAsync("industrial/set-binding-enabled",
                new JsonObject { ["id"] = "b-sched-0", ["enabled"] = true }));
            Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));
            tick = Assert.IsType<JsonObject>(api.Query("industrial/binding-scheduler-status"));
            Assert.Equal(800, tick["scanned"]!.GetValue<int>());

            // Explicit backfill still works for a healthy binding, and the bad
            // binding stays independently diagnosable.
            var runsBefore = (await ledger.ListRunsAsync()).Count;
            Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "b-sched-1", ["lookbackDays"] = 7,
            }));
            Assert.True((await ledger.ListRunsAsync()).Count > runsBefore);
            Assert.Contains("missing-source",
                (await ledger.ListScheduleFiresAsync()).Single(fire => fire.Key == failed.Key).Error);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(
        string root, string sourceRoot, int concurrency,
        out ApiGateway api, out INodeCatalog nodes, out ICollectionLedger ledger,
        out ICollectionBindingRegistry bindings)
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
            ["bindingConcurrency"] = concurrency.ToString(),
            ["schedulerIntervalSeconds"] = "3600",
        });
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        nodes = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
        bindings = runtime.RequireCapability(IndustrialKeys.CollectionBindings);
        return runtime;
    }
}
