using System.Globalization;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>File-level and target-level ledger. Delivery claims are atomic
/// database updates with a token/lease; run status is derived from run-scoped
/// delivery outcomes, never from caller-supplied strings.</summary>
public sealed class CollectionLedger : ICollectionLedger
{
    public const int MaxAttempts = 3;
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);
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
            Status = CollectionStatus.Created,
        };
        db.CollectionRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return ToContract(run);
    }

    public async ValueTask<CollectionRun> BeginRunAsync(string runId, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await Exec(db,
            "UPDATE CollectionRuns SET Status = {0} WHERE Id = {1} AND Status = {2}",
            CollectionStatus.Running, runId, CollectionStatus.Created, ct);
        if (rows != 1)
        {
            var current = await GetRunAsync(runId, ct);
            if (current is null) throw new InvalidOperationException($"run '{runId}' not found");
            return current;
        }
        return (await GetRunAsync(runId, ct))!;
    }

    public async ValueTask<CollectionRun> CompleteRunAsync(
        string runId, string status, string? error, CancellationToken ct = default)
    {
        if (status is not (CollectionStatus.Succeeded or CollectionStatus.PartialFailure or
                           CollectionStatus.Failed or CollectionStatus.Cancelled or CollectionStatus.Interrupted))
        {
            throw new ArgumentException($"invalid terminal run status '{status}'", nameof(status));
        }

        await using var db = _db();
        var run = await db.CollectionRuns.SingleAsync(x => x.Id == runId, ct);
        var allowed = run.Status switch
        {
            CollectionStatus.Created => status is CollectionStatus.Running or CollectionStatus.Cancelled,
            CollectionStatus.Running or CollectionStatus.Interrupted =>
                status is CollectionStatus.Succeeded or CollectionStatus.PartialFailure or
                          CollectionStatus.Failed or CollectionStatus.Cancelled or CollectionStatus.Interrupted,
            _ => false,
        };
        if (!allowed) return ToContract(run);
        run.Status = status;
        run.Error = error;
        if (CollectionStatus.IsTerminal(status)) run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToContract(run);
    }

    public async ValueTask<CollectionRun> FinalizeRunAsync(string runId, CancellationToken ct = default)
    {
        await using var db = _db();
        var run = await db.CollectionRuns.SingleAsync(x => x.Id == runId, ct);
        if (CollectionStatus.IsTerminal(run.Status)) return ToContract(run);

        var rows = await db.CollectionRunDeliveries.AsNoTracking()
            .Where(row => row.RunId == runId)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            // Legacy callers without run-scoped rows: use all deliveries that
            // reference files discovered by this run.
            var fileIds = await db.CollectionFiles.AsNoTracking()
                .Where(file => file.CollectionId == runId)
                .Select(file => file.Id)
                .ToListAsync(ct);
            var deliveries = await db.FileDeliveries.AsNoTracking()
                .Where(delivery => fileIds.Contains(delivery.CollectionFileId))
                .ToListAsync(ct);
            if (deliveries.Count == 0) return await CompleteRunAsync(runId, CollectionStatus.Succeeded, null, ct);
            var legacySucceeded = deliveries.Count(x => x.Status == CollectionStatus.Succeeded);
            var legacyFailed = deliveries.Count(x => x.Status == CollectionStatus.Failed);
            var active = deliveries.Count(x => x.Status == CollectionStatus.InProgress);
            return await CompleteRunAsync(runId,
                active > 0 ? CollectionStatus.Interrupted :
                legacyFailed == 0 ? CollectionStatus.Succeeded :
                legacySucceeded > 0 ? CollectionStatus.PartialFailure : CollectionStatus.Failed,
                legacyFailed == 0 ? null : "one or more deliveries failed", ct);
        }

        var statusCounts = rows.GroupBy(row => row.Status)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var claimed = statusCounts.GetValueOrDefault("Claimed");
        var considered = statusCounts.GetValueOrDefault("Considered");
        var interrupted = statusCounts.GetValueOrDefault(CollectionStatus.Interrupted);
        var failed = statusCounts.GetValueOrDefault(CollectionStatus.Failed);
        var succeeded = statusCounts.GetValueOrDefault(CollectionStatus.Succeeded) +
                        statusCounts.GetValueOrDefault("SkippedSucceeded");

        if (claimed > 0 || considered > 0) return ToContract(run);
        var finalStatus = interrupted > 0 ? CollectionStatus.Interrupted :
            failed == 0 ? CollectionStatus.Succeeded :
            succeeded > 0 ? CollectionStatus.PartialFailure : CollectionStatus.Failed;
        var error = rows.FirstOrDefault(row => row.LastError is not null)?.LastError;
        return await CompleteRunAsync(runId, finalStatus, error, ct);
    }

    public async ValueTask<CollectionRun> CancelRunAsync(
        string runId, string reason, CancellationToken ct = default) =>
        await CompleteRunAsync(runId, CollectionStatus.Cancelled, reason, ct);

    public ValueTask<int> RecoverExpiredLeasesAsync(
        DateTimeOffset now, CancellationToken ct = default)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        return RecoverLeasesAsync(
            row => row.LeaseExpiresAtUnixMs > 0 && row.LeaseExpiresAtUnixMs <= nowMs,
            "lease expired before completion", ct);
    }

    /// <summary>Startup-only recovery: after a hard process exit no claim owner
    /// is alive, so even an unexpired lease must be released before its Elsa
    /// instance can be resumed. Never call this while a run is executing.</summary>
    public ValueTask<int> RecoverAbandonedLeasesAsync(CancellationToken ct = default) =>
        RecoverLeasesAsync(_ => true, "process restarted before delivery completed", ct);

    private static string ScheduleFireKey(string workflowId, int version, DateTimeOffset dueAtUtc) =>
        $"{workflowId}:v{version}:{dueAtUtc.ToUniversalTime():O}";

    public async ValueTask EnsureScheduleFiresAsync(
        string workflowId, int version, IEnumerable<DateTimeOffset> dueInstants,
        CancellationToken ct = default)
    {
        await using var db = _db();
        var planned = DateTimeOffset.UtcNow.ToString("O");
        foreach (var instant in dueInstants)
        {
            await Exec(db,
                """
                INSERT OR IGNORE INTO ScheduleFires
                    (Key, WorkflowId, Version, DueAtUtc, FiredAtUtc, Status, Attempts, PlannedAtUtc)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 0, {6});
                """,
                ScheduleFireKey(workflowId, version, instant), workflowId, version,
                instant.ToUniversalTime().ToString("O"), string.Empty,
                ScheduleFireStatus.Pending, planned, ct);
        }
    }

    public async ValueTask<ScheduleFireClaim?> TryStartScheduleFireAsync(
        string workflowId, int version, DateTimeOffset dueAtUtc, TimeSpan lease,
        CancellationToken ct = default)
    {
        await using var db = _db();
        var key = ScheduleFireKey(workflowId, version, dueAtUtc);
        // Insert first so a caller may claim without a separate materialize
        // step; INSERT OR IGNORE keeps the operation idempotent.
        await Exec(db,
            """
            INSERT OR IGNORE INTO ScheduleFires
                (Key, WorkflowId, Version, DueAtUtc, FiredAtUtc, Status, Attempts, PlannedAtUtc)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 0, {6});
            """,
            key, workflowId, version, dueAtUtc.ToUniversalTime().ToString("O"),
            string.Empty, ScheduleFireStatus.Pending, DateTimeOffset.UtcNow.ToString("O"), ct);
        var token = $"sf-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var leaseEnd = now + (long)lease.TotalMilliseconds;
        var rows = await Exec(db,
            """
            UPDATE ScheduleFires
               SET Status = {0}, ClaimToken = {1}, LeaseExpiresAtUnixMs = {2}, Attempts = Attempts + 1
             WHERE Key = {3}
               AND Status IN ({4}, {5}, {8})
               AND (LeaseExpiresAtUnixMs = 0 OR LeaseExpiresAtUnixMs <= {6})
               AND Attempts < {7};
            """,
            ScheduleFireStatus.Running, token, leaseEnd, key,
            ScheduleFireStatus.Pending, ScheduleFireStatus.Failed, now, MaxAttempts,
            ScheduleFireStatus.Running, ct);
        if (rows != 1) return null;
        var attempts = await db.Database.SqlQueryRaw<ScheduleFireAttemptRow>(
            "SELECT Attempts FROM ScheduleFires WHERE Key = {0}", key).SingleAsync(ct);
        return new ScheduleFireClaim(key, token, attempts.Attempts);
    }

    public async ValueTask<bool> TryClaimScheduleFireAsync(
        string workflowId, int version, DateTimeOffset dueAtUtc, CancellationToken ct = default) =>
        await TryStartScheduleFireAsync(workflowId, version, dueAtUtc, TimeSpan.FromMinutes(5), ct) is not null;

    public async ValueTask CompleteScheduleFireAsync(
        string key, string token, string status, string? error, CancellationToken ct = default)
    {
        if (status is not (ScheduleFireStatus.Succeeded or ScheduleFireStatus.Failed))
            throw new ArgumentException($"invalid schedule fire status '{status}'", nameof(status));
        await using var db = _db();
        await Exec(db,
            """
            UPDATE ScheduleFires
               SET Status = {0}, Error = {1}, FinishedAtUtc = {2},
                   ClaimToken = NULL, LeaseExpiresAtUnixMs = 0
             WHERE Key = {3} AND ClaimToken = {4} AND Status = {5};
            """,
            status, error ?? string.Empty, DateTimeOffset.UtcNow.ToString("O"),
            key, token, ScheduleFireStatus.Running, ct);
    }

    public async ValueTask<int> AbandonScheduleFiresAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        return await Exec(db,
            """
            UPDATE ScheduleFires
               SET Status = {0}, Error = {1}, ClaimToken = NULL, LeaseExpiresAtUnixMs = 0
             WHERE Status = {2};
            """,
            ScheduleFireStatus.Failed, "process restarted before scheduled run completed",
            ScheduleFireStatus.Running, ct);
    }

    public async ValueTask<IReadOnlyList<ScheduleFireView>> ListScheduleFiresAsync(CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Database.SqlQueryRaw<ScheduleFireRow>(
            """
            SELECT Key, WorkflowId, Version, DueAtUtc, Status, Attempts, Error, FinishedAtUtc,
                   LeaseExpiresAtUnixMs
              FROM ScheduleFires
             ORDER BY DueAtUtc DESC;
            """).ToListAsync(ct);
        return rows.Select(row => new ScheduleFireView(
            row.Key, row.WorkflowId, row.Version,
            DateTimeOffset.Parse(row.DueAtUtc, CultureInfo.InvariantCulture),
            row.Status, row.Attempts,
            string.IsNullOrEmpty(row.Error) ? null : row.Error,
            string.IsNullOrEmpty(row.FinishedAtUtc)
                ? null : DateTimeOffset.Parse(row.FinishedAtUtc, CultureInfo.InvariantCulture),
            row.LeaseExpiresAtUnixMs))
            .ToArray();
    }

    public async ValueTask<(DateTimeOffset? Checkpoint, int Skipped, bool BacklogTruncated)> GetScheduleCheckpointAsync(
        string workflowId, int version, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.Database.SqlQueryRaw<ScheduleCheckpointRow>(
            """
            SELECT CheckpointUtc, Skipped, BacklogTruncated FROM ScheduleCheckpoints
             WHERE WorkflowId = {0} AND Version = {1};
            """, workflowId, version).ToListAsync(ct);
        if (rows.Count == 0) return (null, 0, false);
        var row = rows[0];
        return (string.IsNullOrWhiteSpace(row.CheckpointUtc)
            ? null : DateTimeOffset.Parse(row.CheckpointUtc, CultureInfo.InvariantCulture),
            row.Skipped, row.BacklogTruncated != 0);
    }

    public async ValueTask SetScheduleCheckpointAsync(
        string workflowId, int version, DateTimeOffset checkpoint, int skippedIncrement,
        CancellationToken ct = default)
    {
        await using var db = _db();
        await Exec(db,
            """
            INSERT INTO ScheduleCheckpoints (WorkflowId, Version, CheckpointUtc, Skipped)
            VALUES ({0}, {1}, {2}, {3})
            ON CONFLICT(WorkflowId, Version) DO UPDATE SET
                CheckpointUtc = excluded.CheckpointUtc,
                Skipped = ScheduleCheckpoints.Skipped + excluded.Skipped;
            """,
            workflowId, version, checkpoint.ToUniversalTime().ToString("O"), skippedIncrement, ct);
    }

    public async ValueTask<bool> CommitScheduleWindowAsync(
        string workflowId, int version, DateTimeOffset? expectedCheckpoint,
        DateTimeOffset nextCheckpoint, IReadOnlyList<DateTimeOffset> dueInstants,
        int skippedIncrement, bool backlogTruncated, CancellationToken ct = default)
    {
        await using var db = _db();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Database.SqlQueryRaw<ScheduleCheckpointRow>(
            """
            SELECT CheckpointUtc, Skipped, BacklogTruncated FROM ScheduleCheckpoints
             WHERE WorkflowId = {0} AND Version = {1};
            """, workflowId, version).ToListAsync(ct);
        var current = rows.Count == 0 ? null : rows[0].CheckpointUtc;
        var expected = expectedCheckpoint?.ToUniversalTime().ToString("O") ?? string.Empty;
        if (!string.Equals(current ?? string.Empty, expected, StringComparison.Ordinal))
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var planned = DateTimeOffset.UtcNow.ToString("O");
        foreach (var instant in dueInstants)
        {
            await Exec(db,
                """
                INSERT OR IGNORE INTO ScheduleFires
                    (Key, WorkflowId, Version, DueAtUtc, FiredAtUtc, Status, Attempts, PlannedAtUtc)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 0, {6});
                """,
                ScheduleFireKey(workflowId, version, instant), workflowId, version,
                instant.ToUniversalTime().ToString("O"), string.Empty,
                ScheduleFireStatus.Pending, planned, ct);
        }
        await Exec(db,
            """
            INSERT INTO ScheduleCheckpoints (WorkflowId, Version, CheckpointUtc, Skipped, BacklogTruncated)
            VALUES ({0}, {1}, {2}, {3}, {4})
            ON CONFLICT(WorkflowId, Version) DO UPDATE SET
                CheckpointUtc = excluded.CheckpointUtc,
                Skipped = ScheduleCheckpoints.Skipped + excluded.Skipped,
                BacklogTruncated = excluded.BacklogTruncated;
            """,
            workflowId, version, nextCheckpoint.ToUniversalTime().ToString("O"),
            skippedIncrement, backlogTruncated ? 1 : 0, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private sealed class ScheduleCheckpointRow
    {
        public string CheckpointUtc { get; set; } = string.Empty;
        public int Skipped { get; set; }
        public int BacklogTruncated { get; set; }
    }

    private sealed class ScheduleFireAttemptRow
    {
        public int Attempts { get; set; }
    }

    private sealed class ScheduleFireRow
    {
        public string Key { get; set; } = string.Empty;
        public string WorkflowId { get; set; } = string.Empty;
        public int Version { get; set; }
        public string DueAtUtc { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? Error { get; set; }
        public string? FinishedAtUtc { get; set; }
        public long LeaseExpiresAtUnixMs { get; set; }
    }

    private async ValueTask<int> RecoverLeasesAsync(
        Func<FileDeliveryEntity, bool> predicate, string reason, CancellationToken ct)
    {
        await using var db = _db();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var held = await db.FileDeliveries.AsNoTracking()
            .Where(row => row.Status == CollectionStatus.InProgress)
            .ToListAsync(ct);
        var expired = held.Where(predicate).ToArray();
        if (expired.Length == 0) return 0;

        foreach (var delivery in expired)
        {
            await Exec(db,
                """
                UPDATE FileDeliveries SET Status = {0}, ClaimToken = NULL,
                    LeaseExpiresAt = NULL, LeaseExpiresAtUnixMs = 0,
                    Error = {1}, Version = Version + 1
                WHERE Id = {2} AND Status = {3} AND ClaimToken = {4}
                """,
                CollectionStatus.Interrupted, reason,
                delivery.Id, CollectionStatus.InProgress, delivery.ClaimToken ?? string.Empty, ct);
            await Exec(db,
                """
                UPDATE CollectionRunDeliveries SET Status = {0}, ClaimToken = NULL, LastError = {1}
                WHERE DeliveryId = {2} AND ClaimToken = {3}
                """,
                CollectionStatus.Interrupted, reason,
                delivery.Id, delivery.ClaimToken ?? string.Empty, ct);
            var runIds = await db.CollectionRunDeliveries.AsNoTracking()
                .Where(row => row.DeliveryId == delivery.Id)
                .Select(row => row.RunId)
                .ToListAsync(ct);
            foreach (var runId in runIds)
            {
                var run = await db.CollectionRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
                if (run is null || CollectionStatus.IsTerminal(run.Status)) continue;
                run.Status = CollectionStatus.Interrupted;
                run.Error = reason;
                run.FinishedAt = null;
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return expired.Length;
    }

    public async ValueTask<CollectionRun?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.CollectionRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        return entity is null ? null : ToContract(entity);
    }

    public async ValueTask<bool> SetRunInstanceAsync(
        string runId, string instanceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        await using var db = _db();
        var rows = await Exec(db,
            """
            UPDATE CollectionRuns SET InstanceId = {0}
            WHERE Id = {1} AND (InstanceId IS NULL OR InstanceId = '' OR InstanceId = {0})
            """,
            instanceId, runId, ct);
        return rows == 1;
    }

    public async ValueTask<string?> GetRunInstanceAsync(string runId, CancellationToken ct = default)
    {
        await using var db = _db();
        return await db.CollectionRuns.AsNoTracking()
            .Where(x => x.Id == runId)
            .Select(x => x.InstanceId)
            .SingleOrDefaultAsync(ct);
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
        if (existing is not null) return ToContract(existing);

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
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
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
        await Exec(db,
            "UPDATE CollectionFiles SET Status = {0} WHERE Id = {1}", status, fileId, ct);
    }

    public async ValueTask<FileDelivery> StartDeliveryAsync(
        string collectionFileId, string targetId, string idempotencyKey,
        string targetKind = "record", string? runId = null, CancellationToken ct = default)
    {
        await using var db = _db();
        var existing = await db.FileDeliveries.AsNoTracking().SingleOrDefaultAsync(
            x => x.CollectionFileId == collectionFileId && x.TargetId == targetId, ct);
        var entity = existing;
        if (entity is null)
        {
            entity = new FileDeliveryEntity
            {
                Id = $"delivery-{Guid.NewGuid():N}",
                CollectionFileId = collectionFileId,
                TargetId = targetId,
                TargetKind = targetKind,
                IdempotencyKey = idempotencyKey,
                Status = CollectionStatus.Pending,
                Attempts = 0,
            };
            db.FileDeliveries.Add(entity);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) when (!ct.IsCancellationRequested)
            {
                entity = await db.FileDeliveries.AsNoTracking().SingleAsync(
                    x => x.CollectionFileId == collectionFileId && x.TargetId == targetId, ct);
            }
        }

        if (runId is not null)
        {
            await AttachRunDeliveryAsync(db, runId, entity.Id,
                entity.Status == CollectionStatus.Succeeded ? "SkippedSucceeded" : "Considered", null, ct);
        }
        return ToContract(entity);
    }

    public async ValueTask<DeliveryClaim?> TryClaimDeliveryAsync(
        string runId, string deliveryId, TimeSpan lease, CancellationToken ct = default)
    {
        await using var db = _db();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var token = $"claim-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var leaseEnd = now + lease;
        var rows = await Exec(db,
            """
            UPDATE FileDeliveries SET Status = {0}, ClaimToken = {1}, ClaimedAt = {2},
                LeaseExpiresAt = {3}, LeaseExpiresAtUnixMs = {4}, Attempts = Attempts + 1,
                Error = NULL, Version = Version + 1, LastAttemptRunId = {12}
            WHERE Id = {5} AND Status IN ({6}, {7}, {8}, {9})
              AND (LeaseExpiresAtUnixMs = 0 OR LeaseExpiresAtUnixMs <= {10})
              AND Attempts < {11}
            """,
            CollectionStatus.InProgress, token, now, leaseEnd, leaseEnd.ToUnixTimeMilliseconds(),
            deliveryId, CollectionStatus.Pending, CollectionStatus.Failed,
            CollectionStatus.Interrupted, CollectionStatus.InProgress,
            now.ToUnixTimeMilliseconds(), MaxAttempts, runId, ct);
        if (rows != 1) return null;

        await AttachRunDeliveryAsync(db, runId, deliveryId, "Claimed", token, ct);
        await tx.CommitAsync(ct);
        return new DeliveryClaim(deliveryId, token, leaseEnd,
            await db.FileDeliveries.AsNoTracking()
                .Where(x => x.Id == deliveryId).Select(x => x.Attempts).SingleAsync(ct));
    }

    public async ValueTask<bool> TryClaimDeliveryAsync(string deliveryId, CancellationToken ct = default) =>
        await TryClaimDeliveryAsync("legacy", deliveryId, DefaultLease, ct) is not null;

    /// <summary>Extends a long-running delivery while its token still owns it.
    /// A lost or superseded token cannot renew, so a fenced-out writer never
    /// keeps a claim alive.</summary>
    public async ValueTask<bool> RenewDeliveryLeaseAsync(
        string deliveryId, string claimToken, TimeSpan lease, CancellationToken ct = default)
    {
        await using var db = _db();
        var leaseEnd = DateTimeOffset.UtcNow + lease;
        var rows = await Exec(db,
            """
            UPDATE FileDeliveries SET LeaseExpiresAt = {0}, LeaseExpiresAtUnixMs = {1},
                Version = Version + 1
            WHERE Id = {2} AND Status = {3} AND ClaimToken = {4}
            """,
            leaseEnd, leaseEnd.ToUnixTimeMilliseconds(), deliveryId,
            CollectionStatus.InProgress, claimToken, ct);
        return rows == 1;
    }

    public async ValueTask<DeliveryOutcome> CompleteDeliveryAsync(
        string deliveryId, string claimToken, int records, CancellationToken ct = default)
    {
        await using var db = _db();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await Exec(db,
            """
            UPDATE FileDeliveries SET Status = {0}, Records = {1}, ClaimToken = NULL,
                ClaimedAt = NULL, LeaseExpiresAt = NULL, LeaseExpiresAtUnixMs = 0,
                Error = NULL, Version = Version + 1
            WHERE Id = {2} AND Status = {3} AND ClaimToken = {4}
            """,
            CollectionStatus.Succeeded, records, deliveryId, CollectionStatus.InProgress, claimToken, ct);
        if (rows != 1)
        {
            await tx.RollbackAsync(ct);
            return DeliveryOutcome.Rejected("delivery is not held by this claim");
        }
        await Exec(db,
            "UPDATE CollectionRunDeliveries SET Status = {0}, ClaimToken = NULL WHERE ClaimToken = {1}",
            CollectionStatus.Succeeded, claimToken, ct);
        var fileId = await db.FileDeliveries.AsNoTracking()
            .Where(x => x.Id == deliveryId).Select(x => x.CollectionFileId).SingleAsync(ct);
        await RollUpFileAsync(db, fileId, ct);
        await tx.CommitAsync(ct);
        return DeliveryOutcome.Ok();
    }

    public async ValueTask CompleteDeliveryAsync(string deliveryId, int records, CancellationToken ct = default)
    {
        await using var db = _db();
        var token = await db.FileDeliveries.AsNoTracking()
            .Where(x => x.Id == deliveryId).Select(x => x.ClaimToken).SingleAsync(ct);
        await CompleteDeliveryAsync(deliveryId, token ?? string.Empty, records, ct);
    }

    public async ValueTask<DeliveryOutcome> CompleteDeliveryAsync(
        string deliveryId, string claimToken, ProcessingResult result, CancellationToken ct = default)
    {
        await using var db = _db();
        var entity = await db.FileDeliveries.SingleOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (entity is null || entity.Status != CollectionStatus.InProgress ||
            !string.Equals(entity.ClaimToken, claimToken, StringComparison.Ordinal))
        {
            return DeliveryOutcome.Rejected("delivery is not held by this claim");
        }

        entity.Status = CollectionStatus.Succeeded;
        entity.Records = result.Accepted;
        entity.Accepted = result.Accepted;
        entity.Rejected = result.Rejected;
        entity.Warnings = result.Warnings;
        entity.StatisticsJson = System.Text.Json.JsonSerializer.Serialize(result.Statistics);
        entity.ClaimToken = null;
        entity.ClaimedAt = null;
        entity.LeaseExpiresAt = null;
        entity.LeaseExpiresAtUnixMs = 0;
        entity.Error = null;
        await AttachRunDeliveryAsync(db, "legacy", deliveryId, CollectionStatus.Succeeded, null, ct);
        await db.SaveChangesAsync(ct);
        await RollUpFileAsync(db, entity.CollectionFileId, ct);
        return DeliveryOutcome.Ok();
    }

    public async ValueTask<DeliveryOutcome> FailDeliveryAsync(
        string deliveryId, string claimToken, string error, CancellationToken ct = default)
    {
        await using var db = _db();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await Exec(db,
            """
            UPDATE FileDeliveries SET Status = {0}, ClaimToken = NULL, ClaimedAt = NULL,
                LeaseExpiresAt = NULL, LeaseExpiresAtUnixMs = 0, Error = {1}, Version = Version + 1
            WHERE Id = {2} AND Status = {3} AND ClaimToken = {4}
            """,
            CollectionStatus.Failed, error, deliveryId, CollectionStatus.InProgress, claimToken, ct);
        if (rows != 1)
        {
            await tx.RollbackAsync(ct);
            return DeliveryOutcome.Rejected("delivery is not held by this claim");
        }
        await Exec(db,
            "UPDATE CollectionRunDeliveries SET Status = {0}, ClaimToken = NULL, LastError = {1} WHERE ClaimToken = {2}",
            CollectionStatus.Failed, error, claimToken, ct);
        var fileId = await db.FileDeliveries.AsNoTracking()
            .Where(x => x.Id == deliveryId).Select(x => x.CollectionFileId).SingleAsync(ct);
        await RollUpFileAsync(db, fileId, ct);
        await tx.CommitAsync(ct);
        return DeliveryOutcome.Ok();
    }

    public async ValueTask FailDeliveryAsync(string deliveryId, string error, CancellationToken ct = default)
    {
        await using var db = _db();
        var token = await db.FileDeliveries.AsNoTracking()
            .Where(x => x.Id == deliveryId).Select(x => x.ClaimToken).SingleAsync(ct);
        await FailDeliveryAsync(deliveryId, token ?? string.Empty, error, ct);
    }

    public async ValueTask MarkDeliveryContestedAsync(
        string runId, string deliveryId, string reason, CancellationToken ct = default)
    {
        await using var db = _db();
        await AttachRunDeliveryAsync(db, runId, deliveryId, "Contested", null, ct);
        await Exec(db,
            "UPDATE CollectionRunDeliveries SET LastError = {0} WHERE RunId = {1} AND DeliveryId = {2}",
            reason, runId, deliveryId, ct);
    }

    public ValueTask<IReadOnlyList<FileDelivery>> GetRetrableDeliveriesAsync(CancellationToken ct = default) =>
        GetRetriableDeliveriesAsync(null, ct);

    public async ValueTask<IReadOnlyList<FileDelivery>> GetRetriableDeliveriesAsync(
        string? targetId = null, CancellationToken ct = default)
    {
        await using var db = _db();
        var rows = await db.FileDeliveries.AsNoTracking()
            .Where(delivery => delivery.Status != CollectionStatus.Succeeded &&
                               delivery.Status != CollectionStatus.InProgress &&
                               delivery.Attempts < MaxAttempts &&
                               (targetId == null || delivery.TargetId == targetId))
            .OrderBy(delivery => delivery.CollectionFileId)
            .ToListAsync(ct);
        return rows.Select(ToContract).ToArray();
    }

    public async ValueTask<FileDelivery?> GetDeliveryAsync(
        string deliveryId, CancellationToken ct = default)
    {
        await using var db = _db();
        var row = await db.FileDeliveries.AsNoTracking()
            .SingleOrDefaultAsync(delivery => delivery.Id == deliveryId, ct);
        return row is null ? null : ToContract(row);
    }

    public async ValueTask<IReadOnlyList<string>> GetRunIdsByDeliveryAsync(
        string deliveryId, CancellationToken ct = default)
    {
        await using var db = _db();
        return await db.CollectionRunDeliveries.AsNoTracking()
            .Where(row => row.DeliveryId == deliveryId)
            .Select(row => row.RunId)
            .Distinct()
            .ToListAsync(ct);
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

    private static Task<int> Exec(IndustrialDbContext db, string sql, params object[] parameters)
    {
        var ct = parameters.OfType<CancellationToken>().FirstOrDefault();
        var args = parameters.Where(parameter => parameter is not CancellationToken).ToArray();
        return db.Database.ExecuteSqlRawAsync(sql, args, ct);
    }

    private static async ValueTask AttachRunDeliveryAsync(
        IndustrialDbContext db, string runId, string deliveryId, string status,
        string? claimToken, CancellationToken ct)
    {
        var row = await db.CollectionRunDeliveries
            .SingleOrDefaultAsync(x => x.RunId == runId && x.DeliveryId == deliveryId, ct);
        if (row is null)
        {
            db.CollectionRunDeliveries.Add(new CollectionRunDeliveryEntity
            {
                RunId = runId,
                DeliveryId = deliveryId,
                Status = status,
                ClaimToken = claimToken,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Status = status;
            row.ClaimToken = claimToken;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async ValueTask RollUpFileAsync(
        IndustrialDbContext db, string collectionFileId, CancellationToken ct)
    {
        var statuses = await db.FileDeliveries.AsNoTracking()
            .Where(row => row.CollectionFileId == collectionFileId)
            .Select(row => row.Status)
            .ToListAsync(ct);
        if (statuses.Count == 0) return;
        var status = statuses.All(value => value == CollectionStatus.Succeeded)
            ? CollectionStatus.Succeeded
            : CollectionStatus.PartialFailure;
        await Exec(db,
            "UPDATE CollectionFiles SET Status = {0} WHERE Id = {1}", status, collectionFileId, ct);
    }

    private static CollectionRun ToContract(CollectionRunEntity entity) =>
        new(entity.Id, entity.WorkflowId, entity.StartedAt, entity.Status, entity.Error,
            entity.FinishedAt, entity.InstanceId);

    private static CollectionFile ToContract(CollectionFileEntity entity) =>
        new(entity.Id, entity.CollectionId,
            new FileIdentity(entity.IdentityKey, entity.RelativePath, entity.Length,
                entity.LastWriteUtc, entity.ContentHash, entity.ResourceId),
            entity.SourceDate, entity.CollectedAt, entity.Status);

    private static FileDelivery ToContract(FileDeliveryEntity entity) =>
        new(entity.Id, entity.CollectionFileId, entity.TargetId, entity.Status,
            entity.Attempts, entity.Error, entity.IdempotencyKey,
            entity.Records, entity.TargetKind, entity.ClaimedAt,
            entity.ClaimToken, entity.LeaseExpiresAt, entity.Error,
            entity.Accepted, entity.Rejected, entity.Warnings, entity.StatisticsJson,
            entity.LastAttemptRunId);
}
