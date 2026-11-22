using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>Paper §6.6: version-aware linking, peer ranges, drift and collision.</summary>
public sealed class VersioningTests
{
    [Fact]
    public async Task PeerRangeSelectsTheHighestCompatibleProvider()
    {
        var v1 = new VersionedKey<string>("storage", "1.4.0");
        var v2 = new VersionedKey<string>("storage", "2.1.0");
        var consumerKey = new VersionedKey<string>("storage", "1.0.0", "[1.0,2.0)");

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(Provider("old", v1, "legacy"));
        await runtime.LoadAsync(Provider("new", v2, "modern"));

        var observed = string.Empty;
        var consumer = await runtime.LoadAsync(new Consumer(
            "consumer-v1",
            context => observed = context.Require(consumerKey),
            consumerKey));

        Assert.Equal(FiberState.Active, consumer.State);
        Assert.Equal("legacy", observed);
    }

    [Fact]
    public async Task MultipleVersionsOfOneInterfaceCoexist()
    {
        var v1 = new VersionedKey<string>("storage", "1.0.0");
        var v2 = new VersionedKey<string>("storage", "2.0.0");
        var needV1 = new VersionedKey<string>("storage", "1.0.0", "[1.0,2.0)");
        var needV2 = new VersionedKey<string>("storage", "2.0.0", "[2.0,3.0)");

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(Provider("v1", v1, "one"));
        await runtime.LoadAsync(Provider("v2", v2, "two"));

        var seen = new List<string>();
        await runtime.LoadAsync(new Consumer("c1", context => seen.Add(context.Require(needV1)), needV1));
        await runtime.LoadAsync(new Consumer("c2", context => seen.Add(context.Require(needV2)), needV2));

        Assert.Equal(new[] { "one", "two" }, seen);
        Assert.Equal(4, runtime.Snapshot().Fibers.Count);
    }

    [Fact]
    public async Task InterfaceDriftLeavesConsumerPendingInsteadOfFailing()
    {
        var providerKey = new VersionedKey<string>("storage", "3.0.0");
        var consumerKey = new VersionedKey<string>("storage", "1.0.0", "[1.0,2.0)");

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(Provider("drifted", providerKey, "incompatible"));
        var consumer = await runtime.LoadAsync(new Consumer("consumer", context => context.Require(consumerKey), consumerKey));

        Assert.Equal(FiberState.Pending, consumer.State);
        Assert.Null(consumer.Failure);
    }

    [Fact]
    public async Task KeyCollisionAcrossTypesDoesNotSatisfyADependency()
    {
        var textProvider = new VersionedKey<string>("shared", "1.0.0");
        var numberConsumer = new VersionedKey<int>("shared", "1.0.0");

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(Provider("text", textProvider, "text"));
        var consumer = await runtime.LoadAsync(new Consumer(
            "number-consumer",
            context => context.Require(numberConsumer),
            numberConsumer));

        Assert.Equal(FiberState.Pending, consumer.State);
    }


    [Fact]
    public async Task VersionedBindingIsRemovedWhenProviderIsDisposed()
    {
        var key = new VersionedKey<string>("ephemeral", "1.0.0");
        var consumerKey = new VersionedKey<string>("ephemeral", "1.0.0", "[1.0,2.0)");
        await using var runtime = new Gocordis.Core.Runtime();

        var provider = await runtime.LoadAsync(Provider("p", key, "value"));
        var consumer = await runtime.LoadAsync(new Consumer("c", context => context.Require(consumerKey), consumerKey));
        Assert.Equal(FiberState.Active, consumer.State);

        await runtime.DisposeAsync(provider);
        Assert.Equal(FiberState.Pending, consumer.State);
        Assert.Null(consumer.Failure);
    }

    private static Component Provider(string name, VersionedKey<string> key, string value) =>
        new Producer(name, key, value);

    private sealed class Producer : Component
    {
        private readonly VersionedKey<string> _key;
        private readonly string _value;

        public Producer(string name, VersionedKey<string> key, string value)
        {
            Name = name;
            _key = key;
            _value = value;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _value);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Consumer : Component
    {
        private readonly Action<Context> _apply;
        private readonly IVersionedKey _key;

        public Consumer(string name, Action<Context> apply, IVersionedKey key)
        {
            Name = name;
            _apply = apply;
            _key = key;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _apply(context);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
