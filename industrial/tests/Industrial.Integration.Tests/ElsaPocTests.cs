using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using Elsa.Workflows.Models;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Gocordis.Builtin.WebConsole;
using Industrial.Contracts;
using Industrial.Elsa;
using Industrial.Host;

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
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Where(type => type != "industrial.ui" && type != "industrial.reader.json")
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());
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
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());

        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Where(type => type != "industrial.ui" && type != "industrial.reader.json")
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());

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
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());

        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
            ["root"] = temp.Root,
            ["seedResource"] = "true",
            ["sourceRoot"] = Path.Combine(temp.Root, "source"),
            ["resourceId"] = "source-1",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Where(type => type != "industrial.ui" && type != "industrial.reader.json")
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());

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
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.archive", "Archive", "Archive", "sink", "Archive", "1.0.0",
            [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)], [],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[8], []));
        // Nodes are stored in deliberately non-topological order. Only the
        // persisted connections may determine which Activity Elsa schedules
        // first; a second delivery node must not restart collection.
        var definitionToSave = new WorkflowDefinition(
            "elsa-flow", "Elsa flow", 1,
            [
                new WorkflowNode("sink", "n.sink", new Dictionary<string, object?>(), 200, 0),
                new WorkflowNode("archive", "n.archive", new Dictionary<string, object?>(), 200, 100),
                new WorkflowNode("reader", "n.reader", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("source", "n.source", new Dictionary<string, object?>(), 0, 0),
            ],
            [
                new WorkflowEdge("source", "files", "reader", "input"),
                new WorkflowEdge("reader", "records", "sink", "records"),
                new WorkflowEdge("reader", "records", "archive", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());
        var saved = await console.Api.ExecuteAsync("industrial/save-workflow",
            System.Text.Json.JsonSerializer.SerializeToNode(definitionToSave));
        Assert.True(saved, "designer save command failed");
        var published = await console.Api.ExecuteAsync("industrial/publish-workflow", new JsonObject
        {
            ["workflowId"] = "elsa-flow",
        });
        Assert.True(published, "designer publish command failed");
        var pinned = await workflows.GetPublishedAsync("elsa-flow");
        var pinnedGraph = JsonNode.Parse(pinned!.ElsaDefinitionJson)!["elsa"]!.AsObject();
        Assert.Equal(4, pinnedGraph["activities"]!.AsArray().Count);
        Assert.Equal(3, pinnedGraph["connections"]!.AsArray().Count);

        // Publishing pins v1. A later draft that points at a missing target
        // must NOT affect a Run that did not request a specific version.
        var draftMutation = definitionToSave with
        {
            Version = 2,
            Name = "Elsa flow draft v2",
            Nodes = definitionToSave.Nodes
                .Select(node => node.Id == "sink"
                    ? node with { Config = new Dictionary<string, object?> { ["targetResourceId"] = "missing-target" } }
                    : node)
                .ToArray(),
        };
        Assert.True(await console.Api.ExecuteAsync("industrial/save-workflow",
            System.Text.Json.JsonSerializer.SerializeToNode(draftMutation)));
        Assert.Equal(2, (await workflows.GetAsync("elsa-flow"))!.Version);
        Assert.Equal(1, (await workflows.GetPublishedAsync("elsa-flow"))!.Version);

        var executed = await console.Api.ExecuteAsync("industrial/run-workflow", new JsonObject
        {
            ["workflowId"] = "elsa-flow",
            ["resourceId"] = "source-1",
        });
        var executionProbe = console.Api.Query("industrial/execution") as JsonObject;
        Assert.True(executed, executionProbe?["error"]?.ToString() ?? "no error detail");
        var execution = (JsonObject)console.Api.Query("industrial/execution")!;
        Assert.Equal("Completed", execution["status"]!.GetValue<string>());
        var executedNodes = execution["executed"]!.AsArray()
            .Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "source", "reader", "sink", "archive" }.OrderBy(id => id),
            executedNodes.OrderBy(id => id));
        Assert.True(executedNodes[0] == "source", "Elsa must start from the connection root");

        await using var ledger = Industrial.Persistence.IndustrialStore.Create(
            Path.Combine(temp.Root, "industrial-ledger.db"));
        Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
        Assert.Single(ledger.CollectionFiles.AsNoTracking().ToList());
        Assert.True(ledger.FileDeliveries.AsNoTracking().Count() == 2,
            "sink and archive each get one delivery from one collection run");
        var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
        Assert.True(CollectionStatus.IsTerminal(run.Status),
            $"run must be terminal after sink AND archive complete, was {run.Status}");
        await using var target = Industrial.Persistence.IndustrialStore.CreateTarget(
            Path.Combine(temp.Root, "industrial-target.db"));
        Assert.Single(target.Records.AsNoTracking().ToList());
    }

    [Fact]
    public async Task RepublishKeepsVersionConsistentAcrossRowDefinitionAndElsaGraph()
    {
        using var temp = new TempDir();
        await ComposeRuntime(temp.Root, async (catalog, workflows, api) =>
        {
            await catalog.RegisterAsync(new NodeDescriptor(
                "v.source", "Source", "Source", "source", "Folder", "1.0.0",
                [], [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
            await catalog.RegisterAsync(new NodeDescriptor(
                "v.reader", "Reader", "Reader", "reader", "FileText", "1.0.0",
                [new NodePort("input", Industrial.Contracts.PortType.FileBatch, true)],
                [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []));
            await catalog.RegisterAsync(new NodeDescriptor(
                "v.sink", "Sink", "Sink", "sink", "Database", "1.0.0",
                [new NodePort("records", Industrial.Contracts.PortType.RecordSet, true)], [],
                new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []));

            var definition = new WorkflowDefinition(
                "version-flow", "Version flow", 1,
                [
                    new WorkflowNode("src", "v.source", new Dictionary<string, object?>(), 0, 0),
                    new WorkflowNode("read", "v.reader", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("store", "v.sink", new Dictionary<string, object?>(), 200, 0),
                ],
                [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "store", "records"),
                ],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                [], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                System.Text.Json.JsonSerializer.SerializeToNode(definition)));

            for (var expected = 1; expected <= 3; expected++)
            {
                Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "version-flow" }));
                var published = await workflows.GetPublishedAsync("version-flow");
                Assert.NotNull(published);
                Assert.Equal(expected, published!.Version);
                Assert.Equal(expected, published.Definition.Version);
                var graph = JsonNode.Parse(published.ElsaDefinitionJson)!["elsa"]!.AsObject();
                Assert.Equal(expected, graph["version"]!.GetValue<int>());
            }
        });
    }

    [Fact]
    public async Task ElsaAppliesTransformNodesAlongGraphEdges()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Root, "source"));
        File.WriteAllText(Path.Combine(temp.Root, "source", "recent.csv"),
            "record_id,reading\n1,42\n2,\n");
        File.SetLastWriteTimeUtc(Path.Combine(temp.Root, "source", "recent.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        await ComposeRuntime(temp.Root, async (_, workflows, api) =>
        {
            var definition = new WorkflowDefinition(
                "transform-flow", "Transform flow", 1,
                [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv",
                        new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("map", "industrial.transform.mapping",
                        new Dictionary<string, object?>
                        {
                            ["mapping"] = System.Text.Json.JsonSerializer.SerializeToElement(
                                new Dictionary<string, string> { ["value"] = "reading" }),
                        }, 200, 0),
                    new WorkflowNode("validate", "industrial.transform.validate",
                        new Dictionary<string, object?>
                        {
                            ["required"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { "value" }),
                        }, 300, 0),
                    new WorkflowNode("store", "industrial.sink.sqlite",
                        new Dictionary<string, object?>(), 400, 0),
                ],
                [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "map", "input"),
                    new WorkflowEdge("map", "output", "validate", "input"),
                    new WorkflowEdge("validate", "output", "store", "records"),
                ],
                new TriggerDefinition("event", null, "file.arrived", new Dictionary<string, object?>()),
                ["source-1"], new Dictionary<string, object?>());

            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                System.Text.Json.JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "transform-flow" }));
            var triggers = Assert.IsType<JsonArray>(api.Query("industrial/triggers"));
            Assert.Contains(triggers, trigger => trigger?["eventName"]?.GetValue<string>() == "file.arrived");
            Assert.True(await api.ExecuteAsync("industrial/trigger-event", new JsonObject
            {
                ["eventName"] = "file.arrived",
                ["resourceId"] = "source-1",
            }));
            var execution = (JsonObject)api.Query("industrial/execution")!;
            Assert.Equal("Completed", execution["status"]!.GetValue<string>());
            Assert.Equal(new[] { "src", "read", "map", "validate", "store" },
                execution["executed"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        });

        await using var target = Industrial.Persistence.IndustrialStore.CreateTarget(
            Path.Combine(temp.Root, "industrial-target.db"));
        var rows = target.Records.AsNoTracking().ToList();
        Assert.Single(rows);
        Assert.EndsWith("|1", rows[0].RecordId, StringComparison.Ordinal);
        Assert.Contains("\"value\"", rows[0].PayloadJson, StringComparison.Ordinal);
        Assert.Contains("42", rows[0].PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElsaRestoresPinnedVersionAfterProcessRestart()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Root, "source"));
        File.WriteAllText(Path.Combine(temp.Root, "source", "after-restart.csv"),
            "record_id,reading\n10,100\n");
        File.SetLastWriteTimeUtc(Path.Combine(temp.Root, "source", "after-restart.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var definition = new WorkflowDefinition(
            "restart-flow", "Restart flow", 1,
            [
                new WorkflowNode("source", "n.source", new Dictionary<string, object?>(), 0, 0),
                new WorkflowNode("sink", "n.sink", new Dictionary<string, object?>(), 100, 0),
            ],
            [new WorkflowEdge("source", "files", "sink", "records")],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await ComposeRuntime(temp.Root, async (catalog, workflows, api) =>
        {
            await RegisterNodesAsync(catalog);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                System.Text.Json.JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow", new JsonObject
            {
                ["workflowId"] = "restart-flow",
            }));
        });

        // A broken v2 draft must not leak into a fresh process that asks for v1.
        await ComposeRuntime(temp.Root, async (catalog, workflows, api) =>
        {
            await RegisterNodesAsync(catalog);
            var broken = definition with
            {
                Version = 2,
                Nodes = definition.Nodes
                    .Select(node => node.Id == "sink"
                        ? node with { Config = new Dictionary<string, object?> { ["targetResourceId"] = "missing-target" } }
                        : node)
                    .ToArray(),
            };
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                System.Text.Json.JsonSerializer.SerializeToNode(broken)));
        });

        await ComposeRuntime(temp.Root, async (catalog, workflows, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "restart-flow",
                ["version"] = 1,
                ["resourceId"] = "source-1",
                ["instanceId"] = "restart-instance-1",
            }));
            var execution = (JsonObject)api.Query("industrial/execution")!;
            Assert.Equal("Completed", execution["status"]!.GetValue<string>());
            Assert.Equal(new[] { "source", "sink" }, execution["executed"]!.AsArray()
                .Select(node => node!.GetValue<string>()).ToArray());
            Assert.Equal("restart-instance-1", execution["instanceId"]!.GetValue<string>());
            Assert.Equal(1, (await workflows.GetPublishedVersionAsync("restart-flow", 1))!.Version);
        });

        // A fresh process can recover the persisted Elsa execution instance.
        await ComposeRuntime(temp.Root, async (_, _, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/inspect-instance", new JsonObject
            {
                ["instanceId"] = "restart-instance-1",
            }));
            var instance = (JsonObject)api.Query("industrial/instance")!;
            Assert.Equal("restart-instance-1", instance["instanceId"]!.GetValue<string>());
            Assert.Equal("restart-flow:v1", instance["definitionId"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task IndustrialDefinitionConvertsToElsaDefinition()
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

        var json = await ElsaDefinitionConverter.ToElsaJson(definition);
        var envelope = JsonNode.Parse(json)!.AsObject();
        var elsa = envelope["elsa"]!.AsObject();
        Assert.Equal("flowchart", elsa["kind"]!.GetValue<string>());
        Assert.Equal("daily", elsa["definitionId"]!.GetValue<string>());
        Assert.Equal(2, elsa["activities"]!.AsArray().Count);
        Assert.Single(elsa["connections"]!.AsArray());
        Assert.Contains("industrial.reader.csv", elsa.ToJsonString(), StringComparison.Ordinal);
        var persistedActivities = elsa["activities"]!.AsArray();
        Assert.Equal("LocalFileSourceActivity",
            persistedActivities[0]!["elsaActivity"]!.GetValue<string>());
        Assert.Equal("CsvReaderActivity", persistedActivities[1]!["elsaActivity"]!.GetValue<string>());
        Assert.Equal(12, IndustrialNodeActivityFactory.RegisteredActivityTypes.Count);
        Assert.Equal(12, IndustrialNodeActivityFactory.RegisteredActivityTypes.Distinct().Count());
        Assert.IsType<LocalFileSourceActivity>(IndustrialNodeActivityFactory.Create(
            "source", "industrial.filesystem.local", "industrial.filesystem.local", ""));
        Assert.IsType<ValidationActivity>(IndustrialNodeActivityFactory.Create(
            "validate", "industrial.transform.validate", "industrial.transform.validate", ""));
        var workflow = ElsaDefinitionConverter.BuildWorkflow(elsa);
        Assert.Equal("daily", workflow.Identity.DefinitionId);
        Assert.Equal(1, workflow.Identity.Version);
    }
    [Fact]
    public async Task BackfillPreviewUsesPublishedSinkTargets()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Root, "source"));
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "source", "recent.csv"),
            "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(Path.Combine(temp.Root, "source", "recent.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var definition = new WorkflowDefinition(
            "preview-flow", "Preview flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("storeA", "industrial.sink.sqlite",
                    new Dictionary<string, object?>(), 200, 0),
                new WorkflowNode("storeB", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "missing-target" }, 200, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "storeA", "records"),
                new WorkflowEdge("read", "records", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await ComposeRuntime(temp.Root, async (_, workflows, api) =>
        {
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                System.Text.Json.JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/preview-backfill", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
                ["lookbackDays"] = 1,
            }));

            var preview = Assert.IsType<JsonObject>(api.Query("industrial/backfill-preview"));
            var file = Assert.IsType<JsonObject>(preview["files"]!.AsArray().Single());
            Assert.Equal(2, file["expectedTargets"]!.GetValue<int>());
            var missing = file["missingTargets"]!.GetValue<string>();
            Assert.Contains("sqlite", missing, StringComparison.Ordinal);
            Assert.Contains("missing-target", missing, StringComparison.Ordinal);
            Assert.True(file["wouldCollect"]!.GetValue<bool>());
        });
    }

    private static async Task ComposeRuntime(
        string root,
        Func<INodeCatalog, IWorkflowRegistry, ApiGateway, Task> act)
    {
        await using var runtime = new Gocordis.Core.Runtime();
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
            ["allowDevelopmentKey"] = "true",
            ["root"] = root,
            ["seedResource"] = "true",
            ["sourceRoot"] = Path.Combine(root, "source"),
            ["resourceId"] = "source-1",
        });
        await registry.ApplyAsync(IndustrialComponents.Types
            .Where(type => type != "industrial.ui" && type != "industrial.reader.json")
            .Select(type => IndustrialComponents.Declaration(type)).ToArray());
        await act(
            runtime.RequireCapability(IndustrialKeys.NodeCatalog),
            runtime.RequireCapability(IndustrialKeys.WorkflowRegistry),
            console.Api);
    }

    private static async Task RegisterNodesAsync(INodeCatalog catalog)
    {
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.source", "Source", "Source", "source", "Folder", "1.0.0",
            [], [new NodePort("files", Industrial.Contracts.PortType.FileBatch, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []));
        await catalog.RegisterAsync(new NodeDescriptor(
            "n.sink", "Sink", "Sink", "sink", "Database", "1.0.0",
            [new NodePort("records", Industrial.Contracts.PortType.FileBatch, true)], [],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "industrial-elsa-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}