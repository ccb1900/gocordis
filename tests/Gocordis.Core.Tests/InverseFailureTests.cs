using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

/// <summary>
/// Red tests for inverse-failure honesty: every inverse is still attempted in
/// LIFO order, but a failed inverse must never be reported as a successful Undo.
/// </summary>
public sealed class InverseFailureTests
{
    [Fact]
    public async Task FailingInverseContinuesTheStackAndIsObservableAsIncomplete()
    {
        var log = new List<string>();
        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new DelegateComponent("failing-inverses", async context =>
        {
            for (var i = 1; i <= 3; i++)
            {
                var current = i;
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        log.Add($"undo:{current}");
                        return current == 2
                            ? ValueTask.FromException(new InvalidOperationException("inverse-2 failed"))
                            : ValueTask.CompletedTask;
                    });
            }

            return null;
        }));

        Assert.Equal(FiberState.Active, fiber.State);
        await runtime.DisposeAsync(fiber).WaitAsync(TimeSpan.FromSeconds(5));

        // LIFO is preserved and the failure does not skip the remaining inverses.
        Assert.Equal(new[] { "undo:3", "undo:2", "undo:1" }, log);
        Assert.True(fiber.CleanupIncomplete);
        var failure = Assert.Single(fiber.CleanupFailures);
        Assert.Contains("inverse-2 failed", failure.Message, StringComparison.Ordinal);

        var snapshot = runtime.Snapshot();
        var record = Assert.Single(snapshot.CleanupFailures);
        Assert.Equal(fiber.Id, record.FiberId);
        Assert.Equal("failing-inverses", record.FiberName);
        Assert.Contains("inverse-2 failed", record.Message, StringComparison.Ordinal);
        Assert.True(snapshot.Fibers.All(f => !f.CleanupIncomplete)); // Gone fibers are not in the live table.
    }

    [Fact]
    public async Task FailingInverseOnDependencyLossBlocksReentryInsteadOfPretendingPending()
    {
        var key = new Key<int>("inverse-failure-key");
        await using var runtime = new Runtime();
        var provider = new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 1);
                return null;
            },
            provide: [key]);
        var first = await runtime.LoadAsync(provider);

        var consumer = await runtime.LoadAsync(new DelegateComponent(
            "consumer",
            async context =>
            {
                context.Require(key);
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () => ValueTask.FromException(new InvalidOperationException("consumer inverse failed")));
                return null;
            },
            inject: [key]));
        Assert.Equal(FiberState.Active, consumer.State);

        // Dispose of the provider waits for consumer teardown to report back.
        await runtime.DisposeAsync(first).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FiberState.Failed, consumer.State);
        Assert.True(consumer.CleanupIncomplete);
        Assert.IsType<CleanupIncompleteException>(consumer.Failure);
        Assert.Contains(
            consumer.Id,
            runtime.Snapshot().CleanupFailures.Select(record => record.FiberId));

        // A failed cleanup is a sanctioned terminal outcome, not a target that
        // the reconciler keeps retrying on the next declaration change.
        await runtime.LoadAsync(provider);
        Assert.Equal(FiberState.Failed, consumer.State);
    }

    [Fact]
    public async Task DivertInverseFailureRunsOnceRecordsBothFailuresAndNeverLooksClean()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inverseRuns = 0;
        await using var runtime = new Runtime();

        var component = new DelegateComponent("divert-inverse-failure", async context =>
        {
            started.SetResult();
            await context.EffectAsync(
                async () => { await release.Task; },
                () =>
                {
                    Interlocked.Increment(ref inverseRuns);
                    return ValueTask.FromException(new InvalidOperationException("divert inverse failed"));
                });
            return null;
        });

        var loading = runtime.LoadAsync(component);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var fiber = runtime.GetFiber(runtime.Snapshot().Fibers.Single().Id);
        Assert.NotNull(fiber);

        var disposal = runtime.DisposeAsync(fiber);
        release.SetResult();
        await Task.WhenAll(loading, disposal).WaitAsync(TimeSpan.FromSeconds(5));

        // The inverse is attempted exactly once: the failed slot is never
        // retried by the later unwind.
        Assert.Equal(1, inverseRuns);

        var snapshot = runtime.Snapshot();
        Assert.Empty(snapshot.Fibers); // disposed fiber is Gone, but evidence survives
        var effectFailure = Assert.Single(snapshot.EffectFailures);
        Assert.False(effectFailure.IsInstall);
        Assert.Equal("divert inverse failed", effectFailure.Message);
        Assert.Equal("InvalidOperationException", effectFailure.ExceptionType);

        var cleanupFailure = Assert.Single(snapshot.CleanupFailures);
        Assert.Equal("divert inverse failed", cleanupFailure.Message);
        Assert.Equal("divert-inverse-failure", cleanupFailure.FiberName);
    }

    [Fact]
    public async Task DivertInverseFailureOnDependencyLossDoesNotPretendCleanRecovery()
    {
        var key = new Key<int>("divert-inverse-dependency");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inverseRuns = 0;
        await using var runtime = new Runtime();

        var provider = await runtime.LoadAsync(new DelegateComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 1);
                return null;
            },
            provide: [key]));

        var consumer = new DelegateComponent(
            "consumer",
            async context =>
            {
                context.Require(key);
                started.SetResult();
                await context.EffectAsync(
                    async () => { await release.Task; },
                    () =>
                    {
                        Interlocked.Increment(ref inverseRuns);
                        return ValueTask.FromException(new InvalidOperationException("lost inverse failed"));
                    });
                return null;
            },
            inject: [key]);
        var loading = runtime.LoadAsync(consumer);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var consumerFiber = Assert.Single(runtime.Snapshot().Fibers, f => f.Name == "consumer");
        var handle = runtime.GetFiber(consumerFiber.Id);
        Assert.NotNull(handle);

        var providerDisposal = runtime.DisposeAsync(provider);
        release.SetResult();
        await Task.WhenAll(loading, providerDisposal).WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.QuiesceAsync();

        Assert.Equal(1, inverseRuns);
        Assert.Equal(FiberState.Failed, handle.State);
        Assert.True(handle.CleanupIncomplete);
        Assert.IsType<CleanupIncompleteException>(handle.Failure);

        var snapshot = runtime.Snapshot();
        Assert.Contains(snapshot.CleanupFailures, record => record.Message == "lost inverse failed");
        Assert.Contains(snapshot.EffectFailures, record =>
            !record.IsInstall && record.Message == "lost inverse failed");
    }
}
