using Gocordis.Core;
using Xunit;
using Xunit.Sdk;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Independent scenario matrices for Thm64/68/70/73/80. Every theory has at
/// least three scenarios and uses only the runnable public Core API; the Thm70
/// case previously mis-filed in PaperConformanceTests lives here as scenario 1.
/// </summary>
public sealed class TheoremMatrixTests
{
    // ---- Thm64: preservation of well-formedness -------------------------------

    public static IEnumerable<object[]> Thm64Scenarios()
    {
        yield return new object[] { "independent-providers" };
        yield return new object[] { "dispose-reload-provider" };
        yield return new object[] { "dependency-withdrawal-recovery" };
    }

    [Theory]
    [MemberData(nameof(Thm64Scenarios))]
    public async Task Thm64_Matrix(string scenario)
    {
        await using var runtime = new Runtime();
        switch (scenario)
        {
            case "independent-providers":
            {
                var keys = Enumerable.Range(0, 5).Select(index => new Key<int>($"k{index}")).ToArray();
                foreach (var key in keys)
                {
                    var captured = key;
                    await runtime.LoadAsync(new Probe(
                        $"p-{captured.Name}",
                        context =>
                        {
                            context.Provide(captured, 1);
                            return Task.CompletedTask;
                        },
                        provide: [captured]));
                }

                Assert.Equal(5, runtime.Snapshot().Fibers.Count);
                AssertWellFormed(runtime);
                break;
            }

            case "dispose-reload-provider":
            {
                var key = new Key<int>("replaceable");
                var oldFiber = await runtime.LoadAsync(new Probe(
                    "old-provider",
                    context =>
                    {
                        context.Provide(key, 1);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                await runtime.DisposeAsync(oldFiber);
                var newFiber = await runtime.LoadAsync(new Probe(
                    "new-provider",
                    context =>
                    {
                        context.Provide(key, 2);
                        return Task.CompletedTask;
                    },
                    provide: [key]));

                Assert.Equal(FiberState.Gone, oldFiber.State);
                Assert.Equal(FiberState.Active, newFiber.State);
                Assert.Single(runtime.Snapshot().Fibers);
                AssertWellFormed(runtime);
                break;
            }

            case "dependency-withdrawal-recovery":
            {
                var key = new Key<int>("volatile");
                var observed = 0;
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
                    context =>
                    {
                        // Do not assert a fixed value here: the same Apply runs
                        // again after recovery, against the replacement provider.
                        observed = context.Require(key);
                        return Task.CompletedTask;
                    },
                    inject: [key]));
                Assert.Equal(1, observed);
                await runtime.DisposeAsync(provider);
                Assert.Equal(FiberState.Pending, consumer.State);
                AssertWellFormed(runtime);

                await runtime.LoadAsync(new Probe(
                    "provider2",
                    context =>
                    {
                        context.Provide(key, 2);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, consumer.State);
                Assert.Equal(2, observed);
                Assert.Null(consumer.Failure);
                AssertWellFormed(runtime);
                break;
            }

            default:
                throw new XunitException($"unknown Thm64 scenario '{scenario}'");
        }
    }

    // ---- Thm68: exact recovery up to observation -------------------------------

    public static IEnumerable<object[]> Thm68Scenarios()
    {
        yield return new object[] { "single-provider" };
        yield return new object[] { "consumer-effect" };
        yield return new object[] { "three-stage-chain" };
    }

    [Theory]
    [MemberData(nameof(Thm68Scenarios))]
    public async Task Thm68_Matrix(string scenario)
    {
        var initial = new Runtime().Snapshot();
        await using var runtime = new Runtime();
        switch (scenario)
        {
            case "single-provider":
            {
                var key = new Key<int>("recover-one");
                var fiber = await runtime.LoadAsync(new Probe(
                    "provider",
                    context =>
                    {
                        context.Provide(key, 1);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                await runtime.DisposeAsync(fiber);
                Assert.Equal(FiberState.Gone, fiber.State);
                break;
            }

            case "consumer-effect":
            {
                var key = new Key<int>("recover-two");
                var cleanups = 0;
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
                        Assert.Equal(1, context.Require(key));
                        await context.EffectAsync(
                            () => ValueTask.CompletedTask,
                            () =>
                            {
                                Interlocked.Increment(ref cleanups);
                                return ValueTask.CompletedTask;
                            });
                    },
                    inject: [key]));

                await runtime.DisposeAsync(consumer);
                await runtime.DisposeAsync(provider);
                Assert.Equal(1, cleanups);
                break;
            }

            case "three-stage-chain":
            {
                var a = new Key<int>("chain-a");
                var b = new Key<int>("chain-b");
                var cleanups = 0;
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    async context =>
                    {
                        Assert.Equal(2, context.Require(a));
                        await context.EffectAsync(
                            () => ValueTask.CompletedTask,
                            () =>
                            {
                                Interlocked.Increment(ref cleanups);
                                return ValueTask.CompletedTask;
                            });
                    },
                    inject: [a]));
                var middle = await runtime.LoadAsync(new Probe(
                    "middle",
                    async context =>
                    {
                        Assert.Equal(1, context.Require(b));
                        context.Provide(a, 2);
                        await context.EffectAsync(
                            () => ValueTask.CompletedTask,
                            () =>
                            {
                                Interlocked.Increment(ref cleanups);
                                return ValueTask.CompletedTask;
                            });
                    },
                    inject: [b],
                    provide: [a]));
                var root = await runtime.LoadAsync(new Probe(
                    "root",
                    context =>
                    {
                        context.Provide(b, 1);
                        return Task.CompletedTask;
                    },
                    provide: [b]));

                Assert.Equal(FiberState.Active, consumer.State);
                Assert.Equal(FiberState.Active, middle.State);
                Assert.Equal(FiberState.Active, root.State);
                await runtime.DisposeAsync(consumer);
                await runtime.DisposeAsync(middle);
                await runtime.DisposeAsync(root);
                Assert.Equal(2, cleanups);
                break;
            }

            default:
                throw new XunitException($"unknown Thm68 scenario '{scenario}'");
        }

        Assert.Empty(runtime.Snapshot().Fibers);
        Assert.True(ObservationEquivalence.Equivalent(initial, runtime.Snapshot()));
    }

    // ---- Thm70: provider episode strictly contains consumer episode ------------

    public static IEnumerable<object[]> Thm70Scenarios()
    {
        yield return new object[] { "consumer-declared-first" };
        yield return new object[] { "provider-replacement" };
        yield return new object[] { "three-node-chain" };
    }

    [Theory]
    [MemberData(nameof(Thm70Scenarios))]
    public async Task Thm70_Matrix(string scenario)
    {
        await using var runtime = new Runtime();
        switch (scenario)
        {
            case "consumer-declared-first":
            {
                var key = new Key<int>("shared");
                var consumerApplied = false;
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    context =>
                    {
                        consumerApplied = true;
                        Assert.Equal(9, context.Require(key));
                        return Task.CompletedTask;
                    },
                    inject: [key]));

                Assert.Equal(FiberState.Pending, consumer.State);
                Assert.False(consumerApplied);

                var provider = await runtime.LoadAsync(new Probe(
                    "provider",
                    context =>
                    {
                        context.Provide(key, 9);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, provider.State);
                Assert.Equal(FiberState.Active, consumer.State);
                Assert.True(consumerApplied);
                Assert.Contains($"{key.Name}->{provider.Id}", SnapshotOf(runtime, "consumer").BoundProviders);
                break;
            }

            case "provider-replacement":
            {
                var key = new Key<int>("swap");
                var observed = 0;
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    context =>
                    {
                        observed = context.Require(key);
                        return Task.CompletedTask;
                    },
                    inject: [key]));
                var provider = await runtime.LoadAsync(new Probe(
                    "provider-v1",
                    context =>
                    {
                        context.Provide(key, 1);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(1, observed);

                await runtime.DisposeAsync(provider);
                Assert.Equal(FiberState.Pending, consumer.State);

                var provider2 = await runtime.LoadAsync(new Probe(
                    "provider-v2",
                    context =>
                    {
                        context.Provide(key, 2);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, consumer.State);
                Assert.Equal(2, observed);
                Assert.Contains($"{key.Name}->{provider2.Id}", SnapshotOf(runtime, "consumer").BoundProviders);
                // The consumer's replacement episode starts after the new provider's.
                Assert.True(consumer.Generation > provider2.Generation,
                    $"consumer generation {consumer.Generation} must follow provider generation {provider2.Generation}");
                break;
            }

            case "three-node-chain":
            {
                var a = new Key<int>("order-a");
                var b = new Key<int>("order-b");
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    context =>
                    {
                        context.Require(a);
                        return Task.CompletedTask;
                    },
                    inject: [a]));
                var middle = await runtime.LoadAsync(new Probe(
                    "middle",
                    context =>
                    {
                        context.Require(b);
                        context.Provide(a, 2);
                        return Task.CompletedTask;
                    },
                    inject: [b],
                    provide: [a]));
                var root = await runtime.LoadAsync(new Probe(
                    "root",
                    context =>
                    {
                        context.Provide(b, 1);
                        return Task.CompletedTask;
                    },
                    provide: [b]));

                Assert.Equal(FiberState.Active, root.State);
                Assert.Equal(FiberState.Active, middle.State);
                Assert.Equal(FiberState.Active, consumer.State);
                Assert.Contains($"{a.Name}->{middle.Id}", SnapshotOf(runtime, "consumer").BoundProviders);

                await runtime.DisposeAsync(root);
                Assert.Equal(FiberState.Pending, middle.State);
                Assert.Equal(FiberState.Pending, consumer.State);

                await runtime.LoadAsync(new Probe(
                    "root2",
                    context =>
                    {
                        context.Provide(b, 3);
                        return Task.CompletedTask;
                    },
                    provide: [b]));
                Assert.Equal(FiberState.Active, middle.State);
                Assert.Equal(FiberState.Active, consumer.State);
                break;
            }

            default:
                throw new XunitException($"unknown Thm70 scenario '{scenario}'");
        }
    }

    // ---- Thm73: progress reaches quiescence ------------------------------------

    public static IEnumerable<object[]> Thm73Scenarios()
    {
        yield return new object[] { "reverse-order-chain" };
        yield return new object[] { "independent-providers" };
        yield return new object[] { "missing-dependency-quiesces" };
    }

    [Theory]
    [MemberData(nameof(Thm73Scenarios))]
    public async Task Thm73_Matrix(string scenario)
    {
        await using var runtime = new Runtime();
        switch (scenario)
        {
            case "reverse-order-chain":
            {
                var a = new Key<int>("progress-a");
                var b = new Key<int>("progress-b");
                var c = new Key<int>("progress-c");
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    context =>
                    {
                        context.Require(a);
                        return Task.CompletedTask;
                    },
                    inject: [a]));
                var middle = await runtime.LoadAsync(new Probe(
                    "middle",
                    context =>
                    {
                        context.Require(b);
                        context.Provide(a, 2);
                        return Task.CompletedTask;
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
                break;
            }

            case "independent-providers":
            {
                var keys = Enumerable.Range(0, 4).Select(index => new Key<int>($"solo{index}")).ToArray();
                foreach (var key in keys)
                {
                    var captured = key;
                    await runtime.LoadAsync(new Probe(
                        $"p-{captured.Name}",
                        context =>
                        {
                            context.Provide(captured, 1);
                            return Task.CompletedTask;
                        },
                        provide: [captured]));
                }

                Assert.All(runtime.Snapshot().Fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
                Assert.Equal(4, runtime.Snapshot().Fibers.Count);
                break;
            }

            case "missing-dependency-quiesces":
            {
                var key = new Key<int>("late");
                var consumer = await runtime.LoadAsync(new Probe(
                    "consumer",
                    context =>
                    {
                        context.Require(key);
                        return Task.CompletedTask;
                    },
                    inject: [key]));
                Assert.Equal(FiberState.Pending, consumer.State);
                Assert.Null(consumer.Failure);

                await runtime.LoadAsync(new Probe(
                    "provider",
                    context =>
                    {
                        context.Provide(key, 7);
                        return Task.CompletedTask;
                    },
                    provide: [key]));
                Assert.Equal(FiberState.Active, consumer.State);
                break;
            }

            default:
                throw new XunitException($"unknown Thm73 scenario '{scenario}'");
        }
    }

    // ---- Thm80: confluence of legal schedules ----------------------------------

    public static IEnumerable<object[]> Thm80Scenarios()
    {
        yield return new object[] { "provider-consumer-load-order" };
        yield return new object[] { "chain-load-order" };
        yield return new object[] { "teardown-order" };
    }

    [Theory]
    [MemberData(nameof(Thm80Scenarios))]
    public async Task Thm80_Matrix(string scenario)
    {
        switch (scenario)
        {
            case "provider-consumer-load-order":
            {
                var key = new Key<int>("confluence");

                static async Task<RuntimeSnapshot> Build(bool consumerFirst, Key<int> key)
                {
                    var runtime = new Runtime();
                    var provider = new Probe("provider", context =>
                    {
                        context.Provide(key, 1);
                        return Task.CompletedTask;
                    }, provide: [key]);
                    var consumer = new Probe("consumer", context =>
                    {
                        context.Require(key);
                        return Task.CompletedTask;
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

                Assert.True(ObservationEquivalence.Equivalent(
                    await Build(true, key), await Build(false, key)));
                break;
            }

            case "chain-load-order":
            {
                var a = new Key<int>("join-a");
                var b = new Key<int>("join-b");

                static async Task<RuntimeSnapshot> Build(bool reverse, Key<int> a, Key<int> b)
                {
                    var runtime = new Runtime();
                    var consumer = new Probe("consumer", context =>
                    {
                        context.Require(a);
                        return Task.CompletedTask;
                    }, inject: [a]);
                    var middle = new Probe("middle", context =>
                    {
                        context.Require(b);
                        context.Provide(a, 2);
                        return Task.CompletedTask;
                    }, inject: [b], provide: [a]);
                    var root = new Probe("root", context =>
                    {
                        context.Provide(b, 1);
                        return Task.CompletedTask;
                    }, provide: [b]);

                    if (reverse)
                    {
                        await runtime.LoadAsync(consumer);
                        await runtime.LoadAsync(middle);
                        await runtime.LoadAsync(root);
                    }
                    else
                    {
                        await runtime.LoadAsync(root);
                        await runtime.LoadAsync(middle);
                        await runtime.LoadAsync(consumer);
                    }

                    return runtime.Snapshot();
                }

                var first = await Build(true, a, b);
                var second = await Build(false, a, b);
                Assert.All(first.Fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
                Assert.True(ObservationEquivalence.Equivalent(first, second));
                break;
            }

            case "teardown-order":
            {
                var key = new Key<int>("teardown");

                static async Task<RuntimeSnapshot> Build(bool consumerFirst, Key<int> key)
                {
                    var runtime = new Runtime();
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
                        context =>
                        {
                            context.Require(key);
                            return Task.CompletedTask;
                        },
                        inject: [key]));
                    if (consumerFirst)
                    {
                        await runtime.DisposeAsync(consumer);
                        await runtime.DisposeAsync(provider);
                    }
                    else
                    {
                        await runtime.DisposeAsync(provider);
                        await runtime.DisposeAsync(consumer);
                    }

                    Assert.Equal(FiberState.Gone, provider.State);
                    Assert.Equal(FiberState.Gone, consumer.State);
                    return runtime.Snapshot();
                }

                Assert.True(ObservationEquivalence.Equivalent(
                    await Build(true, key), await Build(false, key)));
                break;
            }

            default:
                throw new XunitException($"unknown Thm80 scenario '{scenario}'");
        }
    }

    private static FiberSnapshot SnapshotOf(Gocordis.Core.Runtime runtime, string name) =>
        runtime.Snapshot().Fibers.Single(fiber => fiber.Name == name);

    private static void AssertWellFormed(Gocordis.Core.Runtime runtime)
    {
        var snapshot = runtime.Snapshot();
        var fibers = snapshot.Fibers.ToDictionary(fiber => fiber.Id);
        var providers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fiber in snapshot.Fibers)
        {
            foreach (var provided in fiber.Provides)
            {
                Assert.True(providers.Add(provided), $"duplicate provider '{provided}'");
            }

            if (fiber.ParentId is not null)
            {
                Assert.True(fibers.ContainsKey(fiber.ParentId.Value), $"missing parent of '{fiber.Name}'");
            }

            if (fiber.State != FiberState.Active)
            {
                continue;
            }

            Assert.Equal(fiber.Requires.Count, fiber.BoundProviders.Count);
            foreach (var binding in fiber.BoundProviders)
            {
                var separator = binding.IndexOf("->", StringComparison.Ordinal);
                Assert.True(separator > 0, $"malformed binding '{binding}'");
                var providerId = Guid.Parse(binding[(separator + 2)..]);
                Assert.True(fibers.TryGetValue(providerId, out var provider), $"missing provider '{providerId}'");
                Assert.Equal(FiberState.Active, provider.State);
            }
        }
    }

    /// <summary>
    /// Evidence anchor for Thm43 (and C14): independent inverses commute across
    /// the two permitted orders. LIFO is only asserted WITHIN one fiber.
    /// </summary>
    [Fact]
    public async Task Thm43_IndependentInversesCommuteAcrossPermittedOrders()
    {
        static async Task<string[]> Run(bool reverse)
        {
            var log = new List<string>();
            await using var runtime = new Gocordis.Core.Runtime();
            async Task<Fiber> Add(string token) => await runtime.LoadAsync(new Probe(
                $"thm43-{token}",
                async context => await context.EffectAsync(
                    () =>
                    {
                        log.Add($"{token}+");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        log.Add($"{token}-");
                        return ValueTask.CompletedTask;
                    })));

            var left = await Add("x");
            var right = await Add("y");
            if (reverse)
            {
                await runtime.DisposeAsync(right);
                await runtime.DisposeAsync(left);
            }
            else
            {
                await runtime.DisposeAsync(left);
                await runtime.DisposeAsync(right);
            }

            Assert.Empty(runtime.Snapshot().Fibers);
            return log.ToArray();
        }

        var forward = await Run(reverse: false);
        var reversed = await Run(reverse: true);
        Assert.Equal(new[] { "x+", "y+", "x-", "y-" }, forward);
        Assert.Equal(new[] { "x+", "y+", "y-", "x-" }, reversed);
        Assert.Equal(
            forward.Where(entry => entry.EndsWith("-", StringComparison.Ordinal)).OrderBy(entry => entry, StringComparer.Ordinal),
            reversed.Where(entry => entry.EndsWith("-", StringComparison.Ordinal)).OrderBy(entry => entry, StringComparer.Ordinal));
    }

    private sealed class Probe : Component
    {
        private readonly Func<Context, Task> _apply;

        public Probe(
            string name,
            Func<Context, Task> apply,
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

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _apply(context);
            return null;
        }
    }
}
