using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using Elsa.Workflows.Models;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Platform;
using Gocordis.Builtin.WebConsole;
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
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
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
    public async Task PublishCommandUsesElsaConverterInProductChain()
    {
        using var temp = new TempDir();
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

        registry.RegisterComponentFactory("domain", manifest =>
            new Industrial.Plugins.IndustrialDomainComponent(manifest.Config));
        registry.RegisterComponentFactory("nodes", _ => new Industrial.Plugins.IndustrialNodesComponent());
        registry.RegisterComponentFactory("elsa", _ => new IndustrialElsaComponent());
        await registry.ApplyAsync([
            new PluginManifest("domain", "domain", true,
                new Dictionary<string, object?> { ["allowDevelopmentKey"] = "true" }, [], []),
            new PluginManifest("nodes", "nodes", true, new Dictionary<string, object?>(), [], []),
            new PluginManifest("elsa", "elsa", true, new Dictionary<string, object?>(), [], []),
        ]);

        var workflows = runtime.RequireCapability(IndustrialKeys.WorkflowRegistry);
        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.source", "Source", "Source", "source", "Folder", "1.0.0",
            [], [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.reader", "Reader", "Reader", "reader", "FileText", "1.0.0",
            [new NodePort("input", Industrial.Contracts.PortType.FileBatch, true)],
            [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []));
        await workflows.SaveDraftAsync(new WorkflowDefinition(
            "publish-me", "Publish me", 1,
            [
                new WorkflowNode("source", "n.source", new Dictionary<string, object?>(), 0, 0),
                new WorkflowNode("reader", "n.reader", new Dictionary<string, object?>(), 100, 0),
            ],
            [new WorkflowEdge("source", "files", "reader", "input")],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));

        var executed = await console.Api.ExecuteAsync("industrial/publish-workflow", new JsonObject
        {
            ["workflowId"] = "publish-me",
        });
        Assert.True(executed);
        Assert.NotNull(await workflows.GetPublishedAsync("publish-me"));
    }

    [Fact]
    public async Task ElsaRunsWorkflowGraphFromProductCommand()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Root, "source"));
        File.WriteAllText(Path.Combine(temp.Root, "source", "recent.csv"),
            "record_id,reading\n9,99\n");
        File.SetLastWriteTimeUtc(Path.Combine(temp.Root, "source", "recent.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

        registry.RegisterComponentFactory("domain", manifest =>
            new Industrial.Plugins.IndustrialDomainComponent(manifest.Config));
        registry.RegisterComponentFactory("nodes", _ => new Industrial.Plugins.IndustrialNodesComponent());
        registry.RegisterComponentFactory("elsa", _ => new IndustrialElsaComponent());
        await registry.ApplyAsync([
            new PluginManifest("domain", "domain", true,
                new Dictionary<string, object?>
                {
                    ["allowDevelopmentKey"] = "true",
                    ["root"] = temp.Root,
                    ["seedResource"] = "true",
                    ["sourceRoot"] = Path.Combine(temp.Root, "source"),
                    ["resourceId"] = "source-1",
                }, [], []),
            new PluginManifest("nodes", "nodes", true, new Dictionary<string, object?>(), [], []),
            new PluginManifest("elsa", "elsa", true, new Dictionary<string, object?>(), [], []),
        ]);

        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        var workflows = runtime.RequireCapability(IndustrialKeys.WorkflowRegistry);
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.source", "Source", "Source", "source", "Folder", "1.0.0",
            [], [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.reader", "Reader", "Reader", "reader", "FileText", "1.0.0",
            [new NodePort("input", Industrial.Contracts.PortType.FileBatch, true)],
            [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []));
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.sink", "Sink", "Sink", "sink", "Database", "1.0.0",
            [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)], [],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []));
        var definitionToSave = new WorkflowDefinition(
            "elsa-flow", "Elsa flow", 1,
            [
                new WorkflowNode("source", "n.source", new Dictionary<string, object?>(), 0, 0),
                new WorkflowNode("reader", "n.reader", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("sink", "n.sink", new Dictionary<string, object?>(), 200, 0),
            ],
            [
                new WorkflowEdge("source", "files", "reader", "input"),
                new WorkflowEdge("reader", "records", "sink", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());
        var saved = await console.Api.ExecuteAsync("industrial/save-workflow",
            System.Text.Json.JsonSerializer.SerializeToNode(definitionToSave));
        Assert.True(saved, "designer save command failed");

        var executed = await console.Api.ExecuteAsync("industrial/run-workflow", new JsonObject
        {
            ["workflowId"] = "elsa-flow",
            ["resourceId"] = "source-1",
        });
        var executionProbe = console.Api.Query("industrial/execution") as JsonObject;
        Assert.True(executed, executionProbe?["error"]?.ToString() ?? "no error detail");
        var execution = (JsonObject)console.Api.Query("industrial/execution")!;
        Assert.Equal("Completed", execution["status"]!.GetValue<string>());
        Assert.Equal(3, execution["executed"]!.AsArray().Count);
        await using var target = Industrial.Persistence.IndustrialStore.CreateTarget(Path.Combine(temp.Root, "industrial-target.db"));
        Assert.Single(target.Records.AsNoTracking().ToList());
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
        Assert.Contains("Edges", json, StringComparison.Ordinal);
        Assert.Contains("reader", json, StringComparison.Ordinal);
    }
    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "industrial-elsa-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}