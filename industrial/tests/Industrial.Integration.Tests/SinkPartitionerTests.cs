using Industrial.Contracts;
using Industrial.Elsa;

namespace Industrial.Tests;

/// <summary>Partitioner-level evidence for bounded memory and clean failure
/// handling, without needing a full Elsa workflow.</summary>
public sealed class SinkPartitionerTests
{
    [Fact]
    public async Task MultipleSourcesArePartitionedWithBoundedBackpressure()
    {
        const int perSource = 400;
        var produced = 0;
        var consumed = 0;
        var maxGap = 0;
        async IAsyncEnumerable<RecordEnvelope> Source()
        {
            for (var source = 0; source < 4; source++)
            {
                var identity = Identity(source);
                for (var i = 0; i < perSource; i++)
                {
                    yield return Envelope(identity, i);
                    produced++;
                    var gap = produced - consumed;
                    if (gap > maxGap) maxGap = gap;
                    // The producer yields one record at a time; a bounded
                    // channel cannot accumulate more than its capacity.
                    if (gap > SinkRecordPartitioner.Capacity * 4)
                        throw new InvalidOperationException("unbounded partition backlog");
                }
            }
            await ValueTask.CompletedTask;
        }
        var datasets = await SinkRecordPartitioner.PartitionAsync(
            Source(),
            async (_, stream, ct) =>
            {
                await foreach (var record in stream.WithCancellation(ct))
                {
                    GC.KeepAlive(record);
                    await Task.Delay(1, ct); // slow disk
                    Interlocked.Increment(ref consumed);
                }
                return Dataset("sink");
            });
        Assert.Equal(4, datasets.Count);
        Assert.Equal(perSource * 4, consumed);
        Assert.True(maxGap <= SinkRecordPartitioner.Capacity * 4 + 128,
            $"observed backlog {maxGap}");
    }

    [Fact]
    public async Task SpoolFailureSurfacesAndEveryWriterIsCompleted()
    {
        var completed = 0;
        await Assert.ThrowsAsync<AggregateException>(async () =>
            await SinkRecordPartitioner.PartitionAsync(
                Records(2, 50),
                async (sourceKey, stream, ct) =>
                {
                    if (SourceIndex(sourceKey) == 1) throw new IOException("spool unavailable");
                    await foreach (var _ in stream.WithCancellation(ct)) { }
                    Interlocked.Increment(ref completed);
                    return Dataset(sourceKey);
                }));
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task UpstreamFailureStillDrainsSpoolTasks()
    {
        var drained = 0;
        await Assert.ThrowsAsync<IOException>(async () =>
            await SinkRecordPartitioner.PartitionAsync(
                Records(1, 40, failure: new IOException("upstream unavailable"), after: 20),
                async (sourceKey, stream, ct) =>
                {
                    await foreach (var _ in stream.WithCancellation(ct)) drained++;
                    return Dataset(sourceKey);
                }));
        Assert.Equal(20, drained);
    }

    [Fact]
    public async Task CancellationStopsPartitioningAndDrainsSpoolTasks()
    {
        using var cts = new CancellationTokenSource();
        var drained = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await SinkRecordPartitioner.PartitionAsync(
                Records(1, 10_000, cancel: cts, after: 25),
                async (sourceKey, stream, ct) =>
                {
                    await foreach (var _ in stream.WithCancellation(ct)) drained++;
                    return Dataset(sourceKey);
                }, cts.Token));
        Assert.True(drained > 0 && drained < 10_000);
    }

    private static int SourceIndex(string key) => int.Parse(key.Split('|')[0]["source-".Length..], System.Globalization.CultureInfo.InvariantCulture);

    private static FileIdentity Identity(int source) => new(
        $"source-{source}|recent.csv|1|HASH", "recent.csv", 1,
        DateTimeOffset.UtcNow, "HASH", $"source-{source}");

    private static RecordEnvelope Envelope(FileIdentity identity, int index) => new(
        new Dictionary<string, object?> { ["record_id"] = $"source-{index}" },
        identity, DateTimeOffset.UtcNow);

    private static DatasetRef Dataset(string sourceKey) => new(
        $"dataset-{sourceKey}", "instance", "sink", "fingerprint",
        Path.Combine(Path.GetTempPath(), $"{sourceKey}.jsonl"), 0, "sink");

    private static async IAsyncEnumerable<RecordEnvelope> Records(
        int sources, int perSource, Exception? failure = null, int? after = null,
        CancellationTokenSource? cancel = null)
    {
        var emitted = 0;
        for (var source = 0; source < sources; source++)
        {
            var identity = Identity(source);
            for (var i = 0; i < perSource; i++)
            {
                if (after.HasValue && emitted == after.Value)
                {
                    if (failure is not null) throw failure;
                    cancel?.Cancel();
                }
                emitted++;
                yield return Envelope(identity, i);
            }
        }
        await ValueTask.CompletedTask;
    }
}
