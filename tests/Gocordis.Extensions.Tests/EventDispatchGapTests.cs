using Gocordis.Core;
using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Batch C gap closure for the Event dispatcher: JS-style value bail, the
/// middleware-chain waterfall, dispatch-time liveness/cancellation and
/// registration-ordered parallel error aggregation.
/// </summary>
public sealed class EventDispatchGapTests
{
    [Fact]
    public async Task BailValueStopsAtFirstPresentResult()
    {
        var events = new EventDispatcher();
        var key = new EventKey<string>("bailvalue.first");
        var seen = new List<string>();

        using var first = events.OnValue<string, string>(key, value =>
        {
            seen.Add("a");
            return ("from-a", true);
        });
        using var second = events.OnValue<string, string>(key, value =>
        {
            seen.Add("b");
            return ("from-b", true);
        });

        var result = await events.EmitBailValueAsync<string, string>(key, "x");
        Assert.True(result.Present);
        Assert.Equal("from-a", result.Value);
        Assert.Equal(new[] { "a" }, seen);
    }

    [Fact]
    public async Task BailValueContinuesWhenResultIsAbsent()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("bailvalue.absent");
        var seen = new List<string>();

        using var first = events.OnValue<int, string>(key, _ =>
        {
            seen.Add("a");
            return ("a", false); // JS undefined equivalent
        });
        using var second = events.OnValue<int, string>(key, _ =>
        {
            seen.Add("b");
            return ("b", true);
        });
        using var plain = events.On(key, _ =>
        {
            seen.Add("plain");
            return ValueTask.CompletedTask;
        });

        var result = await events.EmitBailValueAsync<int, string>(key, 1);
        Assert.True(result.Present);
        Assert.Equal("b", result.Value);
        Assert.Equal(new[] { "a", "b" }, seen); // plain On never participates
    }

    [Fact]
    public async Task BailValueSkipsHandlerDisposedInsideSnapshot()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("bailvalue.liveness");
        var seen = new List<string>();
        IDisposable? victim = null;

        using var killer = events.OnValue<int, string>(key, _ =>
        {
            seen.Add("killer");
            victim!.Dispose();
            return ("killer", false);
        });
        victim = events.OnValue<int, string>(key, _ =>
        {
            seen.Add("victim");
            return ("victim", true);
        });

        var result = await events.EmitBailValueAsync<int, string>(key, 1);
        Assert.False(result.Present);
        Assert.Equal(new[] { "killer" }, seen);
    }

    [Fact]
    public async Task DispatchSkipsHandlerDisposedInsideSnapshot()
    {
        var events = new EventDispatcher();
        var key = new EventKey<string>("dispatch.liveness");
        var seen = new List<string>();
        IDisposable? victim = null;

        using var killer = events.On(key, _ =>
        {
            seen.Add("killer");
            victim!.Dispose();
            return ValueTask.CompletedTask;
        });
        victim = events.On(key, _ =>
        {
            seen.Add("victim");
            return ValueTask.CompletedTask;
        });

        Assert.Empty(await events.EmitSerialAsync(key, "x"));
        Assert.Equal(new[] { "killer" }, seen);
    }

    [Fact]
    public async Task DispatchCancellationTokenStopsBeforeNextHandler()
    {
        var events = new EventDispatcher();
        var key = new EventKey<string>("dispatch.cancel");
        var ran = 0;
        using var handler = events.On(key, _ =>
        {
            ran++;
            return ValueTask.CompletedTask;
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => events.EmitSerialAsync(key, "x", cts.Token));
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task WaterfallChainRunsBeforeNextAfterInOrder()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("chain.order");
        var seen = new List<string>();

        async ValueTask A(Func<ValueTask> next)
        {
            seen.Add("a-before");
            await next();
            seen.Add("a-after");
        }

        async ValueTask B(Func<ValueTask> next)
        {
            seen.Add("b-before");
            await next();
            seen.Add("b-after");
        }

        using var a = events.OnWaterfall(key, (_, next) => A(next));
        using var b = events.OnWaterfall(key, (_, next) => B(next));
        using var c = events.OnWaterfall(key, (_, _) =>
        {
            seen.Add("c");
            return ValueTask.CompletedTask;
        });

        await events.EmitWaterfallChainAsync(key, 1);
        Assert.Equal(new[] { "a-before", "b-before", "c", "b-after", "a-after" }, seen);
    }

    [Fact]
    public async Task WaterfallChainShortCircuitsWhenNextIsNotCalled()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("chain.short");
        var seen = new List<string>();

        using var a = events.OnWaterfall(key, (_, _) =>
        {
            seen.Add("a");
            return ValueTask.CompletedTask; // no next(): handled here
        });
        using var b = events.OnWaterfall(key, (_, next) =>
        {
            seen.Add("b");
            return next();
        });

        await events.EmitWaterfallChainAsync(key, 1);
        Assert.Equal(new[] { "a" }, seen);
    }

    [Fact]
    public async Task WaterfallChainRejectsDuplicateNext()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("chain.duplicate");
        var seen = new List<string>();

        async ValueTask A(Func<ValueTask> next)
        {
            seen.Add("a-first");
            await next();
            seen.Add("a-second");
            await next(); // contract violation
        }

        using var a = events.OnWaterfall(key, (_, next) => A(next));
        using var b = events.OnWaterfall(key, (_, next) =>
        {
            seen.Add("b");
            return next();
        });

        await Assert.ThrowsAsync<WaterfallNextTwiceException>(
            () => events.EmitWaterfallChainAsync(key, 1).AsTask());
        Assert.Equal(new[] { "a-first", "b", "a-second" }, seen);
    }

    [Fact]
    public async Task ParallelAggregatesErrorsInRegistrationOrder()
    {
        var events = new EventDispatcher();
        var key = new EventKey<int>("parallel.order");

        async ValueTask First()
        {
            await Task.Delay(30); // finishes last but was registered first
            throw new InvalidOperationException("first");
        }

        using var first = events.On(key, _ => First());
        using var second = events.On(key, _ => throw new InvalidOperationException("second"));

        var error = await Assert.ThrowsAsync<ParallelDispatchException>(
            () => events.EmitParallelAsync(key, 1));
        Assert.Equal(new[] { "first", "second" }, error.Errors.Select(ex => ex.Message));
    }
}
