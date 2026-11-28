using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static string DefinitionId(PublishedWorkflow published) =>
        $"{published.WorkflowId}:v{published.Version}";

    /// <summary>Hash of the exact definition snapshot that produced the run.
    /// A bound definition is a different artifact from its published template;
    /// recovery and retry must never silently substitute one for the other.
    /// </summary>
    public static string DefinitionHash(string definitionJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitionJson)));

    public sealed record ExecutionResult(
        string InstanceId,
        string DefinitionId,
        string Status,
        IReadOnlyList<string> Executed,
        string? Error,
        string? DefinitionJson = null,
        string? DefinitionHash = null);

    public sealed record RecoverableInstance(
        string InstanceId, string WorkflowId, int Version, string ResourceId,
        string? DefinitionJson = null, string? DefinitionHash = null);

    public static ValueTask<IReadOnlyList<RecoverableInstance>> ListRecoverableAsync(
        string databasePath, CancellationToken ct = default) =>
        ListInstancesAsync(databasePath, WorkflowStatus.Running, ct);

    public static async ValueTask<IReadOnlyList<RecoverableInstance>> ListInstancesAsync(
        string databasePath, WorkflowStatus? status = null, CancellationToken ct = default)
    {
        await using var provider = ElsaRuntime.CreateProvider(databasePath: databasePath);
        var instances = provider.GetRequiredService<IWorkflowInstanceStore>();
        var rows = await instances.FindManyAsync(new WorkflowInstanceFilter
        {
            WorkflowStatus = status,
            // A hard process exit leaves IsExecuting=true because no graceful
            // shutdown flips it. At startup no local activity owns the instance,
            // and token-fenced ledger claims still prevent double delivery.
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
            state.Properties.TryGetValue("industrial.definitionJson", out var definitionJson);
            state.Properties.TryGetValue("industrial.definitionHash", out var definitionHash);
            result.Add(new RecoverableInstance(
                instanceId?.ToString() ?? state.Id,
                workflowId?.ToString() ?? string.Empty,
                Convert.ToInt32(version),
                resourceId?.ToString() ?? string.Empty,
                definitionJson?.ToString(),
                definitionHash?.ToString()));
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
        string? definitionJson = null,
        string? definitionHash = null,
        CancellationToken ct = default)
    {
        if (definitionJson is not null && definitionHash is not null &&
            !string.Equals(definitionHash, DefinitionHash(definitionJson), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("definition snapshot hash does not match its JSON");
        }

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
        string effectiveJson = definitionJson ?? string.Empty;
        string effectiveHash = definitionHash ?? string.Empty;
        if (resume)
        {
            foreach (var property in existing!.WorkflowState.Properties
                         .Where(property => property.Key.StartsWith("industrial.", StringComparison.Ordinal)))
            {
                state.Properties[property.Key] = property.Value;
            }
            // Recovery/retry replay the original artifact: an unsupplied
            // definition must keep the instance's own snapshot, not the
            // currently published template.
            if (state.Properties.TryGetValue("industrial.definitionJson", out var storedJson) &&
                storedJson is not null)
            {
                var storedText = storedJson.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(effectiveJson) &&
                    !string.Equals(storedText, effectiveJson, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"instance '{instanceId}' was created with a different definition snapshot; " +
                        "refusing to resume with another definition");
                }
                effectiveJson = string.IsNullOrEmpty(effectiveJson) ? storedText : effectiveJson;
            }
            if (string.IsNullOrEmpty(effectiveHash) &&
                state.Properties.TryGetValue("industrial.definitionHash", out var storedHash))
            {
                effectiveHash = storedHash?.ToString() ?? string.Empty;
            }
        }
        if (string.IsNullOrEmpty(effectiveJson))
            effectiveJson = JsonSerializer.Serialize(published.Definition, WebJson);
        if (string.IsNullOrEmpty(effectiveHash) ||
            !string.Equals(effectiveHash, DefinitionHash(effectiveJson), StringComparison.Ordinal))
        {
            effectiveHash = DefinitionHash(effectiveJson);
        }
        state.Properties["industrial.instanceId"] = instanceId;
        state.Properties["industrial.workflowId"] = published.WorkflowId;
        state.Properties["industrial.version"] = published.Version;
        state.Properties["industrial.resourceId"] = resourceId;
        state.Properties["industrial.definitionJson"] = effectiveJson;
        state.Properties["industrial.definitionHash"] = effectiveHash;

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
            // A hard-exit marker must persist even if the caller is being
            // cancelled; otherwise the instance would look silently finished.
            await instances.SaveAsync(result.WorkflowState, CancellationToken.None);
        }
        var incidents = result.WorkflowState.Incidents;
        return new ExecutionResult(
            result.WorkflowState.Id,
            definitionId,
            result.WorkflowState.Status.ToString(),
            executor.Executed,
            incidents.Count == 0 ? null : string.Join("; ", incidents.Select(incident => incident.Message)),
            effectiveJson,
            effectiveHash);
    }

    public static async ValueTask<ExecutionResult?> InspectAsync(
        string instanceId, string databasePath, CancellationToken ct = default)
    {
        await using var provider = ElsaRuntime.CreateProvider(databasePath: databasePath);
        var instances = provider.GetRequiredService<IWorkflowInstanceManager>();
        var instance = await instances.FindByIdAsync(instanceId, ct);
        var state = instance?.WorkflowState;
        if (state is null) return null;
        state.Properties.TryGetValue("industrial.definitionJson", out var definitionJson);
        state.Properties.TryGetValue("industrial.definitionHash", out var definitionHash);
        return new ExecutionResult(
            state.Id, state.DefinitionId, state.Status.ToString(), [], null,
            definitionJson?.ToString(), definitionHash?.ToString());
    }
}
