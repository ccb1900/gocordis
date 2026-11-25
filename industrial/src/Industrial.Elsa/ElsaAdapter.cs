using Elsa.Extensions;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
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

/// <summary>The narrow bridge from a persisted Elsa Activity back to the
/// Cordis-owned data work for one published workflow node.</summary>
public interface IIndustrialNodeExecutor
{
    IReadOnlyList<string> Executed { get; }
    ValueTask ExecuteAsync(
        string nodeId, string activityType, string nodeType, CancellationToken cancellationToken);
}

public sealed class IndustrialElsaComponent : Component
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly ElsaActivityCatalog _catalog = new();
    private readonly IReadOnlyDictionary<string, object?> _config;
    private ElsaWorkflowExecutor.Result? _lastExecution;
    private string? _lastInstanceId;
    private string? _lastDefinitionId;
    private string? _lastError;

    public IndustrialElsaComponent() : this(null) { }

    public IndustrialElsaComponent(IReadOnlyDictionary<string, object?>? config) =>
        _config = config ?? new Dictionary<string, object?>();

    private string Root => _config.TryGetValue("root", out var root) && root is not null &&
                            !string.IsNullOrWhiteSpace(root.ToString())
        ? root.ToString()!
        : Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ROOT")
          ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial");

    private string ElsaDbPath => Path.Combine(Root, "industrial-elsa.db");

    public override string Name => "industrial-elsa";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowValidator,
         IndustrialKeys.WorkflowRegistry, ApiGateway.Key,
         IndustrialKeys.CollectionCoordinator, IndustrialKeys.CollectionLedger,
         IndustrialKeys.ResourceRegistry, IndustrialKeys.CredentialVault,
         IndustrialKeys.HistoricalRangePlanner, IndustrialKeys.FileSource,
         IndustrialKeys.RecordReader, IndustrialKeys.RecordSinkFactories,
         IndustrialKeys.RecordSink, IndustrialKeys.ArchiveSink];
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
        var sinkFactories = context.Require(IndustrialKeys.RecordSinkFactories);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
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
                    var elsJson = await ElsaDefinitionConverter.ToElsaJson(definition);
                    var published = await workflows.PublishAsync(id, elsJson);
                    if (published is null)
                    {
                        throw new InvalidOperationException($"failed to publish workflow '{id}'");
                    }
                }, owner);
                api.RegisterCommand("industrial/run-workflow", async payload =>
                {
                    try
                    {
                        await RunWorkflowAsync(
                            payload, workflows, coordinator, ledger, resources, vault, planner,
                            source, reader, sinkFactories, sink, archive, ElsaDbPath);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        throw;
                    }
                }, owner);
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
                            sinkFactories, sink, archive, ElsaDbPath);
                    }
                }, owner);
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
                    }, owner);
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });
        return null;
    }

    private async ValueTask RunWorkflowAsync(
        JsonNode? payload,
        IWorkflowRegistry workflows,
        ICollectionCoordinator coordinator,
        ICollectionLedger ledger,
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IFileSource source,
        IRecordReader reader,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink defaultSink,
        IArchiveSink archive,
        string elsaDbPath)
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
            ? await workflows.GetPublishedVersionAsync(id, requestedVersion.Value)
            : await workflows.GetPublishedAsync(id);
        if (published is null)
        {
            var suffix = requestedVersion.HasValue ? $" version {requestedVersion.Value}" : string.Empty;
            throw new InvalidOperationException($"workflow '{id}'{suffix} has no published version");
        }

        if (await resources.GetAsync(resourceId) is null)
        {
            throw new InvalidOperationException($"resource '{resourceId}' not found");
        }

        // The RUN input is the persisted published artifact. The draft is never
        // consulted here: publishing v1 and editing v2 must not change a run.
        var definition = published.Definition;
        var executor = new PublishedNodeExecutor(
            id, resourceId, definition, coordinator, ledger, resources, vault,
            planner, source, reader, sinkFactories, defaultSink, archive);
        var instanceId = body["instanceId"]?.GetValue<string>();
        var resume = body["resume"] is JsonValue flag && flag.TryGetValue<bool>(out var shouldResume) && shouldResume;
        _lastError = null;
        var result = await ElsaPersistedExecution.RunAsync(
            published, executor, resourceId, elsaDbPath, instanceId, resume);
        _lastInstanceId = result.InstanceId;
        _lastDefinitionId = result.DefinitionId;
        var productStatus = string.Equals(result.Status, "Finished", StringComparison.Ordinal)
            ? "Completed"
            : result.Status;
        _lastExecution = new ElsaWorkflowExecutor.Result(result.Executed, productStatus);
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
        private readonly Broker<IRecordSinkFactory> _factories;
        private readonly IRecordSink _defaultSink;
        private readonly IArchiveSink _archive;
        private readonly Dictionary<string, List<DiscoveredFile>> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RecordEnvelope>> _records = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CollectionFile> _ledgerFiles = new(StringComparer.Ordinal);
        private readonly List<string> _executed = [];
        private string? _runId;
        private int _remainingConsequences;
        private int _anyFailure;

        public PublishedNodeExecutor(
            string workflowId, string resourceId, WorkflowDefinition definition,
            ICollectionCoordinator coordinator, ICollectionLedger ledger,
            IResourceRegistry resources, ICredentialVault vault,
            IHistoricalRangePlanner planner, IFileSource source, IRecordReader reader,
            Broker<IRecordSinkFactory> factories, IRecordSink defaultSink, IArchiveSink archive)
        {
            _workflowId = workflowId;
            _resourceId = resourceId;
            _definition = definition;
            _ledger = ledger;
            _resources = resources;
            _vault = vault;
            _planner = planner;
            _source = source;
            _reader = reader;
            _factories = factories;
            _defaultSink = defaultSink;
            _archive = archive;
            foreach (var node in definition.Nodes.Where(node =>
                         IsSink(node.Type) || node.Type.Contains("archive", StringComparison.OrdinalIgnoreCase)))
            {
                if (!node.Type.Contains("archive", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _remainingConsequences);
                }
            }
        }

        public IReadOnlyList<string> Executed => _executed;

        private static bool IsSink(string nodeType) =>
            nodeType.Contains("sink", StringComparison.OrdinalIgnoreCase);

        private IEnumerable<string> Inputs(string nodeId) => _definition.Edges
            .Where(edge => edge.Target == nodeId)
            .Select(edge => edge.Source)
            .Distinct(StringComparer.Ordinal);

        private List<RecordEnvelope> RecordsFrom(string nodeId)
        {
            var result = new List<RecordEnvelope>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in Inputs(nodeId))
            {
                if (!_records.TryGetValue(input, out var records)) continue;
                foreach (var record in records)
                {
                    if (seen.Add(record.Source.Key)) result.Add(record);
                }
            }
            return result;
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
            string nodeId, string activityType, string nodeType, CancellationToken cancellationToken)
        {
            _executed.Add(nodeId);
            var node = _definition.Nodes.SingleOrDefault(item => item.Id == nodeId)
                       ?? throw new InvalidOperationException($"published node '{nodeId}' is missing");

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
                var transform = new MappingTransform(node.Config);
                _records[node.Id] = RecordsFrom(node.Id).Select(transform.Apply).ToList();
            }
            else if (nodeType.Contains("validate", StringComparison.OrdinalIgnoreCase))
            {
                var transform = new ValidationTransform(node.Config);
                var valid = new List<RecordEnvelope>();
                foreach (var record in RecordsFrom(node.Id))
                {
                    if (transform.TryApply(record, out var applied, out _) && applied is not null)
                    {
                        valid.Add(applied);
                    }
                }
                _records[node.Id] = valid;
            }
            else if (nodeType.Contains("archive", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteArchiveAsync(node, cancellationToken);
            }
            else if (IsSink(nodeType))
            {
                await ExecuteSinkAsync(node, cancellationToken);
            }
        }

        private async ValueTask ExecuteSourceAsync(WorkflowNode node, CancellationToken ct)
        {
            _runId ??= (await _ledger.StartAsync(_workflowId, ct)).Id;
            var resourceId = node.Config.TryGetValue("resourceId", out var configured)
                             && configured is not null && !string.IsNullOrWhiteSpace(configured.ToString())
                ? configured.ToString()!
                : _resourceId;
            var discovered = await _source.DiscoverAsync(
                resourceId, _planner.Plan(DateTimeOffset.UtcNow, Lookback()), ct);
            var files = new List<DiscoveredFile>();
            foreach (var file in discovered)
            {
                if (!await _source.IsStableAsync(file, TimeSpan.FromMilliseconds(200), ct)) continue;
                var known = await _ledger.GetFileByIdentityAsync(file.ResourceId, file.Identity.Key, ct);
                var ledgerFile = known ?? await _ledger.DiscoverAsync(_runId!, file, ct);
                _ledgerFiles[file.Identity.Key] = ledgerFile;
                files.Add(file);
            }
            _files[node.Id] = files;
        }

        private async ValueTask ExecuteReaderAsync(WorkflowNode node, CancellationToken ct)
        {
            var records = new List<RecordEnvelope>();
            foreach (var file in FilesFrom(node.Id))
            {
                await foreach (var record in _reader.ReadAsync(file, ct).WithCancellation(ct))
                {
                    records.Add(record);
                }
            }
            _records[node.Id] = records;
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
            return await factory.CreateAsync(resource, _vault, ct);
        }

        private async ValueTask ExecuteSinkAsync(WorkflowNode node, CancellationToken ct)
        {
            var sink = await ResolveSinkAsync(node, ct);
            var byFile = RecordsFrom(node.Id)
                .GroupBy(record => record.Source.Key, StringComparer.Ordinal)
                .ToArray();
            foreach (var group in byFile)
            {
                if (!_ledgerFiles.TryGetValue(group.Key, out var ledgerFile)) continue;
                var key = $"{group.Key}->{sink.TargetId}";
                var delivery = await _ledger.StartDeliveryAsync(ledgerFile.Id, sink.TargetId, key, "record", ct);
                if (delivery.Status == "Succeeded" ||
                    !await _ledger.TryClaimDeliveryAsync(delivery.Id, ct)) continue;
                try
                {
                    var result = await sink.WriteAsync(ledgerFile.Id, Stream(group), key, ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, result.Records, ct);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _anyFailure, 1);
                    await _ledger.FailDeliveryAsync(delivery.Id, ex.Message, ct);
                }
            }
            FinishRunIfComplete();
        }

        private async ValueTask ExecuteArchiveAsync(WorkflowNode node, CancellationToken ct)
        {
            foreach (var file in FilesFrom(node.Id))
            {
                if (!_ledgerFiles.TryGetValue(file.Identity.Key, out var ledgerFile)) continue;
                var key = $"{file.Identity.Key}->{_archive.TargetId}";
                var delivery = await _ledger.StartDeliveryAsync(
                    ledgerFile.Id, _archive.TargetId, key, "archive", ct);
                if (delivery.Status == "Succeeded" ||
                    !await _ledger.TryClaimDeliveryAsync(delivery.Id, ct)) continue;
                try
                {
                    await _archive.ArchiveAsync(file, key, ct);
                    await _ledger.CompleteDeliveryAsync(delivery.Id, 1, ct);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _anyFailure, 1);
                    await _ledger.FailDeliveryAsync(delivery.Id, ex.Message, ct);
                }
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

        private void FinishRunIfComplete()
        {
            if (_runId is null || Interlocked.Decrement(ref _remainingConsequences) != 0) return;
            var status = Volatile.Read(ref _anyFailure) == 0 ? "Succeeded" : "PartialFailure";
            _ledger.CompleteRunAsync(_runId, status, null).GetAwaiter().GetResult();
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
