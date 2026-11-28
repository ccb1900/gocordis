using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

public sealed class DeliveryCancellationTests
{
    [Fact]
    public async Task CancellationInsideADeliveryReleasesTheClaimAndStillThrows()
    {
        using var fixture = new Fixture();
        await fixture.RegisterAsync();
        using var cts = new CancellationTokenSource();
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.RunAsync(
                "workflow", "source-1", new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
                new CsvRecordReader(), [new CancellingSink(cts)], null, cts.Token));

        await using var db = fixture.NewDb();
        var delivery = Assert.Single(await db.FileDeliveries.AsNoTracking().ToListAsync());
        Assert.Equal(CollectionStatus.Failed, delivery.Status);
        Assert.Null(delivery.ClaimToken);
        Assert.Equal(0, delivery.LeaseExpiresAtUnixMs);
        var run = Assert.Single(await db.CollectionRuns.AsNoTracking().ToListAsync());
        Assert.Equal(CollectionStatus.Cancelled, run.Status);
    }

    private sealed class CancellingSink(CancellationTokenSource cts) : IRecordSink
    {
        public string TargetId => "target";

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            cts.Cancel();
            await foreach (var _ in records.WithCancellation(ct)) { }
            return new DeliveryResultRef(idempotencyKey, TargetId, 0);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "20260101.csv"), "record_id,reading\n1,42\n");
            LedgerPath = Path.Combine(Root, "ledger.db");
            using var bootstrap = NewDb();
            Resources = new ResourceRegistry(NewDb);
            Ledger = new CollectionLedger(NewDb);
            Source = new LocalFileSource(Resources);
        }

        public string Root { get; }
        public string LedgerPath { get; }
        public IResourceRegistry Resources { get; }
        public CollectionLedger Ledger { get; }
        public IFileSource Source { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);

        public Task RegisterAsync() => Resources.RegisterAsync(new ResourceDefinition(
            "source-1", "filesystem.local", "source-1", "local",
            new Dictionary<string, object?> { ["root"] = Root }, null, 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).AsTask();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
