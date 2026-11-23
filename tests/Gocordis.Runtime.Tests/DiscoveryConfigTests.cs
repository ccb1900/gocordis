using System.Reflection;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch B: declaration discovery and configuration watching. A broken file is
/// a local diagnostic, never a reason to drop its healthy neighbors; reconcile
/// errors are observable without corrupting revision accounting.
/// </summary>
public sealed class DiscoveryConfigTests
{
    [Fact]
    public async Task DiscoverAsync_KeepsGoodManifestsAndRecordsBrokenFiles()
    {
        var directory = NewDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "01-good.toml"), ManifestToml("good", "true"));
            await File.WriteAllTextAsync(Path.Combine(directory, "02-bad-value.toml"), ManifestToml("bad-value", "\"true\""));
            await File.WriteAllTextAsync(Path.Combine(directory, "03-malformed.toml"), MalformedToml);
            await File.WriteAllTextAsync(Path.Combine(directory, "04-also-good.toml"), ManifestToml("also-good", "false"));

            await using var runtime = new Gocordis.Core.Runtime();
            await using var registry = new PluginRegistry(runtime);
            var manifests = await registry.DiscoverAsync(directory);

            Assert.Equal(new[] { "good", "also-good" }, manifests.Select(manifest => manifest.Id));
            Assert.Equal(2, registry.Diagnostics.Count);
            Assert.All(registry.Diagnostics, diagnostic => Assert.IsType<ManifestParseException>(diagnostic.Error));
            Assert.Contains(registry.Diagnostics, diagnostic => diagnostic.Path.EndsWith("02-bad-value.toml", StringComparison.Ordinal));
            Assert.Contains(registry.Diagnostics, diagnostic => diagnostic.Path.EndsWith("03-malformed.toml", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("[]")]
    public void ParseToml_ThrowsManifestParseExceptionForNonBooleanEnabled(string enabled)
    {
        var error = Assert.Throws<ManifestParseException>(() => PluginManifest.ParseToml(ManifestToml("bad", enabled)));
        Assert.Contains("enabled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseToml_StillAcceptsBooleanEnabledAndMissingEnabled()
    {
        var toml = ManifestToml("on", "true") + ManifestToml("default-off", enabled: null);

        var manifests = PluginManifest.ParseToml(toml);

        Assert.Equal(2, manifests.Count);
        Assert.True(manifests[0].Enabled);
        Assert.False(manifests[1].Enabled);
    }

    [Fact]
    public async Task ConfigWatcher_DeletedFileReconcilesWithEmptyDeclarations()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, ManifestToml("old", "true"));
        var observed = new List<int>();

        try
        {
            using var watcher = new ConfigWatcher(path, manifests =>
            {
                observed.Add(manifests.Count);
                return Task.CompletedTask;
            });
            await watcher.SyncAsync();

            File.Delete(path);
            await watcher.SyncAsync();

            Assert.Equal([1, 0], observed);
            Assert.Null(watcher.LastError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigWatcher_BadFileEventKeepsRevisionAndExistingLastError()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, ManifestToml("good", "true"));

        try
        {
            using var watcher = new ConfigWatcher(path, _ => Task.CompletedTask);
            await watcher.SyncAsync();
            watcher.Start();

            await File.WriteAllTextAsync(path, ManifestToml("bad", "\"true\""));
            Assert.True(await WaitForAsync(() => watcher.LastError is not null, TimeSpan.FromSeconds(5)));
            Assert.IsType<ManifestParseException>(watcher.LastError);
            Assert.Equal(0, watcher.Revision);

            await File.WriteAllTextAsync(path, ManifestToml("good", "true"));
            Assert.True(await WaitForAsync(() => watcher.Revision > 0, TimeSpan.FromSeconds(5)));
            Assert.NotNull(watcher.LastError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigWatcher_SyncAndTimedFireReportErrorsTheSameWay()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, MalformedToml);

        try
        {
            using var syncWatcher = new ConfigWatcher(path, _ => Task.CompletedTask);
            await syncWatcher.SyncAsync();
            Assert.IsType<ManifestParseException>(syncWatcher.LastError);
            Assert.Equal(0, syncWatcher.Revision);

            using var fireWatcher = new ConfigWatcher(path, _ => Task.CompletedTask);
            fireWatcher.Start();
            await File.WriteAllTextAsync(path, ManifestToml("bad", "1"));
            Assert.True(await WaitForAsync(() => fireWatcher.LastError is not null, TimeSpan.FromSeconds(5)));
            Assert.IsType<ManifestParseException>(fireWatcher.LastError);
            Assert.Equal(0, fireWatcher.Revision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigWatcher_DisposeWhileEventIsQueuedDoesNotFaultOrReconcile()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, ManifestToml("good", "true"));

        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var watcher = new ConfigWatcher(path, async _ =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                await release.Task;
            });

            // Hold the reconcile gate so the queued file event must wait behind it.
            var sync = watcher.SyncAsync();
            await entered.Task;
            var fire = typeof(ConfigWatcher).GetMethod("FireAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queued = (Task)fire.Invoke(watcher, null)!;

            watcher.Dispose();
            release.SetResult();

            var all = Task.WhenAll(sync, queued);
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.Same(all, completed);
            await all;
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.Equal(0, watcher.Revision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string MalformedToml = "[[components]]\nid =\n";

    private static string ManifestToml(string id, string? enabled) =>
        $"[[components]]\nid = \"{id}\"\ntype = \"echo\"\n" +
        (enabled is null ? string.Empty : $"enabled = {enabled}\n");

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }
}
