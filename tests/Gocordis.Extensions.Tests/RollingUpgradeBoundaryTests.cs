using Gocordis.Core;
using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Paper §6.2: rolling upgrade is not the same claim as "a Broker exists".
/// Three separate gates: member swap under concurrent calls, in-flight call
/// draining at the endpoint linearization point, and failed-upgrade recovery.
/// </summary>
public sealed class RollingUpgradeBoundaryTests
{
    [Fact]
    public async Task BrokerMemberSwapKeepsConcurrentConsumersTransparent()
    {
        var broker = new Broker<string>();
        var v1 = broker.Register("v1", "provider-v1");
        using var start = new CancellationTokenSource();
        var calls = new List<string>();
        bool Saw(string value)
        {
            lock (calls)
            {
                return calls.Contains(value, StringComparer.Ordinal);
            }
        }

        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!start.IsCancellationRequested)
            {
                var value = broker.RoundRobin();
                lock (calls)
                {
                    calls.Add(value);
                }
            }
        })).ToArray();

        await WaitUntilAsync(() => Saw("provider-v1"));
        using var v2 = broker.Register("v2", "provider-v2");
        v1.Dispose();
        // Prove a concurrent consumer actually observed the swapped route.
        await WaitUntilAsync(() => Saw("provider-v2"));
        start.Cancel();
        await Task.WhenAll(callers);

        Assert.All(calls, value => Assert.Contains(value, new[] { "provider-v1", "provider-v2" }));
        Assert.Equal(["v2"], broker.Names());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.True(predicate(), "concurrent consumer did not reach the expected route");
    }

    [Fact]
    public void FailedBrokerUpgradeLeavesThePreviousRouteSetUntouched()
    {
        var broker = new Broker<string>();
        using var v1 = broker.Register("v1", "provider-v1");

        Assert.Throws<InvalidOperationException>(() => broker.Register("v1", "provider-v2"));

        Assert.Equal(["v1"], broker.Names());
        Assert.Equal("provider-v1", broker.First());
    }

    [Fact]
    public async Task EndpointWithdrawalDrainsAnInFlightCallBeforeReleasingTheResource()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var endpoint = new DelegatingCapabilityEndpoint(
            new CapabilityId("org.example", "rolling"),
            new CapabilityContract(new CapabilityId("org.example", "rolling"), CapabilityValueKind.Json, null),
            async (request, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                return request;
            },
            () => { disposed = true; return ValueTask.CompletedTask; });

        var call = endpoint.InvokeAsync(CapabilityEnvelope.Json("{}"), CancellationToken.None).AsTask();
        await entered.Task;
        var withdrawal = endpoint.DisposeAsync().AsTask();

        Assert.False(withdrawal.IsCompleted, "provider resource was released under an in-flight call");
        Assert.False(disposed);
        release.TrySetResult();
        var response = await call;
        await withdrawal;

        Assert.Equal("{}", response.AsText());
        Assert.True(disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await endpoint.InvokeAsync(CapabilityEnvelope.Json("{}"), CancellationToken.None));
    }
}
