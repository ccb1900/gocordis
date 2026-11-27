using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Deleting a resource that a published workflow or binding still
/// points at would turn a working collection into a silent failure.</summary>
public sealed class ResourceReferenceGuardTests
{
    [Fact]
    public async Task ReferencedResourceCannotBeDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-resource-guard-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api, out var nodes);
            await nodes.RegisterAsync(new NodeDescriptor(
                "guard.source", "Source", "Source", "source", "Folder", "1.0.0", [],
                [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
            await SaveResource(api, "guarded-resource");

            var definition = new WorkflowDefinition(
                "guard-flow", "Guard flow", 1,
                [new WorkflowNode("src", "guard.source",
                    new Dictionary<string, object?> { ["resourceId"] = "guarded-resource" }, 0, 0)],
                [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["guarded-resource"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "guard-flow" }));

            var rejection = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await api.ExecuteAsync("industrial/delete-resource",
                    new JsonObject { ["id"] = "guarded-resource" }));
            Assert.Contains("guard-flow", rejection.Message, StringComparison.Ordinal);
            Assert.Contains("guarded-resource", await ReadResources(api), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task UnreferencedResourceCanBeDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-resource-free-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api, out _);
            await SaveResource(api, "free-resource");
            Assert.True(await api.ExecuteAsync("industrial/delete-resource",
                new JsonObject { ["id"] = "free-resource" }));
            Assert.DoesNotContain("free-resource", await ReadResources(api), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task MachineReferencedByBindingCannotBeDeleted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-machine-guard-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api, out _);
            await api.ExecuteAsync("industrial/save-machine", new JsonObject
            {
                ["id"] = "guarded-machine", ["name"] = "Guarded", ["endpoint"] = "10.0.0.1",
                ["enabled"] = true,
            });
            await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                          "machine-binding,Binding,guarded-machine,flow,1,profile,source,target,manual,\n",
            });
            var rejection = await Assert.ThrowsAnyAsync<Exception>(async () =>
                await api.ExecuteAsync("industrial/delete-machine", new JsonObject { ["id"] = "guarded-machine" }));
            Assert.Contains("machine-binding", rejection.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static async ValueTask SaveResource(ApiGateway api, string id) =>
        Assert.True(await api.ExecuteAsync("industrial/save-resource", new JsonObject
        {
            ["id"] = id, ["name"] = id, ["type"] = "filesystem.local", ["provider"] = "local",
            ["config"] = new JsonObject { ["root"] = "/tmp" }, ["enabled"] = true,
        }));

    private static async Task<string> ReadResources(ApiGateway api)
    {
        var rows = Assert.IsType<JsonArray>(api.Query("industrial/resources"));
        return JsonSerializer.Serialize(rows);
    }

    private static Runtime CreateRuntime(string root, out ApiGateway api, out INodeCatalog nodes)
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
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "false",
        });
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        nodes = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        return runtime;
    }
}
