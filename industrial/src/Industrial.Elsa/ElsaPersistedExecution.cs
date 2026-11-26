using System.Text.Json.Nodes;
using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.State;
using Industrial.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Industrial.Elsa;

/// <summary>Runs the pinned published artifact through Elsa's runner with an
/// Elsa-managed, persisted workflow instance. The industrial registry stays
/// the editable source of truth; the workflow state is derived execution data.</summary>
public static class ElsaPersistedExecution
{
    public static string DefinitionId(PublishedWorkflow published) =>
        $"{published.WorkflowId}:v{published.Version}";

    public sealed record ExecutionResult(
        string InstanceId,
        string DefinitionId,
        string Status,
        IReadOnlyList<string> Executed,
        string? Error);

    public sealed record RecoverableInstance(
        string InstanceId, string WorkflowId, int Version, string ResourceId);

    public static async ValueTask<IReadOnlyList<RecoverableInstance>> ListRecoverableAsync(
        string databasePath, CancellationToken ct = default)
    {
        await using var provider = ElsaRuntime.CreateProvider(databasePath: databasePath);
        var instances = provider.GetRequiredService<IWorkflowInstanceStore>();
        var rows = await instances.FindManyAsync(new WorkflowInstanceFilter
        {
            WorkflowStatus = WorkflowStatus.Running,
            IsExecuting = false,
        }, ct);
        var result = new List<RecoverableInstance>();
        foreach (var row in rows)
        {
            var state = row.WorkflowState;
            if (state is null) continue;
            if (!state.Properties.TryGetValue("industrial.instanceId", out var instanceId)) continue;
            if (!state.Properties.TryGetValue("industrial.workflowId", out var workflowId)) continue;
            if (!state.Properties.TryGetValue("industrial.version", out var version)) continue;
            if (!state.Properties.TryGetValue("industrial.resourceId", out var resourceId)) continue;
            result.Add(new RecoverableInstance(
                instanceId?.ToString() ?? state.Id,
                workflowId?.ToString() ?? string.Empty,
                Convert.ToInt32(version),
                resourceId?.ToString() ?? string.Empty));
        }
        return result;
    }

    public static async ValueTask<ExecutionResult> RunAsync(
        PublishedWorkflow published,
        IIndustrialNodeExecutor executor,
        string resourceId,
        string databasePath,
        string? instanceId = null,
        bool resume = false,
        CancellationToken ct = default)
    {
        instanceId ??= $"industrial-{Guid.NewGuid():N}";
        var definitionId = DefinitionId(published);
        var envelope = JsonNode.Parse(published.ElsaDefinitionJson) as JsonObject
                       ?? throw new InvalidOperationException("published Elsa definition is not an object");
        var graph = envelope["elsa"] as JsonObject
                    ?? throw new InvalidOperationException("published Elsa definition has no graph");
        var workflow = ElsaDefinitionConverter.BuildWorkflow(graph, definitionId);

        await using var provider = ElsaRuntime.CreateProvider(executor, databasePath);
        var graphBuilder = provider.GetRequiredService<IWorkflowGraphBuilder>();
        var runner = provider.GetRequiredService<IWorkflowRunner>();
        var instances = provider.GetRequiredService<IWorkflowInstanceManager>();
        var workflowGraph = await graphBuilder.BuildAsync(workflow, ct);

        var existing = await instances.FindByIdAsync(instanceId, ct);
        if (resume && existing?.WorkflowState is null)
        {
            throw new InvalidOperationException($"Elsa instance '{instanceId}' was not found");
        }
        if (resume && !string.Equals(existing!.WorkflowState.DefinitionId, definitionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"instance '{instanceId}' belongs to definition '{existing.WorkflowState.DefinitionId}', " +
                $"not '{definitionId}'");
        }

        // Recovery reuses the instance identity but replays the graph from its
        // root. Already committed industrial stages are skipped by fingerprint,
        // so replay is idempotent; Elsa's old activity contexts must not survive
        // because the process that created them is gone.
        var state = new WorkflowState
        {
            Id = instanceId,
            DefinitionId = definitionId,
            DefinitionVersionId = definitionId,
            DefinitionVersion = published.Version,
            Name = $"{published.WorkflowId}:{instanceId}",
        };
        if (resume)
        {
            foreach (var property in existing!.WorkflowState.Properties
                         .Where(property => property.Key.StartsWith("industrial.", StringComparison.Ordinal)))
            {
                state.Properties[property.Key] = property.Value;
            }
        }
        state.Properties["industrial.instanceId"] = instanceId;
        state.Properties["industrial.workflowId"] = published.WorkflowId;
        state.Properties["industrial.version"] = published.Version;
        state.Properties["industrial.resourceId"] = resourceId;

        var options = new RunWorkflowOptions
        {
            WorkflowInstanceId = instanceId,
            RequireNewInstance = !resume,
            Input = new Dictionary<string, object> { ["resourceId"] = resourceId },
            Properties = new Dictionary<string, object>(state.Properties),
        };
        // The runner owns instance creation and persistence; explicitly saving
        // here would race with its own commit and violate the one-lifecycle rule.
        var result = await runner.RunAsync(workflowGraph, state, options, ct);
        if (executor.FatalExit)
        {
            // A hard process exit must not be recorded as a finished workflow.
            // Keep Elsa's instance resumable; the industrial ledger still owns
            // which deliveries were actually completed.
            result.WorkflowState.Status = WorkflowStatus.Running;
            result.WorkflowState.IsExecuting = false;
            await instances.SaveAsync(result.WorkflowState, ct);
        }
        var incidents = result.WorkflowState.Incidents;
        return new ExecutionResult(
            result.WorkflowState.Id,
            definitionId,
            result.WorkflowState.Status.ToString(),
            executor.Executed,
            incidents.Count == 0 ? null : string.Join("; ", incidents.Select(incident => incident.Message)));
    }

    public static async ValueTask<ExecutionResult?> InspectAsync(
        string instanceId, string databasePath, CancellationToken ct = default)
    {
        await using var provider = ElsaRuntime.CreateProvider(databasePath: databasePath);
        var instances = provider.GetRequiredService<IWorkflowInstanceManager>();
        var instance = await instances.FindByIdAsync(instanceId, ct);
        var state = instance?.WorkflowState;
        return state is null
            ? null
            : new ExecutionResult(state.Id, state.DefinitionId, state.Status.ToString(), [], null);
    }
}
