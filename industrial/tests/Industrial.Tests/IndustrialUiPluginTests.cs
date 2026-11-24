using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
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

        Assert.Equal(6, console.Ui.Pages().Count);
        Assert.Contains(console.Ui.Pages(), page => page.Id == "industrial.designer");
        Assert.NotNull(console.Api.Query("industrial/dashboard"));
        Assert.NotNull(console.Api.Query("industrial/nodes"));
        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        Assert.Equal(10, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync([domain, nodes, ui with { Enabled = false }]);
        Assert.DoesNotContain(console.Ui.Pages(), page => page.Owner.ComponentId == "industrial-ui");
        Assert.Null(console.Api.Query("industrial/dashboard"));
        Assert.Equal(10, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync([domain, nodes, ui]);
        Assert.Equal(6, console.Ui.Pages().Count);
        Assert.NotNull(console.Api.Query("industrial/dashboard"));
    }

    [Fact]
    public async Task RunCollectionCommandUsesTheSameLedgerAsTheDomainPlugin()
    {
        using var temp = new TempDir();
        var sample = Path.Combine(temp.SourceRoot, "20260101.csv");
        File.WriteAllText(sample, "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(sample, DateTimeOffset.UtcNow.AddDays(-2).UtcDateTime);
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
