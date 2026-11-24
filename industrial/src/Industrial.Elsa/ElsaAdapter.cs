using Elsa.Extensions;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Industrial.Contracts;
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
    public IReadOnlyList<string> List() => _types.OrderBy(type => type, StringComparer.Ordinal).ToArray();
    internal void Clear() => _types.Clear();
}

public sealed class IndustrialElsaComponent : Component
{
    private readonly ElsaActivityCatalog _catalog = new();

    public override string Name => "industrial-elsa";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowValidator,
         IndustrialKeys.WorkflowRegistry, ApiGateway.Key];
    public override IReadOnlyList<ICapabilityKey> Provide() => [IndustrialKeys.ElsaActivityCatalog];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var nodes = context.Require(IndustrialKeys.NodeCatalog);
        var validator = context.Require(IndustrialKeys.WorkflowValidator);
        var workflows = context.Require(IndustrialKeys.WorkflowRegistry);
        var api = context.Require(ApiGateway.Key);
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
                return ValueTask.CompletedTask;
            },
            () => { api.UnregisterAll(owner); return ValueTask.CompletedTask; });
        return null;
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
