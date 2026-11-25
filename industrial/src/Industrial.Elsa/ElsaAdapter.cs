using Elsa.Extensions;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Industrial.Contracts;
using Industrial.Core;
using Microsoft.Extensions.DependencyInjection;

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

public sealed class IndustrialElsaComponent : Component
{
    private readonly ElsaActivityCatalog _catalog = new();
    private ElsaWorkflowExecutor.Result? _lastExecution;
    private string? _lastError;

    public override string Name => "industrial-elsa";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowValidator,
         IndustrialKeys.WorkflowRegistry, ApiGateway.Key,
         IndustrialKeys.CollectionCoordinator, IndustrialKeys.ResourceRegistry,
         IndustrialKeys.CredentialVault, IndustrialKeys.HistoricalRangePlanner,
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
        var resources = context.Require(IndustrialKeys.ResourceRegistry);
        var vault = context.Require(IndustrialKeys.CredentialVault);
        var planner = context.Require(IndustrialKeys.HistoricalRangePlanner);
        var reader = context.Require(IndustrialKeys.RecordReader);
        var sinkFactories = context.Require(IndustrialKeys.RecordSinkFactories);
        var sink = context.Require(IndustrialKeys.RecordSink);
        var archive = context.Require(IndustrialKeys.ArchiveSink);
        var owner = ContributionOwner.ForContext(context, "industrial", "elsa-publish");
        // Built-in activity types are contributed by this plugin; descriptors
        // added later by other plugins are registered on their own activation.
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
                        body.ToJsonString())
                                     ?? throw new InvalidOperationException("invalid workflow definition");
                    await workflows.SaveDraftAsync(definition);
                }, owner);
                api.RegisterCommand("industrial/publish-workflow", payload =>
                {
                    var body = payload as JsonObject ?? throw new InvalidOperationException("JSON payload required");
                    var id = body["workflowId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidOperationException("workflowId is required");
                    }

                    var definition = workflows.GetAsync(id).GetAwaiter().GetResult()
                                     ?? throw new InvalidOperationException($"workflow '{id}' not found");
                    var elsJson = new ElsaDefinitionConverter().ToElsaJson(definition);
                    workflows.PublishAsync(id, elsJson).GetAwaiter().GetResult();
                    return ValueTask.CompletedTask;
                }, owner);
                api.RegisterCommand("industrial/run-workflow", async payload =>
                {
                    try
                    {
                        await RunWorkflowAsync(
                            payload, workflows, coordinator, resources, vault, planner, reader,
                            sinkFactories, sink, archive);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        throw;
                    }
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
                        ["error"] = _lastError is null ? null : JsonValue.Create(_lastError),
                        ["executed"] = new JsonArray(_lastExecution.Executed
                            .Select(step => (JsonNode?)JsonValue.Create(step)).ToArray()),
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
        IResourceRegistry resources,
        ICredentialVault vault,
        IHistoricalRangePlanner planner,
        IRecordReader reader,
        Broker<IRecordSinkFactory> sinkFactories,
        IRecordSink defaultSink,
        IArchiveSink archive)
    {
        var body = payload as JsonObject ?? new JsonObject();
        var id = body["workflowId"]?.GetValue<string>()
                 ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_WORKFLOW") ?? "daily-collection";
        var resourceId = body["resourceId"]?.GetValue<string>()
                         ?? Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE") ?? "source-1";
        var definition = await workflows.GetAsync(id)
                         ?? throw new InvalidOperationException($"workflow '{id}' not found");
        if (await resources.GetAsync(resourceId) is null)
        {
            throw new InvalidOperationException($"resource '{resourceId}' not found");
        }

        // Target resources decide the delivery implementation and supply its
        // secret from the vault. A node carries only targetResourceId.
        var sinks = await ResolveSinksAsync(
            definition, resources, vault, sinkFactories, defaultSink);
        var pipeline = ApplyTransforms(reader, definition);
        var runner = new ElsaWorkflowExecutor();
        _lastError = null;
        _lastExecution = await runner.ExecuteAsync(definition, async (node, ct) =>
        {
            // Control flow belongs to Elsa. Data work is delegated to Cordis
            // capabilities: the delivery node performs the collection through
            // the one application-level coordinator.
            if (node.Type.Contains("sink", StringComparison.OrdinalIgnoreCase) ||
                node.Type.Contains("archive", StringComparison.OrdinalIgnoreCase))
            {
                await coordinator.RunAsync(
                    id, resourceId, planner.Plan(DateTimeOffset.UtcNow, TimeSpan.FromDays(1)),
                    pipeline, sinks, archive, ct);
            }
        });
    }

    private static IRecordReader ApplyTransforms(
        IRecordReader reader, WorkflowDefinition definition)
    {
        var mappings = definition.Nodes
            .Where(node => node.Type.Contains("transform.mapping", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Config).ToArray();
        var validations = definition.Nodes
            .Where(node => node.Type.Contains("transform.validate", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Config).ToArray();
        return mappings.Length == 0 && validations.Length == 0
            ? reader
            : new WorkflowTransformReader(reader, mappings, validations);
    }

    private static async ValueTask<IReadOnlyList<IRecordSink>> ResolveSinksAsync(
        WorkflowDefinition definition,
        IResourceRegistry resources,
        ICredentialVault vault,
        Broker<IRecordSinkFactory> factories,
        IRecordSink defaultSink)
    {
        var sinks = new List<IRecordSink>();
        foreach (var node in definition.Nodes.Where(node =>
                     node.Type.Contains("sink", StringComparison.OrdinalIgnoreCase)))
        {
            var targetId = node.Config.TryGetValue("targetResourceId", out var value)
                ? value?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                sinks.Add(defaultSink);
                continue;
            }

            var resource = await resources.GetAsync(targetId)
                           ?? throw new InvalidOperationException(
                               $"target resource '{targetId}' is not registered");
            var factory = factories.Providers.FirstOrDefault(candidate =>
                string.Equals(candidate.ResourceType, resource.Type, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"no sink factory is registered for resource type '{resource.Type}'");
            sinks.Add(await factory.CreateAsync(resource, vault));
        }
        return sinks;
    }
}

/// <summary>Activity contributed by an industrial plugin. Elsa owns execution;
/// Cordis owns plugin lifecycle. No second plugin manager is introduced.</summary>
public sealed class CollectFileActivity : CodeActivity
{
    public string FileId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string EdgeSummary { get; set; } = string.Empty;
    public static int ExecutionCount;

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        Interlocked.Increment(ref ExecutionCount);
        context.SetResult($"collected:{FileId}");
        return ValueTask.CompletedTask;
    }
}

/// <summary>Maps the single editable industrial Definition to a published Elsa
/// workflow. The G6 graph model is never used as the persistence format.</summary>
public sealed class ElsaDefinitionConverter
{
    public string ToElsaJson(WorkflowDefinition definition)
    {
        var sequence = new Sequence();
        foreach (var node in definition.Nodes.OrderBy(node => node.Type, StringComparer.Ordinal))
        {
            var edges = definition.Edges
                .Where(edge => edge.Source == node.Id || edge.Target == node.Id)
                .Select(edge => $"{edge.Source}:{edge.SourcePort}->{edge.Target}:{edge.TargetPort}");
            sequence.Activities.Add(new CollectFileActivity
            {
                FileId = node.Id,
                Id = node.Id,
                Name = $"{node.Type} [{string.Join("|", edges)}]",
                ActivityType = node.Type,
                EdgeSummary = string.Join("|", edges),
            });
        }

        var workflow = new Workflow(sequence)
        {
            Identity = new WorkflowIdentity(
                definition.WorkflowId,
                definition.Version,
                $"{definition.Name}|{string.Join(";", definition.Edges.Select(edge =>
                    $"{edge.Source}:{edge.SourcePort}->{edge.Target}:{edge.TargetPort}"))}",
                null),
        };
        var envelope = new JsonObject
        {
            ["elsa"] = System.Text.Json.JsonSerializer.SerializeToNode(workflow),
            ["definition"] = System.Text.Json.JsonSerializer.SerializeToNode(definition),
        };
        return envelope.ToJsonString();
    }
}

/// <summary>Small PoC verifier: prove a Cordis-contributed Activity can be
/// executed by Elsa with a real service provider before committing to Elsa.</summary>
/// <summary>Executes an industrial workflow graph through Elsa. Control flow
/// (node order, activation, completion) is Elsa's responsibility; each node's
/// data work is a delegate supplied by the Cordis plugin that owns the
/// capability. No Cordis Fiber state is modified here.</summary>
public sealed class ElsaWorkflowExecutor
{
    public sealed record Result(IReadOnlyList<string> Executed, string Status);

    public async ValueTask<Result> ExecuteAsync(
        WorkflowDefinition definition,
        Func<WorkflowNode, CancellationToken, ValueTask> runNode,
        CancellationToken ct = default)
    {
        var order = TopologicalOrder(definition);
        var executed = new List<string>();
        var services = new ServiceCollection();
        services.AddElsa();
        await using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var sequence = new Sequence();
        foreach (var node in order)
        {
            var current = node;
            sequence.Activities.Add(new DelegatingActivity(async context =>
            {
                await runNode(current, context.CancellationToken);
                executed.Add(current.Id);
            }));
        }

        var workflow = new Workflow(sequence)
        {
            Identity = new WorkflowIdentity(definition.WorkflowId, definition.Version, definition.Name, null),
        };
        await runner.RunAsync(workflow, cancellationToken: ct);
        return new Result(executed, "Completed");
    }

    private static IReadOnlyList<WorkflowNode> TopologicalOrder(WorkflowDefinition definition)
    {
        var byId = definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var indegree = definition.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in definition.Edges)
        {
            if (byId.ContainsKey(edge.Source) && byId.ContainsKey(edge.Target))
            {
                indegree[edge.Target]++;
            }
        }

        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var order = new List<WorkflowNode>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(byId[id]);
            foreach (var edge in definition.Edges.Where(edge => edge.Source == id))
            {
                if (!byId.ContainsKey(edge.Target)) continue;
                if (--indegree[edge.Target] == 0) queue.Enqueue(edge.Target);
            }
        }

        if (order.Count != definition.Nodes.Count)
        {
            throw new InvalidOperationException("workflow graph contains a cycle");
        }

        return order;
    }

    private sealed class DelegatingActivity : CodeActivity
    {
        private readonly Func<ActivityExecutionContext, ValueTask> _run;
        public DelegatingActivity(Func<ActivityExecutionContext, ValueTask> run) => _run = run;

        protected override ValueTask ExecuteAsync(ActivityExecutionContext context) => _run(context);
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
