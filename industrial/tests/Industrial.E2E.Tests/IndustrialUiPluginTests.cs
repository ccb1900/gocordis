using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Host;
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

        var config = new Dictionary<string, object?>
        {
            ["root"] = temp.Root,
            ["allowDevelopmentKey"] = "true",
            ["seedResource"] = "true",
            ["sourceRoot"] = temp.SourceRoot,
        };
        IndustrialComponents.RegisterFactories(registry, config);
        var declarations = IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray();
        PluginManifest[] WithUi(bool enabled) => declarations
            .Select(declaration => declaration.Id == "industrial.ui"
                ? declaration with { Enabled = enabled }
                : declaration).ToArray();
        await registry.ApplyAsync(declarations);

        Assert.Equal(9, console.Ui.Pages().Count);
        Assert.Contains(console.Ui.Pages(), page => page.Id == "industrial.designer");
        // Pages/routes/navigation/data are code contributions of this plugin;
        // the browser bundle is a UI resource of the same PACKAGE and is bound
        // by UiPackageBinder in the packaged product path, not here.
        Assert.Equal(9, console.Ui.Routes().Count(route => route.Owner.ComponentId == "industrial-ui"));
        Assert.Equal(9, console.Ui.Navigations().Count(nav => nav.Owner.ComponentId == "industrial-ui"));
        Assert.NotNull(console.Api.Query("industrial/dashboard"));
        Assert.NotNull(console.Api.Query("industrial/nodes"));
        Assert.NotNull(console.Api.Query("industrial/workflow-definitions"));
        var nodesWire = Assert.IsType<JsonArray>(console.Api.Query("industrial/nodes"));
        var csv = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.reader.csv");
        Assert.Equal("FileBatch", csv!["inputs"]![0]!["type"]!.GetValue<string>());
        var source = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.filesystem.local");
        Assert.Equal("filesystem.local", source!["configurationSchema"]!["resourceType"]!.GetValue<string>());
        Assert.Equal("resource", source["configurationSchema"]!["properties"]!["resourceId"]!["format"]!.GetValue<string>());
        var mapping = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.transform.mapping");
        Assert.Equal("object", mapping!["configurationSchema"]!["properties"]!["mapping"]!["type"]!.GetValue<string>());
        var validation = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.transform.validate");
        Assert.Equal("array", validation!["configurationSchema"]!["properties"]!["required"]!["type"]!.GetValue<string>());
        var target = Assert.Single(nodesWire, node => node?["nodeTypeId"]?.GetValue<string>() == "industrial.sink.sqlite");
        Assert.NotNull(target!["configurationSchema"]!["properties"]!["targetResourceId"]);
        Assert.Null(target["configurationSchema"]!["properties"]!["resourceId"]);
        Assert.NotNull(console.Api.Query("industrial/credentials"));
        Assert.IsType<JsonArray>(console.Api.Query("industrial/dashboard-rows"));
        var designer = Assert.Single(console.Ui.Pages(), page => page.Id == "industrial.designer");
        Assert.True(designer.Views is JsonArray, "views must be a declarative block array");
        Assert.Contains(designer.Views!.AsArray(), block =>
            block?["kind"]?.GetValue<string>() == "industrial-workflow-canvas");
        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        Assert.Equal(13, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync(WithUi(false));
        Assert.DoesNotContain(console.Ui.Pages(), page => page.Owner.ComponentId == "industrial-ui");
        Assert.DoesNotContain(console.Ui.Routes(), route => route.Owner.ComponentId == "industrial-ui");
        Assert.DoesNotContain(console.Ui.Navigations(), nav => nav.Owner.ComponentId == "industrial-ui");
        Assert.Null(console.Api.Query("industrial/dashboard"));
        Assert.Equal(13, (await catalog.ListAsync()).Count);

        await registry.ApplyAsync(WithUi(true));
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

        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = temp.Root,
            ["allowDevelopmentKey"] = "true",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());

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

        // The guard is a persisted-data invariant: unbinding the workflow
        // plugin or looking only at the current draft must not weaken it.
        resources.UnbindWorkflows();
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.RemoveAsync("guarded").AsTask());
        await workflows.SaveDraftAsync(new WorkflowDefinition(
            "guarded-target-flow", "Guarded target flow", 1,
            [new WorkflowNode("sink", "guarded.source",
                new Dictionary<string, object?> { ["targetResourceId"] = "guarded" }, 0, 0)],
            [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.RemoveAsync("guarded").AsTask());
    }

    [Fact]
    public async Task ResourceAndCredentialCommandsManageBusinessData()
    {
        using var temp = new TempDir();
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = temp.Root,
            ["allowDevelopmentKey"] = "true",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());

        Assert.True(await console.Api.ExecuteAsync("industrial/save-credential", new JsonObject
        {
            ["id"] = "cred-1", ["name"] = "Share", ["kind"] = "username-password", ["secret"] = "first",
        }));
        Assert.True(await console.Api.ExecuteAsync("industrial/save-resource", new JsonObject
        {
            ["id"] = "resource-1", ["type"] = "filesystem.local", ["name"] = "Share",
            ["provider"] = "manual", ["enabled"] = true, ["credentialId"] = "cred-1",
            ["config"] = new JsonObject { ["root"] = temp.SourceRoot },
        }));
        var resources = runtime.RequireCapability(IndustrialKeys.ResourceRegistry);
        var credentials = runtime.RequireCapability(IndustrialKeys.CredentialVault);
        Assert.Equal("cred-1", (await resources.GetAsync("resource-1"))!.CredentialId);
        Assert.Equal("first", await credentials.RevealAsync("cred-1"));

        Assert.True(await console.Api.ExecuteAsync("industrial/rotate-credential", new JsonObject
        {
            ["id"] = "cred-1", ["secret"] = "second",
        }));
        Assert.Equal("second", await credentials.RevealAsync("cred-1"));

        Assert.True(await console.Api.ExecuteAsync("industrial/save-machine", new JsonObject
        {
            ["id"] = "machine-1", ["name"] = "PLC-1", ["endpoint"] = "opc.tcp://plc1",
            ["credentialId"] = "cred-1", ["enabled"] = true,
        }));
        var machines = runtime.RequireCapability(IndustrialKeys.MachineRegistry);
        Assert.Equal("PLC-1", (await machines.GetAsync("machine-1"))!.Name);
        var machineRows = Assert.IsType<JsonArray>(console.Api.Query("industrial/machines"));
        Assert.Single(machineRows);
        Assert.True(await console.Api.ExecuteAsync("industrial/delete-machine", new JsonObject
        {
            ["id"] = "machine-1",
        }));
        Assert.Null(await machines.GetAsync("machine-1"));

        Assert.True(await console.Api.ExecuteAsync("industrial/delete-resource", new JsonObject
        {
            ["id"] = "resource-1",
        }));
        Assert.Null(await resources.GetAsync("resource-1"));
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
