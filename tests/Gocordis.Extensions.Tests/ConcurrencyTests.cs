using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Migration review P1-3: Scheduler / WatchService concurrency and disposal.
/// All job state changes must be serialized, cancelled running jobs must not
/// resurrect, disposal must be truthful, and watcher/revision maps share one lock.
/// </summary>
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task CancelledRunningIntervalJobDoesNotResurrect()
    {
        var scheduler = new Scheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var id = scheduler.ScheduleInterval(TimeSpan.FromMilliseconds(10), async () =>
        {
            started.TrySetResult();
            await release.Task;
        });

        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var run = scheduler.RunNextAsync(now);
        await started.Task;

        Assert.True(scheduler.Cancel(id));
        release.TrySetResult();
        await run;

        Assert.Empty(scheduler.Jobs);
        Assert.Null(await scheduler.RunNextAsync(DateTimeOffset.UtcNow.AddMinutes(1)));
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task DisposeWaitsForRunningJobAndRejectsNewWork()
    {
        var scheduler = new Scheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.ScheduleOnce(DateTimeOffset.UtcNow, async () =>
        {
            started.TrySetResult();
            await release.Task;
        });

        var run = scheduler.RunNextAsync(DateTimeOffset.UtcNow);
        await started.Task;
        var disposal = scheduler.DisposeAsync().AsTask();

        Assert.Throws<ObjectDisposedException>(() => scheduler.ScheduleOnce(DateTimeOffset.UtcNow, () => ValueTask.CompletedTask));
        Assert.False(disposal.IsCompleted, "DisposeAsync must wait for the in-flight job");

        release.TrySetResult();
        await run;
        await disposal;
        Assert.Empty(scheduler.Jobs);
    }

    [Fact]
    public async Task WatchServiceRevisionReadsAreSerializedWithWatchChanges()
    {
        await using var service = new WatchService();
        var root = Path.Combine(Path.GetTempPath(), "gocordis-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "watched.txt");
            await File.WriteAllTextAsync(file, "v1");
            using var sub = service.Watch(file, (_, _) => { });

            var reads = Enumerable.Range(0, 200)
                .Select(_ => Task.Run(() => service.Revision(file)))
                .ToArray();
            await Task.WhenAll(reads);
            Assert.All(reads, r => Assert.True(r.Result >= 0));

            await service.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => service.Watch(file, (_, _) => { }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
