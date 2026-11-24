using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Paper §3.4.2: distinct declared coeffects over disjoint providers commute.
/// This is the provider/declaration obligation in CLR form — binding order must
/// not change the observable committed view. It is bounded behavioral evidence,
/// not a proof for arbitrary provider operations.
/// </summary>
public sealed class CoeffectCommutativityTests
{
    private enum Op
    {
        ProviderA,
        ProviderB,
        ConsumerA,
        ConsumerB,
    }

    [Fact]
    public async Task DisjointDeclaredCoeffectsCommuteAcrossTwoLoadOrders()
    {
        var first = Run([Op.ProviderA, Op.ProviderB, Op.ConsumerA, Op.ConsumerB]);
        var second = Run([Op.ProviderB, Op.ProviderA, Op.ConsumerB, Op.ConsumerA]);
        var forward = await first;
        var reversed = await second;

        Assert.All(forward.Fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
        Assert.All(reversed.Fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
        Assert.True(ObservationEquivalence.Equivalent(forward, reversed));
    }

    private static async Task<RuntimeSnapshot> Run(IEnumerable<Op> ops)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var a = new Key<string>("commute/a");
        var b = new Key<string>("commute/b");
        foreach (var op in ops)
        {
            switch (op)
            {
                case Op.ProviderA:
                    await runtime.LoadAsync(new Provider("provider-a", a, "a"));
                    break;
                case Op.ProviderB:
                    await runtime.LoadAsync(new Provider("provider-b", b, "b"));
                    break;
                case Op.ConsumerA:
                    await runtime.LoadAsync(new Consumer(a, "consumer-a"));
                    break;
                case Op.ConsumerB:
                    await runtime.LoadAsync(new Consumer(b, "consumer-b"));
                    break;
            }
        }

        return runtime.Snapshot();
    }

    private sealed class Provider : Component
    {
        private readonly Key<string> _key;
        private readonly string _value;
        public Provider(string name, Key<string> key, string value)
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
        private readonly Key<string> _key;
        public Consumer(Key<string> key, string name)
        {
            _key = key;
            Name = name;
        }

        public override string Name { get; }
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Assert.Equal(_key.Name.EndsWith("/a", StringComparison.Ordinal) ? "a" : "b",
                context.Require(_key));
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
