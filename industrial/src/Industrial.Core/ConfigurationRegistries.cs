using System.Globalization;
using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>File format profiles are reusable across machines. Secrets are
/// never stored here: mapping/validation only describes data shape.</summary>
public sealed class FileProfileRegistry(Func<IndustrialDbContext> db) : IFileProfileRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<FileProfileDefinition> RegisterAsync(
        FileProfileDefinition profile, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        await using var context = db();
        var existing = await GetAsync(profile.Id, ct);
        var next = profile with { Version = (existing?.Version ?? 0) + 1, UpdatedAt = DateTimeOffset.UtcNow };
        var created = next.CreatedAt == default ? DateTimeOffset.UtcNow : next.CreatedAt;
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT OR REPLACE INTO FileProfiles
              (Id, Name, Version, FileNamePattern, Format, Delimiter, HasHeader, Encoding,
               MappingJson, RequiredJson, Enabled, CreatedAt, UpdatedAt)
            VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12});
            """,
            [next.Id, next.Name, next.Version, next.FileNamePattern, next.Format, next.Delimiter,
             next.HasHeader ? 1 : 0, next.Encoding, JsonSerializer.Serialize(next.Mapping, Json),
             JsonSerializer.Serialize(next.RequiredFields, Json), next.Enabled ? 1 : 0,
             created.ToString("O"), next.UpdatedAt.ToString("O")], ct);
        return next with { CreatedAt = created };
    }

    public async ValueTask<FileProfileDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var context = db();
        var rows = await context.Database.SqlQueryRaw<FileProfileRow>(
            "SELECT * FROM FileProfiles WHERE Id = {0};", id).ToListAsync(ct);
        return rows.Count == 0 ? null : ToDefinition(rows[0]);
    }

    public async ValueTask<IReadOnlyList<FileProfileDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var context = db();
        var rows = await context.Database.SqlQueryRaw<FileProfileRow>(
            "SELECT * FROM FileProfiles ORDER BY Id;").ToListAsync(ct);
        return rows.Select(ToDefinition).ToArray();
    }

    public async ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await using var context = db();
        var references = await context.Database.SqlQueryRaw<CountRow>(
            "SELECT COUNT(*) AS Value FROM CollectionBindings WHERE FileProfileId = {0};", id)
            .ToListAsync(ct);
        if (references.FirstOrDefault()?.Value > 0)
            throw new InvalidOperationException($"file profile '{id}' is referenced by collection bindings");
        var rows = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM FileProfiles WHERE Id = {0};", [id], ct);
        return rows > 0;
    }

    private static FileProfileDefinition ToDefinition(FileProfileRow row) => new(
        row.Id, row.Name, row.Version, row.FileNamePattern, row.Format, row.Delimiter,
        row.HasHeader != 0, row.Encoding,
        JsonSerializer.Deserialize<Dictionary<string, object?>>(row.MappingJson, Json) ?? [],
        JsonSerializer.Deserialize<List<string>>(row.RequiredJson, Json) ?? [],
        row.Enabled != 0,
        DateTimeOffset.Parse(row.CreatedAt, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(row.UpdatedAt, CultureInfo.InvariantCulture));

    private sealed class CountRow { public int Value { get; set; } }

    private sealed class FileProfileRow
    {
        public string Id { get; set; } = ""; public string Name { get; set; } = "";
        public int Version { get; set; } public string FileNamePattern { get; set; } = "";
        public string Format { get; set; } = ""; public string Delimiter { get; set; } = "";
        public int HasHeader { get; set; } public string Encoding { get; set; } = "";
        public string MappingJson { get; set; } = "{}"; public string RequiredJson { get; set; } = "[]";
        public int Enabled { get; set; } public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }
}

/// <summary>Bindings are the 80x10 configuration unit: one row per
/// machine/file/template combination, never a copied workflow definition.</summary>
public sealed class CollectionBindingRegistry(Func<IndustrialDbContext> db) : ICollectionBindingRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<CollectionBindingDefinition> RegisterAsync(
        CollectionBindingDefinition binding, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Id);
        await using var context = db();
        var existing = await GetAsync(binding.Id, ct);
        var next = binding with { CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow };
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT OR REPLACE INTO CollectionBindings
              (Id, Name, MachineId, WorkflowId, WorkflowVersion, FileProfileId,
               SourceResourceId, TargetJson, TriggerJson, Enabled, CreatedAt, UpdatedAt)
            VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11});
            """,
            [next.Id, next.Name, next.MachineId, next.WorkflowId, next.WorkflowVersion,
             next.FileProfileId, next.SourceResourceId,
             JsonSerializer.Serialize(next.TargetResourceIds, Json),
             JsonSerializer.Serialize(next.Trigger, Json), next.Enabled ? 1 : 0,
             next.CreatedAt.ToString("O"), DateTimeOffset.UtcNow.ToString("O")], ct);
        return next;
    }

    public async ValueTask<CollectionBindingDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var context = db();
        var rows = await context.Database.SqlQueryRaw<BindingRow>(
            "SELECT * FROM CollectionBindings WHERE Id = {0};", id).ToListAsync(ct);
        return rows.Count == 0 ? null : ToDefinition(rows[0]);
    }

    public async ValueTask<IReadOnlyList<CollectionBindingDefinition>> ListAsync(CancellationToken ct = default)
    {
        await using var context = db();
        var rows = await context.Database.SqlQueryRaw<BindingRow>(
            "SELECT * FROM CollectionBindings ORDER BY Id;").ToListAsync(ct);
        return rows.Select(ToDefinition).ToArray();
    }

    public async ValueTask<BindingPreview> PreviewBulkAsync(
        IReadOnlyList<string> machineIds, IReadOnlyList<string> fileProfileIds,
        string workflowId, int workflowVersion, string sourceResourcePattern,
        IReadOnlyList<string> targetResourceIds, CancellationToken ct = default)
    {
        await using var context = db();
        var errors = new List<string>();
        var existingMachines = (await context.Machines.AsNoTracking()
            .Where(m => machineIds.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)).ToHashSet();
        var existingProfiles = (await context.Database.SqlQueryRaw<IdRow>(
            "SELECT Id FROM FileProfiles;").ToListAsync(ct)).Select(r => r.Id).ToHashSet();
        var existingResources = (await context.Resources.AsNoTracking()
            .Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        var published = await context.Workflows.AsNoTracking().AnyAsync(
            w => w.WorkflowId == workflowId && w.Version == workflowVersion &&
                 w.Kind == "Published", ct);
        if (!published) errors.Add($"workflow {workflowId}:v{workflowVersion} is not published");
        foreach (var machine in machineIds.Where(id => !existingMachines.Contains(id)))
            errors.Add($"machine {machine} does not exist");
        foreach (var profile in fileProfileIds.Where(id => !existingProfiles.Contains(id)))
            errors.Add($"file profile {profile} does not exist");
        var missing = targetResourceIds.Where(id => !existingResources.Contains(id)).ToList();
        var conflicts = new List<string>();
        foreach (var machine in machineIds)
        {
            var source = sourceResourcePattern.Replace("{machine}", machine, StringComparison.Ordinal);
            if (!existingResources.Contains(source)) missing.Add(source);
        }
        var existing = (await context.Database.SqlQueryRaw<BindingRow>(
            "SELECT * FROM CollectionBindings;").ToListAsync(ct)).Select(ToDefinition).ToArray();
        foreach (var machine in machineIds)
        foreach (var profile in fileProfileIds)
        {
            var id = BindingId(workflowId, workflowVersion, machine, profile);
            if (existing.Any(b => b.Id == id)) conflicts.Add(id);
        }
        var total = machineIds.Count * fileProfileIds.Count;
        return new BindingPreview(total, total - conflicts.Count - errors.Count,
            conflicts, missing, errors);
    }

    public async ValueTask<BindingPreview> CreateBulkAsync(
        IReadOnlyList<string> machineIds, IReadOnlyList<string> fileProfileIds,
        string workflowId, int workflowVersion, string sourceResourcePattern,
        IReadOnlyList<string> targetResourceIds, string triggerKind = "manual",
        string? cron = null, CancellationToken ct = default)
    {
        var preview = await PreviewBulkAsync(machineIds, fileProfileIds, workflowId, workflowVersion,
            sourceResourcePattern, targetResourceIds, ct);
        if (preview.Errors.Count > 0 || preview.MissingResources.Count > 0) return preview;
        var created = 0;
        foreach (var machine in machineIds)
        foreach (var profile in fileProfileIds)
        {
            var id = BindingId(workflowId, workflowVersion, machine, profile);
            if (preview.Conflicts.Contains(id)) continue;
            await RegisterAsync(new CollectionBindingDefinition(
                id, $"{machine} / {profile}", machine, workflowId, workflowVersion, profile,
                sourceResourcePattern.Replace("{machine}", machine, StringComparison.Ordinal),
                targetResourceIds,
                new TriggerDefinition(triggerKind, cron, null, new Dictionary<string, object?>()),
                true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ct);
            created++;
        }
        return preview with { Created = created };
    }

    public async ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await using var context = db();
        return await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM CollectionBindings WHERE Id = {0};", [id], ct) > 0;
    }

    private static string BindingId(string workflowId, int version, string machine, string profile) =>
        $"binding:{workflowId}:v{version}:{machine}:{profile}";

    private static CollectionBindingDefinition ToDefinition(BindingRow row) => new(
        row.Id, row.Name, row.MachineId, row.WorkflowId, row.WorkflowVersion, row.FileProfileId,
        row.SourceResourceId,
        JsonSerializer.Deserialize<List<string>>(row.TargetJson, Json) ?? [],
        JsonSerializer.Deserialize<TriggerDefinition>(row.TriggerJson, Json)
        ?? new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
        row.Enabled != 0,
        DateTimeOffset.Parse(row.CreatedAt, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(row.UpdatedAt, CultureInfo.InvariantCulture));

    private sealed class BindingRow
    {
        public string Id { get; set; } = ""; public string Name { get; set; } = "";
        public string MachineId { get; set; } = ""; public string WorkflowId { get; set; } = "";
        public int WorkflowVersion { get; set; } public string FileProfileId { get; set; } = "";
        public string SourceResourceId { get; set; } = ""; public string TargetJson { get; set; } = "[]";
        public string TriggerJson { get; set; } = "{}"; public int Enabled { get; set; }
        public string CreatedAt { get; set; } = ""; public string UpdatedAt { get; set; } = "";
    }

    private sealed class IdRow { public string Id { get; set; } = ""; }
    private sealed class CountRow { public int Value { get; set; } }
}
