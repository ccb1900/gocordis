using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gocordis.Core;
using Gocordis.Plugins;
using ExcelDataReader;
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
        IndustrialKeys.FileProfiles, IndustrialKeys.CollectionBindings,
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
        var fileProfiles = context.Require(IndustrialKeys.FileProfiles);
        var bindings = context.Require(IndustrialKeys.CollectionBindings);
        var configBindings = bindings;
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
                            ["args"] = new JsonObject
                            {
                                ["deliveryId"] = "$row.id",
                                ["targetId"] = "$row.targetId",
                            },
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
            new UiPageDefinition(
                "industrial.machines", "Machine Management", "/industrial/machines", "generic", "Desktop",
                "Manage machines and the resources bound to them",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-machine-console",
                    ["query"] = "industrial/machines",
                    ["resourcesQuery"] = "industrial/machine-resources",
                }),
                Order: 70, Group: "Industrial"),
            new UiPageDefinition(
                "industrial.file-profiles", "File Profiles", "/industrial/file-profiles", "generic", "FileText",
                "Reusable parsing, mapping and validation profiles",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-file-profiles",
                    ["query"] = "industrial/file-profiles",
                }),
                Order: 78, Group: "Industrial"),
            new UiPageDefinition(
                "industrial.bindings", "Collection Bindings", "/industrial/bindings", "generic", "DeploymentUnit",
                "Bind machines, file profiles and workflow templates without copying workflows",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-binding-console",
                    ["query"] = "industrial/bindings",
                }),
                Order: 79, Group: "Industrial"),
            new UiPageDefinition(
                "industrial.wizard", "Quick Collection", "/industrial/wizard", "generic", "Thunderbolt",
                "Create a simple directory-to-database collection task",
                new JsonArray(new JsonObject
                {
                    ["kind"] = "industrial-collection-wizard",
                    ["query"] = "industrial/resources",
                    ["nodesQuery"] = "industrial/nodes",
                }),
                Order: 5, Group: "Industrial"),
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
                        ["machineId"] = resource.MachineId is null
                            ? null : JsonValue.Create(resource.MachineId),
                        // Configuration contains no secrets; credentials are separate.
                        ["config"] = JsonSerializer.SerializeToNode(resource.Configuration, WireJson),
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
            ("industrial/credential-types", () => new JsonArray(CredentialKinds.All
                .Select(kind => (JsonNode?)new JsonObject
                {
                    ["kind"] = kind,
                    ["fields"] = new JsonArray(CredentialFields(kind)
                        .Select(field => (JsonNode?)new JsonObject
                        {
                            ["name"] = field, ["label"] = field,
                        }).ToArray()),
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
            ("industrial/machine-resources", () => new JsonArray(resources.ListAsync()
                .GetAwaiter().GetResult()
                .Where(resource => !string.IsNullOrWhiteSpace(resource.MachineId))
                .Select(resource => (JsonNode?)new JsonObject
                {
                    ["id"] = resource.Id,
                    ["name"] = resource.Name,
                    ["type"] = resource.Type,
                    ["machineId"] = resource.MachineId,
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
            ("industrial/file-samples", () =>
            {
                var result = new JsonObject();
                foreach (var resource in resources.ListAsync().GetAwaiter().GetResult())
                {
                    try
                    {
                        var root = resource.Configuration.TryGetValue("root", out var configured)
                            ? configured?.ToString() : null;
                        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                        var file = Directory.GetFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(path => File.GetLastWriteTimeUtc(path)).FirstOrDefault();
                        if (file is null) continue;
                        result[resource.Id] = new JsonArray(File.ReadLines(file).Take(4)
                            .Select(line => (JsonNode?)JsonValue.Create(line)).ToArray());
                    }
                    catch (IOException) { /* sample is diagnostics only */ }
                    catch (UnauthorizedAccessException) { }
                }
                return result;
            }),
            ("industrial/export-config", () => (JsonNode)ConfigurationPackage(
                workflows, nodes, resources, fileProfiles, configBindings)),
            ("industrial/file-profiles", () => new JsonArray(fileProfiles.ListAsync()
                .GetAwaiter().GetResult()
                .Select(profile => (JsonNode?)JsonSerializer.SerializeToNode(profile, WireJson)).ToArray())),
            ("industrial/binding-status", () => new JsonObject
            {
                ["bindings"] = new JsonArray(bindings.ListAsync().GetAwaiter().GetResult()
                    .Select(binding => (JsonNode?)new JsonObject
                    {
                        ["id"] = binding.Id,
                        ["enabled"] = binding.Enabled,
                        ["machineId"] = binding.MachineId,
                        ["fileProfileId"] = binding.FileProfileId,
                        ["workflowVersion"] = binding.WorkflowVersion,
                    }).ToArray()),
                ["runs"] = new JsonArray(ledger.ListRunsAsync().GetAwaiter().GetResult()
                    .Select(run => (JsonNode?)new JsonObject
                    {
                        ["id"] = run.Id, ["workflowId"] = run.WorkflowId,
                        ["status"] = run.Status, ["startedAt"] = run.StartedAt.ToString("O"),
                        ["error"] = run.Error is null ? null : JsonValue.Create(run.Error),
                    }).ToArray()),
                ["deliveries"] = new JsonArray(ledger.ListAllDeliveriesAsync().GetAwaiter().GetResult()
                    .Select(delivery => (JsonNode?)new JsonObject
                    {
                        ["id"] = delivery.Id, ["targetId"] = delivery.TargetId,
                        ["status"] = delivery.Status, ["error"] = delivery.Error is null
                            ? null : JsonValue.Create(delivery.Error),
                    }).ToArray()),
                ["scheduledFires"] = new JsonArray(ledger.ListScheduleFiresAsync().GetAwaiter().GetResult()
                    .Select(fire => (JsonNode?)new JsonObject
                    {
                        ["bindingId"] = BindingSchedule.BindingIdFromFireWorkflowId(fire.WorkflowId),
                        ["dueAtUtc"] = fire.DueAtUtc.ToString("O"),
                        ["status"] = fire.Status,
                        ["attempts"] = fire.Attempts,
                        ["error"] = fire.Error is null ? null : JsonValue.Create(fire.Error),
                    }).Where(node => node!["bindingId"] is not null).ToArray()),
            }),
            ("industrial/bindings", () => new JsonArray(bindings.ListAsync()
                .GetAwaiter().GetResult()
                .Select(binding => (JsonNode?)JsonSerializer.SerializeToNode(binding, WireJson)).ToArray())),
            ("industrial/resource-tests", () => new JsonArray(_resourceTests
                .Select(pair => (JsonNode?)new JsonObject
                {
                    ["resourceId"] = pair.Key,
                    ["ok"] = pair.Value.Ok,
                    ["detail"] = pair.Value.Detail,
                    ["code"] = pair.Value.Code,
                    ["testedAt"] = pair.Value.TestedAt?.ToString("O"),
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
                // Delivery retry is owned by the Elsa plugin: it must resume the
                // original published workflow instead of bypassing transforms.
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
                api.RegisterCommand("industrial/delete-credential", payload =>
                    new ValueTask(DeleteCredentialAsync(payload, credentials, resources, machines).AsTask()), owner);
                api.RegisterCommand("industrial/upgrade-binding-version", payload =>
                    new ValueTask(UpgradeBindingVersionAsync(payload, bindings, workflows).AsTask()), owner);
                api.RegisterCommand("industrial/set-binding-enabled", payload =>
                    new ValueTask(SetBindingEnabledAsync(payload, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/import-config", payload =>
                    new ValueTask(ImportConfigAsync(payload, workflows, nodes, fileProfiles, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/import-file-profiles", payload =>
                    new ValueTask(ImportFileProfilesAsync(payload, fileProfiles).AsTask()), owner);
                api.RegisterCommand("industrial/import-bindings", payload =>
                    new ValueTask(ImportBindingsAsync(payload, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/import-machines", payload =>
                    new ValueTask(ImportMachinesAsync(payload, machines).AsTask()), owner);
                api.RegisterCommand("industrial/save-file-profile", payload =>
                    new ValueTask(SaveFileProfileAsync(payload, fileProfiles).AsTask()), owner);
                api.RegisterCommand("industrial/preview-bindings", payload =>
                    new ValueTask(PreviewBindingsAsync(payload, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/create-bindings", payload =>
                    new ValueTask(CreateBindingsAsync(payload, bindings).AsTask()), owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });

        return null;
    }

    private static string? Text(JsonObject? body, string key) =>
        body?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>One import surface for CSV text and .xlsx workbooks. The browser
    /// sends an Excel file as base64; both formats become the same header + rows
    /// shape so machine, profile and binding imports cannot drift apart.</summary>
    private static (string[] Header, List<string[]> Rows) Tabular(JsonObject body)
    {
        var workbook = Text(body, "xlsx") ?? Text(body, "excel");
        if (!string.IsNullOrWhiteSpace(workbook))
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(Convert.FromBase64String(workbook));
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            var header = new List<string>();
            var rows = new List<string[]>();
            var first = true;
            while (reader.Read())
            {
                var cells = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.GetValue(index)?.ToString()?.Trim() ?? string.Empty)
                    .ToArray();
                if (first)
                {
                    header.AddRange(cells);
                    first = false;
                }
                else if (cells.Any(cell => cell.Length > 0))
                {
                    rows.Add(cells);
                }
            }
            if (header.Count == 0)
                throw new InvalidOperationException("Excel workbook has no header row");
            return (header.ToArray(), rows);
        }

        var csv = Text(body, "csv") ?? string.Empty;
        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return ([], []);
        return (lines[0].Split(',').Select(part => part.Trim()).ToArray(),
            lines.Skip(1).Select(line => line.Split(',')).ToList());
    }

    private static string Cell(string[] header, string[] cells, string name, string fallback = "")
    {
        var index = Array.IndexOf(header, name);
        return index >= 0 && index < cells.Length && cells[index].Trim().Length > 0
            ? cells[index].Trim() : fallback;
    }

    /// <summary>A portable template/config package: explicit format version,
    /// node/resource/credential dependencies, and template parameter names. It
    /// never carries credential secrets or database connection strings.</summary>
    private static JsonObject ConfigurationPackage(
        IWorkflowRegistry workflows, INodeCatalog nodes, IResourceRegistry resources,
        IFileProfileRegistry profiles, ICollectionBindingRegistry bindings)
    {
        var definitions = workflows.ListAsync().GetAwaiter().GetResult();
        var descriptors = nodes.ListAsync().GetAwaiter().GetResult();
        var resourceRows = resources.ListAsync().GetAwaiter().GetResult();
        var profileRows = profiles.ListAsync().GetAwaiter().GetResult();
        var bindingRows = bindings.ListAsync().GetAwaiter().GetResult();
        var nodeIds = new SortedSet<string>(StringComparer.Ordinal);
        var resourceIds = new SortedSet<string>(StringComparer.Ordinal);
        var credentialIds = new SortedSet<string>(StringComparer.Ordinal);
        var parameters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match match in Regex.Matches(text, @"\{\{([A-Za-z0-9_]+)\}\}"))
                parameters.Add(match.Groups[1].Value);
        }
        void Resource(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Contains("{{", StringComparison.Ordinal)) return;
            resourceIds.Add(id);
        }

        foreach (var definition in definitions)
        {
            foreach (var node in definition.Nodes)
            {
                nodeIds.Add(node.Type);
                foreach (var value in node.Config.Values) Scan(value?.ToString());
            }
            foreach (var reference in definition.ResourceReferences)
            {
                Scan(reference);
                Resource(reference);
            }
        }
        foreach (var binding in bindingRows)
        {
            Resource(binding.SourceResourceId);
            foreach (var target in binding.TargetResourceIds) Resource(target);
            Scan(binding.FileProfileId);
        }
        foreach (var resource in resourceRows)
            if (!string.IsNullOrWhiteSpace(resource.CredentialId))
                credentialIds.Add(resource.CredentialId);

        var dependencies = new JsonArray();
        foreach (var id in nodeIds)
        {
            var descriptor = descriptors.FirstOrDefault(item =>
                string.Equals(item.NodeTypeId, id, StringComparison.Ordinal));
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "node", ["id"] = id,
                ["version"] = descriptor?.Version ?? string.Empty, ["required"] = true,
            });
        }
        foreach (var id in resourceIds)
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "resource", ["id"] = id, ["required"] = true,
            });
        foreach (var id in profileRows.Select(profile => profile.Id).Distinct(StringComparer.Ordinal))
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "fileProfile", ["id"] = id, ["required"] = false,
            });
        foreach (var id in credentialIds)
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "credential", ["id"] = id,
                ["required"] = true, ["secretIncluded"] = false,
            });

        return new JsonObject
        {
            ["format"] = "gocordis-industrial-config-v1",
            ["formatVersion"] = 2,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["dependencies"] = dependencies,
            ["templateParameters"] = new JsonArray(parameters
                .Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["workflows"] = new JsonArray(definitions
                .Select(item => (JsonNode?)JsonSerializer.SerializeToNode(item, WireJson)).ToArray()),
            ["fileProfiles"] = new JsonArray(profileRows
                .Select(item => (JsonNode?)JsonSerializer.SerializeToNode(item, WireJson)).ToArray()),
            ["bindings"] = new JsonArray(bindingRows
                .Select(item => (JsonNode?)JsonSerializer.SerializeToNode(item, WireJson)).ToArray()),
        };
    }

    private static async ValueTask SaveMachineAsync(JsonNode? payload, IMachineRegistry machines)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("machine id is required");
        var existing = await machines.GetAsync(id);
        await machines.RegisterAsync(new MachineDefinition(
            id,
            Text(body, "name") ?? id,
            Text(body, "endpoint") ?? string.Empty,
            body.ContainsKey("credentialId") ? Text(body, "credentialId") : existing?.CredentialId,
            body["enabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var isEnabled)
                ? isEnabled : existing?.Enabled ?? true,
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
            // An edit that only changes name/credential/enabled must not erase
            // the stored connection configuration.
            null => existing?.Configuration.ToDictionary(pair => pair.Key, pair => pair.Value,
                        StringComparer.Ordinal) ?? new Dictionary<string, object?>(),
            _ => new Dictionary<string, object?>(),
        };
        var credentialId = body["credentialId"] is null
            ? existing?.CredentialId
            : Text(body, "credentialId");
        var enabled = body["enabled"] is JsonValue flag && flag.TryGetValue<bool>(out var isEnabled)
            ? isEnabled
            : existing?.Enabled ?? true;
        await resources.RegisterAsync(new ResourceDefinition(
            id,
            Text(body, "type") ?? existing?.Type ?? "unknown",
            Text(body, "name") ?? id,
            Text(body, "provider") ?? existing?.Provider ?? "manual",
            configuration,
            credentialId,
            existing?.Version ?? 0,
            enabled,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            body.ContainsKey("machineId")
                ? (body["machineId"] is null ? null : Text(body, "machineId"))
                : existing?.MachineId));
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
        var kind = Text(body, "kind") ?? CredentialKinds.UsernamePassword;
        if (!CredentialKinds.All.Contains(kind, StringComparer.Ordinal))
            throw new InvalidOperationException($"unsupported credential kind '{kind}'");
        await credentials.PutAsync(id, Text(body, "name") ?? id,
            kind, Text(body, "secret") ?? string.Empty);
    }

    private static async ValueTask RotateCredentialAsync(JsonNode? payload, ICredentialVault credentials)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        await credentials.RotateAsync(id, Text(body, "secret") ?? string.Empty);
    }

    private static Dictionary<string, string> ReadMap(JsonObject? body)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (body is null) return map;
        foreach (var pair in body)
        {
            var value = pair.Value?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value)) map[pair.Key] = value;
        }
        return map;
    }

    private static async ValueTask UpgradeBindingVersionAsync(
        JsonNode? payload, ICollectionBindingRegistry bindings, IWorkflowRegistry workflows)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var version = body["version"] is JsonValue value && value.TryGetValue<int>(out var parsed)
            ? parsed : throw new InvalidOperationException("version is required");
        var ids = body["ids"] is JsonArray array
            ? array.Select(node => node?.GetValue<string>()).Where(id => id is not null).ToArray()
            : [];
        foreach (var id in ids)
        {
            var binding = await bindings.GetAsync(id!)
                          ?? throw new InvalidOperationException($"binding '{id}' not found");
            if (await workflows.GetPublishedVersionAsync(binding.WorkflowId, version) is null)
                throw new InvalidOperationException(
                    $"workflow {binding.WorkflowId}:v{version} is not published");
            await bindings.RegisterAsync(binding with { WorkflowVersion = version });
        }
    }

    private static async ValueTask SetBindingEnabledAsync(
        JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("binding id is required");
        var binding = await bindings.GetAsync(id)
                      ?? throw new InvalidOperationException($"binding '{id}' not found");
        if (body["enabled"] is not JsonValue flag ||
            !flag.TryGetValue<bool>(out var enabled))
            throw new InvalidOperationException("enabled is required");
        await bindings.RegisterAsync(binding with { Enabled = enabled });
    }

    private static void ValidatePackageDependencies(JsonObject body, INodeCatalog nodes, int formatVersion)
    {
        if (body["dependencies"] is not JsonArray dependencies)
        {
            if (formatVersion >= 2)
                throw new InvalidOperationException("formatVersion 2 requires a dependency manifest");
            return;
        }
        var available = nodes.ListAsync().GetAwaiter().GetResult()
            .Select(descriptor => descriptor.NodeTypeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var entry in dependencies)
        {
            if (entry is not JsonObject dependency)
                throw new InvalidOperationException("dependency manifest entries must be objects");
            var kind = dependency["kind"]?.GetValue<string>();
            var id = dependency["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("dependency manifest entries require kind and id");
            if (kind == "credential" &&
                dependency["secretIncluded"] is JsonValue secret && secret.TryGetValue<bool>(out var included) &&
                included)
                throw new InvalidOperationException("configuration packages must not include credential secrets");
            if (kind == "node" && available.Count > 0 &&
                dependency["required"] is JsonValue required && required.TryGetValue<bool>(out var mandatory) &&
                mandatory && !available.Contains(id))
                throw new InvalidOperationException(
                    $"package requires node type '{id}'; install its provider plugin before import");
        }
    }

    private static async ValueTask ImportConfigAsync(
        JsonNode? payload, IWorkflowRegistry workflows, INodeCatalog nodes,
        IFileProfileRegistry profiles, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var format = Text(body, "format");
        if (format != "gocordis-industrial-config-v1")
            throw new InvalidOperationException("unsupported configuration package format");
        var formatVersion = body["formatVersion"] is JsonValue version &&
                            version.TryGetValue<int>(out var parsedVersion)
            ? parsedVersion
            : 1;
        if (formatVersion is < 1 or > 2)
            throw new InvalidOperationException($"unsupported configuration package formatVersion {formatVersion}");
        ValidatePackageDependencies(body, nodes, formatVersion);
        var resourceMap = ReadMap(body["resourceMap"] as JsonObject);
        var credentialMap = ReadMap(body["credentialMap"] as JsonObject);
        string Map(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            if (credentialMap.TryGetValue(value, out var credential)) return credential;
            return resourceMap.TryGetValue(value, out var resource) ? resource : value;
        }
        if (body["workflows"] is JsonArray workflowRows)
        {
            foreach (var row in workflowRows)
            {
                var definition = JsonSerializer.Deserialize<WorkflowDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (definition is null) continue;
                definition = definition with
                {
                    Nodes = definition.Nodes.Select(node => node with
                    {
                        Config = node.Config.ToDictionary(pair => pair.Key, pair =>
                            pair.Key is "resourceId" or "targetResourceId" && pair.Value is string text
                                ? Map(text) : pair.Value, StringComparer.Ordinal),
                    }).ToArray(),
                    ResourceReferences = definition.ResourceReferences.Select(Map)
                        .Where(value => value.Length > 0).ToArray(),
                };
                await workflows.SaveDraftAsync(definition);
            }
        }
        if (body["fileProfiles"] is JsonArray profileRows)
        {
            foreach (var row in profileRows)
            {
                var profile = JsonSerializer.Deserialize<FileProfileDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (profile is not null) await profiles.RegisterAsync(profile);
            }
        }
        if (body["bindings"] is JsonArray bindingRows)
        {
            foreach (var row in bindingRows)
            {
                var binding = JsonSerializer.Deserialize<CollectionBindingDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (binding is null) continue;
                await bindings.RegisterAsync(binding with
                {
                    SourceResourceId = Map(binding.SourceResourceId),
                    TargetResourceIds = binding.TargetResourceIds.Select(Map).ToArray(),
                });
            }
        }
    }

    private static async ValueTask ImportFileProfilesAsync(
        JsonNode? payload, IFileProfileRegistry profiles)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var (header, rows) = Tabular(body);
        if (rows.Count == 0) return;
        var idIndex = Array.IndexOf(header, "id");
        var nameIndex = Array.IndexOf(header, "name");
        if (idIndex < 0 || nameIndex < 0)
            throw new InvalidOperationException("file profile import requires id and name columns");
        foreach (var cells in rows)
        {
            var id = Cell(header, cells, "id");
            if (id.Length == 0) continue;
            string Field(string name, string fallback) => Cell(header, cells, name, fallback);
            var mapping = new Dictionary<string, object?>();
            foreach (var pair in Field("mapping", string.Empty).Split(';'))
            {
                var parts = pair.Split('=').Select(x => x.Trim()).ToArray();
                if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0) mapping[parts[0]] = parts[1];
            }
            await profiles.RegisterAsync(new FileProfileDefinition(
                id, Field("name", id), 0,
                Field("fileNamePattern", "*.csv"), Field("format", "csv"),
                Field("delimiter", ","),
                bool.TryParse(Field("hasHeader", "true"), out var hasHeader) ? hasHeader : true,
                Field("encoding", "utf-8"), mapping,
                Field("required", string.Empty).Split(';').Where(x => x.Trim().Length > 0)
                    .Select(x => x.Trim()).ToArray(),
                true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    private static async ValueTask ImportBindingsAsync(
        JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var (header, rows) = Tabular(body);
        if (rows.Count == 0) return;
        foreach (var cells in rows)
        {
            var id = Cell(header, cells, "id");
            if (id.Length == 0) continue;
            var kind = Cell(header, cells, "triggerKind", "manual");
            var cron = Cell(header, cells, "cron");
            await bindings.RegisterAsync(new CollectionBindingDefinition(
                id, Cell(header, cells, "name", id), Cell(header, cells, "machineId"),
                Cell(header, cells, "workflowId"),
                int.TryParse(Cell(header, cells, "workflowVersion", "1"), out var version) ? version : 1,
                Cell(header, cells, "fileProfileId"), Cell(header, cells, "sourceResourceId"),
                Cell(header, cells, "targetResourceIds").Split(';').Where(x => x.Trim().Length > 0)
                    .Select(x => x.Trim()).ToArray(),
                new TriggerDefinition(kind, cron.Length > 0 ? cron : null, null,
                    new Dictionary<string, object?>()),
                true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    private static async ValueTask ImportMachinesAsync(JsonNode? payload, IMachineRegistry machines)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var (header, rows) = Tabular(body);
        if (rows.Count == 0) return;
        if (Array.IndexOf(header, "id") < 0 || Array.IndexOf(header, "name") < 0)
            throw new InvalidOperationException("machine import requires id and name columns");
        foreach (var cells in rows)
        {
            var id = Cell(header, cells, "id");
            if (id.Length == 0) continue;
            var existing = await machines.GetAsync(id);
            await machines.RegisterAsync(new MachineDefinition(
                id, Cell(header, cells, "name", id),
                Cell(header, cells, "endpoint"),
                Cell(header, cells, "credentialId", existing?.CredentialId ?? string.Empty),
                existing?.Enabled ?? true,
                existing?.CreatedAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    private static async ValueTask SaveFileProfileAsync(JsonNode? payload, IFileProfileRegistry registry)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("file profile id is required");
        var existing = await registry.GetAsync(id);
        var profile = new FileProfileDefinition(
            id,
            Text(body, "name") ?? id,
            existing?.Version ?? 0,
            Text(body, "fileNamePattern") ?? "*.csv",
            Text(body, "format") ?? "csv",
            Text(body, "delimiter") ?? ",",
            body["hasHeader"] is JsonValue header && header.TryGetValue<bool>(out var hasHeader)
                ? hasHeader : true,
            Text(body, "encoding") ?? "utf-8",
            body["mapping"] is JsonObject map
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(map.ToJsonString()) ?? []
                : existing?.Mapping ?? new Dictionary<string, object?>(),
            body["required"] is JsonArray required
                ? required.Select(node => node?.GetValue<string>() ?? string.Empty)
                    .Where(value => value.Length > 0).ToArray()
                : existing?.RequiredFields ?? [],
            body["enabled"] is JsonValue flag && flag.TryGetValue<bool>(out var enabled) ? enabled : true,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        await registry.RegisterAsync(profile);
    }

    private static BindingRequest ReadBindingRequest(JsonObject body)
    {
        string[] List(string name) => body[name] is JsonArray array
            ? array.Select(node => node?.GetValue<string>() ?? string.Empty)
                .Where(value => value.Length > 0).ToArray()
            : [];
        return new BindingRequest(
            List("machineIds"), List("fileProfileIds"),
            Text(body, "workflowId") ?? string.Empty,
            body["workflowVersion"] is JsonValue version && version.TryGetValue<int>(out var parsed)
                ? parsed : 1,
            Text(body, "sourceResourcePattern") ?? "resource-{machine}",
            List("targetResourceIds"), Text(body, "triggerKind") ?? "manual",
            Text(body, "cron"));
    }

    private sealed record BindingRequest(
        IReadOnlyList<string> MachineIds, IReadOnlyList<string> FileProfileIds,
        string WorkflowId, int WorkflowVersion, string SourceResourcePattern,
        IReadOnlyList<string> TargetResourceIds, string TriggerKind, string? Cron);

    private static async ValueTask PreviewBindingsAsync(JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var request = ReadBindingRequest(payload as JsonObject
            ?? throw new InvalidOperationException("JSON payload required"));
        var preview = await bindings.PreviewBulkAsync(request.MachineIds, request.FileProfileIds,
            request.WorkflowId, request.WorkflowVersion, request.SourceResourcePattern,
            request.TargetResourceIds);
        // Command result is carried through an exception-free query in the UI;
        // here we only validate that the preview can be produced.
        _ = preview;
    }

    private static async ValueTask CreateBindingsAsync(JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var request = ReadBindingRequest(payload as JsonObject
            ?? throw new InvalidOperationException("JSON payload required"));
        await bindings.CreateBulkAsync(request.MachineIds, request.FileProfileIds,
            request.WorkflowId, request.WorkflowVersion, request.SourceResourcePattern,
            request.TargetResourceIds, request.TriggerKind, request.Cron);
    }

    private static async ValueTask DeleteCredentialAsync(
        JsonNode? payload, ICredentialVault credentials,
        IResourceRegistry resources, IMachineRegistry machines)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        var usedByResource = (await resources.ListAsync()).Any(resource => resource.CredentialId == id);
        var usedByMachine = (await machines.ListAsync()).Any(machine => machine.CredentialId == id);
        if (usedByResource || usedByMachine)
            throw new InvalidOperationException(
                $"credential '{id}' is still referenced by resources or machines");
        await credentials.RemoveAsync(id);
    }

    private static string[] CredentialFields(string kind) => kind switch
    {
        CredentialKinds.UsernamePassword => ["username", "password"],
        CredentialKinds.DomainAccount => ["username", "password", "domain"],
        CredentialKinds.ApiToken => ["token"],
        _ => ["secret"],
    };

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
