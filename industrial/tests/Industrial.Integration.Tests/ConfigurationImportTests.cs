using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Browser-facing command surface for CSV imports, trigger policy and
/// immutable template version selection. Uses a fresh host so persistence is
/// proven across registry instances.</summary>
public sealed class ConfigurationImportTests
{
    [Fact]
    public async Task CsvImportsTriggerPolicyVersionUpgradeAndRestartPersist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-config-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using (var runtime = CreateRuntime(root, out var api, out _))
            {
                await api.ExecuteAsync("industrial/import-machines", new JsonObject
                {
                    ["csv"] = "id,name,endpoint\nm-1,Machine 1,10.0.0.1\n",
                });
                await api.ExecuteAsync("industrial/import-file-profiles", new JsonObject
                {
                    ["csv"] = "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n" +
                              "p-1,Profile 1,*.csv,csv,,true,utf-8,value=reading,value\n",
                });
                await api.ExecuteAsync("industrial/import-bindings", new JsonObject
                {
                    ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                              "b-1,Binding 1,m-1,template,1,p-1,source-m-1,target-db,schedule,0 2 * * *\n",
                });
                await api.ExecuteAsync("industrial/save-workflow", JsonSerializer.SerializeToNode(
                    new Industrial.Contracts.WorkflowDefinition("template", "Template", 1, [], [],
                        new Industrial.Contracts.TriggerDefinition("manual", null, null,
                            new Dictionary<string, object?>()), [], new Dictionary<string, object?>())));
                await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "template" });
                await api.ExecuteAsync("industrial/save-workflow", JsonSerializer.SerializeToNode(
                    new Industrial.Contracts.WorkflowDefinition("template", "Template", 2, [], [],
                        new Industrial.Contracts.TriggerDefinition("manual", null, null,
                            new Dictionary<string, object?>()), [], new Dictionary<string, object?>())));
                await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "template" });
                await api.ExecuteAsync("industrial/upgrade-binding-version", new JsonObject
                {
                    ["ids"] = new JsonArray("b-1"), ["version"] = 2,
                });
            }

            // Restart: a fresh runtime reads the same persisted rows.
            await using (var runtime = CreateRuntime(root, out var api, out _))
            {
                var bindings = Assert.IsType<JsonArray>(
                    api.Query("industrial/bindings"));
                var binding = bindings[0]!.AsObject();
                Assert.Equal("b-1", binding["id"]!.GetValue<string>());
                Assert.Equal(2, binding["workflowVersion"]!.GetValue<int>());
                Assert.Equal("schedule", binding["trigger"]!["kind"]!.GetValue<string>());
                Assert.Equal("0 2 * * *", binding["trigger"]!["cron"]!.GetValue<string>());
                var machines = Assert.IsType<JsonArray>(api.Query("industrial/machines"));
                Assert.Single(machines);
                var profiles = Assert.IsType<JsonArray>(api.Query("industrial/file-profiles"));
                Assert.Single(profiles);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BindingBackfillRunsThePublishedTemplateWithBoundResources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-binding-run-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var completedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{completedDate:yyyyMMdd}.csv"),
            "record_id,reading\n1,42\n");
        try
        {
            await using var runtime = CreateRuntime(root, out var api, out var nodes,
                seedResource: true, sourceRoot: sourceRoot);
            await nodes.RegisterAsync(new NodeDescriptor(
                "bind.source", "Source", "Source", "source", "Folder", "1.0.0", [],
                [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
            await nodes.RegisterAsync(new NodeDescriptor(
                "bind.reader", "Reader", "Reader", "reader", "FileText", "1.0.0",
                [new NodePort("input", Industrial.Contracts.PortType.FileBatch, true)],
                [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []));
            await nodes.RegisterAsync(new NodeDescriptor(
                "bind.sink", "Sink", "Sink", "sink", "Database", "1.0.0",
                [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)], [],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []));
            await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "bound-target", ["name"] = "Bound target", ["type"] = "sqlite",
                ["provider"] = "local",
                ["config"] = new JsonObject { ["database"] = Path.Combine(root, "bound.db") },
                ["enabled"] = true,
            });
            var definition = new WorkflowDefinition(
                "bind-template", "Binding template", 1,
                [
                    new WorkflowNode("src", "bind.source", new Dictionary<string, object?>(), 0, 0),
                    new WorkflowNode("read", "bind.reader", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("sink", "bind.sink",
                        new Dictionary<string, object?> { ["targetResourceId"] = "{{TargetResource}}" },
                        200, 0),
                ],
                [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "sink", "records"),
                ],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "bind-template" }));
            await api.ExecuteAsync("industrial/import-file-profiles", new JsonObject
            {
                ["csv"] = "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n" +
                          "p-run,Run profile,*.csv,csv,,true,utf-8,value=reading,value\n",
            });
            await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                          "b-run,Run binding,machine-run,bind-template,1,p-run,source-1,bound-target,schedule,0 3 * * *\n",
            });

            Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "b-run", ["lookbackDays"] = 7,
            }));
            var execution = Assert.IsType<JsonObject>(api.Query("industrial/execution"));
            Assert.Equal("Completed", execution["status"]!.GetValue<string>());
            Assert.Equal(new[] { "read", "sink", "src" }, execution["executed"]!.AsArray()
                .Select(node => node!.GetValue<string>()).OrderBy(id => id));

            var status = Assert.IsType<JsonObject>(api.Query("industrial/binding-status"));
            Assert.Contains(status["runs"]!.AsArray(), run =>
                run!["workflowId"]!.GetValue<string>() == "bind-template");
            Assert.Contains(status["deliveries"]!.AsArray(), delivery =>
                delivery!["targetId"]!.GetValue<string>() == "bound-target" &&
                delivery["status"]!.GetValue<string>() == "Succeeded");

            // Runtime substitution must not edit the immutable published snapshot.
            var workflows = runtime.RequireCapability(IndustrialKeys.WorkflowRegistry);
            var published = await workflows.GetPublishedVersionAsync("bind-template", 1);
            Assert.Equal("{{TargetResource}}",
                published!.Definition.Nodes[2].Config["targetResourceId"]!.ToString());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(
        string root, out ApiGateway api, out INodeCatalog nodes,
        bool seedResource = false, string? sourceRoot = null)
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
        var config = new Dictionary<string, object?>
        {
            ["root"] = root, ["allowDevelopmentKey"] = "true",
            ["seedResource"] = seedResource ? "true" : "false",
        };
        if (sourceRoot is not null) config["sourceRoot"] = sourceRoot;
        IndustrialComponents.RegisterFactories(registry, config);
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        nodes = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        return runtime;
    }
}
