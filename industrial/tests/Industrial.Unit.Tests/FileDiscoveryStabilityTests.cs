using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Unit.Tests;

public sealed class FileDiscoveryStabilityTests
{
    private static readonly SourceDateRange Range =
        new(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddDays(1));

    [Fact]
    public async Task DiscoveryHashesTheSettledContentAfterTheStabilityGate()
    {
        using var temp = new TempRoot();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "20260101.csv"), "a,b\n");
        var changed = false;
        var source = new LocalFileSource(new StubResources(temp.Root), afterStabilityCheck: path =>
        {
            if (changed) return;
            File.WriteAllText(path, "a,b\nx,y\n");
            changed = true;
        });

        var found = await source.DiscoverAsync("source-1", Range);

        // The write happened after the stability gate but before hashing, so
        // the identity must describe the settled content, not a stale hash.
        var file = Assert.Single(found);
        var current = FileIdentityFactory.Create(file.FullPath, temp.Root, "source-1");
        Assert.Equal(current.ContentHash, file.Identity.ContentHash);
        Assert.Equal(current.Length, file.Identity.Length);
    }

    [Fact]
    public async Task ActualDiscoveryRejectsAFileChangedDuringTheQuietPeriod()
    {
        using var temp = new TempRoot();
        var path = Path.Combine(temp.Root, "20260101.csv");
        await File.WriteAllTextAsync(path, "a,b\n");
        var source = new LocalFileSource(new StubResources(temp.Root));

        var discovery = source.DiscoverAsync("source-1", Range).AsTask();
        await Task.Delay(50);
        await File.WriteAllTextAsync(path, "a,b\nx,y\n");
        var found = await discovery;

        Assert.Empty(found);
    }

    [Fact]
    public async Task StabilityHelperUsesRealSizeAndMtimeObservations()
    {
        using var temp = new TempRoot();
        var path = Path.Combine(temp.Root, "file.csv");
        await File.WriteAllTextAsync(path, "a\n");
        Assert.True(await FileStability.IsStableAsync(path, TimeSpan.Zero));

        var probe = FileStability.IsStableAsync(path, TimeSpan.FromMilliseconds(80));
        await Task.Delay(10);
        await File.WriteAllTextAsync(path, "a\nb\nc\n");
        Assert.False(await probe);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await FileStability.IsStableAsync(path, TimeSpan.FromMilliseconds(80), cts.Token));
    }

    [Fact]
    public void IdentityKeyIsIndependentOfBusinessDate()
    {
        using var temp = new TempRoot();
        var path = Path.Combine(temp.Root, "20260101.csv");
        File.WriteAllText(path, "a,b\n1,2\n");
        var plain = FileIdentityFactory.Create(path, temp.Root, "source-1");
        var dated = FileIdentityFactory.Create(path, temp.Root, "source-1",
            new DateTimeOffset(2031, 5, 6, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(plain.Key, dated.Key);
        Assert.Equal(plain.ContentHash, dated.ContentHash);
    }

    [Fact]
    public async Task DiscoverySortsDeterministicallyAndHonorsCancellation()
    {
        using var temp = new TempRoot();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "20260101-b.csv"), "a,b\n");
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "20260101-a.csv"), "a,b\n");
        var source = new LocalFileSource(new StubResources(temp.Root));

        var found = await source.DiscoverAsync("source-1", Range);
        Assert.Equal(["20260101-a.csv", "20260101-b.csv"],
            found.Select(file => Path.GetFileName(file.FullPath)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await source.DiscoverAsync("source-1", Range, cts.Token));
    }

    [Fact]
    public async Task UnreadableSubdirectoryDoesNotAbortDiscovery()
    {
        using var temp = new TempRoot();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "20260101-visible.csv"), "a,b\n");
        var blocked = Path.Combine(temp.Root, "blocked");
        Directory.CreateDirectory(blocked);
        await File.WriteAllTextAsync(Path.Combine(blocked, "20260101-hidden.csv"), "a,b\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(blocked, UnixFileMode.None);
        try
        {
            var source = new LocalFileSource(new StubResources(temp.Root));
            var found = await source.DiscoverAsync("source-1", Range);
            Assert.Contains(found, file => file.FullPath.EndsWith("20260101-visible.csv", StringComparison.Ordinal));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed class StubResources(string root) : IResourceRegistry
    {
        private readonly ResourceDefinition _resource = new(
            "source-1", "filesystem.local", "source-1", "local",
            new Dictionary<string, object?> { ["root"] = root }, null, 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        public ValueTask<ResourceDefinition> RegisterAsync(
            ResourceDefinition resource, CancellationToken ct = default) => ValueTask.FromResult(_resource);

        public ValueTask<ResourceDefinition?> GetAsync(
            string id, CancellationToken ct = default) => ValueTask.FromResult<ResourceDefinition?>(_resource);

        public ValueTask<IReadOnlyList<ResourceDefinition>> ListAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinition>>([_resource]);

        public ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default) =>
            ValueTask.FromResult(false);

        public void BindWorkflows(IWorkflowRegistry workflows) { }

        public void UnbindWorkflows() { }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                foreach (var directory in Directory.GetDirectories(Root))
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(directory,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
