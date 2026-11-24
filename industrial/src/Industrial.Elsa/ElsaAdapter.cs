using Elsa.Extensions;
using Gocordis.Core;
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
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowValidator];
    public override IReadOnlyList<ICapabilityKey> Provide() => [IndustrialKeys.ElsaActivityCatalog];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var nodes = context.Require(IndustrialKeys.NodeCatalog);
        var validator = context.Require(IndustrialKeys.WorkflowValidator);
        foreach (var descriptor in await nodes.ListAsync())
        {
            _catalog.Register(descriptor.ActivityType);
        }

        context.Provide(IndustrialKeys.ElsaActivityCatalog, _catalog);
        validator.UseActivityCatalog(_catalog);
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () => { validator.UseActivityCatalog(new ElsaActivityCatalog()); return ValueTask.CompletedTask; });
        return null;
    }
}

/// <summary>Activity contributed by an industrial plugin. Elsa owns execution;
/// Cordis owns plugin lifecycle. No second plugin manager is introduced.</summary>
public sealed class CollectFileActivity : CodeActivity
{
    public string FileId { get; set; } = string.Empty;
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
            sequence.Activities.Add(new CollectFileActivity { FileId = node.Id, Id = node.Id });
        }

        var workflow = new Workflow(sequence)
        {
            Identity = new WorkflowIdentity(definition.WorkflowId, definition.Version, definition.Name, null),
        };
        return System.Text.Json.JsonSerializer.Serialize(workflow);
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
