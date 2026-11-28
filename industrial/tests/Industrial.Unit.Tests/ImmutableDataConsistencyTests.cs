using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Unit.Tests;

/// <summary>The ledger identity must describe the bytes a reader consumes,
/// and every source resource in one workflow must keep its own session.</summary>
public sealed class ImmutableDataConsistencyTests
{
    [Fact]
    public async Task SnapshotRejectsAFileChangedAfterDiscovery()
    {
        using var temp = new TempRoot();
        var source = Path.Combine(temp.Root, "source.csv");
        await File.WriteAllTextAsync(source, "first\n");
        var discovered = FileIdentityFactory.Create(source, temp.Root, "source-1");
        await File.WriteAllTextAsync(source, "second\n");

        var snapshot = await ImmutableFileSnapshot.CreateAsync(
            source, discovered, Path.Combine(temp.Root, "snapshots"));

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task ReaderSeesTheSnapshotBytesEvenWhenTheOriginalChanges()
    {
        using var temp = new TempRoot();
        var source = Path.Combine(temp.Root, "source.csv");
        await File.WriteAllTextAsync(source, "record_id,reading\n1,42\n");
        var discovered = FileIdentityFactory.Create(source, temp.Root, "source-1");

        var snapshot = await ImmutableFileSnapshot.CreateAsync(
            source, discovered, Path.Combine(temp.Root, "snapshots"));
        Assert.NotNull(snapshot);
        await File.WriteAllTextAsync(source, "record_id,reading\n2,99\n");

        Assert.Equal("record_id,reading\n1,42\n", await File.ReadAllTextAsync(snapshot!.FullPath));
        Assert.Equal(discovered.Key, snapshot.Identity.Key);
        Assert.Equal(discovered.ContentHash, snapshot.Identity.ContentHash);
    }

    [Fact]
    public async Task SourceSessionScopeLeasesEverySourceResource()
    {
        var source = new RecordingFileSource();
        var definition = new WorkflowDefinition(
            "multi-unc", "Multi UNC", 1,
            [
                new WorkflowNode("a", "industrial.filesystem.unc",
                    new Dictionary<string, object?> { ["resourceId"] = "unc-a" }, 0, 0),
                new WorkflowNode("b", "industrial.filesystem.unc",
                    new Dictionary<string, object?> { ["resourceId"] = "unc-b" }, 0, 0),
                new WorkflowNode("sink", "industrial.sink.sqlite",
                    new Dictionary<string, object?>
                    {
                        ["resourceId"] = "not-a-source", ["targetResourceId"] = "target",
                    }, 0, 0),
            ],
            [
                new WorkflowEdge("a", "files", "sink", "records"),
                new WorkflowEdge("b", "files", "sink", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["unc-a", "unc-b"], new Dictionary<string, object?>());

        await using (var scope = await SourceSessionScope.AcquireAsync(
                         source, definition, "default-resource"))
        {
            Assert.Contains("default-resource", source.Leased);
            Assert.Contains("unc-a", source.Leased);
            Assert.Contains("unc-b", source.Leased);
            Assert.DoesNotContain("not-a-source", source.Leased);
            Assert.Empty(source.Disposed);
        }

        Assert.Equal(["unc-b", "unc-a", "default-resource"], source.Disposed);
    }

    [Fact]
    public async Task FailedLaterLeaseReleasesAlreadyAcquiredSessions()
    {
        var source = new RecordingFileSource { FailOn = "unc-b" };
        var definition = new WorkflowDefinition(
            "multi-unc-failure", "Multi UNC failure", 1,
            [
                new WorkflowNode("a", "industrial.filesystem.unc",
                    new Dictionary<string, object?> { ["resourceId"] = "unc-a" }, 0, 0),
                new WorkflowNode("b", "industrial.filesystem.unc",
                    new Dictionary<string, object?> { ["resourceId"] = "unc-b" }, 0, 0),
            ],
            [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["unc-a", "unc-b"], new Dictionary<string, object?>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SourceSessionScope.AcquireAsync(source, definition, "default-resource"));

        Assert.Contains("default-resource", source.Leased);
        Assert.Contains("unc-a", source.Leased);
        Assert.DoesNotContain("unc-b", source.Disposed);
        Assert.Contains("unc-a", source.Disposed);
        Assert.Contains("default-resource", source.Disposed);
    }

    [Fact]
    public async Task MaintenanceLockIsExclusiveAndBlocksConcurrentCleanup()
    {
        using var temp = new TempRoot();
        var ledgerPath = Path.Combine(temp.Root, "ledger.db");
        var ledger = new CollectionLedger(() => IndustrialStore.Create(ledgerPath));
        Assert.True(await ledger.TryClaimMaintenanceAsync(
            SnapshotMaintenance.LockName, "holder", TimeSpan.FromMinutes(1)));
        Assert.False(await ledger.TryClaimMaintenanceAsync(
            SnapshotMaintenance.LockName, "other", TimeSpan.FromMinutes(1)));

        var blocked = await SnapshotMaintenance.RunAsync(
            ledger, Path.Combine(temp.Root, "snapshots"));
        Assert.True(blocked.MaintenanceBlocked);
        Assert.Equal(0, blocked.Deleted);

        Assert.True(await ledger.ReleaseMaintenanceAsync(
            SnapshotMaintenance.LockName, "holder"));
        Assert.False(await ledger.IsMaintenanceActiveAsync(SnapshotMaintenance.LockName));
    }

    [Fact]
    public async Task OrphanSnapshotIsReclaimedAfterTheRetentionWindow()
    {
        using var temp = new TempRoot();
        var ledgerPath = Path.Combine(temp.Root, "ledger.db");
        var ledger = new CollectionLedger(() => IndustrialStore.Create(ledgerPath));
        var snapshots = Path.Combine(temp.Root, "snapshots");
        Directory.CreateDirectory(snapshots);
        var orphan = Path.Combine(snapshots, "orphan.csv");
        await File.WriteAllTextAsync(orphan, "orphan\n");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-2));

        var report = await SnapshotMaintenance.RunAsync(
            ledger, snapshots, default, TimeSpan.FromHours(1));

        Assert.True(report.Orphans >= 1);
        Assert.False(File.Exists(orphan));
        Assert.Empty(report.OrphanFailures);
    }

    [Fact]
    public async Task ActiveExecutionBlocksCleanupAndIsReleasedWithTheLease()
    {
        using var temp = new TempRoot();
        var ledger = new CollectionLedger(() => IndustrialStore.Create(
            Path.Combine(temp.Root, "ledger.db")));

        await using var execution = await MaintenanceCoordination.BeginExecutionAsync(
            ledger, "instance-under-test");
        Assert.True(await ledger.HasActiveMaintenanceAsync(MaintenanceNames.ExecutionPrefix));
        Assert.Null(await MaintenanceCoordination.BeginCleanupAsync(ledger));

        await execution.DisposeAsync();
        Assert.False(await ledger.HasActiveMaintenanceAsync(MaintenanceNames.ExecutionPrefix));
        Assert.NotNull(await MaintenanceCoordination.BeginCleanupAsync(ledger));
    }

    [Fact]
    public async Task CleanupLeaseLostWhenAnExecutionLeaseAppears()
    {
        using var temp = new TempRoot();
        var ledger = new CollectionLedger(() => IndustrialStore.Create(
            Path.Combine(temp.Root, "ledger.db")));
        var lease = await MaintenanceCoordination.BeginCleanupAsync(ledger);
        Assert.NotNull(lease);
        Assert.True(await lease!.EnsureValidAsync());

        Assert.True(await ledger.TryClaimMaintenanceAsync(
            "execution:late-workflow", "late", TimeSpan.FromMinutes(10)));

        // The next maintenance check sees the late execution consumer and
        // declares the cleanup lease lost: it must stop deleting immediately.
        Assert.False(await lease.EnsureValidAsync());
        await lease.ReleaseAsync();
    }

    [Fact]
    public async Task ActiveCleanupBlocksExecutionUntilReleased()
    {
        using var temp = new TempRoot();
        var ledger = new CollectionLedger(() => IndustrialStore.Create(
            Path.Combine(temp.Root, "ledger.db")));
        var cleanup = await MaintenanceCoordination.BeginCleanupAsync(ledger);
        Assert.NotNull(cleanup);

        var executionTask = MaintenanceCoordination.BeginExecutionAsync(
            ledger, "waiting-workflow").AsTask();
        await Task.Delay(100);
        Assert.False(executionTask.IsCompleted);

        await cleanup!.ReleaseAsync();
        await using var execution = await executionTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await ledger.IsMaintenanceActiveAsync(MaintenanceNames.Cleanup));
    }

    private sealed class RecordingFileSource : IFileSource
    {
        public string? FailOn { get; init; }
        public List<string> Leased { get; } = [];
        public List<string> Disposed { get; } = [];

        public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
            string resourceId, SourceDateRange range, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<DiscoveredFile>>([]);

        public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
            string resourceId, SourceDateRange range, FileProfileDefinition? profile,
            IReadOnlySet<DateOnly>? businessDates, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<DiscoveredFile>>([]);

        public ValueTask<IAsyncDisposable?> LeaseScopeAsync(
            string resourceId, CancellationToken ct = default)
        {
            Leased.Add(resourceId);
            if (string.Equals(resourceId, FailOn, StringComparison.Ordinal))
                throw new InvalidOperationException($"authentication failed for {resourceId}");
            return ValueTask.FromResult<IAsyncDisposable?>(new Lease(this, resourceId));
        }

        public ValueTask<bool> IsStableAsync(
            DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
            ValueTask.FromResult(true);

        private sealed class Lease(RecordingFileSource owner, string resourceId) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Disposed.Add(resourceId);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(
                Path.GetTempPath(), "industrial-immutable-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
