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
    public async Task HigherCompatibleProviderRebindsRunningConsumer()
    {
        // Migration review P1-1: first activation picks the highest compatible
        // version; a later higher version must cause the consumer to withdraw
        // and re-activate instead of staying on its stale committed binding.
        var v14 = new VersionedKey<string>("storage", "1.4.0");
        var v18 = new VersionedKey<string>("storage", "1.8.0");
        var need = new VersionedKey<string>("storage", "1.0.0", "[1.0,2.0)");

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(Provider("old", v14, "legacy"));

        var observed = string.Empty;
        var consumer = await runtime.LoadAsync(new Consumer(
            "consumer", context => observed = context.Require(need), need));
        Assert.Equal("legacy", observed);

        await runtime.LoadAsync(Provider("new", v18, "modern"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (observed != "modern" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal("modern", observed);
        Assert.Equal(FiberState.Active, consumer.State);
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

    [Fact]
    public async Task SameRealmVersionedCycleIsStillRejected()
    {
        var a = new VersionedKey<string>("cycle-a", "1.0.0");
        var b = new VersionedKey<string>("cycle-b", "1.0.0");
        var needA = new VersionedKey<string>("cycle-a", "1.0.0", "[1.0,2.0)");
        var needB = new VersionedKey<string>("cycle-b", "1.0.0", "[1.0,2.0)");
        // VersionedRelay provides one versioned key and requires the other.

        await using var runtime = new Gocordis.Core.Runtime();
        await Assert.ThrowsAsync<DependencyCycleException>(async () =>
        {
            await runtime.LoadAsync(new VersionedRelay("x", a, needB));
            await runtime.LoadAsync(new VersionedRelay("y", b, needA));
        });
    }

    [Fact]
    public async Task CrossRealmVersionedKeysDoNotFormPhantomCycleEdges()
    {
        // Realm X provides "svc"; Default-realm consumer requires "svc" with a
        // peer range. The bind is realm-scoped (no ancestor fallback), so the
        // consumer stays Pending — and the cycle graph must not claim a cycle.
        var provided = new VersionedKey<string>("svc", "1.0.0");
        var required = new VersionedKey<string>("svc", "1.0.0", "[1.0,2.0)");

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(Provider("x-provider", provided, "x"), realm: new Realm("realm-x"));
        var consumer = await runtime.LoadAsync(new Consumer(
            "default-consumer", context => context.Require(required), required));

        Assert.Equal(FiberState.Active, provider.State);
        Assert.Equal(FiberState.Pending, consumer.State);
        Assert.Null(consumer.Failure);
    }

    /// <summary>Provides one versioned key and requires another, so a pair of
    /// relays forms a versioned dependency cycle when both live in one realm.</summary>
    private sealed class VersionedRelay : Component
    {
        private readonly VersionedKey<string> _provide;
        private readonly IVersionedKey _require;

        public VersionedRelay(string name, VersionedKey<string> provide, IVersionedKey require)
        {
            Name = name;
            _provide = provide;
            _require = require;
        }

        public override string Name { get; }
        public override IReadOnlyList<ICapabilityKey> Inject() => [_require];
        public override IReadOnlyList<ICapabilityKey> Provide() => [_provide];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Require(_require as VersionedKey<string> ?? new VersionedKey<string>("unused", "1.0.0"));
            context.Provide(_provide, $"{Name}-value");
            return ValueTask.FromResult<Cleanup?>(null);
        }
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
