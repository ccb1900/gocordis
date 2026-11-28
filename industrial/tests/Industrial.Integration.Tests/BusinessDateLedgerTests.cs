using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Business dates are independent of WorkflowRun: the runtime plans
/// source+date work, confirms NoData explicitly, and does not re-collect a
/// terminal date unless a known incomplete row or explicit catch-up requires it.
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
    }

    [Fact]
    public async Task BindingRunRecordsBusinessDateOutcomes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-business-dates-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{today:yyyyMMdd}.csv"),
            "record_id,reading\n1,42\n");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{today.AddDays(-1):yyyyMMdd}.csv"),
            "record_id,reading\n2,43\n");
        try
        {
            await using var runtime = CreateRuntime(root, sourceRoot, out var api, out var ledger);
            var definition = new WorkflowDefinition(
                "date-template", "Date template", 1,
                [new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0)],
                [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "date-template" }));
            Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                          "sourceResourceId,targetResourceIds,triggerKind,cron,parameters\n" +
                          "date-binding,Date binding,date-machine,date-template,1,profile,source-1," +
                          "target,schedule,0 2 * * *,catchUpDays=3\n",
            }));
            Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "date-binding", ["lookbackDays"] = 3,
            }));

            var dates = await ledger.ListDatesAsync();
            Assert.Contains(dates, date => date.BusinessDate == today &&
                date.Status == CollectionDateStatus.Succeeded && date.Files == 1);
            Assert.Contains(dates, date => date.BusinessDate == today.AddDays(-1) &&
                date.Status == CollectionDateStatus.Succeeded && date.Files == 1);
            Assert.DoesNotContain(dates, date => date.BusinessDate == today.AddDays(-2));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static CollectionDateState State(string bindingId, DateOnly date, string status) =>
        new($"{bindingId}|{date:yyyy-MM-dd}", bindingId, date, status, 1, null, null, null, 0, null);

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
}
