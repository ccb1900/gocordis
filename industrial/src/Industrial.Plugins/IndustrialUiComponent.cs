using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gocordis.Core;
using Gocordis.Extensions;
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
    // Preview results are per-request: the command stores a report under the
    // caller's request id and publishes a short-lived query under that id. No
    // tab can read another tab's preview, and a stale id cannot gate creation.
    private readonly ConcurrentDictionary<string, JsonObject> _previewResults = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _previewOrder = new();
    private const int MaxPreviewResults = 16;
    // Named paged query variants keep the existing (unpaged) queries intact for
    // the current UI. Page size is configurable but always bounded so a single
    // request cannot materialise unbounded history.
    private const int DefaultPageSize = 200;
    private const int MaxPageSize = 1000;
    private const int MaxPageVariants = 20;
    private JsonObject? _lastImportPreview;
    private JsonObject? _lastBindingPreview;
    private SnapshotCleanupReport? _lastSnapshotReport;
    private string? _snapshotMaintenanceError;
    private StageCleanupReport? _lastStageReport;
    private string? _stageMaintenanceError;

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

    private int IndustrialPageSize => Math.Clamp(
        int.TryParse(Setting("industrialPageSize", DefaultPageSize.ToString()), out var size)
            ? size : DefaultPageSize, 1, MaxPageSize);

    private static (int Skip, int Take) PageWindow(int? limit, int? offset)
    {
        // No limit means the legacy query's full history; a supplied limit is
        // always clamped so a paged request cannot request unbounded rows.
        var take = limit is null ? int.MaxValue : Math.Clamp(limit.Value, 1, MaxPageSize);
        return (Math.Max(0, offset ?? 0), take);
    }

    private string SnapshotRoot
    {
        get
        {
            var configured = Setting("root",
                Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ROOT")
                ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial"));
            var allowDevelopment =
                (_config.TryGetValue("allowDevelopmentKey", out var flag) &&
                 string.Equals(flag?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(
                    Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY"),
                    "true", StringComparison.OrdinalIgnoreCase);
            return Path.Combine(
                IndustrialPaths.NormalizeRoot(configured, allowDevelopment), "industrial-snapshots");
        }
    }

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
        IndustrialKeys.StageOutputStore,
        IndustrialKeys.CredentialVault,
        TimerKeys.Service,
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
        var stageStore = context.Require(IndustrialKeys.StageOutputStore) as StageOutputStore;
        var stageRetention = TimeSpan.FromDays(Math.Clamp(
            int.TryParse(Setting("stageRetentionDays", "7"), out var parsedDays) ? parsedDays : 7,
            0, 365));
        var credentials = context.Require(IndustrialKeys.CredentialVault);
        var owner = ContributionOwner.ForContext(context, "industrial.ui", "industrial-ui");
        var timers = context.Require(TimerKeys.Service);
        var maintenanceMinutes = int.TryParse(
            Setting("snapshotMaintenanceIntervalMinutes", "60"), out var parsedMinutes)
            ? Math.Clamp(parsedMinutes, 1, 1440)
            : 60;
        timers.IntervalOwned(context, TimeSpan.FromMinutes(maintenanceMinutes), async () =>
        {
            try
            {
                _lastSnapshotReport = await SnapshotMaintenance.RunAsync(
                    ledger, SnapshotRoot, context.CancellationToken);
                _snapshotMaintenanceError = null;
            }
            catch (Exception ex)
            {
                // Maintenance is best-effort and retryable on the next tick.
                _snapshotMaintenanceError = ex.Message;
            }
            // One maintenance failure must not suppress the other cleanup.
            await RunStageMaintenanceAsync(ledger, stageStore, stageRetention, context.CancellationToken);
        });

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

        // Shared projections for the default queries and their paged variants.
        // A page variant is a bounded limit/offset window over the same rows;
        // the underlying ledger is read-only and is never mutated by paging.
        JsonNode? ExecutionCenter(int? limit = null, int? offset = null)
        {
            var (skip, take) = PageWindow(limit, offset);
            return new JsonObject
            {
                ["runs"] = new JsonArray(ledger.ListRunsAsync().GetAwaiter().GetResult()
                    .Skip(skip).Take(take)
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
                    .Skip(skip).Take(take)
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
            };
        }

        JsonNode? FileDetails(int? limit = null, int? offset = null)
        {
            var files = ledger.ListAllFilesAsync().GetAwaiter().GetResult();
            // Load the delivery history once into a lookup. The former
            // per-file ListAllDeliveriesAsync call was an N+1 over all history.
            var byFile = ledger.ListAllDeliveriesAsync().GetAwaiter().GetResult()
                .GroupBy(delivery => delivery.CollectionFileId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var (skip, take) = PageWindow(limit, offset);
            return new JsonArray(files.Skip(skip).Take(take).Select(file =>
            {
                var rows = byFile.TryGetValue(file.Id, out var deliveries)
                    ? deliveries.Select(delivery => (JsonNode?)new JsonObject
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
                    }).ToArray()
                    : Array.Empty<JsonNode?>();
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
            }).ToArray());
        }

        JsonNode? Bindings(int? limit = null, int? offset = null)
        {
            var (skip, take) = PageWindow(limit, offset);
            return new JsonArray(bindings.ListAsync().GetAwaiter().GetResult()
                .Skip(skip).Take(take)
                .Select(binding => JsonSerializer.SerializeToNode(binding, WireJson)).ToArray());
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
            ("industrial/execution-center", () => ExecutionCenter()),
            ("industrial/file-details", () => FileDetails()),
            ("industrial/file-samples", () =>
            {
                // Diagnostic preview only: never read an arbitrary configured
                // path, and never echo raw file content outside approved roots.
                var result = new JsonObject();
                var approved = ApprovedRoots();
                if (approved.Length == 0) return result;
                foreach (var resource in resources.ListAsync().GetAwaiter().GetResult())
                {
                    if (!resource.Enabled || resource.Type is not ("filesystem.local" or "filesystem.unc")) continue;
                    try
                    {
                        var root = resource.Configuration.TryGetValue("root", out var configured)
                            ? configured?.ToString() : null;
                        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                        var fullRoot = Path.GetFullPath(root);
                        if (!IsApproved(fullRoot, approved)) continue;
                        var file = Directory.GetFiles(fullRoot, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(path => IsApproved(Path.GetFullPath(path), approved))
                            .OrderByDescending(path => File.GetLastWriteTimeUtc(path)).FirstOrDefault();
                        if (file is null) continue;
                        result[resource.Id] = new JsonArray(File.ReadLines(file).Take(4)
                            .Select(line => (JsonNode?)JsonValue.Create(RedactSample(line))).ToArray());
                    }
                    catch (IOException) { /* sample is diagnostics only */ }
                    catch (UnauthorizedAccessException) { }
                    catch (ArgumentException) { /* malformed path is not a sample */ }
                    catch (NotSupportedException) { }
                }
                return result;
            }),
            ("industrial/export-config", () => (JsonNode)ConfigurationPackage(
                workflows, nodes, resources, fileProfiles, configBindings, machines, credentials)),
            ("industrial/binding-preview", () => _lastBindingPreview is null
                ? null : (JsonNode)_lastBindingPreview.DeepClone()),
            ("industrial/import-preview", () => _lastImportPreview is null
                ? null : (JsonNode)_lastImportPreview.DeepClone()),
            ("industrial/snapshot-status", () => new JsonObject
            {
                ["report"] = _lastSnapshotReport is null
                    ? null : JsonSerializer.SerializeToNode(_lastSnapshotReport, WireJson),
                ["error"] = _snapshotMaintenanceError is null
                    ? null : JsonValue.Create(_snapshotMaintenanceError),
                // Keep the snapshot fields above; stage lifecycle is a separate
                // section so an operator can see counts/bytes/deletions/failures
                // of both cleanups in the one maintenance status query.
                ["stage"] = new JsonObject
                {
                    ["report"] = _lastStageReport is null
                        ? null : JsonSerializer.SerializeToNode(_lastStageReport, WireJson),
                    ["error"] = _stageMaintenanceError is null
                        ? null : JsonValue.Create(_stageMaintenanceError),
                    ["stageRows"] = _lastStageReport?.StageRows ?? 0,
                    ["stageBytes"] = _lastStageReport?.StageBytes ?? 0L,
                    ["candidates"] = _lastStageReport?.Candidates ?? 0,
                    ["rowsDeleted"] = _lastStageReport?.RowsDeleted ?? 0,
                    ["filesDeleted"] = _lastStageReport?.FilesDeleted ?? 0,
                    ["deletedBytes"] = _lastStageReport?.DeletedBytes ?? 0L,
                    ["failures"] = new JsonArray(_lastStageReport?.Failures
                        .Select(failure => (JsonNode?)JsonValue.Create(failure)).ToArray()
                        ?? Array.Empty<JsonNode?>()),
                },
            }),
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
            ("industrial/bindings", () => Bindings()),
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

        // Named paged variants: a caller that needs history in windows can use
        // "<query>/page/{n}" (0-based) with the bounded industrialPageSize.
        // Registration is an effect too, so disabling withdraws every variant.
        var pageSize = IndustrialPageSize;
        foreach (var (name, project) in new (string Name, Func<int?, int?, JsonNode?> Project)[]
        {
            ("industrial/execution-center", ExecutionCenter),
            ("industrial/file-details", FileDetails),
            ("industrial/bindings", Bindings),
        })
        {
            for (var page = 0; page <= MaxPageVariants; page++)
            {
                var pageName = $"{name}/page/{page}";
                var offset = page * pageSize;
                await context.EffectAsync(
                    () =>
                    {
                        api.RegisterQuery(pageName, () => project(pageSize, offset), owner);
                        return ValueTask.CompletedTask;
                    },
                    () => { api.UnregisterQuery(owner, pageName); return ValueTask.CompletedTask; });
            }
        }

        // One scoped effect installs both commands; its inverse removes only
        // this owner's registrations once.
        await context.EffectAsync(
            () =>
            {
                // Collection execution lives ONLY in the Elsa command path
                // (industrial/run-workflow, industrial/run-binding). This
                // component contributes configuration/UI; it must never start
                // or retry a collection directly.
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
                    new ValueTask(DeleteMachineAsync(payload, machines, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/save-resource", payload =>
                    new ValueTask(SaveResourceAsync(payload, resources, _resourceTests).AsTask()), owner);
                api.RegisterCommand("industrial/delete-resource", payload =>
                    new ValueTask(DeleteResourceAsync(payload, resources, workflows, bindings, _resourceTests).AsTask()), owner);
                api.RegisterCommand("industrial/save-credential", payload =>
                    new ValueTask(SaveCredentialAsync(payload, credentials, resources, _resourceTests).AsTask()), owner);
                api.RegisterCommand("industrial/rotate-credential", payload =>
                    new ValueTask(RotateCredentialAsync(payload, credentials, resources, _resourceTests).AsTask()), owner);
                api.RegisterCommand("industrial/delete-credential", payload =>
                    new ValueTask(DeleteCredentialAsync(payload, credentials, resources, machines, _resourceTests).AsTask()), owner);
                api.RegisterCommand("industrial/upgrade-binding-version", payload =>
                    new ValueTask(UpgradeBindingVersionAsync(payload, bindings, workflows).AsTask()), owner);
                api.RegisterCommand("industrial/set-binding-enabled", payload =>
                    new ValueTask(SetBindingEnabledAsync(payload, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/import-config", payload =>
                {
                    var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
                    var requestId = ReadRequestId(body);
                    var report = ImportConfigAsync(body, workflows, nodes, fileProfiles, bindings,
                        machines, credentials, resources, _resourceTests).GetAwaiter().GetResult();
                    PublishPreview(api, owner, requestId, report);
                    return ValueTask.CompletedTask;
                }, owner);
                api.RegisterCommand("industrial/maintain-snapshots", _ =>
                {
                    _lastSnapshotReport = SnapshotMaintenance.RunAsync(
                        ledger, SnapshotRoot, context.CancellationToken).GetAwaiter().GetResult();
                    return ValueTask.CompletedTask;
                }, owner);
                api.RegisterCommand("industrial/stage-maintenance", _ =>
                    new ValueTask(RunStageMaintenanceAsync(
                        ledger, stageStore, stageRetention, context.CancellationToken).AsTask()), owner);
                api.RegisterCommand("industrial/import-file-profiles", payload =>
                    new ValueTask(ImportFileProfilesAsync(payload, fileProfiles).AsTask()), owner);
                api.RegisterCommand("industrial/import-bindings", payload =>
                    new ValueTask(ImportBindingsAsync(payload, bindings).AsTask()), owner);
                api.RegisterCommand("industrial/import-machines", payload =>
                    new ValueTask(ImportMachinesAsync(payload, machines).AsTask()), owner);
                api.RegisterCommand("industrial/preview-import", payload =>
                {
                    var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
                    var requestId = ReadRequestId(body);
                    var report = PreviewImportReport(body);
                    PublishPreview(api, owner, requestId, report);
                    _lastImportPreview = report;
                    return ValueTask.CompletedTask;
                }, owner);
                api.RegisterCommand("industrial/save-file-profile", payload =>
                    new ValueTask(SaveFileProfileAsync(payload, fileProfiles).AsTask()), owner);
                api.RegisterCommand("industrial/preview-bindings", payload =>
                {
                    var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
                    var requestId = ReadRequestId(body);
                    var request = ReadBindingRequest(body);
                    ValidateTrigger(request.TriggerKind, request.Cron, RequestTimezone(body, request), "preview-bindings");
                    var preview = bindings.PreviewBulkAsync(request.MachineIds, request.FileProfileIds,
                        request.WorkflowId, request.WorkflowVersion, request.SourceResourcePattern,
                        request.TargetResourceIds, request.Exclusions).GetAwaiter().GetResult();
                    var report = BindingPreviewReport(preview);
                    PublishPreview(api, owner, requestId, report);
                    _lastBindingPreview = report;
                    return ValueTask.CompletedTask;
                }, owner);
                api.RegisterCommand("industrial/create-bindings", payload =>
                    new ValueTask(CreateBindingsAsync(payload, bindings).AsTask()), owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });

        return null;
    }

    /// <summary>Stage cleanup is ledger-driven: the store owns files and stage
    /// rows, the ledger decides which workflow instances are terminal. Locking
    /// through the same ledger keeps cleanup from racing a workflow that is
    /// about to reuse committed stages. Failures are published in the report;
    /// maintenance itself never throws on a bad spool or row.</summary>
    private async ValueTask RunStageMaintenanceAsync(
        ICollectionLedger ledger, StageOutputStore? stageStore, TimeSpan retention, CancellationToken ct)
    {
        try
        {
            if (stageStore is null)
            {
                _stageMaintenanceError = "stage store does not support ledger-driven cleanup";
                return;
            }
            var lease = await MaintenanceCoordination.BeginCleanupAsync(ledger, ct);
            if (lease is null)
            {
                _lastStageReport = new StageCleanupReport(0, 0, 0, 0, 0, 0, [], [],
                    true, DateTimeOffset.UtcNow);
                _stageMaintenanceError = null;
                return;
            }
            try
            {
                var runs = await ledger.ListRunsAsync(ct);
                var active = runs.Where(run => run.InstanceId is not null &&
                                               !CollectionStatus.IsTerminal(run.Status))
                                 .Select(run => run.InstanceId!)
                                 .ToHashSet(StringComparer.Ordinal);
                var terminal = runs.Where(run => run.InstanceId is not null &&
                                                 CollectionStatus.IsTerminal(run.Status))
                                   .Select(run => run.InstanceId!)
                                   .Where(id => !active.Contains(id))
                                   .ToHashSet(StringComparer.Ordinal);
                _lastStageReport = await stageStore.CleanupAsync(
                    terminal, retention, lease, ct);
            }
            finally
            {
                await lease.ReleaseAsync();
            }
            _stageMaintenanceError = _lastStageReport.Failures.Count == 0
                ? null : string.Join("; ", _lastStageReport.Failures.Take(5));
        }
        catch (Exception ex)
        {
            // A maintenance tick is retryable; record it for snapshot-status.
            _stageMaintenanceError = ex.Message;
        }
    }

    private static string? Text(JsonObject? body, string key) =>
        body?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string[] SplitList(string? value) =>
        (value ?? string.Empty).Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();

    private static string ProviderFor(string type) => type switch
    {
        "filesystem.local" or "filesystem.unc" => "local",
        "sqlserver" or "postgresql" or "mysql" or "sqlite" => type,
        _ => "manual",
    };

    // Keys that describe one connection shape. A type change must retire the
    // old shape's keys; an empty input deletes the specific key the user
    // cleared (instead of silently keeping a stale value).
    private static string[] TypeConfigKeys(string type) => type switch
    {
        "filesystem.local" or "filesystem.unc" => ["root", "path"],
        "sqlserver" => ["server", "host", "database", "initialCatalog", "initial_catalog", "table"],
        "postgresql" or "mysql" => ["host", "database", "port"],
        "sqlite" => ["database", "path", "port"],
        _ => [],
    };

    private static Dictionary<string, object?> MergedConfiguration(
        JsonNode? payload, ResourceDefinition? existing, string type)
    {
        var config = existing is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : existing.Configuration.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Dictionary<string, object?>? incoming = payload switch
        {
            JsonObject map => JsonSerializer.Deserialize<Dictionary<string, object?>>(map.ToJsonString()),
            JsonValue value when value.TryGetValue<string>(out var json) && !string.IsNullOrWhiteSpace(json) =>
                JsonSerializer.Deserialize<Dictionary<string, object?>>(json),
            _ => null,
        };
        if (incoming is not null)
        {
            foreach (var (key, value) in incoming)
            {
                if (value is null || value is string text && string.IsNullOrWhiteSpace(text)) config.Remove(key);
                else config[key] = value;
            }
        }
        if (existing is not null && !string.Equals(existing.Type, type, StringComparison.Ordinal))
        {
            var retained = TypeConfigKeys(type).ToHashSet(StringComparer.Ordinal);
            foreach (var key in TypeConfigKeys(existing.Type))
                if (!retained.Contains(key)) config.Remove(key);
        }
        return config;
    }

    /// <summary>Pragmatic five-to-seven field cron check. Elsa/Cronos remains
    /// the execution authority; this guard only rejects values that can never
    /// parse, so a bad schedule cannot be stored without an error.</summary>
    private static bool CronIsValid(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return false;
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 5 or > 7) return false;
        var months = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
        var days = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        int[] limits = fields.Length switch
        {
            5 => [59, 23, 31, 12, 7],
            6 => [59, 59, 23, 31, 12, 7],
            _ => [59, 59, 23, 31, 12, 7, 9999],
        };
        for (var position = 0; position < fields.Length; position++)
        {
            var field = fields[position];
            if (field.Length == 0 || field.Any(ch =>
                    !(char.IsAsciiLetterOrDigit(ch) || ch is '*' or '?' or ',' or '-' or '/' or '#' or 'L' or 'W')))
                return false;
            foreach (var piece in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var term = piece.Split('/').FirstOrDefault() ?? string.Empty;
                term = term.TrimEnd('L', 'W');
                if (term.Length == 0 || term is "*" or "?") continue;
                if (term.Contains('#'))
                {
                    var parts = term.Split('#');
                    var weekdayLimit = fields.Length == 5 ? limits[4] : fields.Length == 6 ? limits[5] : limits[6];
                    if (parts.Length != 2 || !int.TryParse(parts[0], out var ordinal) ||
                        !int.TryParse(parts[1], out var weekday) || ordinal < 1 || weekday > weekdayLimit) return false;
                    continue;
                }
                if (months.Contains(term, StringComparer.OrdinalIgnoreCase) ||
                    days.Contains(term, StringComparer.OrdinalIgnoreCase)) continue;
                var endpoints = term.Split('-');
                if (endpoints.Any(endpoint => !int.TryParse(endpoint, out var number) ||
                                              number < 0 || number > limits[position])) return false;
                if (term.All(char.IsAsciiDigit)) continue;
            }
        }
        return true;
    }

    private static void ValidateTrigger(string kind, string? cron, string? timezone, string where)
    {
        if (kind is not ("manual" or "schedule" or "event"))
            throw new InvalidOperationException($"{where}: unknown trigger kind '{kind}'");
        if (kind == "schedule" && !CronIsValid(cron))
            throw new InvalidOperationException($"{where}: schedule trigger requires a valid cron expression, got '{cron}'");
        if (!string.IsNullOrWhiteSpace(timezone) &&
            !TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
            throw new InvalidOperationException($"{where}: unknown timezone '{timezone}'");
    }

    // The UI documents Python-style named groups; .NET uses (?<name>...).
    // Normalize at the boundary so the stored rule is executable as-is.
    private static string NormalizeRegex(string pattern) =>
        pattern.Replace("(?P<", "(?<", StringComparison.Ordinal);

    private static void ValidateMetadataPattern(string pattern, string where)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        try { _ = new Regex(NormalizeRegex(pattern)); }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{where}: invalid metadata regex '{pattern}': {ex.Message}");
        }
    }

    private string[] ApprovedRoots()
    {
        var candidates = new List<string>();
        foreach (var key in new[] { "sampleDataRoots", "dataRoots", "dataRoot", "sourceRoot", "root" })
            if (_config.TryGetValue(key, out var configured) && configured is not null)
                candidates.AddRange(SplitList(configured.ToString()));
        // Packaged hosts load declarations from the app manifest, so the
        // component config may be empty even when the host exported these
        // roots as environment variables.
        foreach (var key in new[]
                 {
                     "GOCORDIS_INDUSTRIAL_DATA_ROOTS", "GOCORDIS_INDUSTRIAL_SOURCE_ROOT",
                     "GOCORDIS_INDUSTRIAL_ROOT",
                 })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value)) candidates.AddRange(SplitList(value));
        }
        return candidates.Select(path =>
        {
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }).Where(path => path is not null).Select(path => path!)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsApproved(string fullPath, string[] approved)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return approved.Any(root => fullPath.Equals(root, comparison) ||
            fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison));
    }

    private static string RedactSample(string line)
    {
        const int limit = 240;
        var redacted = Regex.Replace(line,
            @"(?i)(password|passwd|secret|token|api[_-]?key|connectionstring)\s*[""']?\s*[=:]\s*[""']?[^,;\s""'}]+",
            "$1=***");
        return redacted.Length <= limit ? redacted : redacted[..limit] + "…（已截断）";
    }

    private static string PreviewQueryName(string requestId) => $"industrial/preview-result/{requestId}";

    private static string ReadRequestId(JsonObject body)
    {
        var id = Text(body, "requestId");
        if (string.IsNullOrWhiteSpace(id)) return Guid.NewGuid().ToString("N");
        if (id.Length > 80 || id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw new InvalidOperationException("invalid preview requestId");
        return id;
    }

    private void PublishPreview(ApiGateway api, ContributionOwner owner, string requestId, JsonObject report)
    {
        var name = PreviewQueryName(requestId);
        if (!_previewResults.ContainsKey(requestId))
            api.RegisterQuery(name, () => _previewResults.TryGetValue(requestId, out var stored)
                ? (JsonNode?)stored.DeepClone() : null, owner);
        _previewResults[requestId] = report;
        _previewOrder.Enqueue(requestId);
        while (_previewResults.Count > MaxPreviewResults && _previewOrder.TryDequeue(out var oldest))
        {
            _previewResults.TryRemove(oldest, out _);
            api.UnregisterQuery(owner, PreviewQueryName(oldest));
        }
    }

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
        if (string.IsNullOrWhiteSpace(csv)) return ([], []);
        using var text = new StringReader(csv);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };
        using var parser = new CsvParser(text, configuration);
        if (!parser.Read()) return ([], []);
        var csvHeader = parser.Record?.Select(value => value.Trim()).ToArray() ?? [];
        var csvRows = new List<string[]>();
        while (parser.Read())
        {
            csvRows.Add(parser.Record?.ToArray() ?? []);
        }
        return (csvHeader, csvRows);
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
    private static readonly string[] SafeResourceConfigKeys =
        ["root", "path", "server", "host", "database", "port", "table", "directoryPattern", "fileNamePattern"];

    private static JsonObject ConfigurationPackage(
        IWorkflowRegistry workflows, INodeCatalog nodes, IResourceRegistry resources,
        IFileProfileRegistry profiles, ICollectionBindingRegistry bindings,
        IMachineRegistry machines, ICredentialVault credentials)
    {
        var definitions = workflows.ListAsync().GetAwaiter().GetResult();
        var descriptors = nodes.ListAsync().GetAwaiter().GetResult();
        var resourceRows = resources.ListAsync().GetAwaiter().GetResult();
        var profileRows = profiles.ListAsync().GetAwaiter().GetResult();
        var bindingRows = bindings.ListAsync().GetAwaiter().GetResult();
        var machineRows = machines.ListAsync().GetAwaiter().GetResult();
        var credentialRows = credentials.ListAsync().GetAwaiter().GetResult();
        var nodeIds = new SortedSet<string>(StringComparer.Ordinal);
        var resourceIds = new SortedSet<string>(StringComparer.Ordinal);
        var profileIds = new SortedSet<string>(StringComparer.Ordinal);
        var machineIds = new SortedSet<string>(StringComparer.Ordinal);
        var workflowIds = new SortedSet<string>(StringComparer.Ordinal);
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
            workflowIds.Add(definition.WorkflowId);
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
            if (!string.IsNullOrWhiteSpace(binding.FileProfileId)) profileIds.Add(binding.FileProfileId);
            if (!string.IsNullOrWhiteSpace(binding.MachineId)) machineIds.Add(binding.MachineId);
            if (!string.IsNullOrWhiteSpace(binding.WorkflowId)) workflowIds.Add(binding.WorkflowId);
            Scan(binding.FileProfileId);
        }
        foreach (var resource in resourceRows)
        {
            Resource(resource.Id);
            if (!string.IsNullOrWhiteSpace(resource.CredentialId)) credentialIds.Add(resource.CredentialId);
            if (!string.IsNullOrWhiteSpace(resource.MachineId)) machineIds.Add(resource.MachineId);
        }
        foreach (var machine in machineRows)
            if (!string.IsNullOrWhiteSpace(machine.CredentialId)) credentialIds.Add(machine.CredentialId);
        foreach (var credential in credentialRows) credentialIds.Add(credential.Id);

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
                ["kind"] = "fileProfile", ["id"] = id, ["required"] = profileIds.Contains(id),
            });
        foreach (var id in machineRows.Select(machine => machine.Id).Distinct(StringComparer.Ordinal))
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "machine", ["id"] = id, ["required"] = machineIds.Contains(id),
            });
        foreach (var id in workflowIds)
        {
            var declared = definitions.FirstOrDefault(item =>
                string.Equals(item.WorkflowId, id, StringComparison.Ordinal));
            dependencies.Add((JsonNode)new JsonObject
            {
                ["kind"] = "workflow", ["id"] = id,
                ["version"] = declared?.Version.ToString() ?? string.Empty, ["required"] = true,
            });
        }
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
            ["machines"] = new JsonArray(machineRows
                .Select(item => (JsonNode?)JsonSerializer.SerializeToNode(item, WireJson)).ToArray()),
            ["resources"] = new JsonArray(resourceRows.Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item.Id, ["name"] = item.Name, ["type"] = item.Type,
                ["provider"] = item.Provider, ["enabled"] = item.Enabled,
                ["version"] = item.Version, ["machineId"] = item.MachineId is null
                    ? null : JsonValue.Create(item.MachineId),
                ["credentialId"] = item.CredentialId is null
                    ? null : JsonValue.Create(item.CredentialId),
                // Allow-listed connection fields only: no secrets, no connection strings.
                ["config"] = JsonSerializer.SerializeToNode(item.Configuration
                    .Where(pair => SafeResourceConfigKeys.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value), WireJson),
            }).ToArray()),
            ["credentials"] = new JsonArray(credentialRows.Select(item => (JsonNode)new JsonObject
            {
                ["id"] = item.Id, ["name"] = item.Name, ["kind"] = item.Kind,
                ["version"] = item.Version,
            }).ToArray()),
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

    private static async ValueTask DeleteMachineAsync(
        JsonNode? payload, IMachineRegistry machines, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("machine id is required");
        var references = (await bindings.ListAsync())
            .Where(binding => string.Equals(binding.MachineId, id, StringComparison.Ordinal))
            .Select(binding => binding.Id).Take(5).ToArray();
        if (references.Length > 0)
            throw new InvalidOperationException(
                $"machine '{id}' is still referenced by bindings: {string.Join(", ", references)}; " +
                "remove those bindings before deleting it");
        await machines.RemoveAsync(id);
    }

    private static async ValueTask SaveResourceAsync(
        JsonNode? payload, IResourceRegistry resources,
        ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("resource id is required");
        var existing = await resources.GetAsync(id);
        var type = Text(body, "type") ?? existing?.Type ?? "unknown";
        // An edit that only changes name/credential/enabled must not erase the
        // stored connection configuration; a supplied config deletes cleared
        // keys and retires keys belonging to the previous resource type.
        var configuration = MergedConfiguration(body["config"], existing, type);
        // ContainsKey distinguishes "field not submitted" from "explicitly
        // cleared with null": only the latter unbinds the credential.
        var credentialId = body.ContainsKey("credentialId")
            ? (body["credentialId"] is null ? null : Text(body, "credentialId"))
            : existing?.CredentialId;
        var enabled = body["enabled"] is JsonValue flag && flag.TryGetValue<bool>(out var isEnabled)
            ? isEnabled
            : existing?.Enabled ?? true;
        var provider = Text(body, "provider");
        if (string.IsNullOrWhiteSpace(provider) ||
            !string.Equals(provider, existing?.Provider, StringComparison.Ordinal) &&
            !string.Equals(type, existing?.Type, StringComparison.Ordinal))
            provider = ProviderFor(type);
        await resources.RegisterAsync(new ResourceDefinition(
            id,
            type,
            Text(body, "name") ?? id,
            provider ?? "manual",
            configuration,
            credentialId,
            existing?.Version ?? 0,
            enabled,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            body.ContainsKey("machineId")
                ? (body["machineId"] is null ? null : Text(body, "machineId"))
                : existing?.MachineId));
        // A saved resource is a new connection: any prior test result is stale.
        results.TryRemove(id, out _);
    }

    private static async ValueTask DeleteResourceAsync(
        JsonNode? payload, IResourceRegistry resources,
        IWorkflowRegistry workflows, ICollectionBindingRegistry bindings,
        ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("resource id is required");
        var references = new List<string>();
        foreach (var definition in await workflows.ListAsync())
        {
            var used = definition.ResourceReferences.Any(reference =>
                           string.Equals(reference, id, StringComparison.Ordinal)) ||
                       definition.Nodes.Any(node =>
                           new[] { "resourceId", "targetResourceId" }.Any(key =>
                               node.Config.TryGetValue(key, out var configured) &&
                               string.Equals(configured?.ToString(), id, StringComparison.Ordinal)));
            if (used) references.Add($"workflow {definition.WorkflowId}");
        }
        foreach (var binding in await bindings.ListAsync())
        {
            if (string.Equals(binding.SourceResourceId, id, StringComparison.Ordinal) ||
                binding.TargetResourceIds.Contains(id, StringComparer.Ordinal))
                references.Add($"binding {binding.Id}");
        }
        if (references.Count > 0)
            throw new InvalidOperationException(
                $"resource '{id}' is still referenced by {string.Join(", ", references.Distinct().Take(5))}; " +
                "remove those references before deleting it");
        await resources.RemoveAsync(id);
        results.TryRemove(id, out _);
    }

    private static async ValueTask SaveCredentialAsync(
        JsonNode? payload, ICredentialVault credentials,
        IResourceRegistry resources, ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        var kind = Text(body, "kind") ?? CredentialKinds.UsernamePassword;
        if (!CredentialKinds.All.Contains(kind, StringComparer.Ordinal))
            throw new InvalidOperationException($"unsupported credential kind '{kind}'");
        var secret = Text(body, "secret");
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("credential secret must not be empty");
        await credentials.PutAsync(id, Text(body, "name") ?? id, kind, secret);
        InvalidateCredentialTests(id, resources, results);
    }

    private static async ValueTask RotateCredentialAsync(
        JsonNode? payload, ICredentialVault credentials,
        IResourceRegistry resources, ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        var secret = Text(body, "secret");
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("credential secret must not be empty");
        await credentials.RotateAsync(id, secret);
        InvalidateCredentialTests(id, resources, results);
    }

    private static void InvalidateCredentialTests(
        string credentialId, IResourceRegistry resources,
        ConcurrentDictionary<string, ResourceTestResult> results)
    {
        foreach (var resource in resources.ListAsync().GetAwaiter().GetResult())
            if (string.Equals(resource.CredentialId, credentialId, StringComparison.Ordinal))
                results.TryRemove(resource.Id, out _);
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

    private static void ValidatePackageDependencies(
        JsonObject body, int formatVersion, IReadOnlyDictionary<string, HashSet<string>> available)
    {
        if (body["dependencies"] is not JsonArray dependencies)
        {
            if (formatVersion >= 2)
                throw new InvalidOperationException("formatVersion 2 requires a dependency manifest");
            return;
        }
        var missing = new List<string>();
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
            // An empty catalog is not proof that a required node is installed.
            if (dependency["required"] is JsonValue required && required.TryGetValue<bool>(out var mandatory) &&
                mandatory && (!available.TryGetValue(kind, out var ids) || !ids.Contains(id)))
                missing.Add($"{kind}:{id}");
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "configuration package has unsatisfied dependencies: " + string.Join(", ", missing.Distinct()) +
                "; install the provider, define/import the dependency, or supply an ID mapping before import");
    }

    private static async ValueTask<JsonObject> ImportConfigAsync(
        JsonNode? payload, IWorkflowRegistry workflows, INodeCatalog nodes,
        IFileProfileRegistry profiles, ICollectionBindingRegistry bindings,
        IMachineRegistry machines, ICredentialVault credentials, IResourceRegistry resources,
        ConcurrentDictionary<string, ResourceTestResult> results)
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
        var resourceMap = ReadMap(body["resourceMap"] as JsonObject);
        var credentialMap = ReadMap(body["credentialMap"] as JsonObject);
        // Kind-aware mapping: a credential alias never rewrites a resource id.
        string MapResource(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return resourceMap.TryGetValue(value, out var mapped) ? mapped : value;
        }
        string MapCredential(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return credentialMap.TryGetValue(value, out var mapped) ? mapped : value;
        }

        HashSet<string> Ids(string key, Func<JsonObject, string?> pick, Func<string?, string> map)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (body[key] is JsonArray array)
                foreach (var row in array)
                    if (row is JsonObject item)
                    {
                        var id = map(pick(item));
                        if (id.Length > 0) set.Add(id);
                    }
            return set;
        }
        var available = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["node"] = nodes.ListAsync().GetAwaiter().GetResult()
                .Select(descriptor => descriptor.NodeTypeId).ToHashSet(StringComparer.Ordinal),
            ["machine"] = Ids("machines", row => Text(row, "id"), value => value ?? string.Empty).Union(
                machines.ListAsync().GetAwaiter().GetResult().Select(item => item.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
            ["resource"] = Ids("resources", row => Text(row, "id"), MapResource).Union(
                resources.ListAsync().GetAwaiter().GetResult().Select(item => item.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
            ["fileProfile"] = Ids("fileProfiles", row => Text(row, "id"), value => value ?? string.Empty).Union(
                profiles.ListAsync().GetAwaiter().GetResult().Select(item => item.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
            ["credential"] = Ids("credentials", row => Text(row, "id"), MapCredential).Union(
                credentials.ListAsync().GetAwaiter().GetResult().Select(item => item.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
            ["workflow"] = Ids("workflows", row => Text(row, "workflowId"), value => value ?? string.Empty).Union(
                workflows.ListAsync().GetAwaiter().GetResult().Select(item => item.WorkflowId), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
        };
        ValidatePackageDependencies(body, formatVersion, available);

        // Validate every binding reference before the first write: a package
        // that cannot build its bindings must not leave half-imported rows.
        var bindingProblems = new List<string>();
        if (body["bindings"] is JsonArray bindingRows)
        {
            foreach (var row in bindingRows)
            {
                var binding = JsonSerializer.Deserialize<CollectionBindingDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (binding is null) continue;
                void Reference(string kind, string? value)
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    if (!available[kind].Contains(value))
                        bindingProblems.Add($"binding '{binding.Id}' references unknown {kind} '{value}'");
                }
                Reference("machine", binding.MachineId);
                Reference("fileProfile", binding.FileProfileId);
                Reference("workflow", binding.WorkflowId);
                Reference("resource", MapResource(binding.SourceResourceId));
                foreach (var target in binding.TargetResourceIds) Reference("resource", MapResource(target));
                ValidateTrigger(binding.Trigger.Kind, binding.Trigger.Cron,
                    binding.Trigger.Config.TryGetValue("timezone", out var zone) ? zone?.ToString() : null,
                    $"binding '{binding.Id}'");
            }
        }
        if (body["workflows"] is JsonArray definitions)
        {
            foreach (var row in definitions)
            {
                var definition = JsonSerializer.Deserialize<WorkflowDefinition>(row?.ToJsonString() ?? "{}", WireJson);
                if (definition is null) continue;
                ValidateTrigger(definition.Trigger.Kind, definition.Trigger.Cron,
                    definition.Trigger.Config.TryGetValue("timezone", out var zone) ? zone?.ToString() : null,
                    $"workflow '{definition.WorkflowId}'");
            }
        }
        if (bindingProblems.Count > 0)
            throw new InvalidOperationException(
                "configuration package has invalid binding references: " + string.Join("; ", bindingProblems.Take(8)));

        var importedWorkflows = 0;
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
                                ? MapResource(text) : pair.Value, StringComparer.Ordinal),
                    }).ToArray(),
                    ResourceReferences = definition.ResourceReferences.Select(MapResource)
                        .Where(value => value.Length > 0).ToArray(),
                };
                await workflows.SaveDraftAsync(definition);
                importedWorkflows++;
            }
        }
        var importedMachines = 0;
        if (body["machines"] is JsonArray machineRows)
        {
            foreach (var row in machineRows)
            {
                var machine = JsonSerializer.Deserialize<MachineDefinition>(row?.ToJsonString() ?? "{}", WireJson);
                if (machine is null || await machines.GetAsync(machine.Id) is not null) continue;
                await machines.RegisterAsync(machine with { CredentialId = MapCredential(machine.CredentialId) });
                importedMachines++;
            }
        }
        var importedResources = 0;
        if (body["resources"] is JsonArray resourceRows)
        {
            foreach (var row in resourceRows)
            {
                var resource = JsonSerializer.Deserialize<ResourceDefinition>(row?.ToJsonString() ?? "{}", WireJson);
                if (resource is null || await resources.GetAsync(resource.Id) is not null) continue;
                await resources.RegisterAsync(resource with
                {
                    CredentialId = MapCredential(resource.CredentialId),
                    Configuration = resource.Configuration.Where(pair =>
                        SafeResourceConfigKeys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
                });
                importedResources++;
                results.TryRemove(resource.Id, out _);
            }
        }
        var importedProfiles = 0;
        if (body["fileProfiles"] is JsonArray profileRows)
        {
            foreach (var row in profileRows)
            {
                var profile = JsonSerializer.Deserialize<FileProfileDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (profile is not null)
                {
                    foreach (var rule in profile.MetadataRules ?? [])
                        ValidateMetadataPattern(rule.Pattern, $"file profile '{profile.Id}' metadata rule '{rule.Key}'");
                    await profiles.RegisterAsync(profile);
                    importedProfiles++;
                }
            }
        }
        var importedBindings = 0;
        if (body["bindings"] is JsonArray importBindingRows)
        {
            foreach (var row in importBindingRows)
            {
                var binding = JsonSerializer.Deserialize<CollectionBindingDefinition>(
                    row?.ToJsonString() ?? "{}", WireJson);
                if (binding is null) continue;
                await bindings.RegisterAsync(binding with
                {
                    SourceResourceId = MapResource(binding.SourceResourceId),
                    TargetResourceIds = binding.TargetResourceIds.Select(MapResource).ToArray(),
                });
                importedBindings++;
            }
        }
        // Drafts are honest: execution remains unavailable until the declared
        // version is published. This report is returned to the caller and
        // surfaced by the console after import.
        return new JsonObject
        {
            ["workflows"] = importedWorkflows, ["machines"] = importedMachines,
            ["resources"] = importedResources, ["fileProfiles"] = importedProfiles,
            ["bindings"] = importedBindings, ["publishRequired"] = importedWorkflows > 0,
            ["message"] = "Imported workflows are drafts; publish each declared version before bindings can run.",
        };
    }

    /// <summary>The UI edits metadata one rule per line; older CSV exports use
    /// semicolons. Commas stay legal inside a regex, so they never split rules.
    /// </summary>
    private static MetadataRule[] ParseMetadataRules(string text, string where)
    {
        var entries = text.Contains('\n')
            ? text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var rules = new List<MetadataRule>();
        for (var index = 0; index < entries.Length; index++)
        {
            var parts = entries[index].Split('|');
            if (parts.Length < 3) continue;
            var pattern = NormalizeRegex(parts[1].Trim());
            ValidateMetadataPattern(pattern, $"{where} metadata rule #{index + 1}");
            rules.Add(new MetadataRule(parts[0].Trim(), pattern, parts[2].Trim(),
                parts.Length < 4 || parts[3].Trim() != "false"));
        }
        return rules.ToArray();
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
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var cells = rows[rowIndex];
            var id = Cell(header, cells, "id");
            if (id.Length == 0) continue;
            var where = $"row {rowIndex + 2}";
            string Field(string name, string fallback) => Cell(header, cells, name, fallback);
            var mapping = new Dictionary<string, object?>();
            foreach (var pair in SplitList(Field("mapping", string.Empty)))
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
                SplitList(Field("required", string.Empty)),
                true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                Field("directoryPattern", string.Empty), Field("datePattern", string.Empty),
                bool.TryParse(Field("flat", "false"), out var flat) && flat,
                ParseMetadataRules(Field("metadataRules", string.Empty), where)));
        }
    }

    /// <summary>Row-level binding import report. Nothing is written; the same
    /// validator runs again inside the import command so an advisory preview
    /// can never authorize an invalid workbook. Row 1 is the header.
    /// </summary>
    private static JsonObject PreviewImportReport(JsonObject body)
    {
        var (header, rows) = Tabular(body);
        var errors = new JsonArray();
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var valid = 0;
        if (!header.Contains("id"))
        {
            errors.Add((JsonNode)new JsonObject
            {
                ["row"] = 0, ["code"] = "HEADER_MISSING", ["message"] = "missing required column: id",
            });
        }
        else
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var cells = rows[index];
                var id = Cell(header, cells, "id");
                var problems = new List<string>();
                if (id.Length == 0) problems.Add("id is required");
                else if (!accepted.Add(id)) problems.Add($"duplicate id '{id}'");
                if (Cell(header, cells, "machineId").Length == 0) problems.Add("machineId is required");
                if (Cell(header, cells, "workflowId").Length == 0) problems.Add("workflowId is required");
                var trigger = Cell(header, cells, "triggerKind", "manual");
                var cron = Cell(header, cells, "cron");
                var timezone = Cell(header, cells, "timezone");
                if (trigger is not ("manual" or "schedule" or "event"))
                    problems.Add($"unknown triggerKind '{trigger}'");
                if (trigger == "schedule" && !CronIsValid(cron))
                    problems.Add(trigger == "schedule" && cron.Length == 0
                        ? "schedule trigger requires cron" : $"invalid cron '{cron}'");
                if (!string.IsNullOrWhiteSpace(timezone) &&
                    !TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _))
                    problems.Add($"unknown timezone '{timezone}'");
                var versionText = Cell(header, cells, "workflowVersion", "1");
                if (!int.TryParse(versionText, out var version) || version < 1)
                    problems.Add($"invalid workflowVersion '{versionText}'");
                if (problems.Count > 0)
                {
                    errors.Add((JsonNode)new JsonObject
                    {
                        ["row"] = index + 2,
                        ["code"] = "INVALID_ROW",
                        ["message"] = string.Join("; ", problems),
                    });
                }
                else valid++;
            }
        }
        return new JsonObject
        {
            ["total"] = rows.Count,
            ["valid"] = valid,
            ["errors"] = errors,
        };
    }

    private static async ValueTask ImportBindingsAsync(
        JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var report = PreviewImportReport(body);
        var errors = report["errors"]?.AsArray() ?? [];
        if (errors.Count > 0)
        {
            var badRows = errors.Select(error => error?["row"]?.GetValue<int>() ?? 0)
                .Where(row => row > 0).Take(5).ToArray();
            throw new InvalidOperationException(
                $"binding import rejected: {string.Join(", ", badRows.Length > 0
                    ? badRows.Select(row => $"row {row}") : new[] { "missing header" })}; " +
                "fix the preview errors before importing");
        }
        var (header, rows) = Tabular(body);
        if (rows.Count == 0) return;
        foreach (var cells in rows)
        {
            var id = Cell(header, cells, "id");
            if (id.Length == 0) continue;
            var kind = Cell(header, cells, "triggerKind", "manual");
            var cron = Cell(header, cells, "cron");
            var timezone = Cell(header, cells, "timezone");
            var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in Cell(header, cells, "parameters").Split(';'))
            {
                var parts = pair.Split('=').Select(part => part.Trim()).ToArray();
                if (parts.Length == 2 && parts[0].Length > 0) parameters[parts[0]] = parts[1];
            }
            if (!string.IsNullOrWhiteSpace(timezone)) parameters["timezone"] = timezone;
            await bindings.RegisterAsync(new CollectionBindingDefinition(
                id, Cell(header, cells, "name", id), Cell(header, cells, "machineId"),
                Cell(header, cells, "workflowId"),
                int.TryParse(Cell(header, cells, "workflowVersion", "1"), out var version) ? version : 1,
                Cell(header, cells, "fileProfileId"), Cell(header, cells, "sourceResourceId"),
                Cell(header, cells, "targetResourceIds").Split(';').Where(x => x.Trim().Length > 0)
                    .Select(x => x.Trim()).ToArray(),
                new TriggerDefinition(kind, string.IsNullOrWhiteSpace(cron) ? null : cron, null,
                    timezone.Length == 0 ? new Dictionary<string, object?>()
                        : new Dictionary<string, object?> { ["timezone"] = timezone }),
                true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, parameters));
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
            DateTimeOffset.UtcNow,
            Text(body, "directoryPattern") ?? string.Empty,
            Text(body, "datePattern") ?? string.Empty,
            body["flat"] is JsonValue flat && flat.TryGetValue<bool>(out var isFlat) && isFlat,
            body["metadataRules"] is JsonArray metadata
                ? metadata.Select(node => node?.AsObject())
                    .Where(node => node is not null)
                    .Select(node => new MetadataRule(
                        Text(node, "from") ?? "filename", NormalizeRegex(Text(node, "pattern") ?? string.Empty),
                        Text(node, "key") ?? string.Empty,
                        node!["required"] is JsonValue requiredFlag &&
                        requiredFlag.TryGetValue<bool>(out var isRequired) ? isRequired : true))
                    .Where(rule => rule.Pattern.Length > 0 && rule.Key.Length > 0).ToArray()
                : existing?.MetadataRules ?? []);
        var rules = profile.MetadataRules ?? [];
        for (var index = 0; index < rules.Count; index++)
            ValidateMetadataPattern(rules[index].Pattern,
                $"file profile '{id}' metadata rule #{index + 1}");
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
            Text(body, "cron"),
            body["parameters"] is JsonObject parameters
                ? parameters.ToDictionary(pair => pair.Key,
                    pair => (object?)pair.Value?.ToString(), StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal),
            body["exclusions"] is JsonArray exclusions
                ? exclusions.Select(node => node?.GetValue<string>() ?? string.Empty)
                    .Where(value => value.Length > 0).ToArray()
            : (Text(body, "exclusions") ?? string.Empty)
                .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).Where(value => value.Length > 0).ToArray());
    }

    private sealed record BindingRequest(
        IReadOnlyList<string> MachineIds, IReadOnlyList<string> FileProfileIds,
        string WorkflowId, int WorkflowVersion, string SourceResourcePattern,
        IReadOnlyList<string> TargetResourceIds, string TriggerKind, string? Cron,
        IReadOnlyDictionary<string, object?> Parameters,
        IReadOnlyList<string> Exclusions);

    private static string? RequestTimezone(JsonObject body, BindingRequest request)
    {
        var direct = Text(body, "timezone");
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        return request.Parameters.TryGetValue("timezone", out var value) ? value?.ToString() : null;
    }

    private static JsonObject BindingPreviewReport(BindingPreview preview) => new()
    {
        ["total"] = preview.Total,
        ["create"] = preview.Created,
        ["conflicts"] = new JsonArray(preview.Conflicts.Select(id => (JsonNode?)id).ToArray()),
        ["missing"] = new JsonArray(preview.MissingResources.Select(id => (JsonNode?)id).ToArray()),
        ["errors"] = new JsonArray(preview.Errors.Select(message => (JsonNode?)message).ToArray()),
    };

    private static async ValueTask CreateBindingsAsync(JsonNode? payload, ICollectionBindingRegistry bindings)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var request = ReadBindingRequest(body);
        ValidateTrigger(request.TriggerKind, request.Cron, RequestTimezone(body, request), "create-bindings");
        await bindings.CreateBulkAsync(request.MachineIds, request.FileProfileIds,
            request.WorkflowId, request.WorkflowVersion, request.SourceResourcePattern,
            request.TargetResourceIds, request.TriggerKind, request.Cron, request.Parameters,
            request.Exclusions);
    }

    private static async ValueTask DeleteCredentialAsync(
        JsonNode? payload, ICredentialVault credentials,
        IResourceRegistry resources, IMachineRegistry machines,
        ConcurrentDictionary<string, ResourceTestResult> results)
    {
        var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
        var id = Text(body, "id") ?? throw new InvalidOperationException("credential id is required");
        var usedByResource = (await resources.ListAsync()).Any(resource => resource.CredentialId == id);
        var usedByMachine = (await machines.ListAsync()).Any(machine => machine.CredentialId == id);
        if (usedByResource || usedByMachine)
            throw new InvalidOperationException(
                $"credential '{id}' is still referenced by resources or machines");
        await credentials.RemoveAsync(id);
        InvalidateCredentialTests(id, resources, results);
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
                // A published workflow is run only through the Elsa command path.
                new UiPageAction("Run workflow", "industrial/run-workflow"),
                new UiPageAction("Retry failed", "industrial/retry-failed"),
            ],
            Order: order, Group: group, Home: home);
    }
}
