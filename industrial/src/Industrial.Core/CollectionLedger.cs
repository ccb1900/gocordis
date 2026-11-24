using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>File-level and target-level ledger. One file may have several
/// deliveries; each target records success/failure independently.</summary>
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

    public async ValueTask<CollectionFile> DiscoverAsync(
        string collectionId, DiscoveredFile file, CancellationToken ct = default)
    {
        await using var db = _db();
        var existing = await db.CollectionFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdentityKey == file.Identity.Key, ct);
        if (existing is not null)
        {
            return ToContract(existing);
        }

        var entity = new CollectionFileEntity
        {
            Id = $"file-{Guid.NewGuid():N}",
            CollectionId = collectionId,
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
        await db.SaveChangesAsync(ct);
        return ToContract(entity);
    }

    public async ValueTask<CollectionFile?> GetFileByIdentityAsync(string identityKey, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdentityKey == identityKey, ct);
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
        string collectionFileId, string targetId, string idempotencyKey, CancellationToken ct = default)
    {
        await using var db = _db();
        var existing = await db.FileDeliveries.AsNoTracking().SingleOrDefaultAsync(
            x => x.TargetId == targetId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            return ToContract(existing);
        }

        var entity = new FileDeliveryEntity
        {
            Id = $"delivery-{Guid.NewGuid():N}",
            CollectionFileId = collectionFileId,
            TargetId = targetId,
            IdempotencyKey = idempotencyKey,
            Status = "Pending",
            Attempts = 0,
        };
        db.FileDeliveries.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToContract(entity);
    }

    public async ValueTask CompleteDeliveryAsync(string deliveryId, int records, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        entity.Status = "Succeeded";
        entity.Attempts++;
        entity.Error = null;
        await db.SaveChangesAsync(ct);
    }

    public async ValueTask FailDeliveryAsync(string deliveryId, string error, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        entity.Status = "Failed";
        entity.Attempts++;
        entity.Error = error;
        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<IReadOnlyList<FileDelivery>> GetRetrableDeliveriesAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.FileDeliveries.AsNoTracking()
            .Where(delivery => delivery.Status == "Failed" && delivery.Attempts < 3)
            .OrderBy(delivery => delivery.CollectionFileId)
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

    private static CollectionRun ToContract(CollectionRunEntity entity) =>
        new(entity.Id, entity.WorkflowId, entity.StartedAt, entity.Status, entity.Error);

    private static CollectionFile ToContract(CollectionFileEntity entity) =>
        new(entity.Id, entity.CollectionId,
            new FileIdentity(entity.IdentityKey, entity.RelativePath, entity.Length,
                entity.LastWriteUtc, entity.ContentHash),
            entity.SourceDate, entity.CollectedAt, entity.Status);

    private static FileDelivery ToContract(FileDeliveryEntity entity) =>
        new(entity.Id, entity.CollectionFileId, entity.TargetId, entity.Status,
            entity.Attempts, entity.Error, entity.IdempotencyKey);
}
