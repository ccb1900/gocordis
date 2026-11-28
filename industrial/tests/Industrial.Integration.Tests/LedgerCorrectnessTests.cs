using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>Behavior gates for the collection ledger: resource-scoped identity,
/// terminal run state, target-scoped retry and a single in-flight claim.</summary>
public sealed class LedgerCorrectnessTests
{
    [Fact]
    public async Task IdenticalFilesInTwoResourcesHaveIndependentLedgerEntries()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        await fixture.RegisterResourceAsync("source-2", fixture.SecondSourceRoot);
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        var sink = new CountingSink("target");

        await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [sink], null);
        await coordinator.RunAsync("workflow", "source-2", fixture.Range,
            new CsvRecordReader(), [sink], null);

        using var ledger = fixture.NewDb();
        var files = ledger.CollectionFiles.AsNoTracking().ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal(["source-1", "source-2"], files.Select(file => file.ResourceId).OrderBy(id => id));
        Assert.Equal(2, ledger.FileDeliveries.AsNoTracking().Count());
        Assert.Equal(2, sink.Invocations);
        var first = await fixture.Source.DiscoverAsync("source-1", fixture.Range);
        Assert.NotNull(await fixture.Ledger.GetFileByIdentityAsync("source-1", first[0].Identity.Key));
    }

    [Fact]
    public async Task RunReachesTerminalStateWithFinishedAt()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var coordinator = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        var runId = await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [new CountingSink("target")], null);
        var run = await fixture.Ledger.GetRunAsync(runId);
        Assert.NotNull(run);
        Assert.Equal("Succeeded", run!.Status);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task RunFailureIsRecordedAsInterruptedForRecovery()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var coordinator = new CollectionCoordinator(
            fixture.Resources, new ThrowingFileSource(), fixture.Ledger);
        string runId = string.Empty;
        try
        {
            await coordinator.RunAsync("workflow", "source-1", fixture.Range,
                new CsvRecordReader(), [new CountingSink("target")], null);
            Assert.Fail("discovery failure must surface to the caller");
        }
        catch (IOException ex)
        {
            using var ledger = fixture.NewDb();
            runId = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList()).Id;
            Assert.Contains("discovery failed", ex.Message);
        }

        var run = await fixture.Ledger.GetRunAsync(runId);
        Assert.NotNull(run);
        Assert.Equal("Interrupted", run!.Status);
        Assert.Contains("discovery failed", run.Error);
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public async Task NewlyConfiguredTargetReceivesAlreadySucceededHistoricalFile()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        var firstTarget = new CountingSink("target-a");

        await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [firstTarget], null);
        Assert.Equal("Succeeded", (await fixture.Ledger.ListAllFilesAsync())[0].Status);

        var newTarget = new CountingSink("target-b");
        await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [firstTarget, newTarget], null);

        Assert.Equal(1, firstTarget.Invocations);
        Assert.Equal(1, newTarget.Invocations);
        using var ledger = fixture.NewDb();
        var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, delivery => Assert.Equal("Succeeded", delivery.Status));
    }

    [Fact]
    public async Task RetryCanTargetOneFailedDelivery()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var firstA = new CountingSink("target-a", fail: true);
        var firstB = new CountingSink("target-b", fail: true);
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [firstA, firstB], null);

        var restoredA = new CountingSink("target-a");
        var restoredB = new CountingSink("target-b");
        var retried = await coordinator.RetryFailedAsync("target-b", [restoredA, restoredB], null);
        Assert.Equal(1, retried);
        Assert.Equal(0, restoredA.Invocations);
        Assert.Equal(1, restoredB.Invocations);

        using var ledger = fixture.NewDb();
        var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
        Assert.Equal("Failed", deliveries.Single(row => row.TargetId == "target-a").Status);
        Assert.Equal("Succeeded", deliveries.Single(row => row.TargetId == "target-b").Status);
    }

    [Fact]
    public async Task SuccessfulRetryRollsUpFileStatus()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var coordinator = new CollectionCoordinator(
            fixture.Resources, fixture.Source, fixture.Ledger, new CsvRecordReader());
        await coordinator.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [new CountingSink("target", fail: true)], null);
        Assert.Equal("PartialFailure", (await fixture.Ledger.ListAllFilesAsync())[0].Status);

        await coordinator.RetryFailedAsync([new CountingSink("target")], null);
        Assert.Equal("Succeeded", (await fixture.Ledger.ListAllFilesAsync())[0].Status);
    }

    [Fact]
    public async Task ConcurrentRunsClaimOneDeliveryPerFileAndTarget()
    {
        using var fixture = new Fixture();
        await fixture.RegisterResourceAsync("source-1", fixture.SourceRoot);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new GatedSink("target", gate.Task);
        var first = new CollectionCoordinator(fixture.Resources, fixture.Source, fixture.Ledger);
        var second = new CollectionCoordinator(
            fixture.Resources, fixture.Source, new CollectionLedger(fixture.NewDb));
        var firstRun = first.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [sink], null).AsTask();
        await Task.Delay(50);
        var secondRun = second.RunAsync("workflow", "source-1", fixture.Range,
            new CsvRecordReader(), [sink], null).AsTask();
        gate.SetResult();
        await Task.WhenAll(firstRun, secondRun);

        Assert.Equal(1, sink.Invocations);
        using var ledger = fixture.NewDb();
        Assert.Single(ledger.CollectionFiles.AsNoTracking().ToList());
        Assert.Single(ledger.FileDeliveries.AsNoTracking().ToList());
    }

    private sealed class CountingSink : IRecordSink
    {
        private readonly bool _fail;
        public CountingSink(string targetId, bool fail = false)
        {
            TargetId = targetId;
            _fail = fail;
        }

        public string TargetId { get; }
        public int Invocations;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Invocations);
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct)) count++;
            if (_fail) throw new IOException("target offline");
            return new DeliveryResultRef("delivery-" + idempotencyKey, TargetId, count);
        }
    }

    private sealed class GatedSink : IRecordSink
    {
        private readonly Task _gate;
        public GatedSink(string targetId, Task gate)
        {
            TargetId = targetId;
            _gate = gate;
        }

        public string TargetId { get; }
        public int Invocations;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Invocations);
            await _gate.WaitAsync(ct);
            var count = 0;
            await foreach (var _ in records.WithCancellation(ct)) count++;
            return new DeliveryResultRef("delivery-" + idempotencyKey, TargetId, count);
        }
    }

    private sealed class ThrowingFileSource : IFileSource
    {
        public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
            string resourceId, SourceDateRange range, CancellationToken ct = default) =>
            throw new IOException("discovery failed");

        public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
            string resourceId, SourceDateRange range, FileProfileDefinition? profile,
            IReadOnlySet<DateOnly>? businessDates, CancellationToken ct = default) =>
            throw new IOException("discovery failed");

        public ValueTask<bool> IsStableAsync(
            DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-ledger-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source-1");
            SecondSourceRoot = Path.Combine(Root, "source-2");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(SecondSourceRoot);
            var contents = "record_id,reading\n1,42\n";
            File.WriteAllText(Path.Combine(SourceRoot, "20260101.csv"), contents);
            File.WriteAllText(Path.Combine(SecondSourceRoot, "20260101.csv"), contents);
            LedgerPath = Path.Combine(Root, "ledger.db");
            using var bootstrap = IndustrialStore.Create(LedgerPath);
            Resources = new ResourceRegistry(NewDb);
            Ledger = new CollectionLedger(NewDb);
            Source = new LocalFileSource(Resources);
            Range = new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string SecondSourceRoot { get; }
        public string LedgerPath { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(LedgerPath);
        public IResourceRegistry Resources { get; }
        public ICollectionLedger Ledger { get; }
        public IFileSource Source { get; }
        public SourceDateRange Range { get; }

        public Task RegisterResourceAsync(string id, string root) =>
            Resources.RegisterAsync(new ResourceDefinition(
                id, "filesystem.local", id, "local",
                new Dictionary<string, object?> { ["root"] = root },
                null, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).AsTask();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
