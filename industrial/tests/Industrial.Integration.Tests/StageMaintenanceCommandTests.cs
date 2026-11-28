using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Host;
using Industrial.Persistence;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>The maintenance command must use the ledger to decide stage
/// lifecycle: a terminal instance is reclaimed after retention, while a run
/// that can still retry keeps its committed spool. Failures are reported in
/// snapshot-status instead of escaping the command.</summary>
public sealed class StageMaintenanceCommandTests
{
    [Fact]
    public async Task StageMaintenanceUsesLedgerTerminalInstancesAndReportsCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-stage-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
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
                ["root"] = root,
                ["allowDevelopmentKey"] = "true",
                ["stageRetentionDays"] = "7",
            });
            await registry.ApplyAsync(IndustrialComponents.Types
                .Select(type => IndustrialComponents.Declaration(type)).ToArray());

            var ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
            var store = (StageOutputStore)runtime.RequireCapability(IndustrialKeys.StageOutputStore);
            var terminal = await SpoolForRunAsync(ledger, store, root, "terminal-instance",
                "terminal.csv", terminal: true);
            var active = await SpoolForRunAsync(ledger, store, root, "active-instance",
                "active.csv", terminal: false);
            Backdate(root, "terminal-instance");
            Backdate(root, "active-instance");

            Assert.True(await console.Api.ExecuteAsync("industrial/stage-maintenance", null));

            var status = Assert.IsType<JsonObject>(console.Api.Query("industrial/snapshot-status"));
            var stage = Assert.IsType<JsonObject>(status["stage"]);
            Assert.Equal(1, stage["rowsDeleted"]!.GetValue<int>());
            // Spool plus its integrity sidecar are two files.
            Assert.Equal(2, stage["filesDeleted"]!.GetValue<int>());
            Assert.True(stage["deletedBytes"]!.GetValue<long>() > 0);
            Assert.Empty(stage["failures"]!.AsArray());
            Assert.Null(stage["error"]);
            Assert.False(File.Exists(terminal.SpoolPath));
            Assert.True(File.Exists(active.SpoolPath));
            Assert.Empty(await store.ListAsync("terminal-instance"));
            Assert.Single(await store.ListAsync("active-instance"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static async Task<DatasetRef> SpoolForRunAsync(
        ICollectionLedger ledger, StageOutputStore store, string root, string instanceId,
        string name, bool terminal)
    {
        var run = await ledger.StartAsync("workflow-1");
        Assert.True(await ledger.SetRunInstanceAsync(run.Id, instanceId));
        await ledger.BeginRunAsync(run.Id);
        if (terminal) await ledger.CompleteRunAsync(run.Id, CollectionStatus.Succeeded, null);

        var path = Path.Combine(root, name);
        await File.WriteAllTextAsync(path, "record_id,reading\n1,10\n");
        var file = new DiscoveredFile(FileIdentityFactory.Create(path, root, "source-1"),
            path, DateTimeOffset.UtcNow, "source-1");
        return await store.SpoolAsync(instanceId, "read", "fp-" + name,
            new CsvRecordReader().ReadAsync(file));
    }

    private static void Backdate(string root, string instanceId)
    {
        using var db = IndustrialStore.CreateStages(root);
        foreach (var row in db.StageOutputs.Where(row => row.InstanceId == instanceId))
            row.CommittedAt = DateTimeOffset.UtcNow.AddDays(-8);
        db.SaveChanges();
    }
}
