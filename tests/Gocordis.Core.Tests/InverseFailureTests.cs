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
}
