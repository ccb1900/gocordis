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
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Business dates are independent of WorkflowRun: the runtime plans
/// source+date work, confirms NoData explicitly, and never re-collects a
/// terminal date unless a known incomplete row or an explicit catch-up asks
/// for it. The boundary is the last COMPLETED local business date, not UTC
/// "today", whose production window is still open.
/// </summary>
public sealed class BusinessDateLedgerTests
{
    [Fact]
    public void PlannerUsesHolesKnownFailuresAndBoundedCatchUp()
    {
        var target = new DateOnly(2026, 11, 18);
        var dates = new[]
        {
            State("b", new DateOnly(2026, 11, 15), CollectionDateStatus.Succeeded),
            State("b", new DateOnly(2026, 11, 16), CollectionDateStatus.Failed),
            State("b", new DateOnly(2026, 11, 17), CollectionDateStatus.Succeeded),
            State("other", new DateOnly(2026, 11, 10), CollectionDateStatus.Failed),
        };
        var planned = BusinessDatePlanner.Plan(dates, "b", target, 7);
        Assert.Equal(new[]
        {
            new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 18),
        }, planned);

        // NoData is not terminal: even a legacy row must be re-probed.
        var noData = new[] { State("b", target.AddDays(-1), CollectionDateStatus.NoData) };
        Assert.Equal(new[]
        {
            target.AddDays(-1), target,
        }, BusinessDatePlanner.Plan(noData, "b", target, 1));

        var bounded = BusinessDatePlanner.Plan([], "b", target, 3);
        Assert.Equal(3, bounded.Count);
        Assert.Equal(target.AddDays(-2), bounded[0]);
        Assert.Equal(30, BusinessDatePlanner.DefaultCatchUpDays);
        Assert.Equal(30, BusinessDatePlanner.MaxAutomaticCatchUpDays);
        Assert.Equal(366, BusinessDatePlanner.MaxExplicitHistoryDays);
    }

    [Fact]
    public void BusinessTimezoneBoundaryUsesLocalYesterdayAndValidatesOverrides()
    {
        var now = new DateTimeOffset(2026, 11, 28, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 27),
            BusinessDatePlanner.LastCompletedBusinessDate(TimeZoneInfo.Utc, now));
        var ahead = TimeZoneInfo.CreateCustomTimeZone(
            "business-ahead", TimeSpan.FromHours(13), "Ahead", "Ahead");
        Assert.Equal(new DateOnly(2026, 11, 28),
            BusinessDatePlanner.LastCompletedBusinessDate(ahead, now));

        Assert.Equal(TimeZoneInfo.Utc.Id,
            BusinessDatePlanner.ResolveBusinessZone(null).Id);
        Assert.Equal(TimeZoneInfo.Utc.Id,
            BusinessDatePlanner.ResolveBusinessZone(new Dictionary<string, object?>
            {
                ["timezone"] = " ",
            }).Id);
        Assert.Throws<InvalidOperationException>(() =>
            BusinessDatePlanner.ResolveBusinessZone(new Dictionary<string, object?>
            {
                ["timezone"] = "Mars/Olympus",
            }));
        Assert.True(BusinessDatePlanner.TryParseOverride("2026-11-01", out var parsed));
        Assert.Equal(new DateOnly(2026, 11, 1), parsed);
        Assert.False(BusinessDatePlanner.TryParseOverride(null, out _));
        Assert.Throws<InvalidOperationException>(() =>
            BusinessDatePlanner.TryParseOverride("today", out _));
    }

    [Fact]
    public async Task AutomaticBindingRunNeverCollectsTheOpenBusinessDate()
    {
        using var temp = new TempDir();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        await WriteDatedFileAsync(temp.SourceRoot, today);
        await WriteDatedFileAsync(temp.SourceRoot, yesterday);
        await using var runtime = CreateRuntime(temp.Root, temp.SourceRoot, out var api, out var ledger);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("date-template"));
        Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
        {
            ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                      "sourceResourceId,targetResourceIds,triggerKind,cron,timezone\n" +
                      "date-binding,Date binding,date-machine,date-template,1,profile,source-1," +
                      "target,schedule,* * * * *,UTC\n",
        }));
        await ledger.SetScheduleCheckpointAsync(
            BindingSchedule.FireWorkflowId("date-binding"), BindingSchedule.FireVersion,
            DateTimeOffset.UtcNow.AddMinutes(-2), 0);
        Assert.True(await api.ExecuteAsync("industrial/binding-scheduler-tick", null));

        var dates = await ledger.ListDatesAsync();
        Assert.DoesNotContain(dates, date => date.BusinessDate == today);
        Assert.Contains(dates, date => date.BusinessDate == yesterday &&
            date.Status == CollectionDateStatus.Succeeded && date.Files == 1);
        Assert.All(await ledger.ListAllFilesAsync(), file =>
            Assert.NotEqual(today, DateOnly.FromDateTime(file.SourceDate.UtcDateTime)));
        var values = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal(yesterday.ToString("yyyy-MM-dd"), values["BusinessDate"]!.GetValue<string>());
        Assert.Equal(TimeZoneInfo.Utc.Id, values["BusinessTimeZone"]!.GetValue<string>());
        Assert.Contains(yesterday.ToString("yyyy-MM-dd"),
            values["BusinessDates"]!.GetValue<string>().Split(','));
    }

    [Fact]
    public async Task BindingTriggerTimezoneDecidesTheCompletedDate()
    {
        using var temp = new TempDir();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var expected = BusinessDatePlanner.LastCompletedBusinessDate(zone);
        await WriteDatedFileAsync(temp.SourceRoot, expected);
        await using var runtime = CreateRuntime(temp.Root, temp.SourceRoot, out var api, out var ledger);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("zone-template"));
        Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
        {
            ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                      "sourceResourceId,targetResourceIds,triggerKind,cron,timezone\n" +
                      "zone-binding,Zone,date-machine,zone-template,1,profile,source-1," +
                      "target,manual,,America/Los_Angeles\n",
        }));
        Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "zone-binding",
        }));
        var values = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal(expected.ToString("yyyy-MM-dd"), values["BusinessDate"]!.GetValue<string>());
        Assert.Equal(zone.Id, values["BusinessTimeZone"]!.GetValue<string>());
        // This template has no delivery node, so the run is correctly Failed;
        // the assertion here is that the ledger planned the zone's completed
        // date rather than UTC/today.
        Assert.Contains(await ledger.ListDatesAsync(),
            date => date.BusinessDate == expected);
    }

    [Fact]
    public async Task ExplicitBusinessDateBackfillsOnePastDateOnly()
    {
        using var temp = new TempDir();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        var past = today.AddDays(-5);
        await WriteDatedFileAsync(temp.SourceRoot, today);
        await WriteDatedFileAsync(temp.SourceRoot, yesterday);
        await WriteDatedFileAsync(temp.SourceRoot, past);
        await using var runtime = CreateRuntime(temp.Root, temp.SourceRoot, out var api, out var ledger);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("history-template"));
        Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
        {
            ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                      "sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                      "history-binding,History,date-machine,history-template,1,profile,source-1," +
                      "target,manual,\n",
        }));

        Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "history-binding", ["businessDate"] = past.ToString("yyyy-MM-dd"),
        }));
        var dates = await ledger.ListDatesAsync();
        Assert.Equal(new[] { past }, dates.Select(date => date.BusinessDate).ToArray());
        var file = Assert.Single(await ledger.ListAllFilesAsync());
        Assert.Equal(past, DateOnly.FromDateTime(file.SourceDate.UtcDateTime));
        var values = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal(past.ToString("yyyy-MM-dd"), values["BusinessDate"]!.GetValue<string>());
        Assert.Single(values["BusinessDates"]!.GetValue<string>().Split(','));

        // An explicit date must never reopen a date whose window is still open.
        var future = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "history-binding", ["date"] = today.ToString("yyyy-MM-dd"),
            }));
        Assert.Contains("completed business date", future.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomaticWindowIsThirtyDaysAndExplicitHistoryAllowsThreeSixtySix()
    {
        using var temp = new TempDir();
        await WriteDatedFileAsync(temp.SourceRoot, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        await using var runtime = CreateRuntime(temp.Root, temp.SourceRoot, out var api, out _);
        await SaveAndPublishAsync(api, SourceOnlyTemplate("window-template"));
        Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
        {
            ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                      "sourceResourceId,targetResourceIds,triggerKind,cron,parameters\n" +
                      "b-normal,Normal,machine,window-template,1,p,source-1,target,manual,\n" +
                      "b-wide,Wide,machine,window-template,1,p,source-1,target,manual,,catchUpDays=60\n" +
                      "b-history,History,machine,window-template,1,p,source-1,target,manual,,catchUpDays=366\n" +
                      "b-over,Over,machine,window-template,1,p,source-1,target,manual,,catchUpDays=367\n",
        }));

        Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "b-normal",
        }));
        var normal = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal(30, normal["BusinessDates"]!.GetValue<string>().Split(',').Length);

        var automatic = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "b-wide",
            }));
        Assert.Contains("30", automatic.Message, StringComparison.Ordinal);
        Assert.Contains("automatic", automatic.Message, StringComparison.Ordinal);

        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40).ToString("yyyy-MM-dd");
        Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
        {
            ["bindingId"] = "b-history", ["businessDate"] = past,
        }));
        var history = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
        Assert.Equal(366, history["BusinessDates"]!.GetValue<string>().Split(',').Length);

        var over = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "b-over", ["businessDate"] = past,
            }));
        Assert.Contains("366", over.Message, StringComparison.Ordinal);
        Assert.Contains("catchUpDays", over.Message, StringComparison.Ordinal);
    }

    private static async Task WriteDatedFileAsync(string root, DateOnly date)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, $"{date:yyyyMMdd}.csv"),
            "record_id,reading\n1,42\n");
    }

    private static CollectionDateState State(string bindingId, DateOnly date, string status) =>
        new($"{bindingId}|{date:yyyy-MM-dd}", bindingId, date, status, 1, null, null, null, 0, null);

    private static async Task SaveAndPublishAsync(ApiGateway api, WorkflowDefinition definition)
    {
        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = definition.WorkflowId }));
    }

    private static WorkflowDefinition SourceOnlyTemplate(string id) => new(
        id, "Source template", 1,
        [new WorkflowNode("src", "industrial.filesystem.local",
            new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0)],
        [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        ["{{SourceResource}}"], new Dictionary<string, object?>());

    private static Runtime CreateRuntime(string root, string sourceRoot,
        out ApiGateway api, out ICollectionLedger ledger)
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
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
        return runtime;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-business-dates-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Root { get; }
        public string SourceRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
