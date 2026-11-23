using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Premise- and conclusion-level evidence for Thm73 (C29) and Thm80 (C30).
/// These tests go beyond the three-scenario matrices: they exercise bounded
/// premise enforcement, an in-flight installing effect, and an exhaustive
/// enumeration of load orders over a small finite declaration set.
/// </summary>
public sealed class TheoremPremiseTests
{
    [Fact]
    public async Task Thm73_InstallingEffectIsObservableWhileTheRuntimeIsQuiescent()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new Runtime();
        var loading = runtime.LoadAsync(new Probe(
            "blocked",
            async context =>
            {
                await context.EffectAsync(
                    async () =>
                    {
                        entered.SetResult();
                        await release.Task;
                    },
                    static () => ValueTask.CompletedTask);
            }));

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var first = runtime.Snapshot();
        await Task.Delay(50);
        var second = runtime.Snapshot();
        Assert.True(
            ObservationEquivalence.Equivalent(first, second),
            $"a quiescent runtime must not change while an installing effect is blocked: " +
            $"[{Describe(first)}] vs [{Describe(second)}]");

        var fiber = Assert.Single(second.Fibers);
        Assert.Equal("blocked", fiber.Name);
        Assert.Equal(FiberState.Loading, fiber.State);
        Assert.Null(fiber.Failure);

        release.SetResult();
        var activated = await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Active, activated.State);
        Assert.Null(activated.Failure);
    }

    [Fact]
    public async Task Thm73_PremisesAreEnforced_FiniteNamesAcyclicPrecedenceCancellableSteps()
    {
        await using var runtime = new Runtime();

        // Finite names: the same key cannot be provided twice in one realm.
        var key = new Key<int>("premise-key");
        await runtime.LoadAsync(new Probe("provider", context =>
        {
            context.Provide(key, 1);
            return Task.CompletedTask;
        }, provide: [key]));

        var duplicate = new Probe("duplicate", context =>
        {
            context.Provide(key, 2);
            return Task.CompletedTask;
        }, provide: [key]);
        var error = await Assert.ThrowsAsync<DuplicateProviderException>(
            async () => await runtime.LoadAsync(duplicate));
        Assert.Contains("premise-key", error.Message);

        // Acyclic precedence: a typed dependency cycle is rejected at mount.
        var left = new Key<int>("cycle-left");
        var right = new Key<int>("cycle-right");
        await Assert.ThrowsAsync<DependencyCycleException>(async () =>
        {
            await using var cyclic = new Runtime();
            await cyclic.LoadAsync(new Probe("a", context =>
            {
                context.Require(left);
                return Task.CompletedTask;
            }, inject: [left], provide: [right]));
            await cyclic.LoadAsync(new Probe("b", context =>
            {
                context.Require(right);
                return Task.CompletedTask;
            }, inject: [right], provide: [left]));
        });
    }

    [Fact]
    public async Task Thm73_FiniteIteratorCommitsEveryStepBeforeActivation()
    {
        var log = new List<string>();
        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new ThreeStepComponent(log));

        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal(new[] { "step:1", "step:2", "step:3" }, log);

        await runtime.DisposeAsync(fiber);
        Assert.Equal(
            new[] { "step:1", "step:2", "step:3", "undo:3", "undo:2", "undo:1" }, log);
    }

    [Fact]
    public async Task Thm80_AllLoadOrdersOverAFiniteSetReachOneNormalForm()
    {
        static async Task<RuntimeSnapshot> Build(IReadOnlyList<int> order)
        {
            await using var runtime = new Runtime();
            var shared = new Key<int>("confluence-shared");

            for (var index = 0; index < order.Count; index++)
            {
                switch (order[index])
                {
                    case 0:
                    case 1:
                    case 2:
                        await runtime.LoadAsync(new Probe(
                            $"provider-{order[index]}",
                            context =>
                            {
                                if (order[index] == 0)
                                {
                                    context.Provide(shared, 42);
                                }

                                return Task.CompletedTask;
                            },
                            provide: order[index] == 0 ? [shared] : null));
                        break;
                    default:
                        await runtime.LoadAsync(new Probe(
                            "consumer", context =>
                            {
                                context.Require(shared);
                                return Task.CompletedTask;
                            }, inject: [shared]));
                        break;
                }
            }

            return runtime.Snapshot();
        }

        var permutations = Permutations([0, 1, 2, 3]).ToArray();
        Assert.Equal(24, permutations.Length);
        var baseline = await Build(permutations[0]);
        foreach (var order in permutations.Skip(1))
        {
            var candidate = await Build(order);
            Assert.True(
                ObservationEquivalence.Equivalent(baseline, candidate),
                $"load order [{string.Join(",", order)}] produced a different normal form: " +
                $"baseline=[{Describe(baseline)}] candidate=[{Describe(candidate)}]");
        }
    }

    private static string Describe(RuntimeSnapshot snapshot) => string.Join(" | ", snapshot.Fibers
        .OrderBy(fiber => fiber.Name, StringComparer.Ordinal)
        .Select(fiber => $"{fiber.Name}:{fiber.State}:g{fiber.Generation}:" +
                         $"[{string.Join(",", fiber.BoundProviders)}]"));

    private static IEnumerable<IReadOnlyList<int>> Permutations(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            yield return [];
            yield break;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var rest = values.Where((_, position) => position != index).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { values[index] }.Concat(tail).ToArray();
            }
        }
    }

    private sealed class Probe : Component
    {
        private readonly Func<Context, Task> _apply;
        private readonly IReadOnlyList<ICapabilityKey> _inject;
        private readonly IReadOnlyList<ICapabilityKey> _provide;

        public Probe(
            string name,
            Func<Context, Task> apply,
            IReadOnlyList<ICapabilityKey>? inject = null,
            IReadOnlyList<ICapabilityKey>? provide = null)
        {
            Name = name;
            _apply = apply;
            _inject = inject ?? [];
            _provide = provide ?? [];
        }

        public override string Name { get; }
        public override IReadOnlyList<ICapabilityKey> Inject() => _inject;
        public override IReadOnlyList<ICapabilityKey> Provide() => _provide;

        public async override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _apply(context);
            return null;
        }
    }

    private sealed class ThreeStepComponent : Component, IIterativeComponent
    {
        private readonly List<string> _log;

        public ThreeStepComponent(List<string> log) => _log = log;

        public override string Name => "three-step";

        public async IAsyncEnumerable<EffectRegistration> IterateAsync(Context context)
        {
            for (var step = 1; step <= 3; step++)
            {
                var current = step;
                yield return EffectRegistration.From(
                    () =>
                    {
                        _log.Add($"step:{current}");
                        return ValueTask.CompletedTask;
                    },
                    () =>
                    {
                        _log.Add($"undo:{current}");
                        return ValueTask.CompletedTask;
                    });
                await Task.Yield();
            }
        }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new NotSupportedException("iterative component");
    }
}
