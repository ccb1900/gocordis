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

        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

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

        for (var attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

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
