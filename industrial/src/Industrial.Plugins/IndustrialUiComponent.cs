using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gocordis.Core;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Plugins;

/// <summary>Industrial application pages and API are Cordis UI contributions:
/// enabling the plugin registers them, disabling withdraws every registration
/// through the owning Fiber's effect stack.</summary>
public sealed class IndustrialUiComponent : Component
{
    // One plugin identity contributes BOTH the backend service/data and the
    // frontend page composition (Koishi shape). The browser bundle is shipped
    // as a client-module UI resource of THIS package; UiPackageBinder owns its
    // lifecycle, so this component must not register a second module entry.

    private static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // PortType is a wire NAME, not an ordinal. The browser parser only
        // accepts the canonical names from Industrial.Contracts.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyDictionary<string, object?> _config;
    private readonly ConcurrentDictionary<string, ResourceTestResult> _resourceTests = new(StringComparer.Ordinal);

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
        IndustrialKeys.ResourceConnectionTester,
        IndustrialKeys.MachineRegistry,
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
        var resourceTester = context.Require(IndustrialKeys.ResourceConnectionTester);
        var machines = context.Require(IndustrialKeys.MachineRegistry);
        var workflows = context.Require(IndustrialKeys.WorkflowRegistry);
        var nodes = context.Require(IndustrialKeys.NodeCatalog);
        var ledger = context.Require(IndustrialKeys.CollectionLedger);
        var credentials = context.Require(IndustrialKeys.CredentialVault);
        var coordinator = context.Require(IndustrialKeys.CollectionCoordinator);
        var planner = context.Require(IndustrialKeys.HistoricalRangePlanner);
        var reader = context.Require(IndustrialKeys.RecordReader);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
        var owner = ContributionOwner.ForContext(context, "industrial.ui", "industrial-ui");

        var pages = new[]
        {
            Page("industrial.dashboard", "Industrial Dashboard", "/industrial", "dashboard", "Dashboard",
                "industrial/dashboard-rows", 10, "Industrial",
                [("metric", "Metric"), ("value", "Value")]),
            new UiPageDefinition(
                "industrial.resources", "Resources", "/industrial/resources", "generic", "Database",
                "Configure sources and targets",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-resource-console",
                    ["query"] = "industrial/resources",
                }),
                Order: 20, Group: "Industrial"),
            // User-owned workflow creation lives in the client module; the
            // page and its data query remain backend contributions.
            new UiPageDefinition(
                "industrial.workflows", "Workflows", "/industrial/workflows", "generic", "Apartment",
                "Create and open collection workflows",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-workflow-list",
                    ["query"] = "industrial/workflows",
                }),
                Order: 30, Group: "Industrial", Home: true),
            // The backend owns the PAGE; the packaged client module only
            // contributes the renderer for this declarative block kind. This
            // is the Koishi-shaped split: one plugin, backend service + UI data.
            new UiPageDefinition(
                "industrial.designer", "Workflow Designer", "/industrial/designer", "generic", "Branches",
                "Industrial workflow designer",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-workflow-canvas",
                    ["title"] = "Workflow",
                    ["query"] = "industrial/workflow-definitions",
                }),
                Order: 40, Group: "Industrial"),
            new UiPageDefinition(
                "industrial.executions", "Execution Center", "/industrial/executions", "generic", "Activity",
                "Runs, files and per-target delivery results",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-execution-center",
                    ["query"] = "industrial/execution-center",
                }),
                Order: 50, Group: "Industrial"),
            // File processing AND per-target delivery status are both visible.
            new UiPageDefinition(
                "industrial.collections", "Collection History", "/industrial/collections", "generic", "History",
                "Industrial collection and delivery history",
                new JsonArray(
                    new JsonObject
                    {
                        ["kind"] = "table", ["title"] = "Files", ["query"] = "industrial/collections",
                        ["columns"] = new JsonArray(
                            (JsonNode?)new JsonObject { ["key"] = "path", ["title"] = "Path" },
                            (JsonNode?)new JsonObject { ["key"] = "sourceDate", ["title"] = "Source date" },
                            (JsonNode?)new JsonObject { ["key"] = "status", ["title"] = "Status" }),
                    },
                    new JsonObject
                    {
                        ["kind"] = "table", ["title"] = "Target deliveries", ["query"] = "industrial/deliveries",
                        ["columns"] = new JsonArray(
                            (JsonNode?)new JsonObject { ["key"] = "targetId", ["title"] = "Target" },
                            (JsonNode?)new JsonObject { ["key"] = "status", ["title"] = "Status" },
                            (JsonNode?)new JsonObject { ["key"] = "records", ["title"] = "Records" },
                            (JsonNode?)new JsonObject { ["key"] = "attempts", ["title"] = "Attempts" },
                            (JsonNode?)new JsonObject { ["key"] = "error", ["title"] = "Error" }),
                        ["rowActions"] = new JsonArray(new JsonObject
                        {
                            ["label"] = "重试此目标",
                            ["command"] = "industrial/retry-failed",
                            ["args"] = new JsonObject { ["targetId"] = "$row.targetId" },
                        }),
                    }),
                Order: 60, Group: "Industrial"),
            new UiPageDefinition(
                "industrial.file-details", "File Details", "/industrial/files", "generic", "FileText",
                "Per-file processing details and actionable failure diagnosis",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-file-details",
                    ["query"] = "industrial/file-details",
                }),
                Order: 65, Group: "Industrial"),
            Page("industrial.machines", "Machine Management", "/industrial/machines", "generic", "Desktop",
                "industrial/machines", 70, "Industrial",
                [("id", "Id"), ("name", "Name"), ("endpoint", "Endpoint"), ("enabled", "Enabled")]),
            new UiPageDefinition(
                "industrial.credentials", "Credential Management", "/industrial/credentials", "generic", "Key",
                "Manage credential references without exposing secrets",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-credential-console",
                    ["query"] = "industrial/credentials",
                }),
                Order: 80, Group: "Industrial"),
            Page("industrial.tasks", "Collection Tasks", "/industrial/tasks", "generic", "Schedule",
                "industrial/workflows", 90, "Industrial",
                [("workflowId", "Workflow"), ("version", "Version"), ("name", "Name")]),
        };
        foreach (var page in pages)
        {
            await context.RegisterUiPageAsync(ui, owner, page);
            await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
                $"industrial.route.{page.Id}", page.Route, page.Id, Order: page.Order));
            await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
                $"industrial.nav.{page.Id}", page.Title, page.Route, page.Icon, page.Group,
                page.Id, page.Order));
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
            ("industrial/resources", () =>
            {
                var credentialNames = credentials.ListAsync().GetAwaiter().GetResult()
                    .ToDictionary(credential => credential.Id, credential => credential.Name);
                return new JsonArray(resources.ListAsync()
                    .GetAwaiter().GetResult()
                    .Select(resource => (JsonNode?)new JsonObject
                    {
                        ["id"] = resource.Id,
                        ["name"] = resource.Name,
                        ["type"] = resource.Type,
                        ["provider"] = resource.Provider,
                        ["enabled"] = resource.Enabled,
                        ["version"] = resource.Version,
                        ["credentialId"] = resource.CredentialId is null
                            ? null
                            : JsonValue.Create(resource.CredentialId),
                        ["credentialName"] = resource.CredentialId is { } id &&
                                             credentialNames.TryGetValue(id, out var name)
                            ? JsonValue.Create(name)
                            : null,
                    }).ToArray());
            }),
            ("industrial/workflows", () => new JsonArray(workflows.ListAsync()
                .GetAwaiter().GetResult()
                .Select(workflow => (JsonNode?)new JsonObject
                {
                    ["workflowId"] = workflow.WorkflowId,
                    ["version"] = workflow.Version,
                    ["name"] = workflow.Name,
                }).ToArray())),
            // Full descriptors (ports, config schema, activity type) are the
            // contract between the backend catalog and the browser block. The
            // UI never hardcodes node types: a new plugin contributes a node
            // description and the palette updates without a frontend rebuild.
            ("industrial/nodes", () => new JsonArray(nodes.ListAsync()
                .GetAwaiter().GetResult()
                .Select(node => JsonSerializer.SerializeToNode(node, WireJson)!)
                .ToArray())),
            ("industrial/workflow-definitions", () => new JsonArray(workflows.ListAsync()
                .GetAwaiter().GetResult()
                .Select(workflow => JsonSerializer.SerializeToNode(workflow, WireJson)!)
                .ToArray())),
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
                    ["sourceResourceId"] = file.Identity.SourceResourceId,
                    ["sourceDate"] = file.SourceDate.ToString("O"),
                    ["status"] = file.Status,
                }).ToArray())),
            ("industrial/deliveries", () => new JsonArray(ledger.ListAllDeliveriesAsync()
                .GetAwaiter().GetResult()
                .Select(delivery => (JsonNode?)new JsonObject
                {
                    ["id"] = delivery.Id,
                    ["collectionFileId"] = delivery.CollectionFileId,
                    ["targetId"] = delivery.TargetId,
                    ["targetKind"] = delivery.TargetKind,
                    ["status"] = delivery.Status,
                    ["attempts"] = delivery.Attempts,
                    ["records"] = delivery.Records,
                    ["error"] = delivery.Error is null ? null : JsonValue.Create(delivery.Error),
                    ["diagnosis"] = Diagnosis(delivery.Error),
                }).ToArray())),
            ("industrial/machines", () => new JsonArray(machines.ListAsync()
                .GetAwaiter().GetResult()
                .Select(machine => (JsonNode?)new JsonObject
                {
                    ["id"] = machine.Id,
                    ["name"] = machine.Name,
                    ["endpoint"] = machine.Endpoint,
                    ["credentialId"] = machine.CredentialId is null
                        ? null
                        : JsonValue.Create(machine.CredentialId),
                    ["enabled"] = machine.Enabled,
                }).ToArray())),
            ("industrial/execution-center", () => new JsonObject
            {
                ["runs"] = new JsonArray(ledger.ListRunsAsync().GetAwaiter().GetResult()
                    .Select(run => (JsonNode?)new JsonObject
                    {
                        ["id"] = run.Id,
                        ["workflowId"] = run.WorkflowId,
                        ["status"] = run.Status,
                        ["startedAt"] = run.StartedAt.ToString("O"),
                        ["finishedAt"] = run.FinishedAt?.ToString("O"),
                        ["error"] = run.Error is null ? null : JsonValue.Create(run.Error),
                    }).ToArray()),
                ["deliveries"] = new JsonArray(ledger.ListAllDeliveriesAsync().GetAwaiter().GetResult()
                    .Select(delivery => (JsonNode?)new JsonObject
                    {
                        ["id"] = delivery.Id,
                        ["collectionFileId"] = delivery.CollectionFileId,
                        ["targetId"] = delivery.TargetId,
                        ["targetKind"] = delivery.TargetKind,
                        ["status"] = delivery.Status,
                        ["attempts"] = delivery.Attempts,
                        ["records"] = delivery.Records,
                        ["error"] = delivery.Error is null ? null : JsonValue.Create(delivery.Error),
                        ["diagnosis"] = Diagnosis(delivery.Error),
                    }).ToArray()),
            }),
            ("industrial/file-details", () => new JsonArray(ledger.ListAllFilesAsync()
                .GetAwaiter().GetResult()
                .Select(file =>
                {
                    var rows = ledger.ListAllDeliveriesAsync().GetAwaiter().GetResult()
                        .Where(delivery => delivery.CollectionFileId == file.Id)
                        .Select(delivery => (JsonNode?)new JsonObject
                        {
                            ["id"] = delivery.Id,
                            ["targetId"] = delivery.TargetId,
                            ["targetKind"] = delivery.TargetKind,
                            ["status"] = delivery.Status,
                            ["attempts"] = delivery.Attempts,
                            ["records"] = delivery.Records,
                            ["accepted"] = delivery.Accepted,
                            ["rejected"] = delivery.Rejected,
                            ["warnings"] = delivery.Warnings,
                            ["error"] = delivery.Error is null ? null : JsonValue.Create(delivery.Error),
                            ["diagnosis"] = Diagnosis(delivery.Error),
                        }).ToArray();
                    return (JsonNode)new JsonObject
                    {
                        ["id"] = file.Id,
                        ["path"] = file.Identity.RelativePath,
                        ["identityKey"] = file.Identity.Key,
                        ["sourceResourceId"] = file.Identity.SourceResourceId,
                        ["length"] = file.Identity.Length,
                        ["sourceDate"] = file.SourceDate.ToString("O"),
                        ["collectedAt"] = file.CollectedAt.ToString("O"),
                        ["status"] = file.Status,
                        ["deliveries"] = new JsonArray(rows),
                    };
                }).ToArray())),
            ("industrial/resource-tests", () => new JsonArray(_resourceTests
                .Select(pair => (JsonNode?)new JsonObject
                {
                    ["resourceId"] = pair.Key,
                    ["ok"] = pair.Value.Ok,
                    ["detail"] = pair.Value.Detail,
                }).OrderBy(node => node!["resourceId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray())),
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
                api.RegisterCommand("industrial/retry-failed", payload =>
                {
                    var body = payload as JsonObject;
                    var targetId = body?["targetId"] is JsonValue value &&
                                   value.TryGetValue<string>(out var id)
                        ? id
                        : null;
                    return new ValueTask(coordinator.RetryFailedAsync(targetId, [sink], archive).AsTask());
                }, owner);
                api.RegisterCommand("industrial/test-resource", payload =>
                {
                    var body = payload as JsonObject;
                    var id = Text(body, "id") ?? Text(body, "resourceId")
                             ?? throw new InvalidOperationException("resource id is required");
                    return new ValueTask(TestResourceAsync(id, resources, resourceTester, _resourceTests).AsTask());
                }, owner);
                api.RegisterCommand("industrial/save-machine", payload =>
                    new ValueTask(SaveMachineAsync(payload, machines).AsTask()), owner);
                api.RegisterCommand("industrial/delete-machine", payload =>
                    new ValueTask(DeleteMachineAsync(payload, machines).AsTask()), owner);
                api.RegisterCommand("industrial/save-resource", payload =>
                    new ValueTask(SaveResourceAsync(payload, resources).AsTask()), owner);
                api.RegisterCommand("industrial/delete-resource", payload =>
                    new ValueTask(DeleteResourceAsync(payload, resources).AsTask()), owner);
                api.RegisterCommand("industrial/save-credential", payload =>
                    new ValueTask(SaveCredentialAsync(payload, credentials).AsTask()), owner);
                api.RegisterCommand("industrial/rotate-credential", payload =>
                    new ValueTask(RotateCredentialAsync(payload, credentials).AsTask()), owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });

        return null;
    }

    private static string? Text(JsonObject? body, string key) =>
        body?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static async ValueTask SaveMachineAsync(JsonNode? payload, IMachineRegistry machines)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("machine id is required");
        var existing = await machines.GetAsync(id);
        await machines.RegisterAsync(new MachineDefinition(
            id,
            Text(body, "name") ?? id,
            Text(body, "endpoint") ?? string.Empty,
            Text(body, "credentialId"),
            body["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var isEnabled) && isEnabled,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
    }

    private static async ValueTask DeleteMachineAsync(JsonNode? payload, IMachineRegistry machines)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("machine id is required");
        await machines.RemoveAsync(id);
    }

    private static async ValueTask SaveResourceAsync(JsonNode? payload, IResourceRegistry resources)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("resource id is required");
        var existing = await resources.GetAsync(id);
        var configuration = body["config"] switch
        {
            JsonObject config => JsonSerializer.Deserialize<Dictionary<string, object?>>(config.ToJsonString()) ?? [],
            JsonValue value when value.TryGetValue<string>(out var json) && !string.IsNullOrWhiteSpace(json) =>
                JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? [],
            _ => new Dictionary<string, object?>(),
        };
        await resources.RegisterAsync(new ResourceDefinition(
            id,
            Text(body, "type") ?? existing?.Type ?? "unknown",
            Text(body, "name") ?? id,
            Text(body, "provider") ?? existing?.Provider ?? "manual",
            configuration,
            Text(body, "credentialId"),
            existing?.Version ?? 0,
            body["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var isEnabled) && isEnabled,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
    }

    private static async ValueTask DeleteResourceAsync(JsonNode? payload, IResourceRegistry resources)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("resource id is required");
        await resources.RemoveAsync(id);
    }

    private static async ValueTask SaveCredentialAsync(JsonNode? payload, ICredentialVault credentials)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        await credentials.PutAsync(id, Text(body, "name") ?? id,
            Text(body, "kind") ?? "username-password", Text(body, "secret") ?? string.Empty);
    }

    private static async ValueTask RotateCredentialAsync(JsonNode? payload, ICredentialVault credentials)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        await credentials.RotateAsync(id, Text(body, "secret") ?? string.Empty);
    }

    private static JsonObject Diagnosis(string? error)
    {
        var diagnosis = FailureDiagnoses.Classify(error);
        return new JsonObject
        {
            ["code"] = diagnosis.Code,
            ["message"] = diagnosis.Message,
            ["action"] = diagnosis.Action,
        };
    }

    private static async ValueTask TestResourceAsync(
        string id, IResourceRegistry resources, IResourceConnectionTester tester,
        ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var resource = await resources.GetAsync(id)
                       ?? throw new InvalidOperationException($"resource '{id}' was not found");
        var result = await tester.TestAsync(resource);
        results[id] = result;
        if (!result.Ok)
        {
            throw new InvalidOperationException(result.Detail);
        }
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
        IReadOnlyList<(string Key, string Title)> columns,
        string? linkColumnKey = null, string? linkHref = null, bool home = false)
    {
        // Views must be an ARRAY of declarative blocks. The shell consumes
        // these blocks directly; no TSX page is hardcoded in the UI host.
        var views = new JsonArray(new JsonObject
        {
            ["kind"] = "table",
            ["title"] = title,
            ["query"] = query,
            ["columns"] = new JsonArray(columns.Select(column =>
            {
                var cell = new JsonObject { ["key"] = column.Key, ["title"] = column.Title };
                if (linkHref is not null && column.Key == linkColumnKey) cell["href"] = linkHref;
                return (JsonNode?)cell;
            }).ToArray()),
        });
        return new UiPageDefinition(
            id, title, route, renderer, icon, $"Industrial page: {title}", views,
            Actions:
            [
                new UiPageAction("Run collection", "industrial/run-collection"),
                new UiPageAction("Retry failed", "industrial/retry-failed"),
            ],
            Order: order, Group: group, Home: home);
    }
}
