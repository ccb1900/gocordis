using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>Regressions for defects found in independent review round 1.</summary>
public sealed class AuditRegressionTests
{
    [Fact]
    public async Task ReliedGuardKeepsProviderAliveDuringConsumerTeardown()
    {
        var key = new Key<int>("svc");
        var valueSeenDuringCleanup = -1;

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = Provider("provider", 42);
        var consumer = new Probe(
            "consumer",
            context =>
            {
                var value = context.Require(key);
                return context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        valueSeenDuringCleanup = context.Require(key);
                        return ValueTask.CompletedTask;
                    }).AsTask();
            },
            inject: [key]);

        var providerFiber = await runtime.LoadAsync(provider);
        var consumerFiber = await runtime.LoadAsync(consumer);

        await runtime.DisposeAsync(providerFiber);

        Assert.Equal(42, valueSeenDuringCleanup);
        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.Equal(FiberState.Gone, providerFiber.State);

        Component Provider(string name, int value) => new Probe(
            name,
            context =>
            {
                context.Provide(key, value);
                return Task.CompletedTask;
            },
            provide: [key]);
    }

    [Fact]
    public async Task SingleStepDivertStillRunsTheApplyCleanup()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupRan = false;

        await using var runtime = new Gocordis.Core.Runtime();
        var component = new CleanupProbe(async context =>
        {
            started.TrySetResult();
            await release.Task;
            return () =>
            {
                cleanupRan = true;
                return ValueTask.CompletedTask;
            };
        });

        var loading = runtime.LoadAsync(component);
        await started.Task;
        var fiber = runtime.GetFiber(runtime.Snapshot().Fibers.Single().Id)!;
        var disposal = runtime.DisposeAsync(fiber);
        release.SetResult();

        await loading;
        await disposal;

        Assert.True(cleanupRan, "Apply cleanup was dropped on divert");
    }

    [Fact]
    public async Task NestedReconcileDoesNotReactivateTheStillActivatingParent()
    {
        var key = new Key<int>("k");
        var emptyRealm = new Realm("empty");
        var activations = 0;

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(new Probe(
            "provider",
            context =>
            {
                context.Provide(key, 1);
                return Task.CompletedTask;
            },
            provide: [key]));

        var parent = new Iterative("parent", Steps, inject: [key]);

        async IAsyncEnumerable<EffectRegistration> Steps(Context context)
        {
            activations++;
            context.Require(key);
            context.Isolate(key, emptyRealm);
            await context.LoadChildAsync(new Probe("child", _ => Task.CompletedTask));
            yield return EffectRegistration.From(
                () => ValueTask.CompletedTask,
                () => ValueTask.CompletedTask);
        }

        var fiber = await runtime.LoadAsync(parent);

        Assert.Equal(1, activations);
        Assert.Equal(FiberState.Pending, fiber.State);
    }

    [Fact]
    public async Task IsolateCannotHijackAKeyOwnedByAnotherRealm()
    {
        var key = new Key<string>("k");
        var other = new Realm("other");

        await using var runtime = new Gocordis.Core.Runtime();
        var first = await runtime.LoadAsync(new Probe(
            "first",
            context =>
            {
                context.Provide(key, "first");
                return Task.CompletedTask;
            },
            provide: [key]));

        var second = await runtime.LoadAsync(new Probe(
            "second",
            context =>
            {
                context.Provide(key, "second");
                context.Isolate(key, Realm.Default);
                return Task.CompletedTask;
            },
            provide: [key]), realm: other);

        Assert.Equal(FiberState.Active, first.State);
        Assert.Equal(FiberState.Failed, second.State);
        Assert.IsType<DuplicateProviderException>(second.Failure);
    }

    [Fact]
    public async Task SnapshotIsSafeUnderConcurrentLoadDispose()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var work = Enumerable.Range(0, 40).Select(index => Task.Run(async () =>
        {
            var component = new Probe($"p{index}", async _ => await Task.Yield());
            var fiber = await runtime.LoadAsync(component);
            await runtime.DisposeAsync(fiber);
        })).ToArray();
        var snapshots = Task.Run(async () =>
        {
            for (var i = 0; i < 200; i++)
            {
                _ = runtime.Snapshot().Fibers.Count;
                await Task.Yield();
            }
        });

        await Task.WhenAll(work.Append(snapshots));
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    private sealed class CleanupProbe : Component
    {
        private readonly Func<Context, Task<Cleanup?>> _apply;

        public CleanupProbe(Func<Context, Task<Cleanup?>> apply) => _apply = apply;

        public override string Name => "cleanup-probe";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context) => await _apply(context);
    }

    private sealed class Probe : Component
    {
        private readonly Func<Context, Task<Cleanup?>> _apply;

        public Probe(string name, Func<Context, Task> apply, IReadOnlyList<ICapabilityKey>? inject = null, IReadOnlyList<ICapabilityKey>? provide = null)
            : this(name, async context => { await apply(context); return null; }, inject, provide)
        {
        }

        public Probe(string name, Func<Context, Task<Cleanup?>> apply, IReadOnlyList<ICapabilityKey>? inject = null, IReadOnlyList<ICapabilityKey>? provide = null)
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

    private sealed class Iterative : Component, IIterativeComponent
    {
        private readonly Func<Context, IAsyncEnumerable<EffectRegistration>> _iterate;

        public Iterative(string name, Func<Context, IAsyncEnumerable<EffectRegistration>> iterate, IReadOnlyList<ICapabilityKey>? inject = null)
        {
            Name = name;
            _iterate = iterate;
            InjectKeys = inject ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

        private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<EffectRegistration> IterateAsync(Context context) => _iterate(context);
    }
}
