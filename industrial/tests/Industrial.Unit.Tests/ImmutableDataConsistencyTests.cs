using Industrial.Contracts;
using Industrial.Core;

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
                    new Dictionary<string, object?> { ["targetResourceId"] = "target" }, 0, 0),
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
            Assert.Equal(["default-resource", "unc-a", "unc-b"], source.Leased);
            Assert.Empty(source.Disposed);
        }

        Assert.Equal(["unc-b", "unc-a", "default-resource"], source.Disposed);
    }

    private sealed class RecordingFileSource : IFileSource
    {
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
