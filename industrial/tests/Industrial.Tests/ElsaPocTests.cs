using Elsa.Workflows.Models;
using Gocordis.Core;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Elsa;

namespace Industrial.Tests;

/// <summary>Phase 4 gate: Elsa 3 can execute an Activity contributed by an
/// industrial plugin, and the industrial Definition converts to a published
/// Elsa workflow without becoming a second editable source of truth.</summary>
public sealed class ElsaPocTests
{
    [Fact]
    public async Task ElsaExecutesAPluginContributedActivity()
    {
        var before = CollectFileActivity.ExecutionCount;
        var result = await ElsaPoC.ExecuteAsync("file-1");
        Assert.Equal("RunWorkflowResult", result);
        Assert.True(CollectFileActivity.ExecutionCount > before,
            "Elsa did not execute the contributed Activity");
    }

    [Fact]
    public async Task ElsaActivityCatalogIsACordisCapability()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("industrial.domain", manifest =>
            new Industrial.Plugins.IndustrialDomainComponent(manifest.Config));
        registry.RegisterComponentFactory("industrial.elsa", _ => new IndustrialElsaComponent());
        registry.RegisterComponentFactory("industrial.nodes", _ => new Industrial.Plugins.IndustrialNodesComponent());
        await registry.ApplyAsync([
            new PluginManifest("domain", "industrial.domain", true,
                new Dictionary<string, object?> { ["allowDevelopmentKey"] = "true" }, [], []),
            new PluginManifest("nodes", "industrial.nodes", true,
                new Dictionary<string, object?>(), [], []),
            new PluginManifest("elsa", "industrial.elsa", true,
                new Dictionary<string, object?>(), [], []),
        ]);
        var catalog = runtime.RequireCapability(IndustrialKeys.ElsaActivityCatalog);
        Assert.Contains(IndustrialActivityTypes.All[2], catalog.List(), StringComparer.Ordinal);
    }

    [Fact]
    public void IndustrialDefinitionConvertsToElsaDefinition()
    {
        var definition = new WorkflowDefinition(
            "daily", "Daily collection", 1,
            [
                new WorkflowNode("source", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "share" }, 0, 0),
                new WorkflowNode("reader", "industrial.reader.csv", new Dictionary<string, object?>(), 200, 0),
            ],
            [new WorkflowEdge("source", "files", "reader", "input")],
            new TriggerDefinition("schedule", "0 2 * * *", null, new Dictionary<string, object?>()),
            ["share"],
            new Dictionary<string, object?>());

        var converter = new ElsaDefinitionConverter();
        var json = converter.ToElsaJson(definition);
        Assert.Contains("daily", json, StringComparison.Ordinal);
        Assert.True(json.Length > 100);
        Assert.Contains("Identity", json, StringComparison.Ordinal);
    }
}
