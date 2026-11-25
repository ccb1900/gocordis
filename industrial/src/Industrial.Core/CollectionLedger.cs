using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>File-level and target-level ledger. One file may have several
/// deliveries; each target records success/failure independently. Status
/// transitions are owned here, never by callers mutating entities.</summary>
public sealed class CollectionLedger : ICollectionLedger
{
    private readonly Func<IndustrialDbContext> _db;

    public CollectionLedger(Func<IndustrialDbContext> db) => _db = db;

    public async ValueTask<CollectionRun> StartAsync(string workflowId, CancellationToken ct = default)
    {
        await using var db = _db();
        var run = new CollectionRunEntity
        {
            Id = $"run-{Guid.NewGuid():N}",
            WorkflowId = workflowId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "Running",
        };
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return ToContract(run);
    }

    public async ValueTask<CollectionRun> CompleteRunAsync(
        string runId, string status, string? error, CancellationToken ct = default)
    {
        if (status is not ("Succeeded" or "PartialFailure" or "Failed"))
        {
            throw new ArgumentException($"invalid terminal run status '{status}'", nameof(status));
        }

        await using var db = _db();
        var run = await db.CollectionRuns.SingleAsync(x => x.Id == runId, ct);
        if (run.Status == "Running")
        {
            run.Status = status;
            run.Error = error;
            run.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return ToContract(run);
    }

    public async ValueTask<CollectionRun?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        return entity is null ? null : ToContract(entity);
    }

    public async ValueTask<IReadOnlyList<CollectionRun>> ListRunsAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.CollectionRuns.AsNoTracking().ToListAsync(ct);
        // SQLite cannot translate DateTimeOffset in ORDER BY; sort in memory.
        return rows.Select(ToContract).OrderByDescending(run => run.StartedAt).ToArray();
    }

    public async ValueTask<CollectionFile> DiscoverAsync(
        string collectionId, DiscoveredFile file, CancellationToken ct = default)
    {
        await using var db = _db();
        var existing = await db.CollectionFiles.AsNoTracking().SingleOrDefaultAsync(
            x => x.ResourceId == file.ResourceId && x.IdentityKey == file.Identity.Key, ct);
        if (existing is not null)
        {
            return ToContract(existing);
        }

        var entity = new CollectionFileEntity
        {
            Id = $"file-{Guid.NewGuid():N}",
            CollectionId = collectionId,
            ResourceId = file.ResourceId,
            IdentityKey = file.Identity.Key,
            RelativePath = file.Identity.RelativePath,
            Length = file.Identity.Length,
            LastWriteUtc = file.Identity.LastWriteUtc,
            ContentHash = file.Identity.ContentHash,
            SourceDate = file.SourceDate,
            CollectedAt = DateTimeOffset.UtcNow,
            Status = "Discovered",
        };
        db.CollectionFiles.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return ToContract(entity);
        }
        catch (DbUpdateException) when (ct.IsCancellationRequested == false)
        {
            // A concurrent run discovered the same resource/file first. The
            // unique index is the authority; return the winning row.
            await using var reread = _db();
            var winner = await reread.CollectionFiles.AsNoTracking().SingleAsync(
                x => x.ResourceId == file.ResourceId && x.IdentityKey == file.Identity.Key, ct);
            return ToContract(winner);
        }
    }

    public ValueTask<CollectionFile?> GetFileByIdentityAsync(string identityKey, CancellationToken ct = default) =>
        GetFileByIdentityAsync(string.Empty, identityKey, ct);

    public async ValueTask<CollectionFile?> GetFileByIdentityAsync(
        string resourceId, string identityKey, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionFiles.AsNoTracking().SingleOrDefaultAsync(
            x => x.IdentityKey == identityKey &&
                 (resourceId == string.Empty || x.ResourceId == resourceId), ct);
        return entity is null ? null : ToContract(entity);
    }

    public async ValueTask<CollectionFile?> GetFileAsync(string fileId, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId, ct);
        return entity is null ? null : ToContract(entity);
    }

    public async ValueTask MarkFileAsync(string fileId, string status, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionFiles.SingleAsync(x => x.Id == fileId, ct);
        entity.Status = status;
        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<FileDelivery> StartDeliveryAsync(
        string collectionFileId, string targetId, string idempotencyKey,
        string targetKind = "record", CancellationToken ct = default)
    {
        await using var db = _db();
        var existing = await db.FileDeliveries.AsNoTracking().SingleOrDefaultAsync(
            x => x.CollectionFileId == collectionFileId && x.TargetId == targetId, ct);
        if (existing is not null)
        {
            return ToContract(existing);
        }

        var entity = new FileDeliveryEntity
        {
            Id = $"delivery-{Guid.NewGuid():N}",
            CollectionFileId = collectionFileId,
            TargetId = targetId,
            TargetKind = targetKind,
            IdempotencyKey = idempotencyKey,
            Status = "Pending",
            Attempts = 0,
        };
        db.FileDeliveries.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return ToContract(entity);
        }
        catch (DbUpdateException) when (ct.IsCancellationRequested == false)
        {
            await using var reread = _db();
            var winner = await reread.FileDeliveries.AsNoTracking().SingleAsync(
                x => x.CollectionFileId == collectionFileId && x.TargetId == targetId, ct);
            return ToContract(winner);
        }
    }

    public async ValueTask<bool> TryClaimDeliveryAsync(string deliveryId, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (entity is null || entity.Status is not ("Pending" or "Failed"))
        {
            return false;
        }

        entity.Status = "InProgress";
        entity.ClaimedAt = DateTimeOffset.UtcNow;
        entity.Error = null;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async ValueTask CompleteDeliveryAsync(string deliveryId, int records, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        if (entity.Status == "Succeeded")
        {
            return;
        }

        entity.Status = "Succeeded";
        entity.Attempts++;
        entity.Records = records;
        entity.Error = null;
        entity.ClaimedAt = null;
        await db.SaveChangesAsync(ct);
        await RollUpFileAsync(entity.CollectionFileId, ct);
    }

    public async ValueTask FailDeliveryAsync(string deliveryId, string error, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        entity.Status = "Failed";
        entity.Attempts++;
        entity.Error = error;
        entity.ClaimedAt = null;
        await db.SaveChangesAsync(ct);
        await RollUpFileAsync(entity.CollectionFileId, ct);
    }

    public ValueTask<IReadOnlyList<FileDelivery>> GetRetrableDeliveriesAsync(CancellationToken ct = default) =>
        GetRetriableDeliveriesAsync(null, ct);

    public async ValueTask<IReadOnlyList<FileDelivery>> GetRetriableDeliveriesAsync(
        string? targetId = null, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.FileDeliveries.AsNoTracking()
            .Where(delivery => delivery.Status == "Failed" && delivery.Attempts < 3 &&
                               (targetId == null || delivery.TargetId == targetId))
            .OrderBy(delivery => delivery.CollectionFileId)
            .ToListAsync(ct);
        return rows.Select(ToContract).ToArray();
    }

    public async ValueTask<IReadOnlyList<FileDelivery>> ListAllDeliveriesAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.FileDeliveries.AsNoTracking()
            .OrderBy(delivery => delivery.TargetId)
            .ToListAsync(ct);
        return rows.Select(ToContract).ToArray();
    }

    public async ValueTask<IReadOnlyList<CollectionFile>> ListFilesAsync(
        string collectionId, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.CollectionFiles.AsNoTracking()
            .Where(file => file.CollectionId == collectionId)
            .ToListAsync(ct);
        return rows.Select(ToContract).OrderBy(file => file.SourceDate).ToArray();
    }

    public async ValueTask<IReadOnlyList<CollectionFile>> ListAllFilesAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.CollectionFiles.AsNoTracking().ToListAsync(ct);
        return rows.Select(ToContract).OrderBy(file => file.CollectedAt).ToArray();
    }

    private async ValueTask RollUpFileAsync(string collectionFileId, CancellationToken ct)
    {
        await using var db = _db();
        var statuses = await db.FileDeliveries.AsNoTracking()
            .Where(row => row.CollectionFileId == collectionFileId)
            .Select(row => row.Status)
            .ToListAsync(ct);
        if (statuses.Count == 0) return;
        var status = statuses.Any(value => value != "Succeeded") ? "PartialFailure" : "Succeeded";
        var file = await db.CollectionFiles.SingleAsync(x => x.Id == collectionFileId, ct);
        if (file.Status == status) return;
        file.Status = status;
        await db.SaveChangesAsync(ct);
    }

    private static CollectionRun ToContract(CollectionRunEntity entity) =>
        new(entity.Id, entity.WorkflowId, entity.StartedAt, entity.Status, entity.Error, entity.FinishedAt);

    private static CollectionFile ToContract(CollectionFileEntity entity) =>
        new(entity.Id, entity.CollectionId,
            new FileIdentity(entity.IdentityKey, entity.RelativePath, entity.Length,
                entity.LastWriteUtc, entity.ContentHash, entity.ResourceId),
            entity.SourceDate, entity.CollectedAt, entity.Status);

    private static FileDelivery ToContract(FileDeliveryEntity entity) =>
        new(entity.Id, entity.CollectionFileId, entity.TargetId, entity.Status,
            entity.Attempts, entity.Error, entity.IdempotencyKey,
            entity.Records, entity.TargetKind, entity.ClaimedAt);
}
