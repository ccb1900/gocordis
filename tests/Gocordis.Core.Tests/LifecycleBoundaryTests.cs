using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

/// <summary>
/// Lifecycle boundary regressions (migration review P0-1 / P0-2):
/// a Context bound to one activation must stop mutating the Runtime as soon
/// as teardown begins, and a gate lease must not be inherited by background
/// tasks as a permanent right to bypass lifecycle serialization.
/// </summary>
public sealed class LifecycleBoundaryTests
{
    [Fact]
    public async Task StaleContextCannotMutateRuntimeDuringTeardown()
    {
        await using var runtime = new Runtime();
        var provided = new Key<int>("provided");
        var second = new Key<int>("second");
        var evt = new EventKey<int>("evt");

        await runtime.LoadAsync(new DelegateComponent(
            "event-provider",
            context =>
            {
                context.Provide(evt, 0);
                return ValueTask.FromResult<Cleanup?>(null);
            },
            provide: [evt]));

        Context? captured = null;
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var fiber = await runtime.LoadAsync(new DelegateComponent(
            "owner",
            context =>
            {
                captured = context;
                context.Provide(provided, 1);
                return ValueTask.FromResult<Cleanup?>(async () =>
                {
                    cleanupEntered.TrySetResult();
                    await releaseCleanup.Task;
                });
            },
            provide: [provided, second],
            inject: [evt]));

        var disposal = runtime.DisposeAsync(fiber);
        await cleanupEntered.Task; // teardown started: Retired + canceled, generation unchanged

        var stale = captured!;
        Assert.Throws<DivertedException>(() => stale.Provide(second, 2));
        Assert.Throws<DivertedException>(() => stale.OnAsync(evt, _ => ValueTask.CompletedTask));
        await Assert.ThrowsAsync<DivertedException>(async () =>
            await stale.LoadChildAsync(new DelegateComponent("child", _ => ValueTask.FromResult<Cleanup?>(null))));

        releaseCleanup.TrySetResult();
        await disposal;
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task BackgroundTaskInheritingExecutionContextCannotBypassGate()
    {
        await using var runtime = new Runtime();
        Task? inherited = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await runtime.LoadAsync(new DelegateComponent("spawner", async context =>
        {
            // Capture execution context with the gate lease set (we are inside Apply).
            inherited = Task.Run(async () =>
            {
                await release.Task;
                // Runs long after the outer LoadAsync completed. The inherited
                // lease must have been revoked, so this goes through the gate.
                for (var i = 0; i < 8; i++)
                {
                    var child = await runtime.LoadAsync(
                        new DelegateComponent($"bg-{i}", _ => ValueTask.FromResult<Cleanup?>(null)));
                    await runtime.DisposeAsync(child);
                }
            });
            await Task.CompletedTask;
            return null;
        }));

        release.TrySetResult();
        await inherited!;

        var fibers = runtime.Snapshot().Fibers;
        Assert.DoesNotContain(fibers, f => f.Name.StartsWith("bg-", StringComparison.Ordinal));
        Assert.Contains(fibers, f => f.Name == "spawner" && f.State == FiberState.Active);
    }
}
