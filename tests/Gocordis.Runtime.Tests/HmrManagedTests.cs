using System.Reflection;
using System.Runtime.CompilerServices;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Runtime.Managed;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>Paper §5.2.2 phase 3 on the CLR: an HMR reload retires the old
/// collectible ALC through Core and stages the replacement from a new module
/// path. The old ALC must become reclaimable once the old fiber is gone.</summary>
public sealed class HmrManagedTests
{
    [Fact]
    public async Task HmrReloadReclaimsTheOldAssemblyLoadContext()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        await using var loader = new ManagedPluginLoader();
        var contexts = new Dictionary<string, WeakReference>(StringComparer.Ordinal);

        registry.RegisterComponentFactory("hmr-managed", manifest =>
        {
            var path = manifest.Source!;
            var plugin = loader.Load($"{manifest.Id}@{path}", path, "SamplePlugin.SampleComponent");
            contexts[path] = new WeakReference(plugin.Context);
            return new ManagedPluginComponent(loader, plugin);
        });

        using var temp = new TempDir();
        var v2 = temp.Path("v2");
        Directory.CreateDirectory(v2);
        var v1Path = SamplePath();
        foreach (var name in new[] { "SamplePlugin.dll", "SamplePlugin.deps.json" })
        {
            File.Copy(Path.Combine(Path.GetDirectoryName(v1Path)!, name), Path.Combine(v2, name), overwrite: true);
        }

        var manifest = new PluginManifest("hmr", "hmr-managed", true,
            new Dictionary<string, object?>(), [], [], Url: "sample-module", Source: v1Path);
        var (plan, oldContext) = await ReloadAndObserve(registry, manifest, v1Path, v2);

        Assert.Equal(new[] { "hmr" }, plan.StaleEntryIds);
        Assert.Equal(FiberState.Active, registry.Entries["hmr"].Fiber!.State);
        Assert.Equal(Path.Combine(v2, "SamplePlugin.dll"), registry.Entries["hmr"].Manifest.Source);

        GcAssert.Reclaimed(oldContext);
        Assert.False(oldContext.IsAlive, "HMR left the previous plugin ALC alive");

        [MethodImpl(MethodImplOptions.NoInlining)]
        async Task<(HmrPlan, WeakReference)> ReloadAndObserve(
            PluginRegistry registry, PluginManifest manifest, string v1Path, string v2Path)
        {
            await registry.ApplyAsync([manifest]);
            var oldFiber = registry.Entries["hmr"].Fiber!;
            // The claim is about the COLLECTIBLE LOAD CONTEXT, not merely the
            // component wrapper. The factory records each ALC weak reference.
            var oldContext = contexts[v1Path];
            var request = new HmrRequest(
                new HashSet<string> { "sample-module" },
                new HashSet<string> { "Gocordis.Core", "Gocordis.Plugins" },
                new Dictionary<string, IReadOnlyList<string>> { ["sample-module"] = [] },
                [manifest]);
            var plan = await HmrEngine.ReloadAsync(registry, request,
                candidate => candidate with { Source = Path.Combine(v2Path, "SamplePlugin.dll"), Version = "2.0.0" });
            Assert.NotSame(oldFiber, registry.Entries["hmr"].Fiber);
            return (plan, oldContext);
        }
    }

    [Fact]
    public async Task FailedManagedReloadKeepsTheOldFiberAndItsCollectibleContextAlive()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        await using var loader = new ManagedPluginLoader();
        var contexts = new Dictionary<string, WeakReference>(StringComparer.Ordinal);
        registry.RegisterComponentFactory("hmr-managed", manifest =>
        {
            var path = manifest.Source!;
            var plugin = loader.Load($"{manifest.Id}@{path}", path, "SamplePlugin.SampleComponent");
            contexts[path] = new WeakReference(plugin.Context);
            return new ManagedPluginComponent(loader, plugin);
        });

        using var temp = new TempDir();
        var bad = temp.Path("bad-module");
        Directory.CreateDirectory(bad);
        var badDll = Path.Combine(bad, "SamplePlugin.dll");
        File.WriteAllText(badDll, "not a managed assembly");
        var v1Path = SamplePath();
        var manifest = new PluginManifest("hmr", "hmr-managed", true,
            new Dictionary<string, object?>(), [], [], Url: "sample-module", Source: v1Path);
        await registry.ApplyAsync([manifest]);
        var oldFiber = registry.Entries["hmr"].Fiber!;
        var oldPlugin = Assert.Single(loader.Plugins.Values);

        var request = new HmrRequest(
            new HashSet<string> { "sample-module" },
            new HashSet<string> { "Gocordis.Core", "Gocordis.Plugins" },
            new Dictionary<string, IReadOnlyList<string>> { ["sample-module"] = [] },
            [manifest]);
        await Assert.ThrowsAnyAsync<Exception>(() => HmrEngine.ReloadAsync(
            registry, request, candidate => candidate with { Source = badDll, Version = "2.0.0" }));

        Assert.Same(oldFiber, registry.Entries["hmr"].Fiber!);
        Assert.Equal(v1Path, registry.Entries["hmr"].Manifest.Source);
        Assert.Equal(FiberState.Active, oldFiber.State);
        // The old plugin object (and therefore its collectible ALC) is still
        // registered with the loader; a failed HMR must not release it.
        Assert.Same(oldPlugin, Assert.Single(loader.Plugins.Values));
    }

    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "SamplePlugin", "bin", "Debug", "net10.0");
            if (File.Exists(Path.Combine(candidate, "SamplePlugin.dll")))
            {
                return Path.Combine(candidate, "SamplePlugin.dll");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("SamplePlugin build output not found");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Unused() => _ = typeof(Assembly);

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-hmr-" + Guid.NewGuid().ToString("N"));

        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
