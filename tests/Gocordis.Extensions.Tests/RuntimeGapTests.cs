using System.Collections.Concurrent;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Batch 3 runtime/scheduling/diagnostics extension gaps. These tests exercise
/// the public surface the way a component host does: owner activation, watcher
/// readiness, pure diagnoses and generation-fenced replacements.
/// </summary>
public sealed class RuntimeGapTests
{
    [Fact]
    public async Task TimerOwnedEntryStopsWhenOwnerUnwinds()
    {
        await using var runtime = new Runtime();
        await using var timers = new TimerService();
        var calls = 0;
        TimerHandle? handle = null;

        var owner = await runtime.LoadAsync(new Step("timer-owner", context =>
        {
            handle = timers.TimeoutOwned(context, TimeSpan.FromMilliseconds(50), () =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.CompletedTask;
            });
            return ValueTask.FromResult<Cleanup?>(null);
        }));

        Assert.NotNull(handle);
        await runtime.DisposeAsync(owner);
        await Task.Delay(150);

        Assert.Equal(0, calls);
        Assert.False(handle!.Stop(), "the unwind inverse already stopped the entry");
    }

    [Fact]
    public async Task TimeoutOwnedFiresWhileOwnerIsActive()
    {
        await using var runtime = new Runtime();
        await using var timers = new TimerService();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var owner = await runtime.LoadAsync(new Step("timer-active", context =>
        {
            _ = timers.IntervalOwned(context, TimeSpan.FromMilliseconds(20), () =>
            {
                fired.TrySetResult();
                return ValueTask.CompletedTask;
            });
            return ValueTask.FromResult<Cleanup?>(null);
        }));

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await runtime.DisposeAsync(owner);
    }

    [Fact]
    public async Task TimeoutWaitResolvesAndFollowsOwnerCancellation()
    {
        await using var runtime = new Runtime();
        await using var timers = new TimerService();
        Task? completing = null;
        Task? cancelled = null;

        var owner = await runtime.LoadAsync(new Step("wait-owner", context =>
        {
            completing = timers.TimeoutWait(context, TimeSpan.FromMilliseconds(20));
            cancelled = timers.TimeoutWait(context, TimeSpan.FromSeconds(30));
            return ValueTask.FromResult<Cleanup?>(null);
        }));

        await completing!.WaitAsync(TimeSpan.FromSeconds(3));
        await runtime.DisposeAsync(owner);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled!);
    }

    [Fact]
    public async Task TicksStopAfterOwnerUnwinds()
    {
        await using var runtime = new Runtime();
        await using var timers = new TimerService();
        IAsyncEnumerable<DateTimeOffset>? ticks = null;

        var owner = await runtime.LoadAsync(new Step("ticks-owner", context =>
        {
            ticks = timers.Ticks(context, TimeSpan.FromMilliseconds(15));
            return ValueTask.FromResult<Cleanup?>(null);
        }));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var seen = new List<DateTimeOffset>();
        await foreach (var tick in ticks!.WithCancellation(cancellation.Token))
        {
            seen.Add(tick);
            if (seen.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(2, seen.Count);
        Assert.True(seen[1] >= seen[0]);

        await runtime.DisposeAsync(owner);
        var after = 0;
        using var shortCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await foreach (var _ in ticks!.WithCancellation(shortCancellation.Token))
        {
            after++;
        }

        Assert.Equal(0, after);
    }

    [Fact]
    public async Task TimeoutContextCancelsOnFireAndOnOwnerUnwind()
    {
        await using var runtime = new Runtime();
        await using var timers = new TimerService();
        CancellationTokenSource? fired = null;
        CancellationTokenSource? withdrawn = null;

        var owner = await runtime.LoadAsync(new Step("timeout-context-owner", context =>
        {
            fired = timers.TimeoutContext(context, TimeSpan.FromMilliseconds(20));
            withdrawn = timers.TimeoutContext(context, TimeSpan.FromSeconds(30));
            return ValueTask.FromResult<Cleanup?>(null);
        }));

        Assert.True(await SpinUntilAsync(() => fired!.IsCancellationRequested));
        Assert.False(withdrawn!.IsCancellationRequested);

        await runtime.DisposeAsync(owner);
        Assert.True(withdrawn.IsCancellationRequested);
    }

    [Fact]
    public async Task WatchRecursiveFilterReadyErrorAndRevoke()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gocordis-recursive-watch-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        await using var service = new ReversibleWatchService();
        var events = new ConcurrentQueue<WatchEvent>();

        try
        {
            using var subscription = service.Watch(root, events.Enqueue,
                new WatchOptions(Recursive: true, Filter: "**/*.txt"));

            Assert.True(await SpinUntilAsync(() => events.Any(e => e.Kind == WatchEventKind.Ready)));
            await File.WriteAllTextAsync(Path.Combine(nested, "deep.txt"), "1");
            await File.WriteAllTextAsync(Path.Combine(nested, "ignored.log"), "2");

            Assert.True(await SpinUntilAsync(() => events.Any(e =>
                e.Kind == WatchEventKind.Change && e.Path.EndsWith("deep.txt", StringComparison.Ordinal))));
            Assert.DoesNotContain(events, e => e.Path.EndsWith("ignored.log", StringComparison.Ordinal));

            var missing = Path.Combine(root, "missing-dir", "x.txt");
            service.Watch(missing, events.Enqueue);
            Assert.Contains(events, e =>
                e.Kind == WatchEventKind.Error && e.Path == missing && !string.IsNullOrEmpty(e.Error));

            await Task.Delay(100); // let duplicate native events settle before the fence
            var beforeRevoke = events.Count;
            subscription.Dispose();
            subscription.Dispose(); // idempotent
            await File.WriteAllTextAsync(Path.Combine(nested, "deep.txt"), "3");
            await Task.Delay(150);
            Assert.Equal(beforeRevoke, events.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchOwnedProviderFollowsOwnerActivation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gocordis-owned-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await using var runtime = new Runtime();
        await using var service = new ReversibleWatchService();
        var events = new ConcurrentQueue<WatchEvent>();

        try
        {
            var owner = await runtime.LoadAsync(new Step("watch-owner", context =>
            {
                service.WatchOwned(context, root, events.Enqueue, new WatchOptions(Recursive: true));
                return ValueTask.FromResult<Cleanup?>(null);
            }));

            Assert.True(await SpinUntilAsync(() => events.Any(e => e.Kind == WatchEventKind.Ready)));
            await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "1");
            Assert.True(await SpinUntilAsync(() => events.Any(e => e.Kind == WatchEventKind.Change)));

            await runtime.DisposeAsync(owner);
            await Task.Delay(100); // let duplicate native events settle before the fence
            var before = events.Count;
            await File.WriteAllTextAsync(Path.Combine(root, "two.txt"), "2");
            await Task.Delay(150);
            Assert.Equal(before, events.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiagnoseReportsVersionMismatchAndUnknownProvider()
    {
        var service = new DiagnosticsService("1.0.0");
        ModuleDeclaration[] modules =
        [
            new("core", "1.0.0", HostVersionConstraint: ">=2.0.0"),
            new("fs", "1.0.0", Provides: ["fs.capability"]),
        ];
        PluginManifest[] plugins =
        [
            new("consumer", "echo", true, new Dictionary<string, object?>(),
                Requires: ["missing.capability"], Provides: ["consumer.capability"]),
        ];

        var diagnostics = service.Diagnose(modules, plugins);

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Code == DiagnosticCode.VersionMismatch &&
            d.Message.Contains("core", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Code == DiagnosticCode.UnknownProvider &&
            d.Message.Contains("missing.capability", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnoseReportsUnknownDependencyAndIsDeterministic()
    {
        var service = new DiagnosticsService("2.0.0");
        ModuleDeclaration[] modules =
        [
            new("app", "1.0.0", Dependencies: ["ghost"], HostVersionConstraint: ">=1.0.0"),
        ];

        var first = service.Diagnose(modules, []);
        var second = service.Diagnose(modules, []);

        Assert.Equal(first, second);
        Assert.Contains(first, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Code == DiagnosticCode.UnknownDependency &&
            d.Message.Contains("ghost", StringComparison.Ordinal));
        Assert.DoesNotContain(first, d => d.Code == DiagnosticCode.VersionMismatch);
    }

    [Fact]
    public async Task GenericHmrStaleGenerationCannotReleaseNewTarget()
    {
        await using var hmr = new GenericHmr();
        hmr.RegisterTarget(new HmrTarget("t", "component"));
        hmr.Bind("t", "v1");

        using var stale = hmr.Acquire("t");
        var result = await hmr.ReplaceAsync("t", new HmrTarget("t", "component", "v2"));

        Assert.True(result.Succeeded, result.Error);
        var current = hmr.CurrentBinding("t")!;
        var replacement = Assert.IsType<HmrTarget>(current.Value);
        Assert.Equal("v2", replacement.Type);
        Assert.NotEqual(stale.Generation, current.Generation);

        using var fresh = hmr.Acquire("t");
        Assert.False(stale.Release(), "a stale generation must not touch the new binding");
        Assert.Equal(1, hmr.CurrentBinding("t")!.UsageCount);
        Assert.True(fresh.Release());
        Assert.Equal(0, hmr.CurrentBinding("t")!.UsageCount);
    }

    [Fact]
    public async Task GenericHmrReplaceValidatesFirstAndKeepsOldTargetOnFailure()
    {
        await using var hmr = new GenericHmr();
        hmr.RegisterTarget(new HmrTarget("t", "component"));
        hmr.Bind("t", "v1");
        var before = hmr.CurrentBinding("t")!;

        var rejected = await hmr.ReplaceAsync("t", new HmrTarget("t", "component", "bad"),
            validate: (_, _) => Task.FromResult(HmrValidation.Reject("incompatible")));

        Assert.False(rejected.Succeeded);
        Assert.Contains("incompatible", rejected.Error, StringComparison.Ordinal);
        Assert.Equal(before.Generation, hmr.CurrentBinding("t")!.Generation);
        Assert.Same(before.Value, hmr.CurrentBinding("t")!.Value);

        var failed = await hmr.ReplaceAsync("t", new HmrTarget("t", "component", "broken"),
            activate: (_, _) => ValueTask.FromException<object>(new InvalidOperationException("boom")));

        Assert.False(failed.Succeeded);
        Assert.Contains("boom", failed.Error, StringComparison.Ordinal);
        Assert.Same(before.Value, hmr.CurrentBinding("t")!.Value);
    }

    private static async Task<bool> SpinUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    private sealed class Step : Component
    {
        private readonly Func<Context, ValueTask<Cleanup?>> _apply;

        public Step(string name, Func<Context, ValueTask<Cleanup?>> apply)
        {
            Name = name;
            _apply = apply;
        }

        public override string Name { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) => _apply(context);
    }
}
