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
    private readonly IReadOnlyDictionary<string, object?> _config;

    public IndustrialUiComponent() : this(FromEnvironment()) { }

    public IndustrialUiComponent(IReadOnlyDictionary<string, object?> config) => _config = config;

    private static IReadOnlyDictionary<string, object?> FromEnvironment() =>
        new Dictionary<string, object?>
        {
            ["workflowId"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_WORKFLOW") ?? "daily-collection",
            ["resourceId"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE") ?? "source-1",
        };

    private string Setting(string key, string fallback) =>
        _config.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()!
            : fallback;

    public override string Name => "industrial-ui";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
    [
        UiContributionRegistry.Key,
        ApiGateway.Key,
        IndustrialKeys.ResourceRegistry,
        IndustrialKeys.WorkflowRegistry,
        IndustrialKeys.NodeCatalog,
        IndustrialKeys.CollectionLedger,
        IndustrialKeys.CredentialVault,
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
        var credentials = context.Require(IndustrialKeys.CredentialVault);
        var coordinator = context.Require(IndustrialKeys.CollectionCoordinator);
        var planner = context.Require(IndustrialKeys.HistoricalRangePlanner);
        var reader = context.Require(IndustrialKeys.RecordReader);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
        var owner = ContributionOwner.ForContext(context, "industrial", "ui");

        var pages = new[]
        {
            Page("industrial.dashboard", "Industrial Dashboard", "/industrial", "dashboard", "Dashboard",
                "industrial/dashboard-rows", 10, "Industrial",
                [("metric", "Metric"), ("value", "Value")]),
            Page("industrial.resources", "Resources", "/industrial/resources", "generic", "Database",
                "industrial/resources", 20, "Industrial",
                [("id", "Id"), ("name", "Name"), ("type", "Type"), ("provider", "Provider"), ("enabled", "Enabled")]),
            Page("industrial.workflows", "Workflows", "/industrial/workflows", "generic", "Apartment",
                "industrial/workflows", 30, "Industrial",
                [("workflowId", "Workflow"), ("version", "Version"), ("name", "Name")]),
            Page("industrial.designer", "Workflow Designer", "/industrial/designer", "generic", "Branches",
                "industrial/nodes", 40, "Industrial",
                [("nodeTypeId", "Node type"), ("name", "Name"), ("category", "Category"), ("version", "Version")]),
            Page("industrial.executions", "Execution Center", "/industrial/executions", "generic", "Activity",
                "industrial/dashboard-rows", 50, "Industrial",
                [("metric", "Metric"), ("value", "Value")]),
            Page("industrial.collections", "Collection History", "/industrial/collections", "generic", "History",
                "industrial/collections", 60, "Industrial",
                [("id", "Id"), ("path", "Path"), ("sourceDate", "Source date"), ("status", "Status")]),
            Page("industrial.machines", "Machine Management", "/industrial/machines", "generic", "Desktop",
                "industrial/resources", 70, "Industrial",
                [("id", "Id"), ("name", "Name"), ("type", "Type"), ("enabled", "Enabled")]),
            Page("industrial.credentials", "Credential Management", "/industrial/credentials", "generic", "Key",
                "industrial/credentials", 80, "Industrial",
                [("id", "Id"), ("name", "Name"), ("kind", "Kind"), ("version", "Version")]),
            Page("industrial.tasks", "Collection Tasks", "/industrial/tasks", "generic", "Schedule",
                "industrial/workflows", 90, "Industrial",
                [("workflowId", "Workflow"), ("version", "Version"), ("name", "Name")]),
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
            ("industrial/dashboard-rows", () => new JsonArray(
                (JsonNode?)new JsonObject { ["metric"] = "Resources", ["value"] = resources.ListAsync().GetAwaiter().GetResult().Count },
                (JsonNode?)new JsonObject { ["metric"] = "Workflows", ["value"] = workflows.ListAsync().GetAwaiter().GetResult().Count },
                (JsonNode?)new JsonObject { ["metric"] = "Nodes", ["value"] = nodes.ListAsync().GetAwaiter().GetResult().Count },
                (JsonNode?)new JsonObject { ["metric"] = "Files", ["value"] = ledger.ListAllFilesAsync().GetAwaiter().GetResult().Count })),
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
            ("industrial/credentials", () => new JsonArray(credentials.ListAsync()
                .GetAwaiter().GetResult()
                .Select(credential => (JsonNode?)new JsonObject
                {
                    ["id"] = credential.Id,
                    ["name"] = credential.Name,
                    ["kind"] = credential.Kind,
                    ["version"] = credential.Version,
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

        // One scoped effect installs both commands; its inverse removes only
        // this owner's registrations once.
        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("industrial/run-collection", payload =>
                    new ValueTask(RunAsync(payload, coordinator, planner, reader, sink, archive,
                        Setting("workflowId", "daily-collection"),
                        Setting("resourceId", "source-1")).AsTask()), owner);
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
        IArchiveSink archive,
        string defaultWorkflowId,
        string defaultResourceId)
    {
        var body = payload as JsonObject ?? new JsonObject();
        var workflowId = body["workflowId"]?.GetValue<string>() ?? defaultWorkflowId;
        var resourceId = body["resourceId"]?.GetValue<string>() ?? defaultResourceId;
        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException("workflowId and resourceId are required");
        }

        var range = planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
        await coordinator.RunAsync(workflowId, resourceId, range, reader, [sink], archive);
    }

    private static UiPageDefinition Page(
        string id, string title, string route, string renderer, string icon,
        string query, int order, string group,
        IReadOnlyList<(string Key, string Title)> columns)
    {
        // Views must be an ARRAY of declarative blocks. The shell consumes
        // these blocks directly; no TSX page is hardcoded in the UI host.
        var views = new JsonArray(new JsonObject
        {
            ["kind"] = "table",
            ["title"] = title,
            ["query"] = query,
            ["columns"] = new JsonArray(columns.Select(column =>
                (JsonNode?)new JsonObject { ["key"] = column.Key, ["title"] = column.Title }).ToArray()),
        });
        return new UiPageDefinition(
            id, title, route, renderer, icon, $"Industrial page: {title}", views,
            Actions:
            [
                new UiPageAction("Run collection", "industrial/run-collection"),
                new UiPageAction("Retry failed", "industrial/retry-failed"),
            ],
            Order: order, Group: group);
    }
}
