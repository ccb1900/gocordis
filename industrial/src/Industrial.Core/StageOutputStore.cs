using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Core;

/// <summary>Cordis-owned data plane. Elsa keeps control flow; this store keeps
/// stage outputs as small refs plus an NDJSON spool so a resumed Activity never
/// needs to re-read or re-transform a completed stage.</summary>
public sealed class StageOutputStore : IStageOutputStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<IndustrialStageDbContext> _db;
    private readonly string _root;

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
        var count = 0;
        try
        {
            await using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await foreach (var record in records.WithCancellation(ct))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, Json));
                    count++;
                }
                await writer.FlushAsync(ct);
            }
            File.Move(partialPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }

        var dataset = new DatasetRef(
            $"dataset-{Guid.NewGuid():N}", instanceId, nodeId, fingerprint, finalPath, count, nodeId);
        return await CommitAsync(instanceId, nodeId, fingerprint, dataset, ct);
    }

    public async IAsyncEnumerable<RecordEnvelope> ReadAsync(
        DatasetRef dataset, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(dataset.SpoolPath))
        {
            throw new FileNotFoundException($"stage spool for {dataset.NodeId} is missing", dataset.SpoolPath);
        }
        await using var stream = new FileStream(dataset.SpoolPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<RecordEnvelope>(line, Json);
            if (record is not null) yield return record;
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

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? "stage" : new string(chars);
    }
}
