using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

/// <summary>
/// A batch must not change the observable semantics of independent loads that
/// happen concurrently on the same Runtime.
/// </summary>
public sealed class RuntimeBatchIsolationTests
{
    [Fact]
    public async Task IndependentLoadDuringBatchKeepsItsInFlightStateObservable()
    {
        await using var runtime = new Runtime();
        var batchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independentGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var batch = runtime.LoadBatchAsync([
            new GatedComponent("batch-gated", batchStarted, batchGate),
            new SimpleComponent("batch-simple"),
        ]);
        await batchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var independent = runtime.LoadAsync(
            new GatedComponent("independent", independentStarted, independentGate));
        await independentStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The independent load's in-flight state is observable despite the
        // concurrent batch's suppressed intermediate publications.
        Assert.Contains(runtime.Snapshot().Fibers, fiber =>
            fiber.Name == "independent" && fiber.State == FiberState.Loading);

        independentGate.SetResult();
        batchGate.SetResult();
        var independentFiber = await independent.WaitAsync(TimeSpan.FromSeconds(5));
        var batchFibers = await batch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Active, independentFiber.State);
        Assert.Equal(2, batchFibers.Count);
        Assert.All(batchFibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
    }

    private sealed class SimpleComponent : Component
    {
        public SimpleComponent(string name) => Name = name;
        public override string Name { get; }
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class GatedComponent : Component
    {
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _gate;

        public GatedComponent(string name, TaskCompletionSource started, TaskCompletionSource gate)
        {
            Name = name;
            _started = started;
            _gate = gate;
        }

        public override string Name { get; }

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(async () =>
            {
                _started.TrySetResult();
                await _gate.Task;
            }, static () => ValueTask.CompletedTask);
            return null;
        }
    }
}

public sealed class EffectFailureRetentionTests
{
    [Fact]
    public async Task EffectFailureHistoryIsBoundedAndKeepsTheLatestFailure()
    {
        await using var runtime = new Runtime();
        for (var i = 0; i < 300; i++)
        {
            await runtime.LoadAsync(new DelegateComponent($"boom-{i}", async context =>
            {
                await context.EffectAsync(
                    () => throw new InvalidOperationException($"failure-{i}"),
                    static () => ValueTask.CompletedTask);
                return null;
            }));
        }

        var failures = runtime.Snapshot().EffectFailures;
        Assert.Equal(256, failures.Count);
        Assert.Equal("failure-299", failures[^1].Message);
        Assert.IsType<InvalidOperationException>(failures[^1].Exception);
    }
}

public sealed class ConcurrentBatchSnapshotTests
{
    [Fact]
    public async Task InterleavedBatchesPublishFinalSnapshotInBothCompletionOrders()
    {
        foreach (var reverse in new[] { false, true })
        {
            await using var runtime = new Runtime();
            var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var aGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var a = runtime.LoadBatchAsync([
                new Gated("batch-a", aStarted, aGate), new Plain("batch-a-simple")]);
            await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var b = runtime.LoadBatchAsync([
                new Gated("batch-b", bStarted, bGate), new Plain("batch-b-simple")]);
            await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (reverse)
            {
                bGate.SetResult();
                aGate.SetResult();
            }
            else
            {
                aGate.SetResult();
                bGate.SetResult();
            }

            await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(5));
            var fibers = runtime.Snapshot().Fibers;
            Assert.Equal(4, fibers.Count);
            Assert.All(fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
        }
    }

    [Fact]
    public async Task OneFailingBatchDoesNotPoisonTheOtherBatchesFinalSnapshot()
    {
        await using var runtime = new Runtime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var good = runtime.LoadBatchAsync([
            new Gated("good", started, gate), new Plain("good-simple")]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failing = runtime.LoadBatchAsync([new Failing("bad")]);

        gate.SetResult();
        await Task.WhenAll(good, failing).WaitAsync(TimeSpan.FromSeconds(5));
        var fibers = runtime.Snapshot().Fibers;
        // A failed fiber stays observable as Failed; the healthy batch must
        // still be fully visible and no fiber may be stuck mid-transition.
        Assert.True(fibers.Count == 3,
            string.Join(",", fibers.Select(f => $"{f.Name}:{f.State}")));
        Assert.Contains(fibers, fiber => fiber.Name == "bad" && fiber.State == FiberState.Failed);
        Assert.All(fibers.Where(fiber => fiber.Name != "bad"),
            fiber => Assert.Equal(FiberState.Active, fiber.State));
        Assert.DoesNotContain(fibers, fiber =>
            fiber.State is FiberState.Loading or FiberState.Unloading);
        Assert.DoesNotContain(fibers, fiber =>
            fiber.State is FiberState.Loading or FiberState.Unloading);
    }

    private sealed class Plain : Component
    {
        public Plain(string name) => Name = name;
        public override string Name { get; }
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class Gated : Component
    {
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _gate;
        public Gated(string name, TaskCompletionSource started, TaskCompletionSource gate)
        {
            Name = name;
            _started = started;
            _gate = gate;
        }

        public override string Name { get; }

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(async () =>
            {
                _started.TrySetResult();
                await _gate.Task;
            }, static () => ValueTask.CompletedTask);
            return null;
        }
    }

    private sealed class Failing : Component
    {
        public Failing(string name) => Name = name;
        public override string Name { get; }

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(
                () => throw new InvalidOperationException("batch failure"),
                static () => ValueTask.CompletedTask);
            return null;
        }
    }
}
