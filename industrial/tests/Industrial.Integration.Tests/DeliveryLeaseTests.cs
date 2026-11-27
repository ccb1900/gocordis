using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Phase 1 gates: delivery claims are atomic, fenced by token/lease,
/// and run status is derived from run-scoped delivery outcomes.</summary>
public sealed class DeliveryLeaseTests
{
    [Fact]
    public async Task ConcurrentClaimsProduceExactlyOneValidToken()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);

        var a = new CollectionLedger(fixture.NewDb);
        var b = new CollectionLedger(fixture.NewDb);
        var winnerA = await a.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        var winnerB = await b.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));

        Assert.True((winnerA is null) != (winnerB is null));
        var winner = winnerA ?? winnerB;
        Assert.NotNull(winner);
        await using var db = fixture.NewDb();
        var row = await db.FileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(CollectionStatus.InProgress, row.Status);
        Assert.Equal(winner!.ClaimToken, row.ClaimToken);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task CompleteAndFailRequireMatchingTokenAndInProgressState()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var claim = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        Assert.NotNull(claim);

        Assert.False((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, "wrong-token", 1)).Accepted);
        Assert.False((await fixture.Ledger.FailDeliveryAsync(delivery.Id, "wrong-token", "bad")).Accepted);
        Assert.True((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, claim!.ClaimToken, 3)).Accepted);
        Assert.False((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, claim.ClaimToken, 3)).Accepted);
        Assert.False((await fixture.Ledger.FailDeliveryAsync(delivery.Id, claim.ClaimToken, "late")).Accepted);

        await using var db = fixture.NewDb();
        var row = await db.FileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(CollectionStatus.Succeeded, row.Status);
        Assert.Equal(3, row.Records);
        Assert.Null(row.ClaimToken);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task ExpiredLeaseIsReclaimableAndOldTokenIsRejected()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var first = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(first);

        await using (var db = fixture.NewDb())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE FileDeliveries SET LeaseExpiresAtUnixMs = 1 WHERE Id = {0}", delivery.Id);
        }

        var second = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        Assert.NotNull(second);
        Assert.NotEqual(first!.ClaimToken, second!.ClaimToken);
        Assert.False((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, first.ClaimToken, 1)).Accepted);
        Assert.True((await fixture.Ledger.CompleteDeliveryAsync(delivery.Id, second.ClaimToken, 1)).Accepted);

        await using var finalDb = fixture.NewDb();
        Assert.Equal(2, await finalDb.FileDeliveries.AsNoTracking()
            .Where(x => x.Id == delivery.Id).Select(x => x.Attempts).SingleAsync());
    }

    [Fact]
    public async Task RecoverExpiredLeasesMarksDeliveryAndRunInterrupted()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var claim = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(claim);

        await using (var db = fixture.NewDb())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE FileDeliveries SET LeaseExpiresAtUnixMs = 1 WHERE Id = {0}", delivery.Id);
        }

        Assert.Equal(1, await fixture.Ledger.RecoverExpiredLeasesAsync(DateTimeOffset.UtcNow));
        var recovered = await fixture.Ledger.GetRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Interrupted, recovered!.Status);
        Assert.Null(recovered.FinishedAt);
        await using var db2 = fixture.NewDb();
        var row = await db2.FileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(CollectionStatus.Interrupted, row.Status);
        Assert.Contains("lease expired", row.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunStateMachineIsGuardedAndDerivesStatusFromItsOwnDeliveries()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        Assert.Equal(CollectionStatus.Created, run.Status);
        run = await fixture.Ledger.BeginRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Running, run.Status);

        var firstDelivery = await SeedDeliveryAsync(fixture, run.Id);
        var claim = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, firstDelivery.Id, TimeSpan.FromMinutes(1));
        await fixture.Ledger.CompleteDeliveryAsync(firstDelivery.Id, claim!.ClaimToken, 1);

        var finalized = await fixture.Ledger.FinalizeRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Succeeded, finalized.Status);
        Assert.NotNull(finalized.FinishedAt);
        var replay = await fixture.Ledger.FinalizeRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Succeeded, replay.Status);
        var illegal = await fixture.Ledger.BeginRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Succeeded, illegal.Status);

        var laterRun = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(laterRun.Id);
        var skipped = await fixture.Ledger.StartDeliveryAsync(
            firstDelivery.CollectionFileId, firstDelivery.TargetId,
            firstDelivery.IdempotencyKey, "record", laterRun.Id);
        Assert.Equal(CollectionStatus.Succeeded, skipped.Status);
        Assert.Equal(CollectionStatus.Succeeded, (await fixture.Ledger.FinalizeRunAsync(laterRun.Id)).Status);
        Assert.Equal(CollectionStatus.Succeeded, (await fixture.Ledger.GetRunAsync(run.Id))!.Status);
    }

    [Fact]
    public async Task ScheduleFireIsClaimedAtMostOnceAcrossRestarts()
    {
        using var fixture = new Fixture();
        var due = DateTimeOffset.UtcNow.AddMinutes(-1);
        var restarted = new CollectionLedger(fixture.NewDb);

        Assert.True(await fixture.Ledger.TryClaimScheduleFireAsync("flow", 1, due));
        Assert.False(await fixture.Ledger.TryClaimScheduleFireAsync("flow", 1, due));
        Assert.False(await restarted.TryClaimScheduleFireAsync("flow", 1, due));
        Assert.True(await restarted.TryClaimScheduleFireAsync("flow", 1, due.AddMinutes(1)));
    }

    [Fact]
    public async Task ScheduleFireLifecycleSeparatesClaimCompletionAndRecovery()
    {
        using var fixture = new Fixture();
        var due = DateTimeOffset.UtcNow.AddMinutes(-1);
        var second = due.AddMinutes(-1);
        await fixture.Ledger.EnsureScheduleFiresAsync("flow", 1, new[] { due, second });
        await fixture.Ledger.EnsureScheduleFiresAsync("flow", 1, new[] { due, second });
        var fires = await fixture.Ledger.ListScheduleFiresAsync();
        Assert.Equal(2, fires.Count);
        Assert.All(fires, fire => Assert.Equal(ScheduleFireStatus.Pending, fire.Status));

        var claim = await fixture.Ledger.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5));
        Assert.NotNull(claim);
        Assert.Equal(1, claim!.Attempts);
        Assert.Null(await fixture.Ledger.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5)));

        // A stale token cannot turn another host's running fire terminal.
        await fixture.Ledger.CompleteScheduleFireAsync(
            claim.Key, "stale-token", ScheduleFireStatus.Succeeded, null);
        Assert.Equal(ScheduleFireStatus.Running,
            (await fixture.Ledger.ListScheduleFiresAsync()).Single(fire => fire.Key == claim.Key).Status);

        await fixture.Ledger.CompleteScheduleFireAsync(
            claim.Key, claim.Token, ScheduleFireStatus.Succeeded, null);
        Assert.Equal(ScheduleFireStatus.Succeeded,
            (await fixture.Ledger.ListScheduleFiresAsync()).Single(fire => fire.Key == claim.Key).Status);
        Assert.Null(await fixture.Ledger.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task RunningScheduleFireIsFailedOnRestartAndRetriedWithinAttemptLimit()
    {
        using var fixture = new Fixture();
        var due = DateTimeOffset.UtcNow.AddMinutes(-1);
        var claim = await fixture.Ledger.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5));
        Assert.NotNull(claim);

        var restarted = new CollectionLedger(fixture.NewDb);
        Assert.Equal(1, await restarted.AbandonScheduleFiresAsync());
        var fire = (await restarted.ListScheduleFiresAsync()).Single();
        Assert.Equal(ScheduleFireStatus.Failed, fire.Status);
        Assert.Contains("restarted", fire.Error, StringComparison.Ordinal);

        var retry = await restarted.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5));
        Assert.NotNull(retry);
        Assert.Equal(2, retry!.Attempts);
        await restarted.CompleteScheduleFireAsync(retry.Key, retry.Token, ScheduleFireStatus.Failed, "boom");

        var last = await restarted.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5));
        Assert.NotNull(last);
        Assert.Equal(3, last!.Attempts);
        await restarted.CompleteScheduleFireAsync(last.Key, last.Token, ScheduleFireStatus.Failed, "boom");
        Assert.Null(await restarted.TryStartScheduleFireAsync("flow", 1, due, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ScheduleFireRecoveryPredicateIsScopedToItsOwnWorkflowAndVersion()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs);
        var foreign = new ScheduleFireView(
            "foreign", "other-flow", 2, now, ScheduleFireStatus.Running, 1, null, null, 0);
        var mine = new ScheduleFireView(
            "mine", "my-flow", 1, now, ScheduleFireStatus.Running, 1, null, null, 0);
        Assert.False(foreign.IsRecoverable("my-flow", 1, nowMs));
        Assert.True(mine.IsRecoverable("my-flow", 1, nowMs));
        Assert.True(mine.IsRecoverable("my-flow", 1, nowMs - 1)); // lease expired
        Assert.False(new ScheduleFireView("active", "my-flow", 1, now,
            ScheduleFireStatus.Running, 1, null, null, nowMs + 60_000)
            .IsRecoverable("my-flow", 1, nowMs));
    }

    [Fact]
    public async Task ScheduleWindowCommitsFiresAndCheckpointAtomically()
    {
        using var fixture = new Fixture();
        var checkpoint = DateTimeOffset.UtcNow.AddMinutes(-2);
        var due = new[] { checkpoint.AddMinutes(1) };
        Assert.True(await fixture.Ledger.CommitScheduleWindowAsync(
            "flow", 1, null, checkpoint, due, 0, false));
        Assert.False(await fixture.Ledger.CommitScheduleWindowAsync(
            "flow", 1, null, checkpoint.AddMinutes(1), due, 0, false),
            "a stale checkpoint must not be allowed to advance");
        var fire = Assert.Single(await fixture.Ledger.ListScheduleFiresAsync());
        Assert.Equal(due[0].ToUniversalTime(), fire.DueAtUtc.ToUniversalTime());
        var (stored, skipped, truncated) = await fixture.Ledger.GetScheduleCheckpointAsync("flow", 1);
        Assert.Equal(checkpoint.ToUniversalTime(), stored!.Value.ToUniversalTime());
        Assert.Equal(0, skipped);
        Assert.False(truncated);
    }

    [Fact]
    public async Task ManyIndependentWritersClaimTheSameDeliveryExactlyOnce()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            var ledger = new CollectionLedger(fixture.NewDb);
            return await ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        })).ToArray();
        gate.SetResult();
        var claims = await Task.WhenAll(attempts);

        Assert.Single(claims.Where(claim => claim is not null).ToArray());
        await using var db = fixture.NewDb();
        var row = await db.FileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(CollectionStatus.InProgress, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(claims.Single(claim => claim is not null)!.ClaimToken, row.ClaimToken);
    }

    [Fact]
    public async Task LeaseRenewalExtendsOnlyTheCurrentClaim()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var claim = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMilliseconds(1));
        Assert.NotNull(claim);

        await using (var db = fixture.NewDb())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE FileDeliveries SET LeaseExpiresAtUnixMs = 1 WHERE Id = {0}", delivery.Id);
        }

        Assert.True(await fixture.Ledger.RenewDeliveryLeaseAsync(
            delivery.Id, claim!.ClaimToken, TimeSpan.FromMinutes(2)));
        Assert.False(await fixture.Ledger.RenewDeliveryLeaseAsync(
            delivery.Id, "wrong-token", TimeSpan.FromMinutes(2)));
        Assert.True((await fixture.Ledger.CompleteDeliveryAsync(
            delivery.Id, claim.ClaimToken, 1)).Accepted);
        Assert.False(await fixture.Ledger.RenewDeliveryLeaseAsync(
            delivery.Id, claim.ClaimToken, TimeSpan.FromMinutes(2)));

        await using var final = fixture.NewDb();
        var row = await final.FileDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.True(row.LeaseExpiresAtUnixMs > DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() ||
                    row.Status == CollectionStatus.Succeeded);
    }

    [Fact]
    public async Task StartupRecoveryReleasesEvenAnUnexpiredAbandonedClaim()
    {
        using var fixture = new Fixture();
        var run = await fixture.Ledger.StartAsync("workflow");
        await fixture.Ledger.BeginRunAsync(run.Id);
        var delivery = await SeedDeliveryAsync(fixture, run.Id);
        var claim = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(30));
        Assert.NotNull(claim);

        Assert.Equal(1, await fixture.Ledger.RecoverAbandonedLeasesAsync());
        var recovered = await fixture.Ledger.GetRunAsync(run.Id);
        Assert.Equal(CollectionStatus.Interrupted, recovered!.Status);
        var retry = await fixture.Ledger.TryClaimDeliveryAsync(run.Id, delivery.Id, TimeSpan.FromMinutes(1));
        Assert.NotNull(retry);
        Assert.NotEqual(claim!.ClaimToken, retry!.ClaimToken);
    }

    private static async ValueTask<FileDelivery> SeedDeliveryAsync(Fixture fixture, string runId)
    {
        var identity = FileIdentityFactory.Create(
            Path.Combine(fixture.Root, "recent.csv"), fixture.Root, "source-1");
        var file = await fixture.Ledger.DiscoverAsync(runId, new DiscoveredFile(
            identity, Path.Combine(fixture.Root, "recent.csv"), DateTimeOffset.UtcNow, "source-1"));
        return await fixture.Ledger.StartDeliveryAsync(file.Id, "target", "key-1", "record", runId);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-lease-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "recent.csv"), "record_id,reading\n1,42\n");
            LedgerPath = Path.Combine(Root, "ledger.db");
            Ledger = new CollectionLedger(NewDb);
        }

        public string Root { get; }
        public string LedgerPath { get; }
        public CollectionLedger Ledger { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
