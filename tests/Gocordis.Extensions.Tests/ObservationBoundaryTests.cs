using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Public contracts of the observation log: sequence anchors, bounded replay
/// and log-owned subscription lifetime.
/// </summary>
public sealed class ObservationBoundaryTests
{
    [Fact]
    public async Task SubscribeZeroStartsAtOldestRetainedEvent()
    {
        using var log = new ObservationLog<int>(ringCapacity: 2, queueLimit: 8);
        log.Publish(1);
        log.Publish(2);
        log.Publish(3);

        Assert.Equal(2, log.OldestSequence);
        using var subscription = log.Subscribe(0); // documented: oldest retained
        var seen = new List<(long Sequence, int Item)>();
        await foreach (var ev in subscription.EventsAsync())
        {
            seen.Add(ev);
            if (seen.Count == 2)
            {
                break;
            }
        }

        Assert.Equal([(2L, 2), (3L, 3)], seen);
    }

    [Fact]
    public async Task SubscribeLastConsumedAnchorReplaysOnlyNewerEvents()
    {
        using var log = new ObservationLog<int>(ringCapacity: 2, queueLimit: 8);
        log.Publish(1);
        log.Publish(2);
        log.Publish(3);

        Assert.Equal(2, log.OldestSequence);
        // from = last consumed sequence: event 1 is not replayed, 2 and 3 are.
        using var subscription = log.Subscribe(1);
        var seen = new List<(long Sequence, int Item)>();
        await foreach (var ev in subscription.EventsAsync())
        {
            seen.Add(ev);
            if (seen.Count == 2)
            {
                break;
            }
        }

        Assert.Equal([(2L, 2), (3L, 3)], seen);
    }

    [Fact]
    public void SubscribeAnchorAtLatestIsLiveOnly()
    {
        using var log = new ObservationLog<int>(ringCapacity: 4, queueLimit: 8);
        log.Publish(1);
        log.Publish(2);
        using var subscription = log.Subscribe(log.LatestSequence);

        Assert.False(subscription.TryRead(out _));
        log.Publish(3);
        Assert.True(subscription.TryRead(out var live));
        Assert.Equal(3L, live.Sequence);
        Assert.Equal(3, live.Item);
    }

    [Fact]
    public void SubscribeStaleAnchorStillThrowsWhenTheGapIsUnreplayable()
    {
        using var log = new ObservationLog<int>(ringCapacity: 2, queueLimit: 8);
        log.Publish(1);
        log.Publish(2);
        log.Publish(3);
        log.Publish(4);
        Assert.Equal(3, log.OldestSequence); // ring retains [3,4]

        var error = Assert.Throws<SequenceTooOldException>(() => log.Subscribe(1));
        Assert.Equal(1, error.Requested);
        Assert.Equal(3, error.RetainedFrom);

        // A one-behind anchor is NOT stale: event 2 was consumed, 3/4 replay.
        using var recovered = log.Subscribe(2);
        Assert.True(recovered.TryRead(out var first));
        Assert.Equal(3L, first.Sequence);
        Assert.True(recovered.TryRead(out var second));
        Assert.Equal(4L, second.Sequence);
    }

    [Fact]
    public void ReplayLargerThanQueueLimitIsFullySpooled()
    {
        using var log = new ObservationLog<int>(ringCapacity: 16, queueLimit: 4);
        for (var index = 1; index <= 10; index++)
        {
            log.Publish(index);
        }

        using var subscription = log.Subscribe(0);
        for (long expected = 1; expected <= 10; expected++)
        {
            Assert.True(subscription.TryRead(out var ev));
            Assert.Equal(expected, ev.Sequence);
        }

        Assert.False(subscription.TryRead(out _));
        Assert.Null(subscription.Error);
    }

    [Fact]
    public async Task DisposingLogCompletesActiveSubscriptions()
    {
        var log = new ObservationLog<int>(ringCapacity: 8, queueLimit: 8);
        var subscription = log.Subscribe(0);
        var seen = new List<long>();

        var consuming = Task.Run(async () =>
        {
            await foreach (var ev in subscription.EventsAsync())
            {
                seen.Add(ev.Sequence);
            }
        });

        log.Publish(1);
        await Task.Delay(20);
        log.Dispose();

        await consuming.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([1L], seen);
    }
}
