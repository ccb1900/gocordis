using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>The paper theorems not yet represented in C# executable specs.</summary>
public sealed class TheoremTests
{
    [Fact]
    public async Task Thm64_PreservationHoldsAfterLegalOperations()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var keys = Enumerable.Range(0, 6).Select(index => new Key<int>($"k{index}")).ToArray();
        var fibers = new List<Fiber>();

        foreach (var key in keys)
        {
            var captured = key;
            fibers.Add(await runtime.LoadAsync(new Probe(
                $"p-{captured.Name}",
                context =>
                {
                    context.Provide(captured, 1);
                    return Task.CompletedTask;
                },
                provide: [captured])));
        }

        for (var round = 0; round < 5; round++)
        {
            var index = round % fibers.Count;
            await runtime.DisposeAsync(fibers[index]);
            fibers[index] = await runtime.LoadAsync(new Probe(
                $"p-{keys[index].Name}",
                context =>
                {
                    context.Provide(keys[index], 1);
                    return Task.CompletedTask;
                },
                provide: [keys[index]]));
            AssertWellFormed(runtime);
        }
    }

    [Fact]
    public async Task Thm68_RecoveryIsExactUpToObservation()
    {
        var initial = new Gocordis.Core.Runtime().Snapshot();
        var key = new Key<int>("recover");
        var cleanups = 0;

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(new Probe(
            "provider",
            context =>
            {
                context.Provide(key, 1);
                return Task.CompletedTask;
            },
            provide: [key]));
        var consumer = await runtime.LoadAsync(new Probe(
            "consumer",
            async context =>
            {
                context.Require(key);
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        cleanups++;
                        return ValueTask.CompletedTask;
                    });
            },
            inject: [key]));

        await runtime.DisposeAsync(consumer);
        await runtime.DisposeAsync(provider);

        Assert.Equal(1, cleanups);
        Assert.True(ObservationEquivalence.Equivalent(initial, runtime.Snapshot()));
    }

    [Fact]
    public async Task Thm73_ProgressReachesQuiescenceFromReverseOrder()
    {
        var a = new Key<int>("a");
        var b = new Key<int>("b");
        var c = new Key<int>("c");
        await using var runtime = new Gocordis.Core.Runtime();

        var consumer = await runtime.LoadAsync(new Probe(
            "consumer",
            async context =>
            {
                context.Require(a);
                await ValueTask.CompletedTask;
            },
            inject: [a]));
        var middle = await runtime.LoadAsync(new Probe(
            "middle",
            async context =>
            {
                context.Require(b);
                context.Provide(a, 2);
                await ValueTask.CompletedTask;
            },
            inject: [b],
            provide: [a]));
        var root = await runtime.LoadAsync(new Probe(
            "root",
            context =>
            {
                context.Provide(b, 1);
                context.Provide(c, 3);
                return Task.CompletedTask;
            },
            provide: [b, c]));

        Assert.Equal(FiberState.Active, consumer.State);
        Assert.Equal(FiberState.Active, middle.State);
        Assert.Equal(FiberState.Active, root.State);
        Assert.Equal(3, runtime.Snapshot().Fibers.Count);
    }

    [Fact]
    public async Task Thm80_ConfluenceIgnoresLoadOrder()
    {
        static async Task<RuntimeSnapshot> Build(bool consumerFirst)
        {
            var runtime = new Gocordis.Core.Runtime();
            var key = new Key<int>("shared");
            var provider = new Probe("provider", context =>
            {
                context.Provide(key, 1);
                return Task.CompletedTask;
            }, provide: [key]);
            var consumer = new Probe("consumer", async context =>
            {
                context.Require(key);
                await ValueTask.CompletedTask;
            }, inject: [key]);
            if (consumerFirst)
            {
                await runtime.LoadAsync(consumer);
                await runtime.LoadAsync(provider);
            }
            else
            {
                await runtime.LoadAsync(provider);
                await runtime.LoadAsync(consumer);
            }

            return runtime.Snapshot();
        }

        Assert.True(ObservationEquivalence.Equivalent(await Build(true), await Build(false)));
    }

    private static void AssertWellFormed(Gocordis.Core.Runtime runtime)
    {
        var snapshot = runtime.Snapshot();
        var providers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fiber in snapshot.Fibers)
        {
            foreach (var provided in fiber.Provides)
            {
                Assert.True(providers.Add(provided), $"duplicate provider '{provided}'");
            }

            if (fiber.State == FiberState.Active)
            {
                Assert.Equal(fiber.Requires.Count, fiber.BoundProviders.Count);
            }
        }
    }

    private sealed class Probe : Component
    {
        private readonly Func<Context, Task> _apply;

        public Probe(string name, Func<Context, Task> apply, IReadOnlyList<ICapabilityKey>? inject = null, IReadOnlyList<ICapabilityKey>? provide = null)
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

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _apply(context);
            return null;
        }
    }
}
