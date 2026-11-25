using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Resource definitions are business data owned by the industrial
/// application; provider plugins can be disabled without losing them.</summary>
public sealed class ResourceRegistry : IResourceRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<IndustrialDbContext> _db;
    private IWorkflowRegistry? _workflows;

    public ResourceRegistry(Func<IndustrialDbContext> db) => _db = db;

    public void BindWorkflows(IWorkflowRegistry workflows) => _workflows = workflows;
    public void UnbindWorkflows() => _workflows = null;

    public async ValueTask<ResourceDefinition> RegisterAsync(
        ResourceDefinition resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(resource.Id) || string.IsNullOrWhiteSpace(resource.Type) ||
            string.IsNullOrWhiteSpace(resource.Name))
        {
            throw new ArgumentException("resource id, type and name are required", nameof(resource));
        }

        await using var db = _db();
        var existing = await db.Resources.SingleOrDefaultAsync(x => x.Id == resource.Id, ct);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new ResourceEntity
            {
                Id = resource.Id,
                CreatedAt = now,
            };
            db.Resources.Add(existing);
        }
        else if (existing.Version != resource.Version)
        {
            throw new InvalidOperationException(
                $"resource '{resource.Id}' was changed concurrently (expected {resource.Version}, found {existing.Version})");
        }

        existing.Type = resource.Type;
        existing.Name = resource.Name;
        existing.Provider = resource.Provider;
        existing.ConfigurationJson = JsonSerializer.Serialize(resource.Configuration, Json);
        existing.CredentialId = resource.CredentialId;
        existing.Version = resource.Version + 1;
        existing.Enabled = resource.Enabled;
        existing.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return existing.ToContract();
    }

    public async ValueTask<ResourceDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity?.ToContract();
    }

    public async ValueTask<IReadOnlyList<ResourceDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Resources.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        return rows.Select(x => x.ToContract()).ToArray();
    }

    private static bool IsReference(
        IReadOnlyDictionary<string, object?> config, string key, string id) =>
        config.TryGetValue(key, out var value) &&
        string.Equals(value?.ToString(), id, StringComparison.Ordinal);

    public async ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.Resources.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return false;
        }

        // Reference integrity is a persisted-data invariant. It must not depend
        // on the workflow plugin being Active, and it must cover every stored
        // version (including published history), not only the current draft.
        var workflowRows = await db.Workflows.AsNoTracking().ToListAsync(ct);
        foreach (var row in workflowRows)
        {
            if (string.IsNullOrWhiteSpace(row.DefinitionJson)) continue;
            var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json);
            if (workflow is null) continue;
            var referenced = workflow.ResourceReferences.Contains(id, StringComparer.Ordinal) ||
                             workflow.Nodes.Any(node =>
                                 IsReference(node.Config, "resourceId", id) ||
                                 IsReference(node.Config, "targetResourceId", id));
            if (referenced)
            {
                throw new InvalidOperationException(
                    $"resource '{id}' is referenced by workflow '{workflow.WorkflowId}' v{workflow.Version} " +
                    "and cannot be deleted");
            }
        }

        db.Resources.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal static class ResourceMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ResourceDefinition ToContract(this ResourceEntity entity) => new(
        entity.Id,
        entity.Type,
        entity.Name,
        entity.Provider,
        JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.ConfigurationJson, Json) ?? [],
        entity.CredentialId,
        entity.Version,
        entity.Enabled,
        entity.CreatedAt,
        entity.UpdatedAt);
}
