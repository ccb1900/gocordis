using Industrial.Contracts;

namespace Industrial.Core;

public sealed record SnapshotCleanupReport(
    int SnapshotFiles,
    long SnapshotBytes,
    int Candidates,
    int Deleted,
    long DeletedBytes,
    int Orphans,
    long OrphanBytes,
    IReadOnlyList<string> Retained,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> OrphanFailures,
    bool ActiveRunsBlocked,
    bool MaintenanceBlocked,
    DateTimeOffset At);

/// <summary>Snapshot cleanup is ledger-driven and coordinated with workflow
/// execution through the ledger's maintenance lock. A snapshot is only
/// removed when every CollectionFile row that references it is fully
/// delivered and no run is active; unreferenced crash orphans are reclaimed
/// only after a safety retention window. Deletion errors are recorded but
/// never change business state.</summary>
public static class SnapshotMaintenance
{
    public const string LockName = MaintenanceNames.Cleanup;
    public static readonly TimeSpan OrphanRetention = TimeSpan.FromHours(1);

    public static async ValueTask<SnapshotCleanupReport> RunAsync(
        ICollectionLedger ledger, string snapshotRoot, CancellationToken ct = default,
        TimeSpan? orphanRetention = null)
    {
        var at = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath(snapshotRoot);
        var lease = await MaintenanceCoordination.BeginCleanupAsync(ledger, ct);
        if (lease is null)
            return new SnapshotCleanupReport(0, 0, 0, 0, 0, 0, 0, [], [], [],
                false, true, at);
        try
        {
            return await CleanAsync(lease, ledger, root, at,
                orphanRetention ?? OrphanRetention, ct);
        }
        finally
        {
            await lease.ReleaseAsync();
        }
    }

    private static async ValueTask<SnapshotCleanupReport> CleanAsync(
        MaintenanceLease lease, ICollectionLedger ledger, string root, DateTimeOffset at,
        TimeSpan retention, CancellationToken ct)
    {
        var existing = Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToArray()
            : [];
        var totalBytes = existing.Sum(SafeLength);
        var referencedPaths = (await ledger.ListAllFilesAsync(ct))
            .Where(file => !string.IsNullOrWhiteSpace(file.SnapshotPath))
            .Select(file => Path.GetFullPath(file.SnapshotPath!))
            .ToHashSet(StringComparer.Ordinal);

        var runs = await ledger.ListRunsAsync(ct);
        if (runs.Any(run => !CollectionStatus.IsTerminal(run.Status)))
            return new SnapshotCleanupReport(existing.Length, totalBytes, 0, 0, 0, 0, 0,
                [], [], [], true, false, at);

        var files = await ledger.ListAllFilesAsync(ct);
        var deliveries = await ledger.ListAllDeliveriesAsync(ct);
        var referenced = files
            .Where(file => !string.IsNullOrWhiteSpace(file.SnapshotPath))
            .GroupBy(file => Path.GetFullPath(file.SnapshotPath!), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var retained = new List<string>();
        var failures = new List<string>();
        var orphanFailures = new List<string>();
        var deleted = 0;
        var deletedBytes = 0L;
        var candidates = 0;

        foreach (var (path, rows) in referenced)
        {
            if (!IsUnderRoot(path, root))
            {
                retained.Add(path);
                continue;
            }
            var eligible = rows.All(row => row.Status == CollectionStatus.Succeeded &&
                deliveries.Any(delivery => delivery.CollectionFileId == row.Id) &&
                deliveries.Where(delivery => delivery.CollectionFileId == row.Id)
                    .All(delivery => delivery.Status == CollectionStatus.Succeeded));
            if (!eligible || !File.Exists(path))
            {
                retained.Add(path);
                continue;
            }
            candidates++;
            if (!await lease.EnsureValidAsync(ct)) break;
            Delete(path, root, failures, ref deleted, ref deletedBytes);
        }

        var orphanCount = 0;
        var orphanBytes = 0L;
        if (await lease.EnsureValidAsync(ct))
        {
            foreach (var path in existing)
            {
                if (referencedPaths.Contains(path)) continue;
                if (at - File.GetLastWriteTimeUtc(path) < retention) continue;
                if (!IsUnderRoot(path, root)) continue;
                if (!await lease.EnsureValidAsync(ct)) break;
                Delete(path, root, orphanFailures, ref orphanCount, ref orphanBytes);
            }
        }

        return new SnapshotCleanupReport(existing.Length, totalBytes, candidates,
            deleted, deletedBytes, orphanCount, orphanBytes, retained, failures,
            orphanFailures, false, false, at);
    }

    private static void Delete(
        string path, string root, ICollection<string> failures, ref int count, ref long bytes)
    {
        try
        {
            var size = SafeLength(path);
            File.Delete(path);
            count++;
            bytes += size;
            PruneEmptyDirectories(Path.GetDirectoryName(path), root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{path}: {ex.Message}");
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0L; }
        catch (UnauthorizedAccessException) { return 0L; }
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }

    private static void PruneEmptyDirectories(string? directory, string root)
    {
        while (!string.IsNullOrWhiteSpace(directory) && IsUnderRoot(directory, root))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
