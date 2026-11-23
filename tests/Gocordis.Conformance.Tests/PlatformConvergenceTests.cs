using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>C# counterpart of the Go PC01–PC30 platform-convergence gate.</summary>
public sealed class PlatformConvergenceTests
{
    [Fact]
    public async Task PC01_DeclarationReconcileIsIdempotent()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new Echo());
        var declarations = new[] { Manifest("a"), Manifest("b") };

        await registry.ApplyAsync(declarations);
        var first = registry.Entries.Values.ToDictionary(entry => entry.Manifest.Id, entry => entry.Fiber!.Id);
        await registry.ApplyAsync(declarations);
        var second = registry.Entries.Values.ToDictionary(entry => entry.Manifest.Id, entry => entry.Fiber!.Id);

        Assert.Equal(first, second);
        Assert.Equal(2, runtime.Snapshot().Fibers.Count);
    }

    [Fact]
    public async Task PC12_PluginRegistryNeverCreatesASecondLifecycle()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new Echo());

        await registry.ApplyAsync([Manifest("a"), Manifest("b", enabled: false)]);

        Assert.Single(runtime.Snapshot().Fibers);
        Assert.Equal(2, registry.Entries.Count);
        Assert.Null(registry.Entries["b"].Fiber);
    }

    [Fact]
    public async Task PC19_StaleActivationCannotAffectANewGeneration()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var component = new BlockingComponent(async () =>
        {
            started.TrySetResult();
            await release.Task;
            Interlocked.Increment(ref completed);
        });

        var loading = runtime.LoadAsync(component);
        await started.Task;
        var fiber = runtime.GetFiber(runtime.Snapshot().Fibers.Single().Id)!;
        var disposal = runtime.DisposeAsync(fiber);
        release.SetResult();
        await loading;
        await disposal;

        Assert.Equal(1, completed);
        Assert.Equal(FiberState.Gone, fiber.State);
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task PC30_CloseLeavesEveryFiberGone()
    {
        var runtime = new Gocordis.Core.Runtime();
        var key = new Key<int>("convergence");
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
        var providerFiber = await runtime.LoadAsync(provider);
        var consumerFiber = await runtime.LoadAsync(consumer);

        await runtime.CloseAsync();

        Assert.Equal(FiberState.Gone, providerFiber.State);
        Assert.Equal(FiberState.Gone, consumerFiber.State);
        Assert.Empty(runtime.Snapshot().Fibers);
        await runtime.DisposeAsync();
    }

    private static PluginManifest Manifest(string id, bool enabled = true) =>
        new(id, "echo", enabled, new Dictionary<string, object?>(), [], []);

    private sealed class Echo : Component
    {
        private readonly Key<int> _key = new($"echo-{Guid.NewGuid():N}");

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, 1);
            return null;
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

    private sealed class BlockingComponent : Component
    {
        private readonly Func<Task> _body;

        public BlockingComponent(Func<Task> body) => _body = body;

        public override string Name => "blocking";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _body();
            return null;
        }
    }
}
