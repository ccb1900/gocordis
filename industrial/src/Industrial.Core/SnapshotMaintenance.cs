using Industrial.Contracts;

namespace Industrial.Core;

public sealed record SnapshotCleanupReport(
    int SnapshotFiles,
    long SnapshotBytes,
    int Candidates,
    int Deleted,
    long DeletedBytes,
    IReadOnlyList<string> Retained,
    IReadOnlyList<string> Failures,
    bool ActiveRunsBlocked,
    DateTimeOffset At);

/// <summary>Snapshot cleanup is ledger-driven: a snapshot is only removed
/// when every CollectionFile row that references it is fully delivered and
/// no run is still active. Any deletion error is recorded but never changes
/// business state.</summary>
public static class SnapshotMaintenance
{
    public static async ValueTask<SnapshotCleanupReport> RunAsync(
        ICollectionLedger ledger, string snapshotRoot, CancellationToken ct = default)
    {
        var at = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath(snapshotRoot);
        var existing = Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToArray()
            : [];
        var totalBytes = existing.Sum(path =>
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return 0L; }
        });
        var runs = await ledger.ListRunsAsync(ct);
        if (runs.Any(run => !CollectionStatus.IsTerminal(run.Status)))
            return new SnapshotCleanupReport(existing.Length, totalBytes, 0, 0, 0,
                [], [], true, at);

        var files = await ledger.ListAllFilesAsync(ct);
        var deliveries = await ledger.ListAllDeliveriesAsync(ct);
        var referenced = files
            .Where(file => !string.IsNullOrWhiteSpace(file.SnapshotPath))
            .GroupBy(file => Path.GetFullPath(file.SnapshotPath!), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var retained = new List<string>();
        var failures = new List<string>();
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
            try
            {
                var bytes = new FileInfo(path).Length;
                File.Delete(path);
                deleted++;
                deletedBytes += bytes;
                PruneEmptyDirectories(Path.GetDirectoryName(path), root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }

        return new SnapshotCleanupReport(existing.Length, totalBytes, candidates,
            deleted, deletedBytes, retained, failures, false, at);
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
