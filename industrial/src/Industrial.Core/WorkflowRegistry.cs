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
        var errors = await _validator.ValidateAsync(definition, ct);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "workflow validation failed: " +
                string.Join("; ", errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")));
        }

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

        existing.Version = definition.Version;
        existing.DefinitionJson = JsonSerializer.Serialize(definition, Json);
        existing.IsPublished = false;
        existing.ElsaDefinitionJson = null;
        existing.PublishedAt = null;
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
        var rows = await db.Workflows.AsNoTracking().ToListAsync(ct);
        return rows.Select(row => JsonSerializer.Deserialize<WorkflowDefinition>(row.DefinitionJson, Json)!)
            .Where(definition => definition is not null)
            .GroupBy(definition => definition.WorkflowId)
            .Select(group => group.OrderByDescending(definition => definition.Version).First())
            .ToArray();
    }

    public async ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, string elsaDefinitionJson, CancellationToken ct = default)
    {
        var definition = await GetAsync(workflowId, ct)
                         ?? throw new InvalidOperationException($"workflow '{workflowId}' not found");
        var errors = await _validator.ValidateAsync(definition, ct);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("cannot publish an invalid workflow");
        }

        await using var db = _db();
        var previous = await db.Workflows
            .SingleOrDefaultAsync(x => x.WorkflowId == workflowId && x.IsPublished, ct);
        if (previous is not null)
        {
            // Keep the previous published row as history; only its active flag
            // moves, so the unique published index still admits exactly one row.
            previous.IsPublished = false;
        }

        var publishedVersion = previous is null ? definition.Version : previous.Version + 1;
        var published = new WorkflowEntity
        {
            WorkflowId = workflowId,
            Version = publishedVersion,
            Kind = "Published",
            DefinitionJson = JsonSerializer.Serialize(definition, Json),
            ElsaDefinitionJson = elsaDefinitionJson,
            IsPublished = true,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        db.Workflows.Add(published);
        await db.SaveChangesAsync(ct);
        return new PublishedWorkflow(
            workflowId, publishedVersion, definition, elsaDefinitionJson, published.PublishedAt!.Value);
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
