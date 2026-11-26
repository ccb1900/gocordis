using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Persistence;
using Industrial.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Scenario A (docs/collector.md §15): a Reader plugin installed after
/// the product is built contributes a palette node and a real file format
/// route. No Core / Workflow Core / designer code is changed.</summary>
public sealed class IndustrialReaderPluginTests
{
    [Fact]
    public async Task NewReaderPluginAppearsInPaletteAndCanSavePublishAndRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var sample = Path.Combine(sourceRoot, "recent.json");
        File.WriteAllText(sample, """[{"record_id":"json-1","reading":7}]""");
        File.SetLastWriteTimeUtc(sample, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        try
        {
            await using var runtime = new Runtime();
            var console = new ConsoleHost(runtime);
            await using var registry = new PluginRegistry(runtime);
            console.AttachRegistry(registry);
            registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
            registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
            await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());

            IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
            {
                ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "true",
                ["sourceRoot"] = sourceRoot,
            });
            var declarations = IndustrialComponents.Types
                .Select(type => IndustrialComponents.Declaration(type)).ToArray();
            await registry.ApplyAsync(declarations);

            var palette = Assert.IsType<JsonArray>(console.Api.Query("industrial/nodes"));
            Assert.Contains(palette, node =>
                node?["nodeTypeId"]?.GetValue<string>() == IndustrialJsonReaderComponent.NodeTypeId);
            var activities = runtime.RequireCapability(IndustrialKeys.ElsaActivityCatalog);
            Assert.True(activities.Contains(IndustrialJsonReaderComponent.ActivityType));

            var definition = new WorkflowDefinition(
                "json-collection", "JSON collection", 1,
                [
                    new WorkflowNode("source", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, -200, 0),
                    new WorkflowNode("reader", IndustrialJsonReaderComponent.NodeTypeId,
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("map", "industrial.transform.mapping",
                        new Dictionary<string, object?>
                        {
                            ["mapping"] = JsonSerializer.SerializeToElement(
                                new Dictionary<string, string> { ["value"] = "reading" }),
                        }, 100, 0),
                    new WorkflowNode("validate", "industrial.transform.validate",
                        new Dictionary<string, object?>
                        {
                            ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                        }, 150, 0),
                    new WorkflowNode("sink", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 200, 0),
                ],
                [
                    new WorkflowEdge("source", "files", "reader", "input"),
                    new WorkflowEdge("reader", "records", "map", "input"),
                    new WorkflowEdge("map", "output", "validate", "input"),
                    new WorkflowEdge("validate", "output", "sink", "records"),
                ],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["source-1"], new Dictionary<string, object?>());

            Assert.True(await console.Api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition) as JsonObject));
            Assert.True(await console.Api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await console.Api.ExecuteAsync("industrial/run-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId, ["resourceId"] = "source-1" }));

            await using var target = IndustrialStore.CreateTarget(Path.Combine(root, "industrial-target.db"));
            var rows = await target.Records
                .Select(record => new { record.RecordId, record.PayloadJson })
                .ToListAsync();
            Assert.Contains(rows, row => row.RecordId == "json-1" && row.PayloadJson.Contains("\"value\""));

            await registry.ApplyAsync(declarations.Select(declaration =>
                declaration.Id == "industrial.reader.json"
                    ? declaration with { Enabled = false }
                    : declaration).ToArray());
            var withdrawn = Assert.IsType<JsonArray>(console.Api.Query("industrial/nodes"));
            Assert.DoesNotContain(withdrawn, node =>
                node?["nodeTypeId"]?.GetValue<string>() == IndustrialJsonReaderComponent.NodeTypeId);
            Assert.False(activities.Contains(IndustrialJsonReaderComponent.ActivityType));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PluginManifest Decl(string id, IReadOnlyDictionary<string, object?>? config = null) =>
        new(id, id, true, config ?? new Dictionary<string, object?>(), [], []);
}
