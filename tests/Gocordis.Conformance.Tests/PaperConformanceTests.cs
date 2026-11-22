using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Executable counterparts of the paper entries in docs/migration/semantic-mapping.md.
/// These assert observable behavior only, never Go signatures or C# internals.
/// </summary>
public sealed class PaperConformanceTests
{
    [Fact]
    public async Task C06_StrictLifoRecoveryLeavesNoResidue()
    {
        var order = new List<string>();
        await using var runtime = new Runtime();
        var component = new ProbeComponent(
            "effects",
            async context =>
            {
                foreach (var index in Enumerable.Range(1, 4))
                {
                    var current = index;
                    await context.EffectAsync(
                        () =>
                        {
                            order.Add($"install:{current}");
                            return ValueTask.CompletedTask;
                        },
                        () =>
                        {
                            order.Add($"undo:{current}");
                            return ValueTask.CompletedTask;
                        });
                }

                return null;
            });

        var fiber = await runtime.LoadAsync(component);
        await runtime.DisposeAsync(fiber);

        Assert.Equal(
            new[] { "undo:4", "undo:3", "undo:2", "undo:1" },
            order.Where(entry => entry.StartsWith("undo")).ToArray());
        Assert.Equal(4, order.Count(entry => entry.StartsWith("install")));
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    [Fact]
    public async Task Thm70_ProviderEpisodeContainsConsumerEpisode()
    {
        var key = new Key<int>("shared");
        var consumerApplied = false;

        await using var runtime = new Runtime();
        var consumer = new ProbeComponent(
            "consumer",
            context =>
            {
                consumerApplied = true;
                Assert.Equal(9, context.Require(key));
                return ValueTask.FromResult<Cleanup?>(null);
            },
            inject: [key]);

        // The consumer is declared first but cannot activate: provider must exist first.
        var consumerFiber = await runtime.LoadAsync(consumer);
        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.False(consumerApplied);

        var provider = new ProbeComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 9);
                return null;
            },
            provide: [key]);
        var providerFiber = await runtime.LoadAsync(provider);

        Assert.Equal(FiberState.Active, providerFiber.State);
        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.True(consumerApplied);
    }

    [Fact]
    public async Task C22_DependencyLossIsWithdrawalNotFailure()
    {
        var key = new Key<int>("volatile");
        await using var runtime = new Runtime();
        var provider = new ProbeComponent(
            "provider",
            async context =>
            {
                context.Provide(key, 1);
                return null;
            },
            provide: [key]);
        var consumer = new ProbeComponent(
            "consumer",
            async context =>
            {
                context.Require(key);
                return null;
            },
            inject: [key]);

        var providerFiber = await runtime.LoadAsync(provider);
        var consumerFiber = await runtime.LoadAsync(consumer);
        await runtime.DisposeAsync(providerFiber);

        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.Null(consumerFiber.Failure);

        await runtime.LoadAsync(new ProbeComponent(
            "provider2",
            async context =>
            {
                context.Provide(key, 2);
                return null;
            },
            provide: [key]));

        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.Null(consumerFiber.Failure);
    }

    [Fact]
    public async Task C13_ObservationEquivalenceIgnoresGenerativeFiberIds()
    {
        static async Task<RuntimeSnapshot> BuildSnapshot()
        {
            var runtime = new Runtime();
            var key = new Key<int>("shared");
            await runtime.LoadAsync(new ProbeComponent(
                "provider",
                async context =>
                {
                    context.Provide(key, 1);
                    return null;
                },
                provide: [key]));
            await runtime.LoadAsync(new ProbeComponent(
                "consumer",
                async context =>
                {
                    context.Require(key);
                    return null;
                },
                inject: [key]));
            return runtime.Snapshot();
        }

        var first = await BuildSnapshot();
        var second = await BuildSnapshot();

        Assert.NotEqual(
            first.Fibers.Select(f => f.Id),
            second.Fibers.Select(f => f.Id));
        Assert.True(ObservationEquivalence.Equivalent(first, second));
    }

    private sealed class ProbeComponent : Component
    {
        private readonly Func<Context, ValueTask<Cleanup?>> _apply;

        public ProbeComponent(
            string name,
            Func<Context, ValueTask<Cleanup?>> apply,
            IReadOnlyList<ICapabilityKey>? inject = null,
            IReadOnlyList<ICapabilityKey>? provide = null)
        {
            Name = name;
            _apply = apply;
            InjectKeys = inject ?? [];
            ProvideKeys = provide ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

        public override IReadOnlyList<ICapabilityKey> Provide() => ProvideKeys;

        private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

        private IReadOnlyList<ICapabilityKey> ProvideKeys { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) => _apply(context);
    }
}
