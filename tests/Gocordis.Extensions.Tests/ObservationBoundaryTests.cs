using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Final fix batch: observation anchor semantics and log-owned subscription
/// lifetime. These are observable contracts of the public API.
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
    public void SubscribeStaleAnchorStillThrows()
    {
        using var log = new ObservationLog<int>(ringCapacity: 2, queueLimit: 8);
        log.Publish(1);
        log.Publish(2);
        log.Publish(3);

        Assert.Throws<SequenceTooOldException>(() => log.Subscribe(1));
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
