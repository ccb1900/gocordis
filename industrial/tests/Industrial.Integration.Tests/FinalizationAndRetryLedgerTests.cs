using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Run outcomes must be derived from what the run actually delivered,
/// and a failed delivery stays retryable after a re-plan.</summary>
public sealed class FinalizationAndRetryLedgerTests
{
    [Fact]
    public async Task ContestedDeliveryFinalizesAsFailedNotSucceeded()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await fixture.SeedDeliveryAsync(run.Id);
        await fixture.Ledger.MarkDeliveryContestedAsync(run.Id, delivery.Id, "held by another run");

        var finalized = await fixture.Ledger.FinalizeRunAsync(run.Id);

        Assert.Equal(CollectionStatus.Failed, finalized.Status);
        Assert.Contains("held by another run", finalized.Error, StringComparison.Ordinal);
        Assert.NotNull(finalized.FinishedAt);
    }

    [Fact]
    public async Task UnfinishedClaimFinalizesAsInterrupted()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await fixture.SeedDeliveryAsync(run.Id);
        Assert.NotNull(await fixture.Ledger.TryClaimDeliveryAsync(
            run.Id, delivery.Id, TimeSpan.FromMinutes(1)));

        var finalized = await fixture.Ledger.FinalizeRunAsync(run.Id);

        Assert.Equal(CollectionStatus.Interrupted, finalized.Status);
        Assert.Null(finalized.FinishedAt);
    }

    [Fact]
    public async Task DiscoveredFilesWithoutDeliveryDoNotReportSuccess()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        await fixture.DiscoverAsync(run.Id);

        var finalized = await fixture.Ledger.FinalizeRunAsync(run.Id);

        Assert.Equal(CollectionStatus.Failed, finalized.Status);
        Assert.Contains("no delivery", finalized.Error, StringComparison.OrdinalIgnoreCase);

        var empty = await fixture.Ledger.StartAsync("empty");
        await fixture.Ledger.BeginRunAsync(empty.Id);
        Assert.Equal(CollectionStatus.Succeeded, (await fixture.Ledger.FinalizeRunAsync(empty.Id)).Status);
    }

    [Fact]
    public async Task LegacyPendingDeliveryIsNotCountedAsSuccess()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var file = await fixture.DiscoverAsync(run.Id);
        // No run-scoped delivery row: exercise the legacy derivation.
        await fixture.Ledger.StartDeliveryAsync(file.Id, "target", "legacy-key");

        var finalized = await fixture.Ledger.FinalizeRunAsync(run.Id);

        Assert.Equal(CollectionStatus.Interrupted, finalized.Status);
        Assert.Null(finalized.FinishedAt);
    }

    [Fact]
    public async Task FailedDeliveryCanBeClaimedAgainAfterThreeAttempts()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await fixture.SeedDeliveryAsync(run.Id);
        for (var expected = 1; expected <= 3; expected++)
        {
            var claim = await fixture.Ledger.TryClaimDeliveryAsync(
                run.Id, delivery.Id, TimeSpan.FromMinutes(1));
            Assert.NotNull(claim);
            Assert.Equal(expected, claim!.Attempts);
            await fixture.Ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, "target offline");
        }

        Assert.Contains(await fixture.Ledger.GetRetriableDeliveriesAsync(),
            row => row.Id == delivery.Id);
        var fourth = await fixture.Ledger.TryClaimDeliveryAsync(
            run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        Assert.NotNull(fourth);
        Assert.Equal(4, fourth!.Attempts);

        // Removing the count cap does not weaken token fencing.
        var stale = await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, "stale-token", 1);
        Assert.False(stale.Accepted);
        Assert.True((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, fourth.ClaimToken, 1)).Accepted);
    }

    [Fact]
    public async Task ClaimDateReturnsTheRealAttemptCountFromTheRow()
    {
        using var fixture = new Fixture();
        var date = new DateOnly(2026, 1, 2);
        var first = await fixture.Ledger.ClaimDateAsync(
            "binding", date, "run-1", TimeSpan.FromMinutes(5));
        Assert.NotNull(first);
        Assert.Equal(1, first!.Attempts);
        Assert.Equal("run-1", first.LastRunId);

        await fixture.Ledger.CompleteDateAsync(
            first.Id, first.LeaseToken!, CollectionDateStatus.Failed, 0, "boom");
        var second = await fixture.Ledger.ClaimDateAsync(
            "binding", date, "run-2", TimeSpan.FromMinutes(5));
        Assert.NotNull(second);
        Assert.Equal(2, second!.Attempts);
        Assert.Equal("run-2", second.LastRunId);
        Assert.Null(second.Error);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-finalize-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            SourcePath = Path.Combine(Root, "recent.csv");
            File.WriteAllText(SourcePath, "record_id,reading\n1,42\n");
            LedgerPath = Path.Combine(Root, "ledger.db");
            using var bootstrap = NewDb();
            Ledger = new CollectionLedger(NewDb);
        }

        public string Root { get; }
        public string SourcePath { get; }
        public string LedgerPath { get; }
        public CollectionLedger Ledger { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);

        public async ValueTask<CollectionFile> DiscoverAsync(string runId)
        {
            var identity = FileIdentityFactory.Create(SourcePath, Root, "source-1");
            return await Ledger.DiscoverAsync(runId,
                new DiscoveredFile(identity, SourcePath, DateTimeOffset.UtcNow, "source-1"));
        }

        public async ValueTask<FileDelivery> SeedDeliveryAsync(string runId)
        {
            var file = await DiscoverAsync(runId);
            return await Ledger.StartDeliveryAsync(file.Id, "target", "key", "record", runId);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
