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

/// <summary>A template package must declare its format version and the
/// dependencies a target environment has to satisfy before import.</summary>
public sealed class ConfigPackageTests
{
    [Fact]
    public async Task TemplatePackageDeclaresVersionDependenciesAndParameters()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-package-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api, out var nodes);
            await nodes.RegisterAsync(new NodeDescriptor(
                "pkg.source", "Source", "Source", "source", "Folder", "1.0.0", [],
                [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
            await api.ExecuteAsync("industrial/save-credential", new JsonObject
            {
                ["id"] = "pkg-cred", ["name"] = "Package credential", ["kind"] = CredentialKinds.UsernamePassword,
                ["secret"] = "{\"username\":\"operator\",\"password\":\"secret\"}",
            });
            await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "pkg-source", ["name"] = "Package source", ["type"] = "filesystem.local",
                ["provider"] = "local", ["credentialId"] = "pkg-cred",
                ["config"] = new JsonObject { ["root"] = "/tmp" }, ["enabled"] = true,
            });
            var definition = new WorkflowDefinition(
                "pkg-template", "Package template", 1,
                [new WorkflowNode("src", "pkg.source",
                    new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0)],
                [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "pkg-template" }));

            var package = Assert.IsType<JsonObject>(api.Query("industrial/export-config"));
            Assert.Equal("gocordis-industrial-config-v1", package["format"]!.GetValue<string>());
            Assert.Equal(2, package["formatVersion"]!.GetValue<int>());
            Assert.Contains(package["templateParameters"]!.AsArray(),
                node => node!.GetValue<string>() == "SourceResource");
            var dependencies = package["dependencies"]!.AsArray()
                .Select(node => node!.AsObject()).ToArray();
            Assert.Contains(dependencies, dependency =>
                dependency["kind"]!.GetValue<string>() == "node" &&
                dependency["id"]!.GetValue<string>() == "pkg.source" &&
                dependency["version"]!.GetValue<string>() == "1.0.0");
            Assert.Contains(dependencies, dependency =>
                dependency["kind"]!.GetValue<string>() == "credential" &&
                dependency["secretIncluded"]!.GetValue<bool>() == false);
            Assert.DoesNotContain(JsonSerializer.Serialize(package), "secret");

            var valid = (JsonObject)package.DeepClone();
            Assert.True(await api.ExecuteAsync("industrial/import-config", valid));

            var withSecret = (JsonObject)package.DeepClone();
            var credential = withSecret["dependencies"]!.AsArray()
                .Select(node => node!.AsObject())
                .First(node => node["kind"]!.GetValue<string>() == "credential");
            credential["secretIncluded"] = true;
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await api.ExecuteAsync("industrial/import-config", withSecret));

            var missingNode = (JsonObject)package.DeepClone();
            var nodeDependency = missingNode["dependencies"]!.AsArray()
                .Select(node => node!.AsObject())
                .First(node => node["kind"]!.GetValue<string>() == "node");
            nodeDependency["id"] = "not-installed";
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await api.ExecuteAsync("industrial/import-config", missingNode));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
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
