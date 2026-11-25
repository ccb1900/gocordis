using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>UI pages, API contributions and node descriptions are Cordis plugin
/// effects. Enabling/disabling a plugin must add/withdraw them without residue.</summary>
public sealed class IndustrialUiPluginTests
{
    [Fact]
    public async Task UiContributionsFollowPluginLifecycleAndNodeCatalogIsIndependent()
    {
        using var temp = new TempDir();
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

        registry.RegisterComponentFactory("industrial.domain", manifest =>
            new IndustrialDomainComponent(manifest.Config));
        registry.RegisterComponentFactory("industrial.nodes", _ => new IndustrialNodesComponent());
        registry.RegisterComponentFactory("industrial.ui", _ => new IndustrialUiComponent());

        var domain = Decl("industrial.domain", new Dictionary<string, object?> { ["root"] = temp.Root, ["allowDevelopmentKey"] = "true" });
        var nodes = Decl("industrial.nodes");
        var ui = Decl("industrial.ui");
        await registry.ApplyAsync([domain, nodes, ui]);

        Assert.Equal(9, console.Ui.Pages().Count);
        Assert.Contains(console.Ui.Pages(), page => page.Id == "industrial.designer");
        // One plugin identity owns the page, its data, navigation, route AND
        // its client module; disabling it withdraws all of them together.
        Assert.Contains(console.Ui.ClientModules(), module => module.Name == "industrial-designer");
        Assert.Equal(9, console.Ui.Routes().Count(route => route.Owner.ComponentId == "industrial-ui"));
        Assert.Equal(9, console.Ui.Navigations().Count(nav => nav.Owner.ComponentId == "industrial-ui"));
        Assert.NotNull(console.Api.Query("industrial/dashboard"));
        Assert.NotNull(console.Api.Query("industrial/nodes"));
        Assert.NotNull(console.Api.Query("industrial/workflow-definitions"));
        var nodesWire = Assert.IsType<JsonArray>(console.Api.Query("industrial/nodes"));
        var csv = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.reader.csv");
        Assert.Equal("FileBatch", csv!["inputs"]![0]!["type"]!.GetValue<string>());
        Assert.NotNull(console.Api.Query("industrial/credentials"));
        Assert.IsType<JsonArray>(console.Api.Query("industrial/dashboard-rows"));
        var designer = Assert.Single(console.Ui.Pages(), page => page.Id == "industrial.designer");
        Assert.True(designer.Views is JsonArray, "views must be a declarative block array");
        Assert.Contains(designer.Views!.AsArray(), block =>
            block?["kind"]?.GetValue<string>() == "industrial-workflow-canvas");
        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        Assert.Equal(12, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync([domain, nodes, ui with { Enabled = false }]);
        Assert.DoesNotContain(console.Ui.Pages(), page => page.Owner.ComponentId == "industrial-ui");
        Assert.DoesNotContain(console.Ui.Routes(), route => route.Owner.ComponentId == "industrial-ui");
        Assert.DoesNotContain(console.Ui.Navigations(), nav => nav.Owner.ComponentId == "industrial-ui");
        Assert.DoesNotContain(console.Ui.ClientModules(), module => module.Name == "industrial-designer");
        Assert.Null(console.Api.Query("industrial/dashboard"));
        Assert.Equal(12, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync([domain, nodes, ui]);
        Assert.Equal(9, console.Ui.Pages().Count);
        Assert.NotNull(console.Api.Query("industrial/dashboard"));
    }

    [Fact]
    public async Task RunCollectionCommandUsesTheSameLedgerAsTheDomainPlugin()
    {
        using var temp = new TempDir();
        var sample = Path.Combine(temp.SourceRoot, "recent.csv");
        File.WriteAllText(sample, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(sample, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

        registry.RegisterComponentFactory("industrial.domain", manifest =>
            new IndustrialDomainComponent(manifest.Config));
        registry.RegisterComponentFactory("industrial.ui", _ => new IndustrialUiComponent());
        await registry.ApplyAsync([
            Decl("industrial.domain", new Dictionary<string, object?> { ["root"] = temp.Root, ["allowDevelopmentKey"] = "true" }),
            Decl("industrial.ui"),
        ]);

        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        await resources.RegisterAsync(new ResourceDefinition(
            "source-1", "filesystem.local", "Source", "local",
            new Dictionary<string, object?> { ["root"] = temp.SourceRoot },
            null, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        var executed = await console.Api.ExecuteAsync("industrial/run-collection", new JsonObject
        {
            ["workflowId"] = "workflow-1",
            ["resourceId"] = "source-1",
        });
        Assert.True(executed);
        await using var ledgerDb = Industrial.Persistence.IndustrialStore.Create(
            Path.Combine(temp.Root, "industrial-ledger.db"));
        var files = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            ledgerDb.CollectionFiles.AsNoTracking());
        Assert.Single(files);
        Assert.Equal("Succeeded", files[0].Status);
    }

    [Fact]
    public async Task SplitPluginsKeepReferencedResourceDeletionGuard()
    {
        using var temp = new TempDir();
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("resource", manifest =>
            new IndustrialResourceComponent(manifest.Config));
        registry.RegisterComponentFactory("credential", manifest =>
            new IndustrialCredentialComponent(manifest.Config));
        registry.RegisterComponentFactory("workflow", manifest =>
            new IndustrialWorkflowComponent(manifest.Config));
        await registry.ApplyAsync([
            Decl("resource", new Dictionary<string, object?>
            {
                ["root"] = temp.Root,
                ["allowDevelopmentKey"] = "true",
            }),
            Decl("credential", new Dictionary<string, object?> { ["allowDevelopmentKey"] = "true" }),
            Decl("workflow", new Dictionary<string, object?> { ["root"] = temp.Root }),
        ]);

        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var workflows = runtime.RequireCapability(IndustrialKeys.WorkflowRegistry);
        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        await resources.RegisterAsync(new ResourceDefinition(
            "guarded", "filesystem.local", "Guarded", "local",
            new Dictionary<string, object?>(), null, 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        await catalog.RegisterAsync(new NodeDescriptor(
            "guarded.source", "Guarded", "Guarded", "source", "Folder", "1.0.0",
            [], [], new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
        await workflows.SaveDraftAsync(new WorkflowDefinition(
            "guarded-flow", "Guarded flow", 1,
            [new WorkflowNode("source", "guarded.source", new Dictionary<string, object?> { ["resourceId"] = "guarded" }, 0, 0)],
            [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["guarded"], new Dictionary<string, object?>()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.RemoveAsync("guarded").AsTask());
    }

    private static PluginManifest Decl(string id, IReadOnlyDictionary<string, object?>? config = null) =>
        new(id, id, true, config ?? new Dictionary<string, object?>(), [], []);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-ui-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            Directory.CreateDirectory(SourceRoot);
        }

        public string Root { get; }
        public string SourceRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
