using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Industrial.Contracts;

namespace Industrial.Plugins;

/// <summary>Industrial application pages and API are Cordis UI contributions:
/// enabling the plugin registers them, disabling withdraws every registration
/// through the owning Fiber's effect stack.</summary>
public sealed class IndustrialUiComponent : Component
{
    public override string Name => "industrial-ui";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
    [
        UiContributionRegistry.Key,
        ApiGateway.Key,
        IndustrialKeys.ResourceRegistry,
        IndustrialKeys.WorkflowRegistry,
        IndustrialKeys.NodeCatalog,
        IndustrialKeys.CollectionLedger,
        IndustrialKeys.CollectionCoordinator,
        IndustrialKeys.HistoricalRangePlanner,
        IndustrialKeys.RecordReader,
        IndustrialKeys.RecordSink,
        IndustrialKeys.ArchiveSink,
    ];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var api = context.Require(ApiGateway.Key);
        var resources = context.Require(IndustrialKeys.ResourceRegistry);
        var workflows = context.Require(IndustrialKeys.WorkflowRegistry);
        var nodes = context.Require(IndustrialKeys.NodeCatalog);
        var ledger = context.Require(IndustrialKeys.CollectionLedger);
        var coordinator = context.Require(IndustrialKeys.CollectionCoordinator);
        var planner = context.Require(IndustrialKeys.HistoricalRangePlanner);
        var reader = context.Require(IndustrialKeys.RecordReader);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
        var owner = ContributionOwner.For("industrial", "ui");

        var pages = new[]
        {
            Page("industrial.dashboard", "Industrial Dashboard", "/industrial", "dashboard", "Dashboard",
                "industrial/dashboard", 10, "Industrial"),
            Page("industrial.resources", "Resources", "/industrial/resources", "generic", "Database",
                "industrial/resources", 20, "Industrial"),
            Page("industrial.workflows", "Workflows", "/industrial/workflows", "generic", "Apartment",
                "industrial/workflows", 30, "Industrial"),
            Page("industrial.designer", "Workflow Designer", "/industrial/designer", "generic", "Branches",
                "industrial/nodes", 40, "Industrial"),
            Page("industrial.executions", "Execution Center", "/industrial/executions", "generic", "Activity",
                "industrial/dashboard", 50, "Industrial"),
            Page("industrial.collections", "Collection History", "/industrial/collections", "generic", "History",
                "industrial/collections", 60, "Industrial"),
        };
        foreach (var page in pages)
        {
            await context.RegisterUiPageAsync(ui, owner, page);
        }

        var queries = new (string Name, Func<JsonNode?> Handler)[]
        {
            ("industrial/dashboard", () => new JsonObject
            {
                ["resources"] = resources.ListAsync().GetAwaiter().GetResult().Count,
                ["workflows"] = workflows.ListAsync().GetAwaiter().GetResult().Count,
                ["nodes"] = nodes.ListAsync().GetAwaiter().GetResult().Count,
                ["collections"] = ledger.ListAllFilesAsync().GetAwaiter().GetResult().Count,
            }),
            ("industrial/resources", () => new JsonArray(resources.ListAsync()
                .GetAwaiter().GetResult()
                .Select(resource => (JsonNode?)new JsonObject
                {
                    ["id"] = resource.Id,
                    ["name"] = resource.Name,
                    ["type"] = resource.Type,
                    ["provider"] = resource.Provider,
                    ["enabled"] = resource.Enabled,
                    ["version"] = resource.Version,
                }).ToArray())),
            ("industrial/workflows", () => new JsonArray(workflows.ListAsync()
                .GetAwaiter().GetResult()
                .Select(workflow => (JsonNode?)new JsonObject
                {
                    ["workflowId"] = workflow.WorkflowId,
                    ["version"] = workflow.Version,
                    ["name"] = workflow.Name,
                }).ToArray())),
            ("industrial/nodes", () => new JsonArray(nodes.ListAsync()
                .GetAwaiter().GetResult()
                .Select(node => (JsonNode?)new JsonObject
                {
                    ["nodeTypeId"] = node.NodeTypeId,
                    ["name"] = node.Name,
                    ["category"] = node.Category,
                    ["version"] = node.Version,
                }).ToArray())),
            ("industrial/collections", () => new JsonArray(ledger.ListAllFilesAsync()
                .GetAwaiter().GetResult()
                .Select(file => (JsonNode?)new JsonObject
                {
                    ["id"] = file.Id,
                    ["path"] = file.Identity.RelativePath,
                    ["sourceDate"] = file.SourceDate.ToString("O"),
                    ["status"] = file.Status,
                }).ToArray())),
        };
        foreach (var (name, handler) in queries)
        {
            await context.EffectAsync(
                () => { api.RegisterQuery(name, handler, owner); return ValueTask.CompletedTask; },
                () => { api.UnregisterQuery(owner, name); return ValueTask.CompletedTask; });
        }

        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("industrial/run-collection", payload =>
                    new ValueTask(RunAsync(payload, coordinator, planner, reader, sink, archive).AsTask()), owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });

        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("industrial/retry-failed", _ =>
                    new ValueTask(coordinator.RetryFailedAsync([sink], archive).AsTask()), owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });

        return null;
    }

    private static async ValueTask RunAsync(
        JsonNode? payload,
        ICollectionCoordinator coordinator,
        IHistoricalRangePlanner planner,
        IRecordReader reader,
        IRecordSink sink,
        IArchiveSink archive)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON object payload required");
        var workflowId = body["workflowId"]?.GetValue<string>();
        var resourceId = body["resourceId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException("workflowId and resourceId are required");
        }

        var range = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
        await coordinator.RunAsync(workflowId, resourceId, range, reader, [sink], archive);
    }

    private static UiPageDefinition Page(
        string id, string title, string route, string renderer, string icon,
        string query, int order, string group) =>
        new(id, title, route, renderer, icon, $"Industrial page: {title}",
            new JsonObject
            {
                ["blocks"] = new JsonArray(new JsonObject
                {
                    ["kind"] = renderer == "dashboard" ? "stats" : "table",
                    ["query"] = query,
                }),
            },
            Actions: null, Order: order, Group: group);
}
