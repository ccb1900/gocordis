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

/// <summary>Scheduler-level evidence: the real timer path materializes missed
/// cron instants, records execution failures durably and retries them once the
/// underlying resource becomes available. No event is silently dropped.</summary>
public sealed class SchedulerRecoveryTests
{
    [Fact]
    public async Task MissedScheduleFiresAreRetriedAfterFailureAndRecovery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var today = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{today:yyyyMMdd}.csv"),
            "record_id,reading\n1,42\n2,43\n3,44\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{today:yyyyMMdd}.csv"),
            today.AddHours(-1).UtcDateTime);
        var targetDb = Path.Combine(temp.Root, "target.db");

        var definition = new WorkflowDefinition(
            "scheduler-recovery-flow", "Scheduler recovery flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "late-source" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "store", "records"),
            ],
            new TriggerDefinition("schedule", "*/1 * * * *", null,
                new Dictionary<string, object?>
                {
                    ["resourceId"] = "late-source", ["timezone"] = "UTC",
                }),
            ["late-source"], new Dictionary<string, object?>());

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
            ["root"] = temp.Root,
            ["seedResource"] = "false",
            ["sourceRoot"] = sourceRoot,
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());
        var ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var api = console.Api;

        var now = DateTimeOffset.UtcNow;
        await resources.RegisterAsync(new ResourceDefinition(
            "target-a", "sqlite", "target-a", "fixture",
            new Dictionary<string, object?> { ["database"] = targetDb }, null, 0, true, now, now));
        await resources.RegisterAsync(new ResourceDefinition(
            "late-source", "filesystem.local", "late-source", "fixture",
            new Dictionary<string, object?> { ["root"] = sourceRoot },
            null, 0, true, now, now));
        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId }));

        // Simulate a host that was down across several cron instants: one fire
        // was previously attempted and failed, another was materialized but
        // never executed. Both must be picked up by the SAME scheduler path.
        var clock = DateTimeOffset.UtcNow;
        var missed = new DateTimeOffset(clock.Year, clock.Month, clock.Day,
            clock.Hour, clock.Minute, 0, TimeSpan.Zero).AddMinutes(-1);
        var failedClaim = await ledger.TryStartScheduleFireAsync(
            definition.WorkflowId, 1, missed, TimeSpan.FromMinutes(5));
        Assert.NotNull(failedClaim);
        await ledger.CompleteScheduleFireAsync(
            failedClaim!.Key, failedClaim.Token, ScheduleFireStatus.Failed, "previous execution failed");
        var pending = missed.AddMinutes(1);
        await ledger.EnsureScheduleFiresAsync(definition.WorkflowId, 1, new[] { pending });

        Assert.True(await api.ExecuteAsync("industrial/scheduler-tick", new JsonObject()));
        var recovered = (await ledger.ListScheduleFiresAsync())
            .Single(fire => fire.Key == failedClaim.Key);
        Assert.Equal(ScheduleFireStatus.Succeeded, recovered.Status);
        Assert.Equal(2, recovered.Attempts);
        Assert.Null(recovered.Error);
        Assert.Equal(ScheduleFireStatus.Succeeded, (await ledger.ListScheduleFiresAsync())
            .Single(fire => fire.DueAtUtc.ToUniversalTime().ToString("O") == pending.ToUniversalTime().ToString("O"))
            .Status);
        Assert.NotEmpty(await ledger.ListRunsAsync());

        await using var target = IndustrialStore.CreateTarget(targetDb);
        Assert.Equal(3, await target.Records.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task LowFrequencyAndSupersededVersionFiresAreRecovered()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var today = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{today:yyyyMMdd}.csv"),
            "record_id,reading\n1,42\n2,43\n3,44\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{today:yyyyMMdd}.csv"),
            today.AddHours(-1).UtcDateTime);
        var targetDb = Path.Combine(temp.Root, "target.db");

        WorkflowDefinition Definition(int version) => new(
            "weekly-flow", "Weekly flow", version,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "late-source" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "store", "records"),
            ],
            new TriggerDefinition("schedule", "0 2 * * 1", null,
                new Dictionary<string, object?>
                {
                    ["resourceId"] = "late-source", ["timezone"] = "UTC",
                }),
            ["late-source"], new Dictionary<string, object?>());

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
            ["root"] = temp.Root,
            ["seedResource"] = "false",
            ["sourceRoot"] = sourceRoot,
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());
        var ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var api = console.Api;
        var now = DateTimeOffset.UtcNow;
        await resources.RegisterAsync(new ResourceDefinition(
            "target-a", "sqlite", "target-a", "fixture",
            new Dictionary<string, object?> { ["database"] = targetDb }, null, 0, true, now, now));
        await resources.RegisterAsync(new ResourceDefinition(
            "late-source", "filesystem.local", "late-source", "fixture",
            new Dictionary<string, object?> { ["root"] = sourceRoot }, null, 0, true, now, now));

        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(Definition(1))));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = "weekly-flow" }));

        var oldCheckpoint = now.AddDays(-20);
        await ledger.SetScheduleCheckpointAsync("weekly-flow", 1, oldCheckpoint, 0);
        var pending = now.AddDays(-10);
        await ledger.EnsureScheduleFiresAsync("weekly-flow", 1, new[] { pending });
        // A Running fire whose zero-length lease has expired must be reclaimable.
        var expiredLeaseKey = (await ledger.TryStartScheduleFireAsync(
            "weekly-flow", 1, now.AddDays(-12), TimeSpan.Zero))!.Key;

        // A newer published version must not hide old-version pending fires.
        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(Definition(2))));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = "weekly-flow" }));

        Assert.True(await api.ExecuteAsync("industrial/scheduler-tick", new JsonObject()));
        var fires = await ledger.ListScheduleFiresAsync();
        Assert.All(fires, fire => Assert.Equal(ScheduleFireStatus.Succeeded, fire.Status));
        Assert.Contains(fires, fire => fire.Key == expiredLeaseKey && fire.Status == ScheduleFireStatus.Succeeded);
        Assert.Contains(fires, fire =>
            fire.DueAtUtc.ToUniversalTime().ToString("O") == pending.ToUniversalTime().ToString("O") &&
            fire.Status == ScheduleFireStatus.Succeeded);
        var (checkpoint, skipped) = await ledger.GetScheduleCheckpointAsync("weekly-flow", 2);
        Assert.NotNull(checkpoint);
        Assert.True(checkpoint!.Value > oldCheckpoint);
        Assert.True(skipped >= 0);
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"industrial-scheduler-{Guid.NewGuid():N}");
        public TempDir() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }
}

