using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

/// <summary>
/// Batch A freeze: concurrency and ownership edges found in review.
/// Every test here calls only public Runtime APIs.
/// </summary>
public sealed class LifecycleConvergenceTests
{
    [Fact]
    public async Task ConcurrentDisposeCallsAllComplete()
    {
        await using var runtime = new Runtime();
        var key = new Key<int>("shared-provider");

        var provider = await runtime.LoadAsync(new DelegateComponent(
            "provider",
            context =>
            {
                context.Provide(key, 1);
                return ValueTask.FromResult<Cleanup?>(null);
            },
            provide: [key]));

        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await runtime.LoadAsync(new DelegateComponent(
            "consumer",
            context =>
            {
                context.Require(key);
                return ValueTask.FromResult<Cleanup?>(async () =>
                {
                    cleanupEntered.TrySetResult();
                    await releaseCleanup.Task;
                });
            },
            inject: [key]));

        var first = runtime.DisposeAsync(provider);
        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = runtime.DisposeAsync(provider);

        releaseCleanup.TrySetResult();
        await Task.WhenAll(
            first.WaitAsync(TimeSpan.FromSeconds(5)),
            second.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(FiberState.Gone, provider.State);
    }

    [Fact]
    public async Task ConcurrentCloseCallsAllComplete()
    {
        var runtime = new Runtime();
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await runtime.LoadAsync(new DelegateComponent(
            "owner",
            _ => ValueTask.FromResult<Cleanup?>(async () =>
            {
                cleanupEntered.TrySetResult();
                await releaseCleanup.Task;
            })));

        var first = runtime.CloseAsync();
        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = runtime.CloseAsync();

        releaseCleanup.TrySetResult();
        await Task.WhenAll(
            first.WaitAsync(TimeSpan.FromSeconds(5)),
            second.WaitAsync(TimeSpan.FromSeconds(5)));

        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task DisposedChildIsDetachedFromItsParent()
    {
        await using var runtime = new Runtime();

        var parent = await runtime.LoadAsync(new DelegateComponent(
            "parent",
            async context =>
            {
                await context.LoadChildAsync(new DelegateComponent(
                    "child",
                    _ => ValueTask.FromResult<Cleanup?>(null)));
                return null;
            }));

        var child = Assert.Single(parent.Children);
        await runtime.DisposeAsync(child);

        Assert.Equal(FiberState.Gone, child.State);
        Assert.Equal(FiberState.Active, parent.State);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public async Task DisposeParentWithFailedOrPendingChildDoesNotHang()
    {
        // Migration review P0-1: a child with no CurrentActivation (Failed or
        // Pending) was counted as an unwind waiter, but StartUnwind returned
        // immediately for it, so the parent never received a terminal report.
        var runtime = new Runtime();
        try
        {
            var neverProvided = new Key<int>($"never-provided-{Guid.NewGuid():N}");
            var parent = await runtime.LoadAsync(new DelegateComponent(
                "parent-of-inert-children",
                async context =>
                {
                    await context.LoadChildAsync(new DelegateComponent(
                        "failing-child",
                        _ => throw new InvalidOperationException("child apply failed")));
                    await context.LoadChildAsync(new DelegateComponent(
                        "pending-child",
                        _ => ValueTask.FromResult<Cleanup?>(null),
                        inject: [neverProvided]));
                    return null;
                }));

            Assert.Equal(2, parent.Children.Count);
            var failed = parent.Children.Single(c => c.Component.Name == "failing-child");
            var pending = parent.Children.Single(c => c.Component.Name == "pending-child");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (failed.State != FiberState.Failed && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.Equal(FiberState.Failed, failed.State);
            Assert.Equal(FiberState.Pending, pending.State);

            await runtime.DisposeAsync(parent).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(FiberState.Gone, parent.State);
            Assert.Empty(parent.Children);
            Assert.Equal(FiberState.Gone, failed.State);
            Assert.Equal(FiberState.Gone, pending.State);

            await runtime.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // A broken build never replies to Close; do not turn the timeout
            // failure into a second, indefinite hang during cleanup.
            try
            {
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
            }
        }
    }

    [Fact]
    public async Task GetFiberIsSafeUnderConcurrentLoadAndDispose()
    {
        await using var runtime = new Runtime();
        var key = new Key<int>("poll-key");
        var anchor = await runtime.LoadAsync(new DelegateComponent(
            "anchor",
            context =>
            {
                context.Provide(key, 1);
                return ValueTask.FromResult<Cleanup?>(null);
            },
            provide: [key]));

        using var stop = new CancellationTokenSource();
        var polling = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = runtime.GetFiber(anchor.Id);
                _ = runtime.Snapshot();
            }
        });

        for (var i = 0; i < 80; i++)
        {
            var fiber = await runtime.LoadAsync(new DelegateComponent(
                $"worker-{i}",
                _ => ValueTask.FromResult<Cleanup?>(null)));
            await runtime.DisposeAsync(fiber);
        }

        stop.Cancel();
        await polling.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Active, anchor.State);
    }
}
