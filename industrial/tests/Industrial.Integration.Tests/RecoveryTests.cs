using System.Text;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Reliability gates: a partially failed delivery and a large file
/// must both converge to exact, duplicate-free business data.</summary>
public sealed class RecoveryTests
{
    [Fact]
    public async Task MidStreamFailureIsRetriedWithoutDuplicateRows()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var reader = new CsvRecordReader();
        var flaky = new DelegateSink("target", async (_, records, _, ct) =>
        {
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct))
            {
                count++;
                if (count == 2) throw new IOException("mid-stream disconnect");
            }
            return new DeliveryResultRef("partial", "target", count);
        });
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, reader);

        await coordinator.RunAsync(
            "workflow", "source-1", fixture.Range, reader, [flaky], null);
        var failed = await fixture.Ledger.GetRetriableDeliveriesAsync();
        Assert.Single(failed);

        var recovered = new SqliteRecordSink(fixture.NewTargetDb, "target");
        Assert.Equal(1, await coordinator.RetryFailedAsync([recovered], null));
        await using var target = fixture.NewTargetDb();
        Assert.Equal(2, await target.Records.AsNoTracking().CountAsync());
        Assert.Equal(2, target.Records.AsNoTracking().Select(row => row.RecordId).Distinct().Count());
    }

    [Fact]
    public async Task LargeFileCountsAreExactAndReplayDoesNotDuplicate()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync();
        var path = Path.Combine(fixture.SourceRoot, "20261125-large.csv");
        var builder = new StringBuilder("record_id,reading\n");
        for (var i = 0; i < 20_000; i++) builder.Append(i).Append(',').Append(i).Append('\n');
        await File.WriteAllTextAsync(path, builder.ToString());
        File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var reader = new CsvRecordReader();
        var sink = new SqliteRecordSink(fixture.NewTargetDb);
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, reader);
        await coordinator.RunAsync("workflow", "source-1", fixture.Range, reader, [sink], null);
        await coordinator.RunAsync("workflow", "source-1", fixture.Range, reader, [sink], null);

        await using var target = fixture.NewTargetDb();
        // The fixture also contains the small 20261125.csv, so two additional
        // source-scoped identities are expected; replay must not duplicate any.
        Assert.Equal(20_002, await target.Records.AsNoTracking().CountAsync());
        Assert.Equal(20_002, await target.Records.AsNoTracking()
            .Select(row => row.RecordId).Distinct().CountAsync());
    }

    [Fact]
    public async Task ReusingAStagedSourceAfterOverwriteIsRejected()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.SourceRoot, "20261125.csv");
        var staged = FileIdentityFactory.Create(path, fixture.SourceRoot, "source-1");
        var refFile = new BatchFileRef(
            staged.Key, "source-1", staged.Key, staged.RelativePath,
            staged.ContentHash, fixture.Range.FromInclusive, staged.Length, staged.LastWriteUtc);

        await File.WriteAllTextAsync(path, "record_id,reading\n1,999\n");
        var changed = FileIdentityFactory.Create(path, fixture.SourceRoot, "source-1");
        var error = Assert.Throws<InvalidOperationException>(() => StagedFileIdentity.Verify(refFile, changed));
        Assert.Contains("changed on disk", error.Message, StringComparison.Ordinal);
    }

    private sealed class DelegateSink : IRecordSink
    {
        private readonly Func<string, IAsyncEnumerable<RecordEnvelope>, string, CancellationToken,
            ValueTask<DeliveryResultRef>> _write;

        public DelegateSink(string targetId, Func<string, IAsyncEnumerable<RecordEnvelope>, string,
            CancellationToken, ValueTask<DeliveryResultRef>> write)
        {
            TargetId = targetId;
            _write = write;
        }

        public string TargetId { get; }

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            _write(collectionFileId, records, idempotencyKey, ct);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-recovery-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            Directory.CreateDirectory(SourceRoot);
            File.WriteAllText(Path.Combine(SourceRoot, "20261125.csv"), "record_id,reading\n1,1\n2,2\n");
            File.SetLastWriteTimeUtc(Path.Combine(SourceRoot, "20261125.csv"),
                DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
            LedgerPath = Path.Combine(Root, "ledger.db");
            TargetPath = Path.Combine(Root, "target.db");
            Resources = new ResourceRegistry(NewDb);
            Ledger = new CollectionLedger(NewDb);
            Source = new LocalFileSource(Resources);
            Range = new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string LedgerPath { get; }
        public string TargetPath { get; }
        public SourceDateRange Range { get; }
        public ResourceRegistry Resources { get; }
        public CollectionLedger Ledger { get; }
        public LocalFileSource Source { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);
        public IndustrialTargetDbContext NewTargetDb() => IndustrialStore.CreateTarget(TargetPath);

        public Task RegisterResourceAsync() => Resources.RegisterAsync(new ResourceDefinition(
            "source-1", "filesystem.local", "Source", "local",
            new Dictionary<string, object?> { ["root"] = SourceRoot },
            null, 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).AsTask();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
