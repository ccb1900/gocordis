using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Security.Cryptography;
using System.Text;
using Elsa.Extensions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Cronos;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Models;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Industrial.Contracts;
using Industrial.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Industrial.Elsa;

/// <summary>Read-only catalog of Activity types contributed by the industrial
/// Elsa plugin. Elsa still owns execution; this is not a second plugin manager.</summary>
public sealed class ElsaActivityCatalog : IElsaActivityCatalog
{
    private readonly HashSet<string> _types = new(StringComparer.Ordinal);
    public bool Contains(string activityType) => _types.Contains(activityType);
    public void Register(string activityType)
    {
        if (!string.IsNullOrWhiteSpace(activityType))
        {
            _types.Add(activityType);
        }
    }
    public void Unregister(string activityType)
    {
        if (!string.IsNullOrWhiteSpace(activityType))
        {
            _types.Remove(activityType);
        }
    }
    public IReadOnlyList<string> List() => _types.OrderBy(type => type, StringComparer.Ordinal).ToArray();
    internal void Clear() => _types.Clear();
}

/// <summary>Bounded, UI-safe diagnostics for one node execution.</summary>
public sealed record NodeExecutionView(
    string NodeId, string Status, int Records, int Files,
    string? Error, IReadOnlyList<string> Samples);

/// <summary>The narrow bridge from a persisted Elsa Activity back to the
/// Cordis-owned data work for one published workflow node.</summary>
public interface IIndustrialNodeExecutor
{
    IReadOnlyList<string> Executed { get; }
    IReadOnlyList<NodeExecutionView> NodeViews { get; }
    /// <summary>The ledger run produced by this execution, if a source node ran.</summary>
    string? RunId { get; }
    /// <summary>True when at least one delivery failed inside this execution.</summary>
    bool AnyFailure { get; }
    bool FatalExit { get; }
    ValueTask ExecuteAsync(
        string instanceId, string nodeId, string activityType, string nodeType,
        CancellationToken cancellationToken);
}

public sealed class IndustrialElsaComponent : Component
{
    private Broker<IIndustrialNodeHandler>? _nodeHandlers;
    private ICollectionBindingRegistry? _bindings;
    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly ElsaActivityCatalog _catalog = new();
    private readonly IReadOnlyDictionary<string, object?> _config;

    // Query-facing only. Never consulted when deciding the outcome of a run:
    // concurrent runs would otherwise read each other's status and source dates.
    private ElsaWorkflowExecutor.Result? _lastExecution;
    private string? _lastInstanceId;
    private string? _lastDefinitionId;
    private string? _lastError;
    private string? _lastRecovery;
    private IReadOnlyList<NodeExecutionView> _lastNodeViews = [];
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly SemaphoreSlim _bindingScheduleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _lastScheduleError = new(StringComparer.Ordinal);
    private JsonObject? _lastBackfillPreview;
    private JsonObject? _lastBindingTick;
    private JsonObject? _lastBindingValues;
    private int _bindingConcurrency = 8;

    private const int DefaultCatchUpDays = 7;
    private const int MaxCatchUpDays = 366;

    /// <summary>Identity/derived values owned by the binding or its profile.
    /// Run parameters must never shadow them, case-insensitively.</summary>
    private static readonly HashSet<string> ReservedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Binding", "Machine", "Profile", "FileProfile", "SourceResource",
        "BusinessDates", "FileNamePattern", "Format", "Delimiter", "HasHeader",
        "Encoding", "Mapping", "Required",
    };

    private static bool IsReservedParameter(string key) =>
        ReservedParameters.Contains(key) ||
        (key.StartsWith("TargetResource", StringComparison.OrdinalIgnoreCase) &&
         key.Length > "TargetResource".Length &&
         key["TargetResource".Length..].All(char.IsDigit));

    /// <summary>All state produced by one execution. Returned to each caller so
    /// scheduler, binding and retry decisions use that run's own outcome.</summary>
    private sealed record RunOutcome(
        string InstanceId,
        string DefinitionId,
        string Status,
        IReadOnlyList<string> Executed,
        IReadOnlyDictionary<DateOnly, int> SourceDates,
        IReadOnlySet<DateOnly> SkippedDates,
        IReadOnlyList<NodeExecutionView> NodeViews,
        string? Error,
        string? RunId,
        string? LedgerStatus,
        string? LedgerError,
        bool AnyFailure,
        string DefinitionJson,
        string DefinitionHash)
    {
        public bool Completed => Status is "Completed" or "Finished";

        public bool DeliveryFailed => Error is not null || AnyFailure ||
            LedgerStatus is CollectionStatus.Failed or CollectionStatus.PartialFailure or
                CollectionStatus.Interrupted or CollectionStatus.Cancelled;

        public string? FailureError => Error ?? LedgerError ??
            (AnyFailure ? "one or more deliveries failed" : $"workflow did not complete ({Status})");
    }

    public IndustrialElsaComponent() : this(null) { }

    public IndustrialElsaComponent(IReadOnlyDictionary<string, object?>? config) =>
        _config = config ?? new Dictionary<string, object?>();

    private static bool AllowDevelopmentRoot(IReadOnlyDictionary<string, object?> config) =>
        config.TryGetValue("allowDevelopmentKey", out var flag) &&
        string.Equals(flag?.ToString(), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY"),
            "true", StringComparison.OrdinalIgnoreCase);

    private string Root
    {
        get
        {
            var configured = _config.TryGetValue("root", out var root) && root is not null &&
                             !string.IsNullOrWhiteSpace(root.ToString())
                ? root.ToString()!
                : Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ROOT")
                  ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial");
            return IndustrialPaths.NormalizeRoot(configured, AllowDevelopmentRoot(_config));
        }
    }

    private string ElsaDbPath => Path.Combine(Root, "industrial-elsa.db");

    private string SnapshotRoot => Path.Combine(Root, "industrial-snapshots");

    public override string Name => "industrial-elsa";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowValidator,
         IndustrialKeys.WorkflowRegistry, ApiGateway.Key,
         IndustrialKeys.CollectionCoordinator, IndustrialKeys.CollectionLedger,
         IndustrialKeys.ResourceRegistry, IndustrialKeys.CredentialVault,
         IndustrialKeys.HistoricalRangePlanner, IndustrialKeys.FileSource,
         IndustrialKeys.RecordReader, IndustrialKeys.RecordSinkFactories,
         IndustrialKeys.StageOutputStore, TimerKeys.Service,
         IndustrialKeys.RecordSink, IndustrialKeys.ArchiveSink,
         IndustrialKeys.NodeExecutionHandlers, IndustrialKeys.CollectionBindings,
         IndustrialKeys.FileProfiles];
    public override IReadOnlyList<ICapabilityKey> Provide() => [IndustrialKeys.ElsaActivityCatalog];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var nodes = context.Require(IndustrialKeys.NodeCatalog);
        var validator = context.Require(IndustrialKeys.WorkflowValidator);
        var workflows = context.Require(IndustrialKeys.WorkflowRegistry);
        var api = context.Require(ApiGateway.Key);
        var coordinator = context.Require(IndustrialKeys.CollectionCoordinator);
        var ledger = context.Require(IndustrialKeys.CollectionLedger);
        var resources = context.Require(IndustrialKeys.ResourceRegistry);
        var vault = context.Require(IndustrialKeys.CredentialVault);
        var planner = context.Require(IndustrialKeys.HistoricalRangePlanner);
        var source = context.Require(IndustrialKeys.FileSource);
        var reader = context.Require(IndustrialKeys.RecordReader);
        var stage = context.Require(IndustrialKeys.StageOutputStore);
        var sinkFactories = context.Require(IndustrialKeys.RecordSinkFactories);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
        _nodeHandlers = context.Require(IndustrialKeys.NodeExecutionHandlers);
        var bindings = context.Require(IndustrialKeys.CollectionBindings);
        var profiles = context.Require(IndustrialKeys.FileProfiles);
        _bindings = bindings;
        var configuredConcurrency = _config.TryGetValue("bindingConcurrency", out var configuredLimit)
                                    && int.TryParse(configuredLimit?.ToString(), out var parsedLimit)
            ? parsedLimit
            : int.TryParse(
                Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_BINDING_CONCURRENCY"),
                out var envLimit)
                ? envLimit
                : 8;
        _bindingConcurrency = Math.Clamp(configuredConcurrency, 1, 64);
        var schedulerSeconds = _config.TryGetValue("schedulerIntervalSeconds", out var configuredInterval)
                               && int.TryParse(configuredInterval?.ToString(), out var parsedInterval)
            ? parsedInterval
            : int.TryParse(
                Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SCHEDULER_INTERVAL_SECONDS"),
                out var envInterval)
                ? envInterval
                : 15;
        var schedulerInterval = TimeSpan.FromSeconds(Math.Clamp(schedulerSeconds, 1, 3600));
        var timers = context.Require(TimerKeys.Service);
        var owner = ContributionOwner.ForContext(context, "industrial", "elsa-publish");
        foreach (var type in IndustrialActivityTypes.All)
        {
            _catalog.Register(type);
        }

        foreach (var descriptor in await nodes.ListAsync())
        {
            _catalog.Register(descriptor.ActivityType);
        }

        var previous = new ElsaActivityCatalog();
        foreach (var type in _catalog.List())
        {
            previous.Register(type);
        }

        context.Provide(IndustrialKeys.ElsaActivityCatalog, _catalog);
        validator.UseActivityCatalog(_catalog);
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () =>
            {
                _catalog.Clear();
                foreach (var type in previous.List())
                {
                    _catalog.Register(type);
                }

                validator.UseActivityCatalog(previous);
                return ValueTask.CompletedTask;
            });

        await context.EffectAsync(
            async () =>
            {
                // Recovery is diagnostics-bearing, never an activation gate:
                // a broken historical instance must not withdraw this plugin's
                // commands and queries from a fresh process.
                try
                {
                    await RecoverAllAsync(workflows, ledger, resources, vault, planner,
                        source, reader, stage, sinkFactories, sink, archive, startup: true,
                        ct: context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _lastRecovery = System.Text.Json.JsonSerializer.Serialize(new JsonObject
                    {
                        ["expiredLeases"] = 0,
                        ["recovered"] = 0,
                        ["failures"] = new JsonArray(JsonValue.Create(ex.Message)),
                    });
                }
            },
            () => ValueTask.CompletedTask);
        timers.IntervalOwned(context, schedulerInterval, async () =>
        {
            await RunDueSchedulesAsync(workflows, coordinator, ledger, resources, vault,
                planner, source, reader, stage, sinkFactories, sink, archive,
                context.CancellationToken);
            await RunDueBindingSchedulesAsync(bindings, profiles, workflows, coordinator,
                ledger, resources, vault, planner, source, reader, stage, sinkFactories,
                sink, archive, context.CancellationToken);
        });
        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("industrial/save-workflow", async payload =>
                {
                    var body = payload as JsonObject
                               ?? throw new InvalidOperationException("JSON definition required");
                    var definition = System.Text.Json.JsonSerializer.Deserialize<WorkflowDefinition>(
                        body.ToJsonString(), WebJson)
                                     ?? throw new InvalidOperationException("invalid workflow definition");
                    await workflows.SaveDraftAsync(definition);
                }, owner);
                api.RegisterCommand("industrial/publish-workflow", async payload =>
                {
                    var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
                    var id = body["workflowId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidOperationException("workflowId is required");
                    }

                    var definition = await workflows.GetAsync(id)
                                     ?? throw new InvalidOperationException($"workflow '{id}' not found");
                    await _publishLock.WaitAsync();
                    try
                    {
                        var version = await workflows.NextPublishVersionAsync(id);
                        var snapshot = definition with { Version = version };
                        var elsJson = await ElsaDefinitionConverter.ToElsaJson(snapshot);
                        await workflows.PublishAsync(id, snapshot, version, elsJson);
                    }
                    finally
                    {
                        _publishLock.Release();
                    }
                }, owner);
                api.RegisterCommand("industrial/run-workflow", async payload =>
                {
                    try
                    {
                        await RunWorkflowAsync(
                            payload, workflows, coordinator, ledger, resources, vault, planner,
                            source, reader, stage, sinkFactories, sink, archive, ElsaDbPath,
                            ct: context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        throw;
                    }
                }, owner);
                api.RegisterCommand("industrial/run-binding", async payload =>
                {
                    try
                    {
                        await RunBindingAsync(payload, bindings, profiles, workflows,
                            coordinator, ledger, resources, vault, planner, source, reader, stage,
                            sinkFactories, sink, archive, ElsaDbPath, context.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        throw;
                    }
                }, owner);
                api.RegisterCommand("industrial/recover-instances", async _ =>
                    await RecoverAllAsync(workflows, ledger, resources, vault, planner,
                        source, reader, stage, sinkFactories, sink, archive,
                        ct: context.CancellationToken), owner);
                // Deterministic scheduler tick for diagnostics and integration
                // tests. Uses the same single path as the timer, never a second
                // scheduler implementation.
                api.RegisterCommand("industrial/scheduler-tick", async _ =>
                {
                    await RunDueSchedulesAsync(workflows, coordinator, ledger, resources, vault,
                        planner, source, reader, stage, sinkFactories, sink, archive,
                        context.CancellationToken);
                }, owner);
                api.RegisterCommand("industrial/binding-scheduler-tick", async _ =>
                {
                    await RunDueBindingSchedulesAsync(bindings, profiles, workflows, coordinator,
                        ledger, resources, vault, planner, source, reader, stage,
                        sinkFactories, sink, archive, context.CancellationToken);
                }, owner);
                api.RegisterQuery("industrial/binding-run-parameters", () =>
                    _lastBindingValues is null ? null : (JsonNode)_lastBindingValues.DeepClone(), owner);
                api.RegisterQuery("industrial/binding-scheduler-status", () =>
                    _lastBindingTick is null
                        ? null
                        : (JsonNode)new JsonObject
                        {
                            ["concurrencyLimit"] = _bindingConcurrency,
                            ["scanned"] = _lastBindingTick["scanned"]?.DeepClone(),
                            ["fired"] = _lastBindingTick["fired"]?.DeepClone(),
                            ["succeeded"] = _lastBindingTick["succeeded"]?.DeepClone(),
                            ["failed"] = _lastBindingTick["failed"]?.DeepClone(),
                            ["peakInFlight"] = _lastBindingTick["peakInFlight"]?.DeepClone(),
                            ["lastError"] = _lastBindingTick["lastError"]?.DeepClone(),
                        }, owner);
                api.RegisterQuery("industrial/storage-paths", () =>
                {
                    var root = Root;
                    return (JsonNode)new JsonObject
                    {
                        ["root"] = root,
                        ["elsaDb"] = Path.Combine(root, "industrial-elsa.db"),
                        ["ledgerDb"] = Path.Combine(root, "industrial-ledger.db"),
                        ["stageRoot"] = root,
                    };
                }, owner);
                api.RegisterQuery("industrial/recovery", () => _lastRecovery is null
                    ? null
                    : JsonNode.Parse(_lastRecovery), owner);
                api.RegisterCommand("industrial/inspect-instance", async payload =>
                {
                    var body = payload as JsonObject;
                    var instanceId = body?["instanceId"]?.GetValue<string>()
                                     ?? _lastInstanceId
                                     ?? throw new InvalidOperationException("instanceId is required");
                    var state = await ElsaPersistedExecution.InspectAsync(instanceId, ElsaDbPath);
                    if (state is null)
                    {
                        throw new InvalidOperationException($"Elsa instance '{instanceId}' was not found");
                    }

                    _lastInstanceId = state.InstanceId;
                    _lastDefinitionId = state.DefinitionId;
                }, owner);
                api.RegisterQuery("industrial/triggers", () => new JsonArray(
                    workflows.ListAllPublishedAsync().GetAwaiter().GetResult()
                        .GroupBy(item => item.WorkflowId)
                        .Select(group => group.OrderByDescending(item => item.Version).First())
                        .Where(item => item.Definition.Trigger.Kind != "manual")
                        .Select(item => (JsonNode?)new JsonObject
                        {
                            ["workflowId"] = item.WorkflowId,
                            ["version"] = item.Version,
                            ["kind"] = item.Definition.Trigger.Kind,
                            ["cron"] = item.Definition.Trigger.Cron is null
                                ? null
                                : JsonValue.Create(item.Definition.Trigger.Cron),
                            ["eventName"] = item.Definition.Trigger.EventName is null
                                ? null
                                : JsonValue.Create(item.Definition.Trigger.EventName),
                        }).ToArray()), owner);
                api.RegisterCommand("industrial/trigger-event", async payload =>
                {
                    var body = payload as JsonObject;
                    var eventName = body?["eventName"]?.GetValue<string>()
                                    ?? throw new InvalidOperationException("eventName is required");
                    var resourceId = body?["resourceId"]?.GetValue<string>()
                                     ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE")
                                     ?? "source-1";
                    var matches = (await workflows.ListAllPublishedAsync())
                        .GroupBy(item => item.WorkflowId)
                        .Select(group => group.OrderByDescending(item => item.Version).First())
                        .Where(item => item.Definition.Trigger.Kind == "event" &&
                                       string.Equals(item.Definition.Trigger.EventName, eventName,
                                           StringComparison.Ordinal))
                        .ToArray();
                    foreach (var match in matches)
                    {
                        await RunWorkflowAsync(new JsonObject
                        {
                            ["workflowId"] = match.WorkflowId,
                            ["version"] = match.Version,
                            ["resourceId"] = resourceId,
                        }, workflows, coordinator, ledger, resources, vault, planner, source, reader,
                            stage, sinkFactories, sink, archive, ElsaDbPath,
                            ct: context.CancellationToken);
                    }
                }, owner);
                api.RegisterCommand("industrial/retry-failed", async payload =>
                {
                    var body = payload as JsonObject ?? new JsonObject();
                    await RetryDeliveryAsync(
                        body["deliveryId"]?.GetValue<string>(),
                        body["targetId"]?.GetValue<string>(),
                        workflows, ledger, resources, vault, planner, source, reader,
                        stage, sinkFactories, sink, archive, context.CancellationToken);
                }, owner);
                api.RegisterQuery("industrial/trigger-status", () =>
                    TriggerStatus(workflows, ledger), owner);
                api.RegisterCommand("industrial/preview-backfill", async payload =>
                {
                    var body = payload as JsonObject ?? new JsonObject();
                    var workflowId = body["workflowId"]?.GetValue<string>() ?? string.Empty;
                    var resourceId = body["resourceId"]?.GetValue<string>() ?? string.Empty;
                    var days = body["lookbackDays"] is JsonValue number &&
                               number.TryGetValue<int>(out var parsed)
                        ? parsed
                        : 1;
                    _lastBackfillPreview = await PreviewBackfillAsync(
                        workflows, source, ledger, planner, resources, vault, sinkFactories,
                        sink, archive, workflowId, resourceId, days, context.CancellationToken);
                }, owner);
                api.RegisterQuery("industrial/backfill-preview", () => _lastBackfillPreview, owner);
                api.RegisterQuery("industrial/instance", () => _lastInstanceId is null
                    ? null
                    : new JsonObject
                    {
                        ["instanceId"] = _lastInstanceId,
                        ["definitionId"] = _lastDefinitionId,
                    }, owner);
                api.RegisterQuery("industrial/workflow-versions", () => new JsonArray(
                    workflows.ListAllPublishedAsync().GetAwaiter().GetResult()
                        .Select(version => (JsonNode?)new JsonObject
                        {
                            ["workflowId"] = version.WorkflowId,
                            ["version"] = version.Version,
                            ["publishedAt"] = version.PublishedAt.ToString("O"),
                        }).ToArray()), owner);
                api.RegisterQuery("industrial/execution", () => _lastExecution is null
                    ? null
                    : new JsonObject
                    {
                        ["status"] = _lastExecution.Status,
                        ["executed"] = new JsonArray(_lastExecution.Executed
                            .Select(step => (JsonNode?)JsonValue.Create(step)).ToArray()),
                        ["error"] = _lastError is null ? null : JsonValue.Create(_lastError),
                        ["instanceId"] = _lastInstanceId is null ? null : JsonValue.Create(_lastInstanceId),
                        ["definitionId"] = _lastDefinitionId is null ? null : JsonValue.Create(_lastDefinitionId),
                        ["nodes"] = new JsonArray(_lastNodeViews.Select(node => (JsonNode)new JsonObject
                        {
                            ["nodeId"] = node.NodeId,
                            ["status"] = node.Status,
                            ["records"] = node.Records,
                            ["files"] = node.Files,
                            ["error"] = node.Error is null ? null : JsonValue.Create(node.Error),
                            ["samples"] = new JsonArray(node.Samples
                                .Select(sample => (JsonNode?)JsonValue.Create(sample)).ToArray()),
                        }).ToArray()),
                    }, owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });
        return null;
    }

    /// <summary>Updates the last-status queries only. The caller keeps the
    /// returned outcome for its own decisions; these fields are never read back
    /// to decide another run's status.</summary>
    private void RecordLast(RunOutcome outcome)
    {
        _lastInstanceId = outcome.InstanceId;
        _lastDefinitionId = outcome.DefinitionId;
        _lastExecution = new ElsaWorkflowExecutor.Result(outcome.Executed, outcome.Status);
        _lastNodeViews = outcome.NodeViews;
        _lastError = outcome.Error ?? outcome.LedgerError;
    }

    /// <summary>Completes a claimed fire with a non-cancellable token and reads
    /// the row back. Completion is only accepted when the claimed token won the
    /// update; a stale or rejected completion stays visible as a failure.</summary>
    private static async ValueTask<bool> CompleteFireAsync(
        ICollectionLedger ledger, ScheduleFireClaim claim, string status, string? error)
    {
        await ledger.CompleteScheduleFireAsync(
            claim.Key, claim.Token, status, error, CancellationToken.None);
        var fires = await ledger.ListScheduleFiresAsync(CancellationToken.None);
        var row = fires.FirstOrDefault(fire =>
            string.Equals(fire.Key, claim.Key, StringComparison.Ordinal));
        return row is not null &&
               string.Equals(row.Status, status, StringComparison.Ordinal) &&
               row.LeaseExpiresAtUnixMs == 0;
    }

    /// <summary>Same verification for a business-date claim. NoData retires the
    /// row; other terminal states must be visible with the claim released.</summary>
    private static async ValueTask<bool> CompleteDateClaimAsync(
        ICollectionLedger ledger, string id, string token, string status,
        int files, string? error)
    {
        await ledger.CompleteDateAsync(id, token, status, files, error, CancellationToken.None);
        var dates = await ledger.ListDatesAsync(CancellationToken.None);
        var row = dates.FirstOrDefault(date => string.Equals(date.Id, id, StringComparison.Ordinal));
        return status == CollectionDateStatus.NoData
            ? row is null
            : row is not null && string.Equals(row.Status, status, StringComparison.Ordinal) &&
              row.LeaseToken is null;
    }

    private async ValueTask RetryDeliveryAsync(
        string? deliveryId, string? targetId,
        IWorkflowRegistry workflows, ICollectionLedger ledger,
        IResourceRegistry resources, ICredentialVault vault, IHistoricalRangePlanner planner,
        IFileSource source, IRecordReader reader, IStageOutputStore stage,
        Broker<IRecordSinkFactory> factories, IRecordSink defaultSink, IArchiveSink archive,
        CancellationToken ct)
    {
        FileDelivery delivery;
        if (!string.IsNullOrWhiteSpace(deliveryId))
        {
            delivery = await ledger.GetDeliveryAsync(deliveryId)
                       ?? throw new InvalidOperationException($"delivery '{deliveryId}' was not found");
        }
        else
        {
            var candidates = await ledger.GetRetriableDeliveriesAsync(targetId);
            delivery = candidates.FirstOrDefault()
                       ?? throw new InvalidOperationException("no retriable delivery was found");
        }

        var file = await ledger.GetFileAsync(delivery.CollectionFileId)
                   ?? throw new InvalidOperationException("the delivery source file was not found");
        var resourceId = file.Identity.SourceResourceId;
        var instances = await ElsaPersistedExecution.ListInstancesAsync(ElsaDbPath, ct: ct);
        var runIds = await ledger.GetRunIdsByDeliveryAsync(delivery.Id);
        var runs = new List<CollectionRun>();
        foreach (var runId in runIds)
        {
            var run = await ledger.GetRunAsync(runId);
            if (run is not null) runs.Add(run);
        }
        var workflowId = runs.FirstOrDefault()?.WorkflowId ?? string.Empty;

        // Exact resolution: delivery → LastAttemptRunId (the run that most
        // recently actually attempted it) → CollectionRun.InstanceId → Elsa
        // instance. Historical run associations are audit data, not a source
        // of truth for which execution produced the current failure.
        ElsaPersistedExecution.RecoverableInstance? instance = null;
        var lastRunId = delivery.LastAttemptRunId;
        if (!string.IsNullOrWhiteSpace(lastRunId))
        {
            var lastRun = await ledger.GetRunAsync(lastRunId!)
                          ?? throw new InvalidOperationException(
                              $"last attempt run '{lastRunId}' for delivery '{delivery.Id}' was not found");
            workflowId = lastRun.WorkflowId;
            var linkedId = await ledger.GetRunInstanceAsync(lastRunId!);
            if (string.IsNullOrWhiteSpace(linkedId))
                throw new InvalidOperationException(
                    $"last attempt run '{lastRunId}' has no persisted Elsa instance; retry is ambiguous");
            instance = instances.FirstOrDefault(candidate =>
                string.Equals(candidate.InstanceId, linkedId, StringComparison.Ordinal) &&
                string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"original instance '{linkedId}' was not found; refusing to retry with another execution");
        }
        else
        {
            // Legacy deliveries predate LastAttemptRunId. Fall back only when
            // NO associated run carries an instance link; a missing instance
            // must never be replaced by an unrelated unique candidate.
            if (runs.Any(run => !string.IsNullOrWhiteSpace(run.InstanceId)))
                throw new InvalidOperationException(
                    $"delivery '{delivery.Id}' has no last-attempt run but associated runs carry instance links");
            var compatible = instances
                .Where(candidate => string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal) &&
                                    (string.IsNullOrWhiteSpace(workflowId) ||
                                     string.Equals(candidate.WorkflowId, workflowId, StringComparison.Ordinal)))
                .ToArray();
            if (compatible.Length != 1)
                throw new InvalidOperationException(
                    $"cannot resolve the original instance for delivery '{delivery.Id}': " +
                    $"{compatible.Length} candidates and no persisted last-attempt run");
            instance = compatible[0];
        }
        var published = await workflows.GetPublishedVersionAsync(instance.WorkflowId, instance.Version, ct)
                        ?? throw new InvalidOperationException(
                            $"published version {instance.WorkflowId}:v{instance.Version} was not found");
        var runDefinitionJson = string.IsNullOrWhiteSpace(lastRunId)
            ? null : await ledger.GetRunDefinitionAsync(lastRunId!, ct);
        // The run snapshot, not the published template, is the retry's input.
        // Both snapshots must agree when the instance carries one; a mismatch
        // means the original execution cannot be reconstructed faithfully.
        if (!string.IsNullOrWhiteSpace(instance.DefinitionJson) &&
            !string.IsNullOrWhiteSpace(runDefinitionJson) &&
            !string.Equals(instance.DefinitionJson, runDefinitionJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"run '{lastRunId}' definition snapshot differs from the original Elsa instance; refusing retry");
        }
        if (!string.IsNullOrWhiteSpace(instance.DefinitionJson) &&
            !string.IsNullOrWhiteSpace(instance.DefinitionHash) &&
            !string.Equals(instance.DefinitionHash,
                ElsaPersistedExecution.DefinitionHash(instance.DefinitionJson), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"instance '{instance.InstanceId}' definition snapshot failed its hash check; refusing retry");
        }
        var snapshotJson = !string.IsNullOrWhiteSpace(instance.DefinitionJson)
            ? instance.DefinitionJson!
            : runDefinitionJson;
        var retryDefinition = string.IsNullOrWhiteSpace(snapshotJson)
            ? published.Definition
            : JsonSerializer.Deserialize<WorkflowDefinition>(snapshotJson!, WebJson)
              ?? published.Definition;
        var effectiveJson = string.IsNullOrWhiteSpace(snapshotJson)
            ? JsonSerializer.Serialize(retryDefinition, WebJson)
            : snapshotJson!;
        var effectiveHash = ElsaPersistedExecution.DefinitionHash(effectiveJson);
        var executor = CreateExecutor(retryDefinition, resourceId, ledger, resources, vault,
            planner, source, reader, stage, factories, defaultSink, archive,
            delivery.Id, delivery.TargetId, _nodeHandlers,
            definitionJson: effectiveJson, definitionHash: effectiveHash,
            snapshotRoot: SnapshotRoot);
        // Keep every source resource's credentialed session for the entire
        // replay, not only discovery.
        await using var sourceSessions = await SourceSessionScope.AcquireAsync(
            source, retryDefinition, resourceId, ct);
        var result = await ElsaPersistedExecution.RunAsync(published, executor, resourceId, ElsaDbPath,
            instance.InstanceId, resume: true, definitionJson: effectiveJson,
            definitionHash: effectiveHash, ct: ct);
        // Query fields only; no subsequent decision reads them.
        _lastInstanceId = result.InstanceId;
        _lastDefinitionId = result.DefinitionId;
        _lastExecution = new ElsaWorkflowExecutor.Result(result.Executed, result.Status);
        _lastNodeViews = executor.NodeViews;
        _lastError = result.Error;
        if (result.Error is not null) throw new InvalidOperationException(result.Error);
    }

    private static IReadOnlySet<DateOnly> PlannedDates(IReadOnlyDictionary<string, object?> config)
    {
        var result = new HashSet<DateOnly>();
        if (!config.TryGetValue("businessDates", out var raw) || raw is null) return result;
        var values = raw switch
        {
            string text => text.Split(',', StringSplitOptions.RemoveEmptyEntries),
            IEnumerable<string> list => list,
            JsonElement element when element.ValueKind == JsonValueKind.Array =>
                element.EnumerateArray().Select(item => item.GetString()),
            JsonArray array => array.Select(item => item?.GetValue<string>()),
            IEnumerable<object?> list => list.Select(item => item?.ToString()),
            _ => [],
        };
        foreach (var value in values)
        {
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date)) result.Add(date);
        }
        return result;
    }

    private static TimeZoneInfo ScheduleZone(TriggerDefinition trigger)
    {
        if (trigger.Config.TryGetValue("timezone", out var configured) &&
            configured is not null &&
            TimeZoneInfo.TryFindSystemTimeZoneById(configured.ToString()!, out var zone))
        {
            return zone;
        }
        return TimeZoneInfo.Utc;
    }

    private static string? TriggerResource(WorkflowDefinition definition) =>
        definition.Trigger.Config.TryGetValue("resourceId", out var configured) &&
        configured is not null && !string.IsNullOrWhiteSpace(configured.ToString())
            ? configured.ToString()
            : definition.ResourceReferences.FirstOrDefault();

    private JsonObject TriggerStatus(
        IWorkflowRegistry workflows, ICollectionLedger ledger)
    {
        var runs = ledger.ListRunsAsync().GetAwaiter().GetResult();
        var fires = ledger.ListScheduleFiresAsync().GetAwaiter().GetResult();
        var rows = workflows.ListAllPublishedAsync().GetAwaiter().GetResult()
            .GroupBy(item => item.WorkflowId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Version).First())
            .Select(item =>
            {
                var trigger = item.Definition.Trigger;
                string? next = null;
                var zone = ScheduleZone(trigger);
                if (trigger.Kind == "schedule" &&
                    CronExpression.TryParse(trigger.Cron ?? string.Empty, out var expression))
                {
                    next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, zone)
                        ?.ToUniversalTime().ToString("O");
                }
                var last = runs.Where(run => run.WorkflowId == item.WorkflowId)
                    .OrderByDescending(run => run.StartedAt).FirstOrDefault();
                var lastFire = fires
                    .Where(fire => fire.WorkflowId == item.WorkflowId && fire.Version == item.Version)
                    .OrderByDescending(fire => fire.DueAtUtc).FirstOrDefault();
                var key = $"{item.WorkflowId}:v{item.Version}";
                var checkpoint = ledger.GetScheduleCheckpointAsync(item.WorkflowId, item.Version)
                    .GetAwaiter().GetResult();
                string? scheduleError = last?.Error ?? lastFire?.Error;
                if (scheduleError is null && _lastScheduleError.TryGetValue(key, out var volatileError))
                    scheduleError = volatileError;
                return (JsonNode)new JsonObject
                {
                    ["workflowId"] = item.WorkflowId,
                    ["version"] = item.Version,
                    ["kind"] = trigger.Kind,
                    ["cron"] = trigger.Cron is null ? null : JsonValue.Create(trigger.Cron),
                    ["eventName"] = trigger.EventName is null ? null : JsonValue.Create(trigger.EventName),
                    ["timezone"] = zone.Id,
                    ["resourceId"] = TriggerResource(item.Definition) is { } resource
                        ? JsonValue.Create(resource) : null,
                    ["nextRun"] = next is null ? null : JsonValue.Create(next),
                    ["lastStatus"] = last is null ? null : JsonValue.Create(last.Status),
                    ["lastStartedAt"] = last is null ? null : JsonValue.Create(last.StartedAt.ToString("O")),
                    ["lastFireStatus"] = lastFire is null ? null : JsonValue.Create(lastFire.Status),
                    ["lastFireDueAt"] = lastFire is null ? null : JsonValue.Create(lastFire.DueAtUtc.ToString("O")),
                    ["lastFireAttempts"] = lastFire is null ? null : JsonValue.Create(lastFire.Attempts),
                    ["skippedFires"] = checkpoint.Skipped == 0 ? null : JsonValue.Create(checkpoint.Skipped),
                    ["backlogTruncated"] = checkpoint.BacklogTruncated
                        ? JsonValue.Create(true) : null,
                    ["lastError"] = scheduleError is null ? null : JsonValue.Create(scheduleError),
                };
            }).ToArray();
        return new JsonObject
        {
            ["triggers"] = new JsonArray(rows),

        };
    }

    /// <summary>Binary-searches the latest cron occurrence strictly before
    /// <paramref name="upperExclusive"/> and at/after the checkpoint. Used for
    /// reverse catch-up so long outages do not require enumerating every tick.
    /// </summary>
    private static DateTimeOffset? LastOccurrenceBefore(
        CronExpression expression, TimeZoneInfo zone,
        DateTimeOffset checkpoint, DateTimeOffset upperExclusive)
    {
        DateTimeOffset? best = null;
        var lo = checkpoint;
        var hi = upperExclusive.AddTicks(-1);
        while (lo <= hi)
        {
            var mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
            var occurrence = expression.GetNextOccurrence(mid, zone, true)?.ToUniversalTime();
            if (occurrence is not null && occurrence < upperExclusive)
            {
                best = occurrence;
                lo = mid.AddTicks(1);
            }
            else
            {
                hi = mid.AddTicks(-1);
            }
        }
        return best;
    }

    private async ValueTask RunDueSchedulesAsync(
        IWorkflowRegistry workflows, ICollectionCoordinator coordinator,
        ICollectionLedger ledger, IResourceRegistry resources, ICredentialVault vault,
        IHistoricalRangePlanner planner, IFileSource source, IRecordReader reader,
        IStageOutputStore stage, Broker<IRecordSinkFactory> factories,
        IRecordSink sink, IArchiveSink archive, CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var allPublished = (await workflows.ListAllPublishedAsync()).ToArray();
            var latest = allPublished
                .GroupBy(item => item.WorkflowId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Version),
                    StringComparer.Ordinal);
            var existingFires = await ledger.ListScheduleFiresAsync();

            foreach (var item in allPublished
                         .OrderBy(item => item.WorkflowId, StringComparer.Ordinal)
                         .ThenBy(item => item.Version))
            {
                ct.ThrowIfCancellationRequested();
                var trigger = item.Definition.Trigger;
                var isSchedule = trigger.Kind == "schedule" &&
                                 CronExpression.TryParse(trigger.Cron ?? string.Empty, out var _);
                var key = $"{item.WorkflowId}:v{item.Version}";
                var dueInstants = new List<DateTimeOffset>();

                if (isSchedule && item.Version == latest[item.WorkflowId])
                {
                    const int catchUpLimit = 3;
                    const int countBudget = 10_000;
                    var checkpointState = await ledger.GetScheduleCheckpointAsync(
                        item.WorkflowId, item.Version, ct);
                    if (checkpointState.Checkpoint is null)
                    {
                        // First install starts at "now": a fresh deployment
                        // must not replay history that predates the product.
                        await ledger.CommitScheduleWindowAsync(
                            item.WorkflowId, item.Version, null, now, [], 0, false, ct);
                    }
                    else
                    {
                        var expression = CronExpression.Parse(trigger.Cron!);
                        var zone = ScheduleZone(trigger);
                        var checkpoint = checkpointState.Checkpoint.Value;
                        // Scan BACKWARD from now: the work is proportional to
                        // the catch-up limit, not to the length of downtime.
                        var upper = now.AddTicks(1);
                        for (var i = 0; i < catchUpLimit; i++)
                        {
                            var found = LastOccurrenceBefore(expression, zone, checkpoint, upper);
                            if (found is null) break;
                            dueInstants.Add(found.Value);
                            upper = found.Value;
                        }
                        dueInstants.Reverse();

                        // Count skipped occurrences from the checkpoint up to the
                        // first materialized fire with a bounded budget. If the
                        // budget is exhausted Skipped is a LOWER BOUND and the
                        // truncation flag is set separately.
                        var skipped = 0;
                        var truncated = false;
                        if (dueInstants.Count > 0)
                        {
                            var cursor = checkpoint;
                            while (skipped < countBudget)
                            {
                                var next = expression.GetNextOccurrence(cursor, zone, true)?.ToUniversalTime();
                                if (next is null || next >= dueInstants[0]) break;
                                skipped++;
                                cursor = next.Value;
                            }
                            truncated = skipped >= countBudget;
                        }
                        if (!await ledger.CommitScheduleWindowAsync(
                                item.WorkflowId, item.Version, checkpoint, now,
                                dueInstants, skipped, truncated, ct))
                        {
                            continue;
                        }
                    }
                }

                // Fires from any version (including superseded ones) are
                // recovered with their ORIGINAL published definition. Only the
                // latest version generates new future fires.
                foreach (var fire in existingFires
                             .Where(fire => fire.IsRecoverable(
                                 item.WorkflowId, item.Version, now.ToUnixTimeMilliseconds())))
                {
                    if (!dueInstants.Contains(fire.DueAtUtc)) dueInstants.Add(fire.DueAtUtc);
                }
                if (dueInstants.Count == 0) continue;

                foreach (var instant in dueInstants)
                {
                    var claim = await ledger.TryStartScheduleFireAsync(
                        item.WorkflowId, item.Version, instant, TimeSpan.FromMinutes(5), ct);
                    if (claim is null) continue;
                    try
                    {
                        var outcome = await RunWorkflowAsync(new JsonObject
                        {
                            ["workflowId"] = item.WorkflowId,
                            ["version"] = item.Version,
                            ["resourceId"] = TriggerResource(item.Definition)
                                             ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE")
                                             ?? "source-1",
                        }, workflows, coordinator, ledger, resources, vault, planner, source, reader,
                            stage, factories, sink, archive, ElsaDbPath, ct: ct);
                        var succeeded = outcome.Completed && !outcome.DeliveryFailed;
                        var error = succeeded ? null : outcome.FailureError;
                        var accepted = await CompleteFireAsync(ledger, claim,
                            succeeded ? ScheduleFireStatus.Succeeded : ScheduleFireStatus.Failed, error);
                        if (succeeded && accepted) _lastScheduleError.TryRemove(key, out _);
                        else _lastScheduleError[key] = error ?? "schedule fire completion was rejected";
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        await CompleteFireAsync(ledger, claim, ScheduleFireStatus.Failed,
                            "scheduled run interrupted");
                        _lastScheduleError[key] = "scheduled run interrupted";
                    }
                    catch (Exception ex)
                    {
                        await CompleteFireAsync(ledger, claim, ScheduleFireStatus.Failed, ex.Message);
                        _lastScheduleError[key] = ex.Message;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _lastScheduleError["scheduler"] = ex.Message;
        }
    }

    private async ValueTask<HashSet<string>> ExpectedTargetsAsync(
        WorkflowDefinition definition, IResourceRegistry resources, ICredentialVault vault,
        Broker<IRecordSinkFactory> factories, IRecordSink defaultSink, IArchiveSink archive,
        CancellationToken ct)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (node.Type.Contains("archive", StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(archive.TargetId);
                continue;
            }
            if (!node.Type.Contains("sink", StringComparison.OrdinalIgnoreCase)) continue;
            var targetResourceId = node.Config.TryGetValue("targetResourceId", out var configured) &&
                                   configured is not null &&
                                   !string.IsNullOrWhiteSpace(configured.ToString())
                ? configured.ToString()!
                : null;
            if (targetResourceId is null)
            {
                targets.Add(defaultSink.TargetId);
                continue;
            }
            try
            {
                var resource = await resources.GetAsync(targetResourceId, ct)
                               ?? throw new InvalidOperationException("resource not found");
                var factory = factories.Providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.ResourceType, resource.Type, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("no sink factory for resource type");
                var sink = await factory.CreateAsync(resource, vault, node.Config, ct);
                targets.Add(sink.TargetId);
            }
            catch (Exception)
            {
                // Preview stays useful even when a target is temporarily
                // misconfigured: fall back to the configured resource id.
                targets.Add(targetResourceId);
            }
        }
        return targets;
    }

    private async ValueTask<JsonObject> PreviewBackfillAsync(
        IWorkflowRegistry workflows, IFileSource source, ICollectionLedger ledger,
        IHistoricalRangePlanner planner, IResourceRegistry resources, ICredentialVault vault,
        Broker<IRecordSinkFactory> factories, IRecordSink defaultSink, IArchiveSink archive,
        string workflowId, string resourceId, int days, CancellationToken ct)
    {
        var published = string.IsNullOrWhiteSpace(workflowId)
            ? null
            : await workflows.GetPublishedAsync(workflowId);
        var definition = published?.Definition ??
                         (string.IsNullOrWhiteSpace(workflowId)
                             ? null
                             : await workflows.GetAsync(workflowId));
        var resource = string.IsNullOrWhiteSpace(resourceId)
            ? TriggerResource(definition ?? EmptyDefinition())
              ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE")
              ?? "source-1"
            : resourceId;
        var lookback = TimeSpan.FromDays(Math.Clamp(days, 1, 365));
        var range = planner.Plan(DateTimeOffset.UtcNow, lookback);
        var discovered = await source.DiscoverAsync(resource, range, ct);
        var known = (await ledger.ListAllFilesAsync(ct)).ToDictionary(
            file => file.Identity.Key, file => file, StringComparer.Ordinal);
        var deliveries = await ledger.ListAllDeliveriesAsync(ct);
        var expectedTargets = definition is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await ExpectedTargetsAsync(definition, resources, vault, factories,
                defaultSink, archive, ct);

        var rows = discovered.Select(file =>
        {
            known.TryGetValue(file.Identity.Key, out var ledgerFile);
            var fileDeliveries = ledgerFile is null
                ? []
                : deliveries.Where(delivery =>
                    delivery.CollectionFileId == ledgerFile.Id &&
                    expectedTargets.Contains(delivery.TargetId)).ToArray();
            var succeeded = fileDeliveries
                .Where(delivery => delivery.Status == CollectionStatus.Succeeded)
                .Select(delivery => delivery.TargetId)
                .ToHashSet(StringComparer.Ordinal);
            var incomplete = fileDeliveries
                .Where(delivery => delivery.Status is CollectionStatus.Failed or
                    CollectionStatus.Interrupted or CollectionStatus.Pending)
                .Select(delivery => delivery.TargetId)
                .ToHashSet(StringComparer.Ordinal);
            var missing = new HashSet<string>(expectedTargets, StringComparer.Ordinal);
            missing.ExceptWith(succeeded);
            var duplicateRisk = succeeded.Intersect(incomplete, StringComparer.Ordinal).ToArray();
            return (JsonNode)new JsonObject
            {
                ["path"] = file.Identity.RelativePath,
                ["sourceDate"] = file.SourceDate.ToString("O"),
                ["inLedger"] = ledgerFile is not null,
                ["expectedTargets"] = expectedTargets.Count,
                ["succeededTargets"] = succeeded.Count,
                ["incompleteTargets"] = incomplete.Count,
                ["missingTargets"] = string.Join(",", missing.OrderBy(id => id, StringComparer.Ordinal)),
                ["wouldSkip"] = expectedTargets.Count > 0 && missing.Count == 0,
                ["wouldCollect"] = expectedTargets.Count == 0 || missing.Count > 0,
                ["duplicateRisk"] = duplicateRisk.Length > 0,
            };
        }).ToArray();
        return new JsonObject
        {
            ["resourceId"] = resource,
            ["from"] = range.FromInclusive.ToString("O"),
            ["to"] = range.ToExclusive.ToString("O"),
            ["files"] = new JsonArray(rows),
            ["summary"] = new JsonObject
            {
                ["discovered"] = discovered.Count,
                ["alreadyComplete"] = rows.Count(row => row!["wouldSkip"]!.GetValue<bool>()),
                ["needsCollection"] = rows.Count(row => row!["wouldCollect"]!.GetValue<bool>()),
                ["duplicateRisk"] = rows.Count(row => row!["duplicateRisk"]!.GetValue<bool>()),
            },
        };
    }

    private static WorkflowDefinition EmptyDefinition() => new(
        string.Empty, string.Empty, 1, [], [],
        new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        [], new Dictionary<string, object?>());

    private static PublishedNodeExecutor CreateExecutor(
        WorkflowDefinition definition, string resourceId, ICollectionLedger ledger,
        IResourceRegistry resources, ICredentialVault vault, IHistoricalRangePlanner planner,
        IFileSource source, IRecordReader reader, IStageOutputStore stage,
        Broker<IRecordSinkFactory> factories, IRecordSink defaultSink, IArchiveSink archive,
        string? retryDeliveryId = null, string? retryTargetId = null,
        Broker<IIndustrialNodeHandler>? handlers = null,
        Action<IReadOnlyDictionary<DateOnly, int>>? onSourceDates = null,
        Action<IReadOnlySet<DateOnly>>? onSkippedDates = null,
        string? definitionJson = null, string? definitionHash = null,
        string? snapshotRoot = null) =>
        new(definition.WorkflowId, resourceId, definition, null!, ledger, resources, vault,
            planner, source, reader, stage, factories, defaultSink, archive,
            retryDeliveryId, retryTargetId, handlers, onSourceDates, onSkippedDates,
            definitionJson, definitionHash, snapshotRoot);

    private async ValueTask RecoverAllAsync(
        IWorkflowRegistry workflows, ICollectionLedger ledger, IResourceRegistry resources,
        ICredentialVault vault, IHistoricalRangePlanner planner, IFileSource source,
        IRecordReader reader, IStageOutputStore stage, Broker<IRecordSinkFactory> factories,
        IRecordSink defaultSink, IArchiveSink archive, bool startup = false,
        CancellationToken ct = default)
    {
        await _recoveryLock.WaitAsync(ct);
        try
        {
            var expiredLeases = await ledger.RecoverExpiredLeasesAsync(DateTimeOffset.UtcNow);
            if (startup)
            {
                expiredLeases += await ledger.RecoverAbandonedLeasesAsync();
                // A claimed schedule fire whose owner died must become visibly
                // Failed; the single scheduler loop then retries it within the
                // bounded attempt policy instead of silently skipping it.
                await ledger.AbandonScheduleFiresAsync();
            }
            var instances = await ElsaPersistedExecution.ListRecoverableAsync(ElsaDbPath, ct);
            var recovered = 0;
            var failures = new List<string>();
            foreach (var instance in instances)
            {
                try
                {
                    var published = await workflows.GetPublishedVersionAsync(
                        instance.WorkflowId, instance.Version);
                    if (published is null)
                    {
                        failures.Add($"{instance.InstanceId}: published version not found");
                        continue;
                    }
                    // The instance owns its exact bound definition. Resuming
                    // from the published template would overwrite the run
                    // snapshot with an unbound artifact on the cached-source
                    // branch, so a failed hash check is a hard recovery failure.
                    var snapshotJson = instance.DefinitionJson;
                    if (!string.IsNullOrWhiteSpace(snapshotJson) &&
                        !string.IsNullOrWhiteSpace(instance.DefinitionHash) &&
                        !string.Equals(instance.DefinitionHash,
                            ElsaPersistedExecution.DefinitionHash(snapshotJson!), StringComparison.Ordinal))
                    {
                        failures.Add($"{instance.InstanceId}: definition snapshot hash mismatch");
                        continue;
                    }

                    var definition = string.IsNullOrWhiteSpace(snapshotJson)
                        ? published.Definition
                        : JsonSerializer.Deserialize<WorkflowDefinition>(snapshotJson!, WebJson)
                          ?? published.Definition;
                    var effectiveJson = string.IsNullOrWhiteSpace(snapshotJson)
                        ? JsonSerializer.Serialize(definition, WebJson)
                        : snapshotJson!;
                    var effectiveHash = ElsaPersistedExecution.DefinitionHash(effectiveJson);
                    var executor = CreateExecutor(definition, instance.ResourceId,
                        ledger, resources, vault, planner, source, reader, stage,
                        factories, defaultSink, archive, handlers: _nodeHandlers,
                        definitionJson: effectiveJson, definitionHash: effectiveHash,
                        snapshotRoot: SnapshotRoot);
                    await using var sourceSessions = await SourceSessionScope.AcquireAsync(
                        source, definition, instance.ResourceId, ct);
                    var result = await ElsaPersistedExecution.RunAsync(published, executor, instance.ResourceId,
                        ElsaDbPath, instance.InstanceId, resume: true, definitionJson: effectiveJson,
                        definitionHash: effectiveHash, ct: ct);
                    // Query-facing only: recovery outcome never decides another run.
                    _lastInstanceId = result.InstanceId;
                    _lastDefinitionId = result.DefinitionId;
                    _lastExecution = new ElsaWorkflowExecutor.Result(result.Executed, result.Status);
                    _lastNodeViews = executor.NodeViews;
                    _lastError = result.Error;
                    recovered++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{instance.InstanceId}: {ex.Message}");
                }
            }
            _lastRecovery = System.Text.Json.JsonSerializer.Serialize(new JsonObject
            {
                ["expiredLeases"] = expiredLeases,
                ["recovered"] = recovered,
                ["failures"] = new JsonArray(failures.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
            });
        }
        finally
        {
            _recoveryLock.Release();
        }
    }

    private sealed class BindingTickCounters
    {
        private int _fired;
        private int _succeeded;
        private int _failed;
        private int _inFlight;
        private int _peak;

        public void Fired() => Interlocked.Increment(ref _fired);
        public void Succeeded() => Interlocked.Increment(ref _succeeded);
        public void Failed() => Interlocked.Increment(ref _failed);
        public void Enter()
        {
            var current = Interlocked.Increment(ref _inFlight);
            while (true)
            {
                var peak = Volatile.Read(ref _peak);
                if (current <= peak || Interlocked.CompareExchange(ref _peak, current, peak) == peak) break;
            }
        }
        public void Exit() => Interlocked.Decrement(ref _inFlight);

        public JsonObject ToJson() => new()
        {
            ["fired"] = Volatile.Read(ref _fired),
            ["succeeded"] = Volatile.Read(ref _succeeded),
            ["failed"] = Volatile.Read(ref _failed),
            ["peakInFlight"] = Volatile.Read(ref _peak),
        };
    }

    /// <summary>Dispatches due cron occurrences of enabled CollectionBindings.
    /// Every binding owns its own ledger checkpoint and fire rows, so one bad
    /// resource cannot cancel or poison another. A semaphore bounds how many
    /// bound runs may execute at once; all tasks are awaited and each failure
    /// is recorded on its own fire row.</summary>
    private async ValueTask RunDueBindingSchedulesAsync(
        ICollectionBindingRegistry bindings,
        IFileProfileRegistry profiles,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        IStageOutputStore stage,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink sink,
        IArchiveSink archive,
        CancellationToken ct)
    {
        var counters = new BindingTickCounters();
        var acquired = false;
        try
        {
            await _bindingScheduleLock.WaitAsync(ct);
            acquired = true;
            var now = DateTimeOffset.UtcNow;
            var scheduled = (await bindings.ListAsync(ct))
                .Where(binding => binding.Enabled && binding.Trigger.Kind == "schedule" &&
                                  CronExpression.TryParse(binding.Trigger.Cron ?? string.Empty, out _))
                .OrderBy(binding => binding.Id, StringComparer.Ordinal)
                .ToArray();
            if (scheduled.Length > 0)
            {
                var existingFires = await ledger.ListScheduleFiresAsync(ct);
                using var gate = new SemaphoreSlim(_bindingConcurrency, _bindingConcurrency);
                await Task.WhenAll(scheduled.Select(binding =>
                    ProcessBindingScheduleSafeAsync(binding, existingFires, now, gate, bindings,
                        profiles, workflows, coordinator, ledger, resources, vault, planner,
                        source, reader, stage, sinkFactories, sink, archive, counters, ct)
                    .AsTask()));
            }
            _lastBindingTick = new JsonObject
            {
                ["at"] = now.ToString("O"),
                ["scanned"] = scheduled.Length,
            };
            foreach (var pair in counters.ToJson()) _lastBindingTick[pair.Key] = pair.Value?.DeepClone();
        }
        catch (Exception ex)
        {
            // A single top-level failure is a diagnostic, never a reason to
            // kill the timer. Manual ticks go through the same guarded path.
            _lastScheduleError["binding-scheduler"] = ex.Message;
            _lastBindingTick = new JsonObject
            {
                ["at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["lastError"] = ex.Message,
            };
            foreach (var pair in counters.ToJson()) _lastBindingTick[pair.Key] = pair.Value?.DeepClone();
        }
        finally
        {
            if (acquired) _bindingScheduleLock.Release();
        }
    }

    private async ValueTask ProcessBindingScheduleSafeAsync(
        CollectionBindingDefinition binding,
        IReadOnlyList<ScheduleFireView> existingFires,
        DateTimeOffset now,
        SemaphoreSlim gate,
        ICollectionBindingRegistry bindings,
        IFileProfileRegistry profiles,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        IStageOutputStore stage,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink sink,
        IArchiveSink archive,
        BindingTickCounters counters,
        CancellationToken ct)
    {
        try
        {
            await ProcessBindingScheduleAsync(binding, existingFires, now, gate, bindings, profiles,
                workflows, coordinator, ledger, resources, vault, planner, source, reader, stage,
                sinkFactories, sink, archive, counters, ct);
        }
        catch (Exception ex)
        {
            counters.Failed();
            _lastScheduleError[BindingSchedule.FireWorkflowId(binding.Id)] = ex.Message;
        }
    }

    private async ValueTask ProcessBindingScheduleAsync(
        CollectionBindingDefinition binding,
        IReadOnlyList<ScheduleFireView> existingFires,
        DateTimeOffset now,
        SemaphoreSlim gate,
        ICollectionBindingRegistry bindings,
        IFileProfileRegistry profiles,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        IStageOutputStore stage,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink sink,
        IArchiveSink archive,
        BindingTickCounters counters,
        CancellationToken ct)
    {
        var key = BindingSchedule.FireWorkflowId(binding.Id);
        const int catchUpLimit = 3;
        const int countBudget = 10_000;
        var state = await ledger.GetScheduleCheckpointAsync(key, BindingSchedule.FireVersion, ct);
        if (state.Checkpoint is null)
        {
            // First sighting starts at now: a fresh binding must not replay
            // history that predates its installation.
            await ledger.CommitScheduleWindowAsync(key, BindingSchedule.FireVersion,
                null, now, [], 0, false, ct);
            return;
        }

        var expression = CronExpression.Parse(binding.Trigger.Cron!);
        var zone = ScheduleZone(binding.Trigger);
        var due = new List<DateTimeOffset>();
        var upper = now.AddTicks(1);
        for (var index = 0; index < catchUpLimit; index++)
        {
            var found = LastOccurrenceBefore(expression, zone, state.Checkpoint.Value, upper);
            if (found is null) break;
            due.Add(found.Value);
            upper = found.Value;
        }
        due.Reverse();

        var skipped = 0;
        var truncated = false;
        if (due.Count > 0)
        {
            var cursor = state.Checkpoint.Value;
            while (skipped < countBudget)
            {
                var next = expression.GetNextOccurrence(cursor, zone, true)?.ToUniversalTime();
                if (next is null || next >= due[0]) break;
                skipped++;
                cursor = next.Value;
            }
            truncated = skipped >= countBudget;
        }
        if (!await ledger.CommitScheduleWindowAsync(key, BindingSchedule.FireVersion,
                state.Checkpoint, now, due, skipped, truncated, ct))
        {
            return;
        }

        foreach (var fire in existingFires.Where(fire => fire.IsRecoverable(
                     key, BindingSchedule.FireVersion, now.ToUnixTimeMilliseconds())))
        {
            if (!due.Contains(fire.DueAtUtc)) due.Add(fire.DueAtUtc);
        }

        foreach (var instant in due)
        {
            await gate.WaitAsync(ct);
            counters.Enter();
            try
            {
                var claim = await ledger.TryStartScheduleFireAsync(key, BindingSchedule.FireVersion,
                    instant, TimeSpan.FromMinutes(5), ct);
                if (claim is null) continue;
                counters.Fired();
                try
                {
                    var outcome = await RunBindingAsync(new JsonObject
                    {
                        ["bindingId"] = binding.Id,
                        ["lookbackDays"] = 1,
                    }, bindings, profiles, workflows, coordinator, ledger, resources, vault,
                        planner, source, reader, stage, sinkFactories, sink, archive, ElsaDbPath, ct);
                    var succeeded = outcome.Completed && !outcome.DeliveryFailed;
                    var error = succeeded ? null : outcome.FailureError;
                    var accepted = await CompleteFireAsync(ledger, claim,
                        succeeded ? ScheduleFireStatus.Succeeded : ScheduleFireStatus.Failed, error);
                    if (succeeded && accepted)
                    {
                        counters.Succeeded();
                        _lastScheduleError.TryRemove(key, out _);
                    }
                    else
                    {
                        counters.Failed();
                        _lastScheduleError[key] = error ?? "binding fire completion was rejected";
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    await CompleteFireAsync(ledger, claim, ScheduleFireStatus.Failed,
                        "binding run interrupted");
                    counters.Failed();
                    _lastScheduleError[key] = "binding run interrupted";
                }
                catch (Exception ex)
                {
                    await CompleteFireAsync(ledger, claim, ScheduleFireStatus.Failed, ex.Message);
                    counters.Failed();
                    _lastScheduleError[key] = ex.Message;
                }
            }
            finally
            {
                counters.Exit();
                gate.Release();
            }
        }
    }

    /// <summary>Runs a binding by substituting its machine/profile/resource
    /// values into an immutable published template snapshot. The published
    /// definition itself is never edited, and no second workflow engine is
    /// involved: the bound snapshot goes through the same RunWorkflowAsync path.
    /// </summary>
    private async ValueTask<RunOutcome> RunBindingAsync(
        JsonNode? payload,
        ICollectionBindingRegistry bindings,
        IFileProfileRegistry profiles,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        IStageOutputStore stage,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink sink,
        IArchiveSink archive,
        string elsaDbPath,
        CancellationToken ct)
    {
        var body = payload as JsonObject
                   ?? throw new InvalidOperationException("JSON payload required");
        var id = body["bindingId"]?.GetValue<string>()
                 ?? throw new InvalidOperationException("bindingId is required");
        var binding = await bindings.GetAsync(id, ct)
                      ?? throw new InvalidOperationException($"binding '{id}' not found");
        if (binding.TargetResourceIds.Count == 0)
            throw new InvalidOperationException($"binding '{id}' has no target resources");
        var days = body["lookbackDays"] is JsonValue number &&
                   number.TryGetValue<int>(out var parsed)
            ? parsed
            : 1;
        var published = await workflows.GetPublishedVersionAsync(
                             binding.WorkflowId, binding.WorkflowVersion, ct)
                         ?? throw new InvalidOperationException(
                             $"workflow '{binding.WorkflowId}' version {binding.WorkflowVersion} " +
                             "has no published version");

        var catchUpDays = ResolveCatchUpDays(id, binding.Parameters);
        var businessDates = BusinessDatePlanner.Plan(
            await ledger.ListDatesAsync(ct), binding.Id,
            DateOnly.FromDateTime(DateTime.UtcNow), catchUpDays);
        var dateClaims = new List<(DateOnly Date, string Id, string Token)>();
        var businessRunId = $"binding-run-{binding.Id}-{Guid.NewGuid():N}";
        foreach (var businessDate in businessDates)
        {
            ct.ThrowIfCancellationRequested();
            var claim = await ledger.ClaimDateAsync(binding.Id, businessDate, businessRunId,
                TimeSpan.FromMinutes(5), ct);
            if (claim is not null) dateClaims.Add((businessDate, claim.Id, claim.LeaseToken!));
        }

        // User parameters first, profile-derived values next, identity values
        // last: a binding parameter can never shadow a reserved identity key.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in binding.Parameters ?? new Dictionary<string, object?>())
        {
            if (IsReservedParameter(pair.Key)) continue;
            values[pair.Key] = pair.Value?.ToString() ?? string.Empty;
        }
        var profile = await profiles.GetAsync(binding.FileProfileId, ct);
        if (profile is not null)
        {
            values["FileNamePattern"] = profile.FileNamePattern;
            values["Format"] = profile.Format;
            values["Delimiter"] = profile.Delimiter;
            values["HasHeader"] = profile.HasHeader ? "true" : "false";
            values["Encoding"] = profile.Encoding;
            values["Mapping"] = string.Join(";", profile.Mapping
                .Select(pair => $"{pair.Key}={pair.Value}"));
            values["Required"] = string.Join(";", profile.RequiredFields);
        }
        values["Binding"] = binding.Id;
        values["BusinessDates"] = string.Join(",", businessDates.Select(date => date.ToString("yyyy-MM-dd")));
        values["Machine"] = binding.MachineId;
        values["Profile"] = binding.FileProfileId;
        values["FileProfile"] = binding.FileProfileId;
        values["SourceResource"] = binding.SourceResourceId;
        for (var index = 0; index < binding.TargetResourceIds.Count; index++)
        {
            values[index == 0 ? "TargetResource" : $"TargetResource{index + 1}"] =
                binding.TargetResourceIds[index];
        }

        _lastBindingValues = new JsonObject(values
            .Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value))));
        var bound = WorkflowBinding.ApplyFileProfile(
            WorkflowBinding.BindParameters(published.Definition, values), profile);
        var dateValues = businessDates.Select(date => (object?)date.ToString("yyyy-MM-dd")).ToArray();
        // Source identity comes from the node's provider contract, not from
        // graph topology or an ad-hoc substring check.
        bound = bound with
        {
            Nodes = bound.Nodes.Select(node => IndustrialNodeKinds.IsSource(node.Type)
                ? node with
                {
                    Config = new Dictionary<string, object?>(node.Config, StringComparer.Ordinal)
                    {
                        ["businessDates"] = dateValues,
                        ["fileProfile"] = profile is null ? null : JsonSerializer.Serialize(profile, WebJson),
                    },
                }
                : node).ToArray(),
        };
        var layout = new Dictionary<string, object?>(bound.Layout, StringComparer.Ordinal)
        {
            ["lookbackDays"] = days,
        };
        var boundDefinition = bound with { Layout = layout };

        var settled = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var outcome = await RunWorkflowAsync(new JsonObject
            {
                ["workflowId"] = binding.WorkflowId,
                ["version"] = binding.WorkflowVersion,
                ["resourceId"] = binding.SourceResourceId,
            }, workflows, coordinator, ledger, resources, vault, planner, source, reader, stage,
                sinkFactories, sink, archive, elsaDbPath,
                overrideDefinition: boundDefinition, ct: ct);

            // A run that finished Elsa but failed a delivery is NOT a successful
            // business date. An unstable-file skip is a re-probe, not NoData.
            var failed = !outcome.Completed || outcome.DeliveryFailed;
            var completions = new List<(DateOnly Date, string Id, string Token,
                string Status, int Files, string? Error)>();
            foreach (var claim in dateClaims)
            {
                ct.ThrowIfCancellationRequested();
                var files = outcome.SourceDates.GetValueOrDefault(claim.Date);
                var skipped = outcome.SkippedDates.Contains(claim.Date);
                string status;
                string? error;
                if (failed)
                {
                    status = CollectionDateStatus.Failed;
                    error = outcome.FailureError;
                }
                else if (files > 0)
                {
                    status = CollectionDateStatus.Succeeded;
                    error = null;
                }
                else if (skipped)
                {
                    status = CollectionDateStatus.Failed;
                    error = $"source files for {claim.Date:yyyy-MM-dd} were unstable and skipped";
                }
                else
                {
                    status = CollectionDateStatus.NoData;
                    error = null;
                }
                completions.Add((claim.Date, claim.Id, claim.Token, status, files, error));
                settled.Add(claim.Id);
            }
            // Completion uses a non-cancellable token; verification reads the
            // rows back once per run so a stale token cannot be mistaken for a
            // successful completion without an N-squared scan.
            foreach (var completion in completions)
            {
                await ledger.CompleteDateAsync(completion.Id, completion.Token,
                    completion.Status, completion.Files, completion.Error, CancellationToken.None);
            }
            if (completions.Count > 0)
            {
                var stored = await ledger.ListDatesAsync(CancellationToken.None);
                foreach (var completion in completions)
                {
                    var row = stored.FirstOrDefault(date =>
                        string.Equals(date.Id, completion.Id, StringComparison.Ordinal));
                    var accepted = completion.Status == CollectionDateStatus.NoData
                        ? row is null
                        : row is not null &&
                          string.Equals(row.Status, completion.Status, StringComparison.Ordinal) &&
                          row.LeaseToken is null;
                    if (!accepted)
                    {
                        _lastScheduleError[$"{binding.Id}:{completion.Date:yyyy-MM-dd}"] =
                            "business-date completion was rejected";
                    }
                }
            }
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            foreach (var claim in dateClaims.Where(claim => !settled.Contains(claim.Id)))
            {
                await CompleteDateClaimAsync(ledger, claim.Id, claim.Token,
                    CollectionDateStatus.Interrupted, 0, "binding run interrupted");
                settled.Add(claim.Id);
            }
            throw;
        }
        catch (Exception ex)
        {
            foreach (var claim in dateClaims.Where(claim => !settled.Contains(claim.Id)))
            {
                await CompleteDateClaimAsync(ledger, claim.Id, claim.Token,
                    CollectionDateStatus.Failed, 0, ex.Message);
                settled.Add(claim.Id);
            }
            throw;
        }
    }

    private static int ResolveCatchUpDays(
        string bindingId, IReadOnlyDictionary<string, object?>? parameters)
    {
        var configured = parameters?.FirstOrDefault(pair =>
            string.Equals(pair.Key, "catchUpDays", StringComparison.OrdinalIgnoreCase));
        var raw = configured?.Value;
        if (raw is null) return DefaultCatchUpDays;
        if (!int.TryParse(raw.ToString(), out var parsed) ||
            parsed < 0 || parsed > MaxCatchUpDays)
        {
            throw new InvalidOperationException(
                $"binding '{bindingId}' catchUpDays must be an integer between 0 and {MaxCatchUpDays}; " +
                $"got '{raw}'");
        }
        return parsed;
    }

    private async ValueTask<RunOutcome> RunWorkflowAsync(
        JsonNode? payload,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        IStageOutputStore stage,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink defaultSink,
        IArchiveSink archive,
        string elsaDbPath,
        WorkflowDefinition? overrideDefinition = null,
        CancellationToken ct = default)
    {
        var body = payload as JsonObject ?? new JsonObject();
        var id = body["workflowId"]?.GetValue<string>()
                 ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_WORKFLOW") ?? "daily-collection";
        var resourceId = body["resourceId"]?.GetValue<string>()
                         ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE") ?? "source-1";
        var requestedVersion = body["version"] is JsonValue version && version.TryGetValue<int>(out var v)
            ? v
            : (int?)null;
        var published = requestedVersion.HasValue
            ? await workflows.GetPublishedVersionAsync(id, requestedVersion.Value, ct)
            : await workflows.GetPublishedAsync(id, ct);
        if (published is null)
        {
            var suffix = requestedVersion.HasValue ? $" version {requestedVersion.Value}" : string.Empty;
            throw new InvalidOperationException($"workflow '{id}'{suffix} has no published version");
        }

        if (await resources.GetAsync(resourceId, ct) is null)
        {
            throw new InvalidOperationException($"resource '{resourceId}' not found");
        }

        var instanceId = body["instanceId"]?.GetValue<string>();
        var resume = body["resume"] is JsonValue flag && flag.TryGetValue<bool>(out var shouldResume) && shouldResume;
        // The RUN input is the persisted published artifact unless a bound
        // snapshot was supplied. A plain resume must also keep the instance's
        // original definition, never the template that happens to be current.
        var definition = overrideDefinition;
        if (definition is null && resume && !string.IsNullOrWhiteSpace(instanceId))
        {
            var existing = await ElsaPersistedExecution.InspectAsync(instanceId!, elsaDbPath, ct);
            if (!string.IsNullOrWhiteSpace(existing?.DefinitionJson))
            {
                definition = JsonSerializer.Deserialize<WorkflowDefinition>(existing!.DefinitionJson!, WebJson);
            }
        }
        definition ??= published.Definition;
        var definitionJson = JsonSerializer.Serialize(definition, WebJson);
        var definitionHash = ElsaPersistedExecution.DefinitionHash(definitionJson);

        // Per-run storage: never write these into component fields that another
        // run could read back after an await.
        var sourceDates = new ConcurrentDictionary<DateOnly, int>();
        var skippedDates = new ConcurrentDictionary<DateOnly, byte>();
        var executor = CreateExecutor(definition, resourceId, ledger, resources, vault,
            planner, source, reader, stage, sinkFactories, defaultSink, archive,
            handlers: _nodeHandlers,
            onSourceDates: dates =>
            {
                foreach (var pair in dates) sourceDates[pair.Key] = pair.Value;
            },
            onSkippedDates: dates =>
            {
                foreach (var date in dates) skippedDates.TryAdd(date, 0);
            },
            definitionJson: definitionJson, definitionHash: definitionHash,
            snapshotRoot: SnapshotRoot);

        // Every source node's resource gets its own credentialed session, and
        // all leases live until the whole workflow has finished.
        await using var sourceSessions = await SourceSessionScope.AcquireAsync(
            source, definition, resourceId, ct);
        var result = await ElsaPersistedExecution.RunAsync(
            published, executor, resourceId, elsaDbPath, instanceId, resume,
            definitionJson, definitionHash, ct);
        var productStatus = string.Equals(result.Status, "Finished", StringComparison.Ordinal)
            ? "Completed"
            : result.Status;
        var ledgerRun = executor.RunId is null
            ? null : await ledger.GetRunAsync(executor.RunId, ct);
        var outcome = new RunOutcome(
            result.InstanceId, result.DefinitionId, productStatus, result.Executed,
            new Dictionary<DateOnly, int>(sourceDates),
            new HashSet<DateOnly>(skippedDates.Keys), executor.NodeViews,
            result.Error, executor.RunId, ledgerRun?.Status, ledgerRun?.Error,
            executor.AnyFailure, result.DefinitionJson ?? definitionJson,
            result.DefinitionHash ?? definitionHash);
        RecordLast(outcome);
        return outcome;
    }

    /// <summary>Executes one persisted node through Cordis capabilities. Each
    /// node reads its inputs from predecessor nodes along the published edges;
    /// there is no centralized transform sweep or per-sink collection restart.</summary>
    private sealed class PublishedNodeExecutor : IIndustrialNodeExecutor
    {
        private readonly string _workflowId;
        private readonly string _resourceId;
        private readonly WorkflowDefinition _definition;
        private readonly ICollectionLedger _ledger;
        private readonly IResourceRegistry _resources;
        private readonly ICredentialVault _vault;
        private readonly IHistoricalRangePlanner _planner;
        private readonly IFileSource _source;
        private readonly IRecordReader _reader;
        private readonly IStageOutputStore _stage;
        private readonly Broker<IRecordSinkFactory> _factories;
        private readonly IRecordSink _defaultSink;
        private readonly IArchiveSink _archive;
        private readonly Broker<IIndustrialNodeHandler>? _handlers;
        private readonly Action<IReadOnlyDictionary<DateOnly, int>>? _onSourceDates;
        private readonly Action<IReadOnlySet<DateOnly>>? _onSkippedDates;
        private readonly string _definitionJson;
        private readonly string _definitionHash;
        private readonly string _snapshotRoot;
        private readonly Dictionary<string, List<DiscoveredFile>> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CollectionFile> _ledgerFiles = new(StringComparer.Ordinal);
        private readonly List<string> _executed = [];
        private readonly Dictionary<string, NodeView> _views = new(StringComparer.Ordinal);
        private string? _runId;
        private readonly string? _retryDeliveryId;
        private readonly string? _retryTargetId;
        private string _instanceId = string.Empty;
        private int _remainingConsequences;
        private int _anyFailure;

        public PublishedNodeExecutor(
            string workflowId, string resourceId, WorkflowDefinition definition,
            ICollectionCoordinator coordinator, ICollectionLedger ledger,
            IResourceRegistry resources, ICredentialVault vault,
            IHistoricalRangePlanner planner, IFileSource source, IRecordReader reader,
            IStageOutputStore stage, Broker<IRecordSinkFactory> factories,
            IRecordSink defaultSink, IArchiveSink archive,
            string? retryDeliveryId = null, string? retryTargetId = null,
            Broker<IIndustrialNodeHandler>? handlers = null,
            Action<IReadOnlyDictionary<DateOnly, int>>? onSourceDates = null,
            Action<IReadOnlySet<DateOnly>>? onSkippedDates = null,
            string? definitionJson = null, string? definitionHash = null,
            string? snapshotRoot = null)
        {
            _handlers = handlers;
            _onSourceDates = onSourceDates;
            _onSkippedDates = onSkippedDates;
            _definitionJson = definitionJson ?? JsonSerializer.Serialize(definition, WebJson);
            _definitionHash = definitionHash ?? ElsaPersistedExecution.DefinitionHash(_definitionJson);
            _snapshotRoot = snapshotRoot
                             ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial", "industrial-snapshots");
            _retryDeliveryId = retryDeliveryId;
            _retryTargetId = retryTargetId;
            _workflowId = workflowId;
            _resourceId = resourceId;
            _definition = definition;
            _ledger = ledger;
            _resources = resources;
            _vault = vault;
            _planner = planner;
            _source = source;
            _reader = reader;
            _stage = stage;
            _factories = factories;
            _defaultSink = defaultSink;
            _archive = archive;
            // Every delivery-shaped node (database sink AND archive) owns one
            // completion slot; the run is terminal only after all of them are
            // done, independent of their order in the flowchart.
            foreach (var node in definition.Nodes)
            {
                var custom = handlers?.Providers.FirstOrDefault(handler =>
                    string.Equals(handler.NodeType, node.Type, StringComparison.Ordinal));
                if (custom?.Completion == IndustrialNodeCompletion.Delivery ||
                    IsSink(node.Type) || node.Type.Contains("archive", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _remainingConsequences);
                }
            }
        }

        public IReadOnlyList<string> Executed => _executed;
        public IReadOnlyList<NodeExecutionView> NodeViews => _views.Values
            .Select(view => new NodeExecutionView(
                view.NodeId, view.Status, view.Records, view.Files, view.Error, view.Samples))
            .OrderBy(view => view.NodeId, StringComparer.Ordinal).ToArray();
        public string? RunId => _runId;
        public bool AnyFailure => Volatile.Read(ref _anyFailure) != 0;
        public bool FatalExit { get; private set; }

        private sealed class NodeView(string nodeId)
        {
            public string NodeId { get; } = nodeId;
            public string Status { get; set; } = "Running";
            public int Records { get; set; }
            public int Files { get; set; }
            public string? Error { get; set; }
            public IReadOnlyList<string> Samples { get; set; } = [];
        }

        private NodeView View(string nodeId)
        {
            if (!_views.TryGetValue(nodeId, out var view))
            {
                view = new NodeView(nodeId);
                _views[nodeId] = view;
            }
            return view;
        }

        private sealed class SampleAccumulator
        {
            private readonly List<string> _values = new(3);
            public int Count { get; private set; }
            public IReadOnlyList<string> Values => _values;
            public void Add(RecordEnvelope record)
            {
                Count++;
                if (_values.Count < 3)
                    _values.Add(record.Values.GetValueOrDefault("record_id")?.ToString() ?? record.Source.Key);
            }
        }

        private static bool IsSink(string nodeType) =>
            nodeType.Contains("sink", StringComparison.OrdinalIgnoreCase);

        private IEnumerable<string> Inputs(string nodeId) => _definition.Edges
            .Where(edge => edge.Target == nodeId)
            .Select(edge => edge.Source)
            .Distinct(StringComparer.Ordinal);

        /// <summary>Stage-backed async record flow. Records are read from the
        /// predecessor's persisted dataset one at a time; no stage keeps a full
        /// batch in memory.</summary>
        private async IAsyncEnumerable<RecordEnvelope> RecordsFromAsync(
            string nodeId, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var input in Inputs(nodeId))
            {
                var predecessor = _definition.Nodes.FirstOrDefault(node =>
                    string.Equals(node.Id, input, StringComparison.Ordinal));
                if (predecessor is null) continue;
                var fingerprint = Fingerprint(predecessor);
                DatasetRef? dataset = null;
                if (predecessor.Type.Contains("reader", StringComparison.OrdinalIgnoreCase))
                {
                    dataset = await _stage.TryGetAsync<DatasetRef>(
                        _instanceId, predecessor.Id, fingerprint, ct);
                }
                else
                {
                    var transformed = await _stage.TryGetAsync<TransformResultRef>(
                        _instanceId, predecessor.Id, fingerprint, ct);
                    dataset = transformed?.Dataset;
                }
                if (dataset is null) continue;
                await foreach (var record in _stage.ReadAsync(dataset, ct).WithCancellation(ct))
                {
                    yield return record;
                }
            }
        }

        private List<DiscoveredFile> FilesFrom(string nodeId)
        {
            var result = new List<DiscoveredFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in Inputs(nodeId))
            {
                if (!_files.TryGetValue(input, out var files)) continue;
                foreach (var file in files)
                {
                    if (seen.Add(file.Identity.Key)) result.Add(file);
                }
            }
            return result.Count > 0 ? result : _files.Values.SelectMany(files => files).ToList();
        }

        private TimeSpan Lookback()
        {
            if (_definition.Layout.TryGetValue("lookbackDays", out var days) && days is not null &&
                double.TryParse(days.ToString(), out var parsed) && parsed > 0)
            {
                return TimeSpan.FromDays(parsed);
            }
            return TimeSpan.FromDays(1);
        }

        public async ValueTask ExecuteAsync(
            string instanceId, string nodeId, string activityType, string nodeType,
            CancellationToken cancellationToken)
        {
            _instanceId = instanceId;
            _executed.Add(nodeId);
            var node = _definition.Nodes.SingleOrDefault(item => item.Id == nodeId)
                       ?? throw new InvalidOperationException($"published node '{nodeId}' is missing");
            var view = View(nodeId);
            try
            {
                var custom = _handlers?.Providers.FirstOrDefault(handler =>
                    string.Equals(handler.NodeType, node.Type, StringComparison.Ordinal));
                if (custom is not null)
                {
                    await custom.ExecuteAsync(new IndustrialNodeExecutionContext(
                        _definition, node, _instanceId, _runId, Inputs(node.Id).ToArray(),
                        RecordsFromAsync, _stage, _ledger, _resources, _vault, _factories,
                        _defaultSink, _source, _reader,
                        (count, samples) =>
                        {
                            view.Records = count;
                            view.Samples = samples;
                        }), cancellationToken);
                    if (view.Status == "Running") view.Status = "Succeeded";
                    if (custom.Completion == IndustrialNodeCompletion.Delivery)
                        await FinishRunIfComplete(cancellationToken);
                    return;
                }
                if (nodeType.Contains("source", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteSourceAsync(node, cancellationToken);
                }
                else if (nodeType.Contains("reader", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteReaderAsync(node, cancellationToken);
                }
                else if (nodeType.Contains("mapping", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteMappingAsync(node, cancellationToken);
                }
                else if (nodeType.Contains("validate", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteValidationAsync(node, cancellationToken);
                }
                else if (nodeType.Contains("archive", StringComparison.OrdinalIgnoreCase))
                {
                    await ExecuteArchiveAsync(node, cancellationToken);
                }
                else if (IsSink(nodeType))
                {
                    await ExecuteSinkAsync(node, cancellationToken);
                }
                if (view.Status == "Running") view.Status = "Succeeded";
            }
            catch (Exception ex)
            {
                view.Status = cancellationToken.IsCancellationRequested ? "Interrupted" : "Failed";
                view.Error = ex.Message;
                throw;
            }
        }


        private string Fingerprint(WorkflowNode node)
        {
            var material = string.Join("|",
                $"{_workflowId}:{_definition.Version}", node.Id, node.Type,
                JsonSerializer.Serialize(node.Config));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }

        private ValueTask<T?> CachedAsync<T>(WorkflowNode node, CancellationToken ct) where T : class =>
            _stage.TryGetAsync<T>(_instanceId, node.Id, Fingerprint(node), ct);

        private async ValueTask<DiscoveredFile> RelocateAsync(BatchFileRef file, CancellationToken ct)
        {
            var resource = await _resources.GetAsync(file.ResourceId, ct)
                           ?? throw new InvalidOperationException($"resource '{file.ResourceId}' is no longer registered");
            var root = resource.Configuration.TryGetValue("root", out var value) ? value?.ToString() : null;
            if (string.IsNullOrWhiteSpace(root) && string.IsNullOrWhiteSpace(file.SnapshotPath))
                throw new InvalidOperationException("resource has no root");
            var fullPath = string.IsNullOrWhiteSpace(file.SnapshotPath)
                ? Path.Combine(root!, file.RelativePath)
                : file.SnapshotPath!;
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"staged source file '{file.IdentityKey}' no longer exists at '{fullPath}'");
            }
            FileIdentity identity;
            if (string.IsNullOrWhiteSpace(file.SnapshotPath))
            {
                identity = FileIdentityFactory.Create(fullPath, root!, resource.Id, file.SourceDate);
            }
            else
            {
                identity = FileIdentityFactory.CreateFromSnapshot(
                    fullPath, file.RelativePath, file.IdentityKey,
                    resource.Id, file.ContentHash, file.SourceDate);
            }
            StagedFileIdentity.Verify(file, identity);
            return new DiscoveredFile(identity, fullPath, file.SourceDate, resource.Id);
        }

        private async ValueTask ExecuteSourceAsync(WorkflowNode node, CancellationToken ct)
        {
            var cached = await CachedAsync<FileBatchRef>(node, ct);
            if (cached is not null)
            {
                // A resumed instance must keep the original run id so its
                // incomplete deliveries belong to the same run-scoped ledger.
                _runId = cached.RunId;
                // A resumed run must still point at the instance whose stage
                // outputs it reuses; legacy runs may not carry the link yet.
                await _ledger.SetRunInstanceAsync(_runId!, _instanceId, ct);
                // Never overwrite a run snapshot with a different definition.
                // Recovery of a bound binding must keep its exact bound JSON.
                var stored = await _ledger.GetRunDefinitionAsync(_runId!, ct);
                if (string.IsNullOrWhiteSpace(stored))
                {
                    await _ledger.SetRunDefinitionAsync(_runId!, _definitionJson, ct);
                }
                else if (!string.Equals(ElsaPersistedExecution.DefinitionHash(stored),
                             _definitionHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"run '{_runId}' snapshot belongs to a different definition; refusing to resume");
                }
                var files = new List<DiscoveredFile>();
                foreach (var refFile in cached.Files)
                {
                    var ledgerFile = await _ledger.GetFileAsync(refFile.CollectionFileId, ct);
                    if (ledgerFile is null) continue;
                    _ledgerFiles[ledgerFile.Identity.Key] = ledgerFile;
                    files.Add(await RelocateAsync(refFile, ct));
                }
                _files[node.Id] = files;
                View(node.Id).Status = "Reused";
                View(node.Id).Files = files.Count;
                return;
            }

            if (_runId is null)
            {
                _runId = (await _ledger.StartAsync(_workflowId, ct)).Id;
                await _ledger.BeginRunAsync(_runId, ct);
                await _ledger.SetRunInstanceAsync(_runId, _instanceId, ct);
                await _ledger.SetRunDefinitionAsync(_runId, _definitionJson, ct);
            }

            var resourceId = node.Config.TryGetValue("resourceId", out var configured)
                             && configured is not null && !string.IsNullOrWhiteSpace(configured.ToString())
                ? configured.ToString()!
                : _resourceId;
            var profileJson = new NodeConfigReader(node.Config).GetString("fileProfile", string.Empty);
            var fileProfile = string.IsNullOrWhiteSpace(profileJson)
                ? null : JsonSerializer.Deserialize<FileProfileDefinition>(profileJson, WebJson);
            var resourceDefinition = await _resources.GetAsync(resourceId, ct);
            var discoveryRoot = resourceDefinition?.Configuration.TryGetValue("root", out var configuredRoot) == true
                ? configuredRoot?.ToString() ?? string.Empty
                : string.Empty;
            var plannedDates = PlannedDates(node.Config);
            // A planned business date may arrive late: its file mtime is older
            // than the lookback but its encoded date is still in the window.
            var range = plannedDates.Count > 0
                ? new SourceDateRange(
                    new DateTimeOffset(plannedDates.Min().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                    new DateTimeOffset(plannedDates.Max().AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)))
                : _planner.Plan(DateTimeOffset.UtcNow, Lookback());
            // Profile date rules are applied inside discovery, before the
            // range filter, so a late directory/file with an old mtime is not
            // lost before FileProfileDiscovery can read its business date.
            var discovered = await _source.DiscoverAsync(
                resourceId, range, fileProfile,
                plannedDates.Count > 0 ? plannedDates : null, ct);
            discovered = FileProfileDiscovery.Apply(fileProfile, discovered, discoveryRoot);
            var fileNamePattern = new NodeConfigReader(node.Config).GetString("fileNamePattern", string.Empty);
            if (!string.IsNullOrWhiteSpace(fileNamePattern))
            {
                var regex = new System.Text.RegularExpressions.Regex(
                    "^" + System.Text.RegularExpressions.Regex.Escape(fileNamePattern)
                        .Replace("\\*", ".*").Replace("\\?", ".") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                discovered = discovered.Where(file =>
                    regex.IsMatch(Path.GetFileName(file.FullPath))).ToArray();
            }
            if (plannedDates.Count > 0)
                discovered = discovered.Where(file => plannedDates.Contains(
                    DateOnly.FromDateTime(file.SourceDate.UtcDateTime))).ToArray();
            var discoveredFiles = new List<DiscoveredFile>();
            var batchFiles = new List<BatchFileRef>();
            var skippedDates = new HashSet<DateOnly>();
            foreach (var file in discovered)
            {
                // Freeze the exact bytes before they enter the ledger. A
                // producer that changed the file after discovery is treated
                // as "not ready" and re-probed, not recorded with a stale hash.
                var snapshot = await ImmutableFileSnapshot.CreateAsync(
                    file.FullPath, file.Identity, _snapshotRoot, ct);
                if (snapshot is null)
                {
                    skippedDates.Add(DateOnly.FromDateTime(file.SourceDate.UtcDateTime));
                    continue;
                }
                var snapshotFile = file with
                {
                    FullPath = snapshot.FullPath,
                    Identity = snapshot.Identity,
                };
                var known = await _ledger.GetFileByIdentityAsync(
                    snapshotFile.ResourceId, snapshotFile.Identity.Key, ct);
                var ledgerFile = known ?? await _ledger.DiscoverAsync(_runId!, snapshotFile, ct);
                _ledgerFiles[snapshotFile.Identity.Key] = ledgerFile;
                discoveredFiles.Add(snapshotFile);
                batchFiles.Add(new BatchFileRef(ledgerFile.Id, snapshotFile.ResourceId,
                    snapshotFile.Identity.Key, snapshotFile.Identity.RelativePath,
                    snapshotFile.Identity.ContentHash, snapshotFile.SourceDate,
                    snapshotFile.Identity.Length, snapshotFile.Identity.LastWriteUtc,
                    snapshot.FullPath));
            }
            _files[node.Id] = discoveredFiles;
            View(node.Id).Files = discoveredFiles.Count;
            _onSourceDates?.Invoke(discoveredFiles
                .GroupBy(file => DateOnly.FromDateTime(file.SourceDate.UtcDateTime))
                .ToDictionary(group => group.Key, group => group.Count()));
            if (skippedDates.Count > 0) _onSkippedDates?.Invoke(skippedDates);
            await _stage.CommitAsync(_instanceId, node.Id, Fingerprint(node),
                new FileBatchRef($"batch-{Guid.NewGuid():N}", _instanceId, node.Id,
                    Fingerprint(node), batchFiles, _runId), ct);
        }

        private async ValueTask ExecuteReaderAsync(WorkflowNode node, CancellationToken ct)
        {
            var cached = await CachedAsync<DatasetRef>(node, ct);
            if (cached is not null)
            {
                var cachedSample = new SampleAccumulator();
                await foreach (var record in _stage.ReadAsync(cached, ct).WithCancellation(ct))
                {
                    cachedSample.Add(record);
                }
                View(node.Id).Status = "Reused";
                View(node.Id).Records = cachedSample.Count;
                View(node.Id).Samples = cachedSample.Values;
                return;
            }

            var reader = node.Type.Contains("reader.csv", StringComparison.OrdinalIgnoreCase)
                ? new CsvRecordReader(node.Config)
                : node.Type.Contains("reader.excel", StringComparison.OrdinalIgnoreCase)
                    ? new ExcelRecordReader(node.Config)
                    : _reader;
            var sample = new SampleAccumulator();
            async IAsyncEnumerable<RecordEnvelope> ReadFiles(
                [EnumeratorCancellation] CancellationToken token)
            {
                foreach (var file in FilesFrom(node.Id))
                {
                    await foreach (var record in reader.ReadAsync(file, token).WithCancellation(token))
                    {
                        sample.Add(record);
                        yield return record;
                    }
                }
            }
            await _stage.SpoolAsync(_instanceId, node.Id, Fingerprint(node), ReadFiles(ct), ct);
            View(node.Id).Records = sample.Count;
            View(node.Id).Samples = sample.Values;
        }

        private async ValueTask ExecuteMappingAsync(WorkflowNode node, CancellationToken ct)
        {
            var cached = await CachedAsync<TransformResultRef>(node, ct);
            if (cached is not null)
            {
                var cachedSample = new SampleAccumulator();
                await foreach (var record in _stage.ReadAsync(cached.Dataset, ct).WithCancellation(ct))
                {
                    cachedSample.Add(record);
                }
                View(node.Id).Status = "Reused";
                View(node.Id).Records = cachedSample.Count;
                View(node.Id).Samples = cachedSample.Values;
                return;
            }
            var transform = new MappingTransform(node.Config);
            var sample = new SampleAccumulator();
            async IAsyncEnumerable<RecordEnvelope> Map(
                [EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var record in RecordsFromAsync(node.Id, token).WithCancellation(token))
                {
                    var mapped = transform.Apply(record);
                    sample.Add(mapped);
                    yield return mapped;
                }
            }
            var dataset = await _stage.SpoolAsync(
                _instanceId, node.Id + "-dataset", Fingerprint(node), Map(ct), ct);
            await _stage.CommitAsync(_instanceId, node.Id, Fingerprint(node),
                new TransformResultRef($"transform-{Guid.NewGuid():N}", _instanceId, node.Id,
                    Fingerprint(node), dataset, sample.Count, 0), ct);
            View(node.Id).Records = sample.Count;
            View(node.Id).Samples = sample.Values;
        }

        private async ValueTask ExecuteValidationAsync(WorkflowNode node, CancellationToken ct)
        {
            var cached = await CachedAsync<TransformResultRef>(node, ct);
            if (cached is not null)
            {
                var cachedSample = new SampleAccumulator();
                await foreach (var record in _stage.ReadAsync(cached.Dataset, ct).WithCancellation(ct))
                {
                    cachedSample.Add(record);
                }
                View(node.Id).Status = "Reused";
                View(node.Id).Records = cachedSample.Count;
                View(node.Id).Samples = cachedSample.Values;
                return;
            }

            var transform = new ValidationTransform(node.Config);
            var sample = new SampleAccumulator();
            var rejected = 0;
            var warnings = 0;

            async IAsyncEnumerable<RecordEnvelope> Valid(
                [EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var record in RecordsFromAsync(node.Id, token).WithCancellation(token))
                {
                    if (transform.TryApply(record, out var applied, out var reason) && applied is not null)
                    {
                        sample.Add(applied);
                        yield return applied;
                        continue;
                    }
                    rejected++;
                    if (transform.OnInvalid == "fail")
                    {
                        throw new InvalidOperationException(
                            $"validation failed for {record.Source.Key}: {reason}");
                    }
                    if (transform.OnInvalid == "warn")
                    {
                        warnings++;
                        sample.Add(record);
                        yield return record;
                    }
                }
            }

            var dataset = await _stage.SpoolAsync(
                _instanceId, node.Id + "-dataset", Fingerprint(node), Valid(ct), ct);
            // Re-evaluate the staged input for quarantine instead of retaining
            // rejected records in memory during the first pass.
            if (transform.OnInvalid == "quarantine" && rejected > 0)
            {
                async IAsyncEnumerable<RecordEnvelope> Quarantine(
                    [EnumeratorCancellation] CancellationToken token)
                {
                    await foreach (var record in RecordsFromAsync(node.Id, token).WithCancellation(token))
                    {
                        if (!transform.TryApply(record, out var applied, out _) || applied is null)
                            yield return record;
                    }
                }
                await _stage.SpoolAsync(_instanceId, node.Id + "-quarantine", Fingerprint(node),
                    Quarantine(ct), ct);
            }

            View(node.Id).Records = sample.Count;
            View(node.Id).Samples = sample.Values;
            if (rejected > 0) View(node.Id).Error = $"{rejected} record(s) rejected";
            await _stage.CommitAsync(_instanceId, node.Id, Fingerprint(node),
                new TransformResultRef($"transform-{Guid.NewGuid():N}", _instanceId, node.Id,
                    Fingerprint(node), dataset, sample.Count, rejected), ct);
        }

        private async ValueTask<IRecordSink> ResolveSinkAsync(WorkflowNode node, CancellationToken ct)
        {
            var targetId = node.Config.TryGetValue("targetResourceId", out var value)
                ? value?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(targetId)) return _defaultSink;
            var resource = await _resources.GetAsync(targetId, ct)
                           ?? throw new InvalidOperationException($"target resource '{targetId}' is not registered");
            var factory = _factories.Providers.FirstOrDefault(candidate =>
                              string.Equals(candidate.ResourceType, resource.Type, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException(
                              $"no sink factory is registered for resource type '{resource.Type}'");
            return await factory.CreateAsync(resource, _vault, node.Config, ct);
        }

        private async ValueTask ExecuteSinkAsync(WorkflowNode node, CancellationToken ct)
        {
            var cachedResult = await CachedAsync<DeliveryResultRef>(node, ct);
            if (cachedResult is not null && _retryDeliveryId is null && _retryTargetId is null)
            {
                View(node.Id).Status = "Reused";
                View(node.Id).Records = cachedResult.Records;
                await FinishRunIfComplete(ct);
                return;
            }
            var sink = await ResolveSinkAsync(node, ct);
            var delivered = 0;
            var stageComplete = true;
            var sinkSample = new SampleAccumulator();
            async IAsyncEnumerable<RecordEnvelope> Observed(
                [EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var record in RecordsFromAsync(node.Id, token).WithCancellation(token))
                {
                    sinkSample.Add(record);
                    yield return record;
                }
            }
            var partitionIndex = 0;
            var datasets = await SinkRecordPartitioner.PartitionAsync(
                Observed(ct),
                (_, stream, token) => _stage.SpoolAsync(
                    _instanceId, $"{node.Id}-partition-{partitionIndex++}",
                    Fingerprint(node), stream, token),
                ct);
            View(node.Id).Samples = sinkSample.Values;
            foreach (var sourceKey in datasets.Keys)
            {
                if (!_ledgerFiles.TryGetValue(sourceKey, out var ledgerFile)) continue;
                var key = $"{sourceKey}->{sink.TargetId}";
                var delivery = await _ledger.StartDeliveryAsync(
                    ledgerFile.Id, sink.TargetId, key, "record", _runId, ct);
                if (_retryDeliveryId is not null && delivery.Id != _retryDeliveryId) continue;
                if (_retryTargetId is not null &&
                    !string.Equals(sink.TargetId, _retryTargetId, StringComparison.Ordinal)) continue;
                if (delivery.Status == "Succeeded") continue;
                var claim = await _ledger.TryClaimDeliveryAsync(
                    _runId!, delivery.Id, TimeSpan.FromMinutes(5), ct);
                if (claim is null)
                {
                    await _ledger.MarkDeliveryContestedAsync(
                        _runId!, delivery.Id, "delivery is held by another run", ct);
                    stageComplete = false;
                    continue;
                }
                try
                {
                    var paced = WithLeaseRenewal(
                        _stage.ReadAsync(datasets[sourceKey], ct), delivery.Id,
                        claim.ClaimToken, TimeSpan.FromMinutes(5), 250, ct);
                    var result = await sink.WriteAsync(ledgerFile.Id, paced, key, ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, claim.ClaimToken, result.Records, ct);
                    delivered += result.Records;
                }
                catch (OperationCanceledException)
                {
                    FatalExit = true;
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _anyFailure, 1);
                    stageComplete = false;
                    await _ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, ex.Message, ct);
                }
            }
            View(node.Id).Records = delivered;
            if (stageComplete)
            {
                await _stage.CommitAsync(_instanceId, node.Id, Fingerprint(node),
                    new DeliveryResultRef($"delivery-stage-{Guid.NewGuid():N}", sink.TargetId, delivered,
                        IdempotencyKey: string.Empty), ct);
            }
            await FinishRunIfComplete(ct);
        }

        private async ValueTask ExecuteArchiveAsync(WorkflowNode node, CancellationToken ct)
        {
            var cachedArchive = await CachedAsync<DeliveryResultRef>(node, ct);
            if (cachedArchive is not null && _retryDeliveryId is null && _retryTargetId is null)
            {
                View(node.Id).Status = "Reused";
                View(node.Id).Files = cachedArchive.Records;
                await FinishRunIfComplete(ct);
                return;
            }
            var archived = 0;
            var stageComplete = true;
            var archiveFiles = FilesFrom(node.Id);
            View(node.Id).Samples = archiveFiles.Take(3)
                .Select(file => file.Identity.Key).ToArray();
            foreach (var file in archiveFiles)
            {
                if (!_ledgerFiles.TryGetValue(file.Identity.Key, out var ledgerFile)) continue;
                var key = $"{file.Identity.Key}->{_archive.TargetId}";
                var delivery = await _ledger.StartDeliveryAsync(
                    ledgerFile.Id, _archive.TargetId, key, "archive", _runId, ct);
                if (_retryDeliveryId is not null && delivery.Id != _retryDeliveryId) continue;
                if (_retryTargetId is not null &&
                    !string.Equals(_archive.TargetId, _retryTargetId, StringComparison.Ordinal)) continue;
                if (delivery.Status == "Succeeded") continue;
                var claim = await _ledger.TryClaimDeliveryAsync(
                    _runId!, delivery.Id, TimeSpan.FromMinutes(5), ct);
                if (claim is null)
                {
                    await _ledger.MarkDeliveryContestedAsync(
                        _runId!, delivery.Id, "delivery is held by another run", ct);
                    stageComplete = false;
                    continue;
                }
                try
                {
                    await _archive.ArchiveAsync(file, key, ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, claim.ClaimToken, 1, ct);
                    archived++;
                }
                catch (OperationCanceledException)
                {
                    FatalExit = true;
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _anyFailure, 1);
                    stageComplete = false;
                    await _ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, ex.Message, ct);
                }
            }
            View(node.Id).Files = archived;
            if (stageComplete)
            {
                await _stage.CommitAsync(_instanceId, node.Id, Fingerprint(node),
                    new DeliveryResultRef($"archive-stage-{Guid.NewGuid():N}", _archive.TargetId, archived,
                        IdempotencyKey: string.Empty), ct);
            }
            await FinishRunIfComplete(ct);
        }

        /// <summary>Keeps a long delivery alive while the sink consumes it.
        /// Renewal is bound to the claim token, so a fenced writer cannot
        /// extend somebody else's lease.</summary>
        private async IAsyncEnumerable<RecordEnvelope> RecordsForSourceAsync(
            string nodeId, string sourceKey, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var record in RecordsFromAsync(nodeId, ct).WithCancellation(ct))
            {
                if (string.Equals(record.Source.Key, sourceKey, StringComparison.Ordinal))
                    yield return record;
            }
        }

        private async IAsyncEnumerable<RecordEnvelope> WithLeaseRenewal(
            IAsyncEnumerable<RecordEnvelope> source, string deliveryId, string claimToken,
            TimeSpan lease, int every,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var index = 0;
            await foreach (var record in source.WithCancellation(ct))
            {
                if (++index % every == 0 &&
                    !await _ledger.RenewDeliveryLeaseAsync(deliveryId, claimToken, lease, ct))
                {
                    throw new InvalidOperationException(
                        $"delivery '{deliveryId}' lost its lease while writing");
                }
                yield return record;
            }
        }

        private static async IAsyncEnumerable<RecordEnvelope> Stream(IEnumerable<RecordEnvelope> source)
        {
            foreach (var record in source)
            {
                await ValueTask.CompletedTask;
                yield return record;
            }
        }

        private async ValueTask FinishRunIfComplete(CancellationToken ct)
        {
            if (_runId is null || Interlocked.Decrement(ref _remainingConsequences) != 0) return;
            await _ledger.FinalizeRunAsync(_runId, ct);
        }
    }
}

/// <summary>Activity kept for the Elsa PoC: prove a Cordis-contributed Activity
/// can be executed by a real Elsa service provider.</summary>
public sealed class CollectFileActivity : CodeActivity
{
    public string FileId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string EdgeSummary { get; set; } = string.Empty;
    public static int ExecutionCount;

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        Interlocked.Increment(ref ExecutionCount);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Partitions one input stream into per-source stage datasets with
/// bounded backpressure. Extracted so failure/cancellation behaviour is
/// independently testable without a full Elsa run.</summary>
/// <summary>Substitutes template parameters in a published immutable snapshot.
/// The published version is never mutated; bindings only supply runtime values.
/// </summary>
public static class WorkflowBinding
{
    /// <summary>Apply a reusable FileProfile to the immutable bound snapshot.
    /// It only writes config keys the existing readers/transforms already
    /// understand; no second parsing or mapping engine is introduced.
    /// </summary>
    public static WorkflowDefinition ApplyFileProfile(
        WorkflowDefinition definition, FileProfileDefinition? profile)
    {
        if (profile is null) return definition;
        return definition with
        {
            Nodes = definition.Nodes.Select(node => ApplyToFile(node, profile)).ToArray(),
        };
    }

    private static WorkflowNode ApplyToFile(WorkflowNode node, FileProfileDefinition profile)
    {
        var type = node.Type.ToLowerInvariant();
        var config = new Dictionary<string, object?>(node.Config, StringComparer.Ordinal);
        if (type.Contains("source"))
        {
            config["fileNamePattern"] = profile.FileNamePattern;
            config["format"] = profile.Format;
        }
        if (type.Contains("reader"))
        {
            config["delimiter"] = profile.Delimiter;
            config["hasHeaderRecord"] = profile.HasHeader;
            config["encoding"] = profile.Encoding;
            config["format"] = profile.Format;
        }
        if (type.Contains("mapping"))
        {
            config["mapping"] = profile.Mapping.ToDictionary(
                pair => pair.Key, pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            config["metadataRules"] = (profile.MetadataRules ?? [])
                .Select(rule => new Dictionary<string, object?>
                {
                    ["from"] = rule.From, ["pattern"] = rule.Pattern,
                    ["key"] = rule.Key, ["required"] = rule.Required,
                }).ToArray();
        }
        if (type.Contains("validate"))
        {
            config["required"] = profile.RequiredFields.ToArray();
        }
        return node with { Config = config };
    }

    public static WorkflowDefinition BindParameters(
        WorkflowDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        string Replace(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            foreach (var pair in values)
                text = text.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.OrdinalIgnoreCase);
            return text;
        }
        object? ReplaceValue(object? value) => value switch
        {
            string text => Replace(text),
            // Workflow definitions arrive over the JSON command boundary, where
            // object-typed config values deserialize as JsonElement rather than
            // string. Substitution must work for both shapes.
            JsonElement element when element.ValueKind == JsonValueKind.String =>
                Replace(element.GetString()),
            JsonValue node when node.TryGetValue<string>(out var nodeText) => Replace(nodeText),
            _ => value,
        };
        return definition with
        {
            Nodes = definition.Nodes.Select(node => node with
            {
                Config = node.Config.ToDictionary(pair => pair.Key,
                    pair => ReplaceValue(pair.Value), StringComparer.Ordinal),
            }).ToArray(),
            ResourceReferences = definition.ResourceReferences.Select(Replace)
                .Where(value => value.Length > 0).ToArray(),
        };
    }
}

public static class SinkRecordPartitioner
{
    public const int Capacity = 128;

    public static async ValueTask<Dictionary<string, DatasetRef>> PartitionAsync(
        IAsyncEnumerable<RecordEnvelope> records,
        Func<string, IAsyncEnumerable<RecordEnvelope>, CancellationToken, ValueTask<DatasetRef>> spool,
        CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var partitions = new Dictionary<string, (Channel<RecordEnvelope> Channel, Task<DatasetRef> Spool)>
            (StringComparer.Ordinal);
        var datasets = new Dictionary<string, DatasetRef>(StringComparer.Ordinal);
        Exception? primary = null;
        var consumerFailed = false;
        try
        {
            await foreach (var record in records.WithCancellation(linked.Token))
            {
                // A dead consumer must be observed BEFORE writing: otherwise a
                // full bounded channel would block the producer forever.
                var faults = partitions.Where(pair => pair.Value.Spool.IsFaulted)
                    .Select(pair => pair.Value.Spool.Exception ?? new Exception("spool failed"))
                    .ToArray();
                if (faults.Length > 0)
                {
                    consumerFailed = true;
                    primary = new AggregateException("sink partition spooling failed", faults);
                    linked.Cancel();
                    break;
                }
                if (!partitions.TryGetValue(record.Source.Key, out var partition))
                {
                    var channel = Channel.CreateBounded<RecordEnvelope>(new BoundedChannelOptions(Capacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = true,
                    });
                    Task<DatasetRef> spoolTask;
                    try
                    {
                        spoolTask = spool(record.Source.Key, Drain(channel.Reader, linked.Token),
                            linked.Token).AsTask();
                    }
                    catch (Exception ex)
                    {
                        spoolTask = Task.FromException<DatasetRef>(ex);
                    }
                    partition = (channel, spoolTask);
                    partitions.Add(record.Source.Key, partition);
                }
                // Race-free failure propagation: wait for EITHER the bounded
                // write to complete OR the consumer to finish/fail. A consumer
                // that dies while the producer is blocked on a full channel is
                // therefore observed immediately.
                var write = partition.Channel.Writer.WriteAsync(record, linked.Token).AsTask();
                var winner = await Task.WhenAny(write, partition.Spool);
                if (!ReferenceEquals(winner, write))
                {
                    consumerFailed = true;
                    primary = new AggregateException("sink partition spooling failed",
                        partition.Spool.Exception
                        ?? new Exception("spool completed before its input was fully written"));
                    linked.Cancel();
                    break;
                }
                await write;
            }
        }
        catch (Exception ex)
        {
            primary ??= ex;
        }

        foreach (var partition in partitions.Values) partition.Channel.Writer.TryComplete();
        List<Exception>? failures = null;
        foreach (var pair in partitions)
        {
            try { datasets[pair.Key] = await pair.Value.Spool; }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && linked.IsCancellationRequested) continue;
                failures ??= [];
                failures.Add(ex);
            }
        }
        if (primary is AggregateException) throw primary;
        if (failures is not null)
            throw new AggregateException("sink partition spooling failed", failures);
        if (primary is not null)
        {
            if (consumerFailed && primary is OperationCanceledException)
                throw new AggregateException("sink partition spooling failed", primary);
            throw primary;
        }
        return datasets;
    }

    private static async IAsyncEnumerable<RecordEnvelope> Drain(
        ChannelReader<RecordEnvelope> reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var record in reader.ReadAllAsync(token).WithCancellation(token))
            yield return record;
    }
}

/// <summary>Maps the single editable industrial Definition to a published Elsa
/// workflow. The G6 graph model is never used as the persistence format.</summary>
public static class ElsaDefinitionConverter
{
    /// <summary>Persists a canonical, replayable graph definition. Elsa's own
    /// serializer round-trip of a custom CodeActivity is version-fragile, so
    /// the stored artifact is the graph topology plus the pinned industrial
    /// definition; the runner materializes that stored topology into Elsa.</summary>
    public static Task<string> ToElsaJson(WorkflowDefinition definition)
    {
        var edgesByNode = definition.Edges
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var graph = new JsonObject
        {
            ["kind"] = "flowchart",
            ["definitionId"] = definition.WorkflowId,
            ["version"] = definition.Version,
            ["name"] = definition.Name,
            ["activities"] = new JsonArray(definition.Nodes.Select(node => (JsonNode)new JsonObject
            {
                ["id"] = node.Id,
                ["nodeType"] = node.Type,
                ["activityType"] = node.Type,
                ["elsaActivity"] = IndustrialNodeActivityFactory.ElsaActivityName(node.Type),
                ["edgeSummary"] = string.Join("|", edgesByNode.TryGetValue(node.Id, out var edges)
                    ? edges.Select(edge => $"{edge.SourcePort}->{edge.Target}:{edge.TargetPort}")
                    : []),
            }).ToArray()),
            ["connections"] = new JsonArray(definition.Edges
                .Where(edge => definition.Nodes.Any(node => node.Id == edge.Source) &&
                               definition.Nodes.Any(node => node.Id == edge.Target))
                .Select(edge => (JsonNode)new JsonObject
                {
                    ["source"] = edge.Source,
                    ["target"] = edge.Target,
                    ["sourcePort"] = edge.SourcePort,
                    ["targetPort"] = edge.TargetPort,
                }).ToArray()),
        };
        var envelope = new JsonObject
        {
            ["elsa"] = graph,
            ["definition"] = System.Text.Json.JsonSerializer.SerializeToNode(definition),
        };
        return Task.FromResult(envelope.ToJsonString());
    }

    public static Workflow BuildWorkflow(WorkflowDefinition definition)
    {
        var graph = JsonNode.Parse(ToElsaJson(definition).GetAwaiter().GetResult())!["elsa"]!.AsObject();
        return BuildWorkflow(graph);
    }

    /// <summary>Materializes the published graph as a real Elsa Flowchart. Node
    /// order is not computed at run time: Elsa follows the persisted connections.</summary>
    public static Workflow BuildWorkflow(JsonObject graph, string? definitionId = null)
    {
        var flowchart = new Flowchart();
        var activities = new Dictionary<string, IActivity>(StringComparer.Ordinal);
        foreach (var item in graph["activities"]?.AsArray() ?? [])
        {
            if (item is not JsonObject obj) continue;
            var id = obj["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) continue;
            var nodeType = obj["nodeType"]?.GetValue<string>()
                            ?? obj["activityType"]?.GetValue<string>()
                            ?? string.Empty;
            var activity = IndustrialNodeActivityFactory.Create(
                id,
                nodeType,
                obj["activityType"]?.GetValue<string>() ?? nodeType,
                obj["edgeSummary"]?.GetValue<string>() ?? string.Empty);
            activities[id] = activity;
            flowchart.Activities.Add(activity);
        }

        foreach (var item in graph["connections"]?.AsArray() ?? [])
        {
            if (item is not JsonObject obj) continue;
            var source = obj["source"]?.GetValue<string>();
            var target = obj["target"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) continue;
            if (!activities.TryGetValue(source, out var from) || !activities.TryGetValue(target, out var to)) continue;
            flowchart.Connections.Add(new Connection(from, to));
        }

        return new Workflow(flowchart)
        {
            Identity = new WorkflowIdentity(
                definitionId ?? graph["definitionId"]?.GetValue<string>() ?? string.Empty,
                graph["version"]?.GetValue<int>() ?? 0,
                graph["name"]?.GetValue<string>() ?? string.Empty,
                null),
        };
    }
}

internal static class ElsaRuntime
{
    private static void ApplyMigrations<TContext>(IServiceProvider provider)
        where TContext : DbContext
    {
        var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    /// <summary>One provider factory for publish and run. The custom Activity
    /// must be registered before Elsa serializes/deserializes the graph.</summary>
    public static ServiceProvider CreateProvider(
        IIndustrialNodeExecutor? executor = null, string? databasePath = null)
    {
        databasePath ??= Path.Combine(Path.GetTempPath(), "gocordis-industrial-elsa.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = $"Data Source={Path.GetFullPath(databasePath)}";
        var services = new ServiceCollection();
        services.AddElsa(elsa =>
        {
            elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore(feature =>
            {
                feature.UseSqlite(connection);
                feature.RunMigrations = true;
            }));
            elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(feature =>
            {
                feature.UseSqlite(connection);
                feature.RunMigrations = true;
            }));
        });
        if (executor is not null)
        {
            services.AddSingleton(executor);
        }
        var provider = services.BuildServiceProvider();
        ApplyMigrations<ManagementElsaDbContext>(provider);
        ApplyMigrations<RuntimeElsaDbContext>(provider);
        var registry = provider.GetRequiredService<IActivityRegistry>();
        foreach (var activityType in IndustrialNodeActivityFactory.RegisteredActivityTypes)
        {
            registry.RegisterAsync(activityType).GetAwaiter().GetResult();
        }
        return provider;
    }
}

/// <summary>Executes the persisted Elsa definition. No topological order and
/// no per-run Sequence are constructed here: Elsa owns the published graph.</summary>
public sealed class ElsaWorkflowExecutor
{
    public sealed record Result(IReadOnlyList<string> Executed, string Status);

    public static async ValueTask<Result> ExecuteAsync(
        PublishedWorkflow published,
        IIndustrialNodeExecutor executor,
        CancellationToken ct = default)
    {
        var envelope = JsonNode.Parse(published.ElsaDefinitionJson) as JsonObject
                       ?? throw new InvalidOperationException("published Elsa definition is not a JSON object");
        var graph = envelope["elsa"] as JsonObject
                    ?? throw new InvalidOperationException("published Elsa definition has no Elsa graph");
        await using var provider = ElsaRuntime.CreateProvider(executor);
        var workflow = ElsaDefinitionConverter.BuildWorkflow(graph);
        var runner = provider.GetRequiredService<IWorkflowRunner>();
        await runner.RunAsync(workflow, cancellationToken: ct);
        return new Result(executor.Executed is { } list ? list : [], "Completed");
    }
}

public static class ElsaPoC
{
    public static async Task<string> ExecuteAsync(string fileId)
    {
        var services = new ServiceCollection();
        services.AddElsa();
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var sequence = new Sequence();
        sequence.Activities.Add(new CollectFileActivity { FileId = fileId });
        var workflow = new Workflow(sequence);
        var result = await runner.RunAsync(workflow);
        return result is null ? "null" : result.GetType().Name;
    }
}
