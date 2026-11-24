using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Batch C gap closure for Broker (named providers, selection policies,
/// reversible registration) and Observe (sequence-anchored replay, stale
/// anchor rejection, slow-subscriber overflow termination).
/// </summary>
public sealed class BrokerObserveGapTests
{
    [Fact]
    public void BrokerNamedProvidersSupportSelectionAndReversibleRegistration()
    {
        var broker = new Broker<string>();
        var seen = new List<string>();

        using var first = broker.Register("v1", "A");
        using var second = broker.Register("v2", "B");

        Assert.Equal(new[] { "v1", "v2" }, broker.Names());
        Assert.Equal(new[] { "A", "B" }, broker.Providers);
        Assert.Equal("A", broker.First());
        Assert.Equal("A", broker.RoundRobin());
        Assert.Equal("B", broker.RoundRobin());
        Assert.Equal("A", broker.RoundRobin());

        broker.Call(provider => seen.Add(provider));
        Assert.Single(seen);
        Assert.Contains(broker.Call(provider => provider), new[] { "A", "B" }); // Call<TResult>

        Assert.Throws<InvalidOperationException>(() => broker.Register("v1", "duplicate"));
        Assert.Equal(new[] { "v1", "v2" }, broker.Names());

        second.Dispose(); // owner handle retreats from the routing set
        Assert.Equal(new[] { "v1" }, broker.Names());
        Assert.Equal("A", broker.First());
    }

    [Fact]
    public void BrokerWithoutProviderThrows()
    {
        var broker = new Broker<int>();
        Assert.Throws<BrokerNoProviderException>(() => broker.Call(_ => { }));
        Assert.Throws<BrokerNoProviderException>(() => broker.First());
        Assert.Throws<BrokerNoProviderException>(() => broker.RoundRobin());
        Assert.Throws<BrokerNoProviderException>(() => broker.RoundRobin(0)); // legacy overload too
    }

    [Fact]
    public void ObserveReplaysFromAnchorAndRejectsStaleAnchor()
    {
        using var log = new ObservationLog<int>(ringCapacity: 4);
        for (var index = 1; index <= 6; index++)
        {
            log.Publish(index);
        }

        Assert.Equal(3, log.OldestSequence); // 4-slot ring retains [3,4,5,6]
        Assert.Equal(6, log.LatestSequence);
        Assert.Throws<SequenceTooOldException>(() => log.Subscribe(1));

        // `from` is the last consumed sequence: replay starts strictly after it.
        using var subscription = log.Subscribe(4);
        Assert.True(subscription.TryRead(out var first));
        Assert.Equal(5, first.Sequence);
        Assert.Equal(5, first.Item);
        Assert.True(subscription.TryRead(out var second));
        Assert.Equal(6, second.Sequence);
        Assert.False(subscription.TryRead(out _));

        log.Publish(7); // live event flows after replay is drained
        Assert.True(subscription.TryRead(out var live));
        Assert.Equal(7, live.Sequence);
        Assert.Equal(7, live.Item);
        Assert.Null(subscription.Error);
    }

    [Fact]
    public async Task ObserveSlowSubscriberOverflowTerminates()
    {
        using var log = new ObservationLog<int>(ringCapacity: 1024, queueLimit: 8);
        using var subscription = log.Subscribe(0);

        for (var index = 1; index <= 20; index++)
        {
            log.Publish(index);
        }

        Assert.IsType<SubscriptionOverflowException>(subscription.Error);

        var drained = 0;
        while (subscription.TryRead(out _))
        {
            drained++;
        }
        Assert.True(drained <= 8, "overflowed subscriber must keep a bounded backlog");

        await Assert.ThrowsAsync<SubscriptionOverflowException>(
            async () =>
            {
                await foreach (var _ in subscription.EventsAsync())
                {
                }
            });
    }
}
