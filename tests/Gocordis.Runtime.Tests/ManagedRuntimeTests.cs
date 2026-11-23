using System.Reflection;
using System.Runtime.CompilerServices;
using Gocordis.Core;
using Gocordis.Runtime.Managed;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class ManagedRuntimeTests
{
    [Fact]
    public async Task LoadsComponentFromAnIndependentAssembly()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var loader = new ManagedPluginLoader();
        var plugin = loader.Load("sample", SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");

        var fiber = await runtime.LoadAsync(plugin.Component);

        Assert.Equal("sample-plugin", plugin.Component.Name);
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Same(typeof(Component).Assembly, plugin.Component.GetType().BaseType!.Assembly);
    }

    [Fact]
    public async Task UnloadReclaimsTheCollectibleAssemblyContext()
    {
        await using var loader = new ManagedPluginLoader();
        var reference = LoadAndUnload(loader, SamplePath("SamplePlugin"));

        GcAssert.Reclaimed(reference);

        Assert.False(reference.IsAlive, "AssemblyLoadContext was not reclaimed");
        await ValueTask.CompletedTask;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference LoadAndUnload(ManagedPluginLoader loader, string path)
        {
            var plugin = loader.Load("reclaim", path, "SamplePlugin.SampleComponent");
            var weak = new WeakReference(plugin.Context);
            loader.Unload("reclaim");
            return weak;
        }
    }

    [Fact]
    public async Task UnloadingProviderLeavesPendingConsumerWithoutKeepingAlcAlive()
    {
        // Migration review P0-3: after the provider fiber is disposed the
        // consumer stays Pending. Its Committed map must no longer hold the
        // provider Fiber (which owns the plugin Component / its ALC).
        var weak = await LoadProviderConsumerUnload();

        GcAssert.Reclaimed(weak);

        Assert.False(weak.IsAlive, "pending consumer kept the provider plugin ALC alive");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static async Task<WeakReference> LoadProviderConsumerUnload()
        {
            await using var runtime = new Gocordis.Core.Runtime();
            await using var loader = new ManagedPluginLoader();

            var plugin = loader.Load("provider", SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");
            var key = (Key<string>)plugin.Component.GetType()
                .GetField("SampleKey", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var weak = new WeakReference(plugin.Context);

            var provider = await runtime.LoadAsync(plugin.Component);
            string? observed = null;
            var consumer = await runtime.LoadAsync(new ProbeComponent(
                "consumer", context => observed = context.Require(key), [key]));

            if (consumer.State != FiberState.Active || observed is null)
            {
                throw new InvalidOperationException("consumer did not bind the managed provider");
            }

            await runtime.DisposeAsync(provider);
            if (consumer.State != FiberState.Pending)
            {
                throw new InvalidOperationException($"consumer should be Pending, got {consumer.State}");
            }

            loader.Unload("provider");
            return weak;
        }
    }

    [Fact]
    public async Task TwoManagedComponentsCanBeActiveSimultaneously()
    {
        // Migration review P0-2: the backend used one static Key<string> for
        // every managed component, so the second fiber failed as a duplicate
        // provider. Each component must own the key it declares and provides.
        await using var runtime = new Gocordis.Core.Runtime();
        await using var loader = new ManagedPluginLoader();

        var first = new ManagedPluginComponent(
            loader, "managed-instance-a", SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");
        var second = new ManagedPluginComponent(
            loader, "managed-instance-b", SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");

        var firstFiber = await runtime.LoadAsync(first).WaitAsync(TimeSpan.FromSeconds(5));
        var secondFiber = await runtime.LoadAsync(second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FiberState.Active, firstFiber.State);
        Assert.Equal(FiberState.Active, secondFiber.State);
        Assert.Null(firstFiber.Failure);
        Assert.Null(secondFiber.Failure);
    }

    [Fact]
    public async Task TwoPluginsCanUseDifferentVersionsOfAPrivateDependency()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var loader = new ManagedPluginLoader();

        var a = loader.Load("a", SamplePath("PluginA"), "PluginA.VersionComponent");
        var b = loader.Load("b", SamplePath("PluginB"), "PluginB.VersionComponent");
        var keyA = (Key<string>)a.Component.GetType().GetField("VersionKey")!.GetValue(null)!;
        var keyB = (Key<string>)b.Component.GetType().GetField("VersionKey")!.GetValue(null)!;

        await runtime.LoadAsync(a.Component);
        await runtime.LoadAsync(b.Component);

        var observed = new List<string>();
        await runtime.LoadAsync(new ProbeComponent(
            "consumer-a",
            context => observed.Add(context.Require(keyA)),
            [keyA]));
        await runtime.LoadAsync(new ProbeComponent(
            "consumer-b",
            context => observed.Add(context.Require(keyB)),
            [keyB]));

        Assert.Equal(new[] { "1.0", "2.0" }, observed);
    }

    [Fact]
    public async Task ManagedInnerIteratorIsScheduledStepByStepByCore()
    {
        var log = new List<string>();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new Gocordis.Core.Runtime();
        await using var loader = new ManagedPluginLoader();
        var staged = Stage("managed-iterative", new IterativePluginComponent(log, secondStarted, releaseSecond), loader);
        var managed = new ManagedPluginComponent(loader, staged);

        var loading = runtime.LoadAsync(managed);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // An external retire diverts the activation; the in-flight step lands
        // first and Core unwinds both steps in strict LIFO order.
        var disposal = runtime.DisposeAsync(runtime.GetFiber(
            runtime.Snapshot().Fibers.Single().Id)!);
        releaseSecond.SetResult();
        var fiber = await loading.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FiberState.Gone, fiber.State);
        Assert.Null(fiber.Failure);
        Assert.Equal(
            new[] { "install:1", "install:2", "undo:2", "undo:1" },
            log);
    }

    [Fact]
    public async Task ManagedInnerIteratorDivertSkipsStepsThatWereNeverRequested()
    {
        var log = new List<string>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new Gocordis.Core.Runtime();
        await using var loader = new ManagedPluginLoader();
        var staged = Stage("managed-divert", new IterativePluginComponent(log, firstStarted, releaseFirst, blockStep: 1), loader);
        var managed = new ManagedPluginComponent(loader, staged);

        var loading = runtime.LoadAsync(managed);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = runtime.DisposeAsync(runtime.GetFiber(
            runtime.Snapshot().Fibers.Single().Id)!);
        releaseFirst.SetResult();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "install:1", "undo:1" }, log);
    }

    private static LoadedManagedPlugin Stage(
        string id,
        Component component,
        ManagedPluginLoader loader)
    {
        var path = SamplePath("SamplePlugin");
        return new LoadedManagedPlugin(
            id,
            new PluginLoadContext(path),
            component,
            loader.GenerationsFor(id).Begin());
    }

    /// <summary>
    /// Inner plugin component with a real async step: ApplyAsync throws on
    /// purpose, so a backend that bypasses Core's iterator path fails the test.
    /// </summary>
    private sealed class IterativePluginComponent : Component, IIterativeComponent
    {
        private readonly List<string> _log;
        private readonly TaskCompletionSource _started;
        private readonly TaskCompletionSource _release;
        private readonly int _blockStep;

        public IterativePluginComponent(
            List<string> log,
            TaskCompletionSource started,
            TaskCompletionSource release,
            int blockStep = 2)
        {
            _log = log;
            _started = started;
            _release = release;
            _blockStep = blockStep;
        }

        public override string Name => "iterative-plugin";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new NotSupportedException("the backend must use Core's iterator path");

        public async IAsyncEnumerable<EffectRegistration> IterateAsync(Context context)
        {
            yield return EffectRegistration.From(
                async () =>
                {
                    _log.Add("install:1");
                    if (_blockStep == 1)
                    {
                        await LandAfterCancellation(context);
                    }
                },
                () =>
                {
                    _log.Add("undo:1");
                    return ValueTask.CompletedTask;
                });

            await Task.Yield();
            yield return EffectRegistration.From(
                async () =>
                {
                    _log.Add("install:2");
                    if (_blockStep == 2)
                    {
                        await LandAfterCancellation(context);
                    }
                },
                () =>
                {
                    _log.Add("undo:2");
                    return ValueTask.CompletedTask;
                });

            async ValueTask LandAfterCancellation(Context context)
            {
                _started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Inertial divert: the step still lands under Core control.
                }

                await _release.Task;
            }
        }
    }

    private static string SamplePath(string project) => Path.Combine(
        FindRepositoryRoot(),
        "samples",
        project,
        "bin",
        "Debug",
        "net10.0",
        $"{project}.dll");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private sealed class ProbeComponent : Component
    {
        private readonly Action<Context> _apply;
        private readonly IReadOnlyList<ICapabilityKey> _inject;

        public ProbeComponent(string name, Action<Context> apply, IReadOnlyList<ICapabilityKey> inject)
        {
            Name = name;
            _apply = apply;
            _inject = inject;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => _inject;

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _apply(context);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
