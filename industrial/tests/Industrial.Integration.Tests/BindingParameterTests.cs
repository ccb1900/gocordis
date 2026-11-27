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

/// <summary>A published template stays immutable; SourceResource, FileProfile,
/// TargetResource and arbitrary binding run parameters are resolved per run.</summary>
public sealed class BindingParameterTests
{
    [Fact]
    public async Task BindingResolvesTemplateParametersAndRunParameters()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-binding-params-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "recent.csv"), "record_id,reading\n1,42\n");
        try
        {
            await using var runtime = CreateRuntime(root, sourceRoot, out var api, out var nodes);
            await nodes.RegisterAsync(new NodeDescriptor(
                "params.source", "Source", "Source", "source", "Folder", "1.0.0", [],
                [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
            await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "target-db", ["name"] = "Target", ["type"] = "sqlite",
                ["provider"] = "local", ["config"] = new JsonObject
                { ["database"] = Path.Combine(root, "target.db") }, ["enabled"] = true,
            });
            var definition = new WorkflowDefinition(
                "params-template", "Parameter template", 1,
                [new WorkflowNode("src", "params.source", new Dictionary<string, object?>
                {
                    ["source"] = "{{SourceResource}}", ["profile"] = "{{FileProfile}}",
                    ["target"] = "{{TargetResource}}", ["shift"] = "{{shift}}",
                }, 0, 0)],
                [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "params-template" }));
            Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                          "sourceResourceId,targetResourceIds,triggerKind,cron,parameters\n" +
                          "b-params,Machine,params-machine,params-template,1,p-params," +
                          "source-1,target-db,manual,,lookbackDays=7;shift=night\n",
            }));

            var bindings = Assert.IsType<JsonArray>(api.Query("industrial/bindings"));
            var binding = Assert.Single(bindings, row => row!["id"]!.GetValue<string>() == "b-params")!;
            Assert.Equal("7", binding["parameters"]!["lookbackDays"]!.GetValue<string>());

            Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "b-params", ["lookbackDays"] = 1,
            }));
            var resolved = Assert.IsType<JsonObject>(api.Query("industrial/binding-run-parameters"));
            Assert.Equal("source-1", resolved["SourceResource"]!.GetValue<string>());
            Assert.Equal("p-params", resolved["FileProfile"]!.GetValue<string>());
            Assert.Equal("target-db", resolved["TargetResource"]!.GetValue<string>());
            Assert.Equal("night", resolved["shift"]!.GetValue<string>());
            Assert.Equal("7", resolved["lookbackDays"]!.GetValue<string>());

            // The exact bound snapshot is persisted on the run so a later
            // delivery retry uses the same profile/parameters, not the template.
            var ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
            var run = (await ledger.ListRunsAsync()).Last();
            var snapshot = await ledger.GetRunDefinitionAsync(run.Id);
            Assert.NotNull(snapshot);
            Assert.Contains("p-params", snapshot!, StringComparison.Ordinal);
            Assert.Contains("night", snapshot, StringComparison.Ordinal);

            // The published snapshot was never modified by substitution.
            var published = await runtime.RequireCapability(IndustrialKeys.WorkflowRegistry)
                .GetPublishedVersionAsync("params-template", 1);
            Assert.Equal("{{SourceResource}}", published!.Definition.ResourceReferences[0]);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(string root, string sourceRoot,
        out ApiGateway api, out INodeCatalog nodes)
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
        nodes = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        return runtime;
    }
}
