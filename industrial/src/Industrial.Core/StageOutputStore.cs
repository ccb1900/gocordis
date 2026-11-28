using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Outcome of one ledger-driven stage cleanup pass. Failures are
/// evidence, never a maintenance crash; retained entries explain why a row was
/// kept so an operator can tell "still needed" from "deleted".</summary>
public sealed record StageCleanupReport(
    int StageRows,
    long StageBytes,
    int Candidates,
    int RowsDeleted,
    int FilesDeleted,
    long DeletedBytes,
    IReadOnlyList<string> Retained,
    IReadOnlyList<string> Failures,
    bool MaintenanceBlocked,
    DateTimeOffset At,
    int Orphans = 0,
    long OrphanBytes = 0,
    IReadOnlyList<string>? OrphanFailures = null,
    bool LeaseLost = false);

/// <summary>Cordis-owned data plane. Elsa keeps control flow; this store keeps
/// stage outputs as small refs plus an NDJSON spool so a resumed Activity never
/// needs to re-read or re-transform a completed stage.</summary>
public sealed class StageOutputStore : IStageOutputStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] LineFeed = [(byte)'\n'];
    /// <summary>Default age before a stage of a terminal instance is eligible.
    /// The window covers a retry/recovery that may still reuse committed stages.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);
    /// <summary>Stage and snapshot cleaners own different data but share one
    /// ledger cleanup lease, so execution coordination has a single protocol.</summary>
    public const string CleanupLockName = MaintenanceNames.Cleanup;
    public static readonly TimeSpan OrphanRetention = TimeSpan.FromHours(1);
    private readonly Func<IndustrialStageDbContext> _db;
    private readonly string _root;

    /// <summary>Integrity manifest written atomically after the spool has been
    /// fully written and renamed. It is a sidecar of the spool rather than a
    /// field on DatasetRef so the existing contract record stays compatible:
    /// old refs (no sidecar) are still readable with count-only verification.</summary>
    private sealed class DatasetIntegrity
    {
        // Explicit public ctor: System.Text.Json requires an accessible
        // parameterless constructor even when the containing type is private.
        public DatasetIntegrity() { }

        public string Digest { get; set; } = string.Empty;
        public int RecordCount { get; set; }
    }

    public StageOutputStore(Func<IndustrialStageDbContext> db, string root)
    {
        _db = db;
        _root = root;
    }

    public async ValueTask<T?> TryGetAsync<T>(
        string instanceId, string nodeId, string fingerprint, CancellationToken ct = default)
        where T : class
    {
        await using var db = _db();
        var row = await db.StageOutputs.AsNoTracking().SingleOrDefaultAsync(
            x => x.InstanceId == instanceId && x.NodeId == nodeId, ct);
        if (row is null) return null;
        if (!string.Equals(row.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"stage fingerprint mismatch for {instanceId}/{nodeId}: " +
                $"expected {row.Fingerprint}, got {fingerprint}");
        }
        return JsonSerializer.Deserialize<T>(row.PayloadJson, Json);
    }

    public async ValueTask<T> CommitAsync<T>(
        string instanceId, string nodeId, string fingerprint, T value, CancellationToken ct = default)
        where T : class
    {
        var id = $"stage-{Guid.NewGuid():N}";
        await using var db = _db();
        var existing = await db.StageOutputs.SingleOrDefaultAsync(
            x => x.InstanceId == instanceId && x.NodeId == nodeId, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"stage fingerprint mismatch for {instanceId}/{nodeId}");
            }
            return JsonSerializer.Deserialize<T>(existing.PayloadJson, Json) ?? value;
        }

        db.StageOutputs.Add(new StageOutputEntity
        {
            Id = id,
            InstanceId = instanceId,
            NodeId = nodeId,
            Kind = typeof(T).Name,
            Fingerprint = fingerprint,
            PayloadJson = JsonSerializer.Serialize(value, Json),
            CommittedAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return value;
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            await using var reread = _db();
            var winner = await reread.StageOutputs.AsNoTracking().SingleAsync(
                x => x.InstanceId == instanceId && x.NodeId == nodeId, ct);
            return JsonSerializer.Deserialize<T>(winner.PayloadJson, Json) ?? value;
        }
    }

    public async ValueTask<DatasetRef> SpoolAsync(
        string instanceId, string nodeId, string fingerprint,
        IAsyncEnumerable<RecordEnvelope> records, CancellationToken ct = default)
    {
        var name = Sanitize(nodeId) + "-" + fingerprint[..Math.Min(12, fingerprint.Length)];
        var directory = Path.Combine(_root, "stages", Sanitize(instanceId));
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, name + ".jsonl");
        var partialPath = finalPath + ".partial";
        var sidecarPath = IntegrityPath(finalPath);
        var sidecarPartialPath = sidecarPath + ".partial";
        var count = 0;
        try
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await foreach (var record in records.WithCancellation(ct))
                    {
                        // Hash the exact bytes written, including the LF separator,
                        // so a truncated tail cannot produce a valid-looking digest.
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Json));
                        hash.AppendData(bytes);
                        hash.AppendData(LineFeed);
                        await stream.WriteAsync(bytes, ct);
                        await stream.WriteAsync(LineFeed, ct);
                        count++;
                    }
                    await stream.FlushAsync(ct);
                }

                // Commit the spool first; the digest is only published once the
                // renamed file is complete.
                File.Move(partialPath, finalPath, overwrite: true);

                var integrity = new DatasetIntegrity
                {
                    Digest = Convert.ToHexString(hash.GetHashAndReset()),
                    RecordCount = count,
                };
                await using (var sidecarStream = new FileStream(sidecarPartialPath, FileMode.Create,
                                 FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(sidecarStream, integrity, Json, ct);
                    await sidecarStream.FlushAsync(ct);
                }
                File.Move(sidecarPartialPath, sidecarPath, overwrite: true);
            }
        }
        finally
        {
            // Both partials are transient; a failure before rename must not
            // leave a file that a retry could mistake for a committed spool.
            DeleteIfExists(partialPath);
            DeleteIfExists(sidecarPartialPath);
        }

        var dataset = new DatasetRef(
            $"dataset-{Guid.NewGuid():N}", instanceId, nodeId, fingerprint, finalPath, count, nodeId);
        return await CommitAsync(instanceId, nodeId, fingerprint, dataset, ct);
    }

    /// <summary>Streams the spool, hashing every byte as it is consumed, and
    /// verifies record count plus committed SHA-256 at EOF before the
    /// enumeration completes. A truncated or edited spool therefore fails the
    /// stage instead of letting the sink report partial data as success.
    /// Legacy refs without a sidecar still work but get count-only checking:
    /// an in-place edit that preserves the line count cannot be detected.</summary>
    /// Integrity is verified BEFORE the first record is yielded. This costs a
    /// second sequential read, but prevents a corrupted tail from producing
    /// external sink writes before the failure is discovered.
    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DatasetRef dataset, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var path = dataset.SpoolPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"stage spool for {dataset.NodeId} is missing", path);
        }

        // One handle owns both preflight validation and delivery. This removes
        // the validate/close/reopen window where external mutation could make
        // the delivered bytes differ from the bytes that were verified.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ValidateSpoolAsync(dataset, path, stream, ct);
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 64 * 1024, true);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            RecordEnvelope? record;
            try
            {
                record = JsonSerializer.Deserialize<RecordEnvelope>(line, Json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"stage spool '{path}' line changed after preflight validation", ex);
            }
            if (record is null)
                throw new InvalidDataException(
                    $"stage spool '{path}' line changed after preflight validation");
            yield return record;
        }
    }

    private async ValueTask ValidateSpoolAsync(DatasetRef dataset, CancellationToken ct)
    {
        var path = dataset.SpoolPath;
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"stage spool for {dataset.NodeId} is missing", path);
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ValidateSpoolAsync(dataset, path, stream, ct);
    }

    private async ValueTask ValidateSpoolAsync(
        DatasetRef dataset, string path, FileStream stream, CancellationToken ct)
    {
        var sidecarPath = IntegrityPath(path);
        DatasetIntegrity? integrity = null;
        if (File.Exists(sidecarPath))
        {
            await using var sidecarStream = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            integrity = await JsonSerializer.DeserializeAsync<DatasetIntegrity>(sidecarStream, Json, ct)
                        ?? throw new InvalidDataException($"stage spool integrity manifest is empty: {sidecarPath}");
            if (string.IsNullOrWhiteSpace(integrity.Digest))
            {
                throw new InvalidDataException($"stage spool integrity manifest has no digest: {sidecarPath}");
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        var lineNumber = 0;
        await foreach (var line in ReadLinesAsync(stream, hash, ct))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            RecordEnvelope? record;
            try
            {
                record = JsonSerializer.Deserialize<RecordEnvelope>(line, Json);
            }
            catch (JsonException ex)
            {
                // Never skip a malformed line: that is how a stage silently
                // delivers fewer records than it committed.
                throw new InvalidDataException(
                    $"stage spool '{path}' line {lineNumber} is not valid JSON", ex);
            }
            if (record is null)
            {
                throw new InvalidDataException(
                    $"stage spool '{path}' line {lineNumber} is not a RecordEnvelope");
            }
            count++;
        }

        // EOF reached: every byte, including the last separator, has been hashed.
        var digest = Convert.ToHexString(hash.GetHashAndReset());
        if (integrity is not null)
        {
            if (!string.Equals(digest, integrity.Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"stage spool '{path}' failed its SHA-256 check: " +
                    $"expected {integrity.Digest}, got {digest}");
            }
            if (count != integrity.RecordCount)
            {
                throw new InvalidDataException(
                    $"stage spool '{path}' record count {count} != committed {integrity.RecordCount}");
            }
        }
        else if (count != dataset.RecordCount)
        {
            throw new InvalidDataException(
                $"stage spool '{path}' record count {count} != committed {dataset.RecordCount} " +
                "(legacy ref: count-only verification, no digest available)");
        }
    }

    public async ValueTask<IReadOnlyList<StageOutputView>> ListAsync(
        string instanceId, CancellationToken ct = default)
    {
        await using var db = _db();
        return await db.StageOutputs.AsNoTracking()
            .Where(row => row.InstanceId == instanceId)
            .OrderBy(row => row.NodeId)
            .Select(row => new StageOutputView(
                row.InstanceId, row.NodeId, row.Kind, row.Fingerprint, row.Id))
            .ToListAsync(ct);
    }

    /// <summary>Convenience overload for callers that already have the ledger's
    /// terminal instance ids. A non-terminal id must never be passed: a retry
    /// needs the committed stage to avoid re-reading/re-transforming input.</summary>
    public ValueTask<StageCleanupReport> CleanupAsync(
        IReadOnlySet<string> terminalInstanceIds, TimeSpan retention,
        MaintenanceLease lease, CancellationToken ct = default,
        TimeSpan? orphanRetention = null)
    {
        ArgumentNullException.ThrowIfNull(terminalInstanceIds);
        ArgumentNullException.ThrowIfNull(lease);
        return CleanupAsync(id => terminalInstanceIds.Contains(id), retention, lease,
            ct, orphanRetention);
    }

    /// <summary>Ledger-driven stage lifecycle. The caller must hold the shared
    /// cleanup lease: data deletion is never allowed outside the one execution/
    /// maintenance protocol. A row is removable only when its instance is
    /// terminal AND the commit is older than the window. Spool files are removed
    /// before their DB row; a file failure keeps the row so the next pass can
    /// retry. Missing spools are already-cleaned state and never fail the pass.</summary>
    public async ValueTask<StageCleanupReport> CleanupAsync(
        Func<string, bool> isTerminal, TimeSpan retention, MaintenanceLease lease,
        CancellationToken ct = default, TimeSpan? orphanRetention = null)
    {
        ArgumentNullException.ThrowIfNull(isTerminal);
        ArgumentNullException.ThrowIfNull(lease);
        if (retention < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));

        var at = DateTimeOffset.UtcNow;
        var cutoff = at - retention;
        var stageRoot = Path.GetFullPath(Path.Combine(_root, "stages"));
        await using var db = _db();
        var rows = await db.StageOutputs.AsNoTracking().ToListAsync(ct);
        var stageBytes = rows.Sum(row => SpoolBytes(row, stageRoot));

        var retained = new List<string>();
        var failures = new List<string>();
        var orphanFailures = new List<string>();
        var candidates = 0;
        var rowsDeleted = 0;
        var filesDeleted = 0;
        var deletedBytes = 0L;
        var orphanCount = 0;
        var orphanBytes = 0L;
        var leaseLost = false;

        foreach (var row in rows)
        {
            if (lease is not null && !await lease.EnsureValidAsync(ct))
            {
                leaseLost = true;
                retained.Add("cleanup lease was lost or an execution lease appeared");
                break;
            }
            var label = $"{row.InstanceId}/{row.NodeId}";
            if (row.CommittedAt > cutoff)
            {
                retained.Add($"{label}: commit is inside the retention window");
                continue;
            }
            if (!isTerminal(row.InstanceId))
            {
                retained.Add($"{label}: instance is not terminal");
                continue;
            }

            var hasSpool = string.Equals(row.Kind, nameof(DatasetRef), StringComparison.Ordinal);
            DatasetRef? dataset = null;
            if (hasSpool)
            {
                try
                {
                    dataset = JsonSerializer.Deserialize<DatasetRef>(row.PayloadJson, Json);
                }
                catch (JsonException ex)
                {
                    failures.Add($"{label}: stage ref is not valid JSON: {ex.Message}");
                    continue;
                }
                if (dataset is null || !TryStagePath(row, dataset, stageRoot, out var spool))
                {
                    failures.Add($"{label}: stage ref does not point inside its own stage directory");
                    continue;
                }
                candidates++;
                if (!DeleteSpool(spool, failures, ref filesDeleted, ref deletedBytes)) continue;
            }
            else
            {
                candidates++;
            }

            try
            {
                // Separate context per row: a failed DELETE must not leave a
                // tracked deleted entity that poisons the remaining rows.
                await using var deleteDb = _db();
                deleteDb.StageOutputs.Remove(new StageOutputEntity { Id = row.Id });
                await deleteDb.SaveChangesAsync(ct);
                rowsDeleted++;
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException)
            {
                failures.Add($"{label}: stage row could not be deleted: {ex.Message}");
            }
        }

        // Crash orphans: a formal spool/sidecar may exist before CommitAsync,
        // and a hard exit can leave .partial files. Remove them only after a
        // safety window, and only inside this store's own stage root.
        if (lease is null || await lease!.EnsureValidAsync(ct))
        {
            var referenced = rows
                .Where(row => string.Equals(row.Kind, nameof(DatasetRef), StringComparison.Ordinal))
                .Select(row =>
                {
                    try
                    {
                        var dataset = JsonSerializer.Deserialize<DatasetRef>(row.PayloadJson, Json);
                        return dataset is not null &&
                               TryStagePath(row, dataset, stageRoot, out var path)
                            ? [path, IntegrityPath(path)]
                            : Array.Empty<string>();
                    }
                    catch (JsonException) { return Array.Empty<string>(); }
                })
                .SelectMany(paths => paths)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stageDirectory = Directory.Exists(stageRoot)
                ? Directory.GetFiles(stageRoot, "*", SearchOption.AllDirectories).ToArray()
                : [];
            foreach (var path in stageDirectory)
            {
                if (referenced.Contains(Path.GetFullPath(path))) continue;
                if (DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) < (orphanRetention ?? OrphanRetention)) continue;
                if (lease is not null && !await lease.EnsureValidAsync(ct))
                {
                    leaseLost = true;
                    break;
                }
                var size = SafeLength(path);
                try
                {
                    File.Delete(path);
                    orphanCount++;
                    orphanBytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    orphanFailures.Add($"{path}: {ex.Message}");
                }
            }
        }
        else
        {
            leaseLost = true;
        }

        return new StageCleanupReport(rows.Count, stageBytes, candidates, rowsDeleted,
            filesDeleted, deletedBytes, retained, failures, false, at,
            orphanCount, orphanBytes, orphanFailures, leaseLost);
    }

    private static string IntegrityPath(string spoolPath) => spoolPath + ".integrity.json";

    private static long SpoolBytes(StageOutputEntity row, string stageRoot)
    {
        if (!string.Equals(row.Kind, nameof(DatasetRef), StringComparison.Ordinal)) return 0L;
        DatasetRef? dataset;
        try { dataset = JsonSerializer.Deserialize<DatasetRef>(row.PayloadJson, Json); }
        catch (JsonException) { return 0L; }
        return dataset is not null && TryStagePath(row, dataset, stageRoot, out var path)
            ? SafeLength(path) + SafeLength(IntegrityPath(path))
            : 0L;
    }

    private static bool TryStagePath(
        StageOutputEntity row, DatasetRef dataset, string stageRoot, out string path)
    {
        path = string.Empty;
        if (!string.Equals(dataset.InstanceId, row.InstanceId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(dataset.SpoolPath) ||
            !string.Equals(Path.GetExtension(dataset.SpoolPath), ".jsonl",
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return false;
        }
        var fullPath = Path.GetFullPath(dataset.SpoolPath);
        var expectedRoot = Path.Combine(stageRoot, Sanitize(row.InstanceId));
        if (!IsUnderRoot(fullPath, expectedRoot)) return false;
        path = fullPath;
        return true;
    }

    private static bool DeleteSpool(
        string path, ICollection<string> failures, ref int filesDeleted, ref long deletedBytes)
    {
        try
        {
            var sidecar = IntegrityPath(path);
            foreach (var candidate in new[] { path, sidecar, path + ".partial", sidecar + ".partial" })
            {
                if (!File.Exists(candidate)) continue;
                deletedBytes += SafeLength(candidate);
                File.Delete(candidate);
                filesDeleted++;
            }
            PruneEmptyDirectory(Path.GetDirectoryName(path), Path.GetDirectoryName(Path.GetDirectoryName(path)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{path}: {ex.Message}");
            return false;
        }
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0L; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0L; }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(
            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }

    private static void PruneEmptyDirectory(string? directory, string? stageRoot)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stageRoot)) return;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var stage = Path.GetFullPath(stageRoot);
        while (true)
        {
            var full = Path.GetFullPath(directory);
            if (string.Equals(full, stage, comparison) || !IsUnderRoot(full, stage)) return;
            if (!Directory.Exists(full)) return;
            if (Directory.EnumerateFileSystemEntries(full).Any()) return;
            Directory.Delete(full);
            directory = Path.GetDirectoryName(full);
            if (directory is null) return;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Line reader over raw bytes so the hash covers the exact file
    /// content (no StreamReader buffering can skip bytes). Memory stays bounded
    /// by a single line; the dataset is never loaded in full.</summary>
    private static async IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream, IncrementalHash hash, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = new byte[64 * 1024];
        var line = new List<byte>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                if (line.Count > 0) yield return Encoding.UTF8.GetString(line.ToArray());
                yield break;
            }

            hash.AppendData(buffer.AsSpan(0, read));
            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value == (byte)'\n')
                {
                    yield return Encoding.UTF8.GetString(line.ToArray());
                    line.Clear();
                }
                else
                {
                    line.Add(value);
                }
            }
        }
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? "stage" : new string(chars);
    }
}
