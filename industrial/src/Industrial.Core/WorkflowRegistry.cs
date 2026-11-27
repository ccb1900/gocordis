using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Industrial workflow definition is the only editable source; an
/// Elsa definition is a published derivative and is never edited directly.</summary>
public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<IndustrialDbContext> _db;
    private readonly IWorkflowValidator _validator;

    public WorkflowRegistry(Func<IndustrialDbContext> db, IWorkflowValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    public async ValueTask<WorkflowDefinition> SaveDraftAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A draft is a work in progress: only structural constraints that make
        // it persistable are enforced. Full capability validation belongs to
        // an explicit validate/publish step, not to saving user progress.
        if (string.IsNullOrWhiteSpace(definition.WorkflowId))
            throw new ArgumentException("workflowId is required", nameof(definition));
        var nodeIds = definition.Nodes.Select(node => node.Id).ToArray();
        if (nodeIds.Length != nodeIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("workflow validation failed: duplicate node id");
        if (definition.Edges.Any(edge =>
                !nodeIds.Contains(edge.Source, StringComparer.Ordinal) ||
                !nodeIds.Contains(edge.Target, StringComparer.Ordinal)))
            throw new InvalidOperationException("workflow validation failed: edge references an unknown node");

        await using var db = _db();
        var existing = await db.Workflows
            .SingleOrDefaultAsync(x => x.WorkflowId == definition.WorkflowId && x.Kind == "Draft", ct);
        if (existing is null)
        {
            existing = new WorkflowEntity
            {
                WorkflowId = definition.WorkflowId,
                Version = definition.Version,
                Kind = "Draft",
            };
            db.Workflows.Add(existing);
        }

        if (existing.Version != definition.Version)
        {
            // Version is part of the key: replace the draft row instead of
            // mutating a key value, which EF correctly rejects.
            db.Workflows.Remove(existing);
            db.Workflows.Add(new WorkflowEntity
            {
                WorkflowId = definition.WorkflowId,
                Version = definition.Version,
                Kind = "Draft",
                DefinitionJson = JsonSerializer.Serialize(definition, Json),
                IsPublished = false,
            });
        }
        else
        {
            existing.DefinitionJson = JsonSerializer.Serialize(definition, Json);
            existing.IsPublished = false;
            existing.ElsaDefinitionJson = null;
            existing.PublishedAt = null;
        }
        await db.SaveChangesAsync(ct);
        return definition;
    }

    public async ValueTask<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken ct = default)
    {
        await using var db = _db();
        var current = await db.Workflows.AsNoTracking()
            .Where(x => x.WorkflowId == workflowId && x.Kind == "Draft")
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        return current is null ? null : JsonSerializer.Deserialize<WorkflowDefinition>(current.DefinitionJson, Json);
    }

    public async ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Workflows.AsNoTracking()
            .Where(row => row.Kind == "Draft")
            .ToListAsync(ct);
        return rows.Select(row => JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!)
            .Where(definition => definition is not null)
            .GroupBy(definition => definition.WorkflowId)
            .Select(group => group.OrderByDescending(definition => definition.Version).First())
            .ToArray();
    }

    public async ValueTask<int> NextPublishVersionAsync(
        string workflowId, CancellationToken ct = default)
    {
        await using var db = _db();
        var latest = await db.Workflows.AsNoTracking()
            .Where(row => row.WorkflowId == workflowId && row.Kind == "Published")
            .OrderByDescending(row => row.Version)
            .Select(row => row.Version)
            .FirstOrDefaultAsync(ct);
        return latest == 0 ? 1 : latest + 1;
    }

    public async ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, string elsaDefinitionJson, CancellationToken ct = default)
    {
        var definition = await GetAsync(workflowId, ct)
                         ?? throw new InvalidOperationException($"workflow '{workflowId}' not found");
        return await PublishAsync(workflowId, definition, elsaDefinitionJson, ct);
    }

    public async ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, int version, string elsaDefinitionJson, CancellationToken ct = default)
    {
        var definition = await GetAsync(workflowId, ct)
                         ?? throw new InvalidOperationException($"workflow '{workflowId}' not found");
        var snapshot = definition with { Version = version > 0 ? version : definition.Version };
        return await PublishAsync(workflowId, snapshot, elsaDefinitionJson, ct);
    }

    public async ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, WorkflowDefinition snapshot, string elsaDefinitionJson,
        CancellationToken ct = default)
    {
        var errors = await _validator.ValidateAsync(snapshot, ct);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("cannot publish an invalid workflow: " +
                string.Join("; ", errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")));
        }

        await using var db = _db();
        var previous = await db.Workflows
            .SingleOrDefaultAsync(x => x.WorkflowId == workflowId && x.IsPublished, ct);
        if (previous is not null) previous.IsPublished = false;
        var nextVersion = await NextPublishVersionAsync(workflowId, ct);
        var immutable = snapshot with { WorkflowId = workflowId, Version = nextVersion };
        var published = new WorkflowEntity
        {
            WorkflowId = workflowId,
            Version = nextVersion,
            Kind = "Published",
            DefinitionJson = JsonSerializer.Serialize(immutable, Json),
            ElsaDefinitionJson = elsaDefinitionJson,
            IsPublished = true,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        db.Workflows.Add(published);
        await db.SaveChangesAsync(ct);
        return new PublishedWorkflow(
            workflowId, nextVersion, immutable, elsaDefinitionJson, published.PublishedAt!.Value);
    }

    public async ValueTask<PublishedWorkflow?> GetPublishedVersionAsync(
        string workflowId, int version, CancellationToken ct = default)
    {
        await using var db = _db();
        var row = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkflowId == workflowId && x.Version == version &&
                 x.Kind == "Published" && x.ElsaDefinitionJson != null, ct);
        return row is null
            ? null
            : new PublishedWorkflow(
                workflowId, row.Version,
                JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!,
                row.ElsaDefinitionJson!,
                row.PublishedAt ?? DateTimeOffset.UnixEpoch);
    }

    public async ValueTask<IReadOnlyList<PublishedWorkflow>> ListPublishedAsync(
        string workflowId, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Workflows.AsNoTracking()
            .Where(row => row.WorkflowId == workflowId && row.ElsaDefinitionJson != null)
            .OrderByDescending(row => row.Version)
            .ToListAsync(ct);
        return rows.Select(row => new PublishedWorkflow(
            row.WorkflowId, row.Version,
            JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!,
            row.ElsaDefinitionJson!,
            row.PublishedAt ?? DateTimeOffset.UnixEpoch)).ToArray();
    }

    public async ValueTask<IReadOnlyList<PublishedWorkflow>> ListAllPublishedAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Workflows.AsNoTracking()
            .Where(row => row.ElsaDefinitionJson != null)
            .OrderByDescending(row => row.Version)
            .ToListAsync(ct);
        return rows.Select(row => new PublishedWorkflow(
            row.WorkflowId, row.Version,
            JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!,
            row.ElsaDefinitionJson!,
            row.PublishedAt ?? DateTimeOffset.UnixEpoch)).ToArray();
    }

    public async ValueTask<PublishedWorkflow?> GetPublishedAsync(string workflowId, CancellationToken ct = default)
    {
        await using var db = _db();
        var row = await db.Workflows.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkflowId == workflowId && x.IsPublished, ct);
        return row?.ElsaDefinitionJson is null
            ? null
            : new PublishedWorkflow(
                workflowId, row.Version,
                JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!,
                row.ElsaDefinitionJson,
                row.PublishedAt ?? DateTimeOffset.UnixEpoch);
    }
}
