using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Preview reports are first-class backend results, not UI-only text,
/// and exception rules are enforced by the registry itself.</summary>
public sealed class ImportPreviewTests
{
    [Fact]
    public async Task RowErrorsAndExclusionsAreReportedBeforeImport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-import-preview-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow", JsonSerializer.SerializeToNode(
                new Industrial.Contracts.WorkflowDefinition("preview-flow", "Preview", 1, [], [],
                    new Industrial.Contracts.TriggerDefinition("manual", null, null,
                        new Dictionary<string, object?>()), [], new Dictionary<string, object?>()))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "preview-flow" }));
            foreach (var machine in new[] { "m1", "m2" })
                Assert.True(await api.ExecuteAsync("industrial/save-machine", new JsonObject
                {
                    ["id"] = machine, ["name"] = machine, ["endpoint"] = "10.0.0.1", ["enabled"] = true,
                }));
            foreach (var profile in new[] { "p1", "p2" })
                Assert.True(await api.ExecuteAsync("industrial/save-file-profile", new JsonObject
                {
                    ["id"] = profile, ["name"] = profile, ["fileNamePattern"] = "*.csv",
                    ["format"] = "csv", ["delimiter"] = ",", ["hasHeader"] = true,
                    ["encoding"] = "utf-8", ["mapping"] = new JsonObject(),
                    ["required"] = new JsonArray(), ["enabled"] = true,
                }));
            Assert.True(await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "source-1", ["name"] = "Source", ["type"] = "filesystem.local",
                ["provider"] = "local", ["config"] = new JsonObject { ["root"] = "/tmp" }, ["enabled"] = true,
            }));
            Assert.True(await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "target", ["name"] = "Target", ["type"] = "sqlite", ["provider"] = "local",
                ["config"] = new JsonObject { ["database"] = Path.Combine(root, "target.db") },
                ["enabled"] = true,
            }));

            Assert.True(await api.ExecuteAsync("industrial/preview-import", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                          "b1,Valid,m1,preview-flow,1,p1,source-1,target,manual,\n" +
                          "b1,Duplicate,m1,preview-flow,1,p1,source-1,target,manual,\n" +
                          "b3,Bad,m1,preview-flow,1,p1,source-1,target,nightly,\n",
            }));
            var report = Assert.IsType<JsonObject>(api.Query("industrial/import-preview"));
            Assert.Equal(3, report["total"]!.GetValue<int>());
            Assert.Equal(1, report["valid"]!.GetValue<int>());
            var errors = report["errors"]!.AsArray();
            Assert.Equal(2, errors.Count);
            Assert.Contains(errors, row => row!["row"]!.GetValue<int>() == 3 &&
                row["message"]!.GetValue<string>().Contains("duplicate"));
            Assert.Contains(errors, row => row!["row"]!.GetValue<int>() == 4 &&
                row["message"]!.GetValue<string>().Contains("triggerKind"));

            var request = new JsonObject
            {
                ["machineIds"] = new JsonArray("m1", "m2"),
                ["fileProfileIds"] = new JsonArray("p1", "p2"),
                ["workflowId"] = "preview-flow", ["workflowVersion"] = 1,
                ["sourceResourcePattern"] = "source-1",
                ["targetResourceIds"] = new JsonArray("target"),
                ["exclusions"] = new JsonArray("m2:p2"),
            };
            Assert.True(await api.ExecuteAsync("industrial/preview-bindings", request.DeepClone()));
            var bulk = Assert.IsType<JsonObject>(api.Query("industrial/binding-preview"));
            Assert.Equal(3, bulk["total"]!.GetValue<int>());
            Assert.Equal(3, bulk["create"]!.GetValue<int>());

            Assert.True(await api.ExecuteAsync("industrial/create-bindings", request.DeepClone()));
            var bindings = Assert.IsType<JsonArray>(api.Query("industrial/bindings"));
            Assert.Equal(3, bindings.Count);
            Assert.DoesNotContain(bindings, row =>
                row!["id"]!.GetValue<string>() == "binding:preview-flow:v1:m2:p2");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(string root, out ApiGateway api)
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
        return runtime;
    }
}
