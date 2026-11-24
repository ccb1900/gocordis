using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using NuGet.Versioning;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Batch 2: one plugin package/install/declare/enable/upgrade/uninstall loop.
/// Installed bytes are real files and real zip packages; runtime lifecycle is
/// still owned by Core through PluginRegistry.
/// </summary>
public sealed class PluginPackageLifecycleTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task PackageRoundTripComputesChecksumsAndRejectsTampering()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "hello plugin");

        var manifest = new PluginPackageManifest(
            Id: "demo", Version: "1.0.0", Type: "demo", HostVersionRange: "[1.0,2.0)");
        var package = temp.File("demo.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source, manifest, package);

        Assert.Single(written.Files);
        Assert.Equal("entry.txt", written.Files[0].Path);

        var read = await PluginPackageReader.ReadAsync(package, HostVersion);
        Assert.Equal("demo", read.Manifest.Id);
        Assert.Equal("hello plugin", System.Text.Encoding.UTF8.GetString(read.Files["entry.txt"]));

        // Rewrite the payload with different bytes but keep the old manifest.
        await TamperPayloadAsync(package, read);

        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            PluginPackageReader.ReadAsync(package, HostVersion));
    }

    [Fact]
    public async Task StoreInstallUpgradeUninstallIsTransactional()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "v1");

        var store = new PluginStore(temp.Path("store"), HostVersion);
        var v1 = temp.File("demo-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("demo", "1.0.0", "demo", "[1.0,2.0)"), v1);

        var added = await store.InstallAsync(v1);
        Assert.Equal(PluginChangeKind.Added, Assert.Single(added.Changes).Kind);
        Assert.True(File.Exists(Path.Combine(temp.Path("store"), "demo", "1.0.0", "entry.txt")));

        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "v2");
        var v2 = temp.File("demo-2.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("demo", "2.0.0", "demo", "[1.0,2.0)"), v2);

        var upgraded = await store.UpgradeAsync(v2);
        Assert.Contains(upgraded.Changes, c => c.Kind == PluginChangeKind.Added && c.Version == "2.0.0");
        Assert.Contains(upgraded.Changes, c => c.Kind == PluginChangeKind.Removed && c.Version == "1.0.0");
        var resolved = await store.ResolveAsync("demo", "[2.0,3.0)");
        Assert.NotNull(resolved);
        Assert.Equal("2.0.0", resolved.Manifest.Version);

        var removed = await store.UninstallAsync("demo");
        Assert.Equal(PluginChangeKind.Removed, Assert.Single(removed.Changes).Kind);
        Assert.Null(await store.ResolveAsync("demo", "[1.0,3.0)"));
    }

    [Fact]
    public async Task StoreUpgradeRejectsWhenRemovedVersionIsOnlySatisfyingDependent()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "provider v1");
        var store = new PluginStore(temp.Path("store"), HostVersion);

        var providerV1 = temp.File("provider-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("provider", "1.0.0", "demo", "[1.0,2.0)"), providerV1);
        await store.InstallAsync(providerV1);

        var consumer = temp.File("consumer-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("consumer", "1.0.0", "demo", "[1.0,2.0)",
                Dependencies: [new PluginDependency("provider", "[1.0,1.5)")]), consumer);
        await store.InstallAsync(consumer);

        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "provider v1.5");
        var providerV15 = temp.File("provider-1.5.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("provider", "1.5.0", "demo", "[1.0,2.0)"), providerV15);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpgradeAsync(providerV15));
        Assert.Contains("consumer", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(await store.ResolveAsync("provider", "1.0.0"));
        Assert.Null(await store.ResolveAsync("provider", "[1.5,2.0)"));
    }

    [Fact]
    public async Task UninstallAndUpgradeUseOneReverseDependencyJudgment()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "x");
        var store = new PluginStore(temp.Path("store"), HostVersion);

        // Same installed snapshot and the same narrow dependent range. Both
        // paths must agree: removing the only satisfying version is blocked.
        var provider = temp.File("provider-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("provider", "1.0.0", "provider", "[1.0,3.0)"), provider);
        await store.InstallAsync(provider);

        var consumer = temp.File("consumer-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("consumer", "1.0.0", "consumer", "[1.0,3.0)",
                Dependencies: [new PluginDependency("provider", "[1.0,1.5)")]), consumer);
        await store.InstallAsync(consumer);

        var installed = await store.ListAsync();
        var versions = installed
            .Where(item => item.Id == "provider")
            .Select(item => NuGetVersion.Parse(item.Version)).ToArray();
        var dependents = PluginDependencyPlanner.FindUnsatisfiedDependents(
            installed, "provider", versions, Array.Empty<NuGetVersion>());
        Assert.Equal(["consumer"], dependents);

        // Uninstall (store guard, now via Planner) must report the same dependent.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UninstallAsync("provider"));
        Assert.Contains("consumer", failure.Message, StringComparison.Ordinal);

        // Upgrade to a version OUTSIDE the dependent's range must be blocked too.
        var provider15 = temp.File("provider-1.5.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("provider", "1.5.0", "provider", "[1.0,3.0)"), provider15);
        var upgradeFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpgradeAsync(provider15));
        Assert.Contains("consumer", upgradeFailure.Message, StringComparison.Ordinal);

        // The package is still installed after both rejected mutations.
        Assert.NotNull(await store.ResolveAsync("provider", "1.0.0"));
    }

    [Fact]
    public async Task StoreRejectsIncompatibleAndMissingDependencyPackages()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "x");
        var store = new PluginStore(temp.Path("store"), HostVersion);

        var incompatible = temp.File("future.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("future", "1.0.0", "demo", "[2.0,3.0)"), incompatible);
        await Assert.ThrowsAsync<IncompatiblePluginException>(() => store.InstallAsync(incompatible));

        var dependent = temp.File("dependent.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("dependent", "1.0.0", "demo", "[1.0,2.0)",
                Dependencies: [new PluginDependency("absent", "[1.0,2.0)")]), dependent);
        await Assert.ThrowsAsync<MissingPluginDependencyException>(() => store.InstallAsync(dependent));

        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task ApplicationLoaderDrivesEnableDisableConfigAndReenable()
    {
        using var temp = new TempDir();
        var (store, registry, runtime, host) = await CreateHostedStoreAsync(temp);
        await InstallDemoAsync(temp, store, "1.0.0");

        var app = NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", true, Config("first")));
        var loader = new ApplicationLoader(store, registry);
        await loader.ApplyAsync(app);

        var first = Assert.Single(registry.Entries).Value.Fiber!;
        Assert.Equal(FiberState.Active, first.State);
        Assert.Equal("first", DemoComponent.LastValue);
        Assert.Single(host.Ui.Pages());

        await loader.ApplyAsync(NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", false, Config("first"))));
        Assert.Equal(FiberState.Gone, first.State);
        Assert.Empty(host.Ui.Pages());

        await loader.ApplyAsync(NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", true, Config("second"))));
        var second = Assert.Single(registry.Entries).Value.Fiber!;
        Assert.NotSame(first, second);
        Assert.Equal("second", DemoComponent.LastValue);

        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task ProductLoopInstallDeclareEnableModifyDisableEnableUpgradeUninstall()
    {
        using var temp = new TempDir();
        var (store, registry, runtime, host) = await CreateHostedStoreAsync(temp);
        var loader = new ApplicationLoader(store, registry);
        var appPath = temp.File("app.gocordis.json");

        // 1-3: create/build/install v1 from a local package path.
        await InstallDemoAsync(temp, store, "1.0.0");
        Assert.Single(await store.ListAsync());

        // 4-6: declare, save application manifest, start/load.
        var app = NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", true, Config("a")));
        await app.SaveAsync(appPath);
        app = await ApplicationManifest.LoadAsync(appPath);
        await loader.ApplyAsync(app);
        var v1Fiber = Assert.Single(registry.Entries).Value.Fiber!;
        Assert.Equal(FiberState.Active, v1Fiber.State);

        // 7: modify plugin config.
        app = app with { Plugins = [new ApplicationPlugin("demo", "[1.0,2.0)", true, Config("b"))] };
        await app.SaveAsync(appPath);
        await loader.ApplyAsync(app);
        var configFiber = Assert.Single(registry.Entries).Value.Fiber!;
        Assert.NotSame(v1Fiber, configFiber);
        Assert.Equal("b", DemoComponent.LastValue);

        // 8-9: disable then re-enable.
        await loader.ApplyAsync(NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", false, Config("b"))));
        Assert.Null(Assert.Single(registry.Entries).Value.Fiber);
        await loader.ApplyAsync(NewApp(new ApplicationPlugin("demo", "[1.0,2.0)", true, Config("b"))));

        // 10: upgrade plugin package and declare the new version.
        await InstallDemoAsync(temp, store, "2.0.0");
        await store.UpgradeAsync(temp.File("demo-2.0.0.gocordis-plugin"));
        app = NewApp(new ApplicationPlugin("demo", "[2.0,3.0)", true, Config("upgraded")));
        await loader.ApplyAsync(app);
        Assert.Equal("upgraded", DemoComponent.LastValue);
        Assert.Equal("2.0.0", registry.Entries["demo"].Manifest.Version);

        // 11-12: remove declaration then uninstall; UI and fiber contributions vanish.
        await loader.ApplyAsync(NewApp());
        Assert.Empty(registry.Entries);
        Assert.Empty(host.Ui.Pages());
        await store.UninstallAsync("demo");
        Assert.Empty(await store.ListAsync());
        Assert.False(File.Exists(Path.Combine(temp.Path("store"), "demo", "2.0.0", "entry.txt")));

        await runtime.DisposeAsync();
    }

    private static ApplicationManifest NewApp(params ApplicationPlugin[] plugins) =>
        new("sample-app", "1.0.0", HostVersion, plugins);

    private static IReadOnlyDictionary<string, object?> Config(string value) =>
        new Dictionary<string, object?> { ["value"] = value };

    private static async Task TamperPayloadAsync(string packagePath, PluginPackageContent original)
    {
        var temp = packagePath + ".tampered";
        using (var stream = File.Create(temp))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("manifest.json");
            await using (var writer = new StreamWriter(manifest.Open()))
            {
                await writer.WriteAsync(JsonSerializer.Serialize(original.Manifest));
            }

            foreach (var (path, bytes) in original.Files)
            {
                var entry = archive.CreateEntry("payload/" + path);
                await using var target = entry.Open();
                var changed = (byte[])bytes.Clone();
                changed[0] ^= 0xFF;
                await target.WriteAsync(changed);
            }
        }

        File.Move(temp, packagePath, overwrite: true);
    }

    private static async Task<(PluginStore Store, PluginRegistry Registry, Gocordis.Core.Runtime Runtime, ConsoleHost Host)> CreateHostedStoreAsync(TempDir temp)
    {
        var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        var registry = new PluginRegistry(runtime);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.RegisterFactory("demo", config => new DemoComponent(
            new Key<string>($"demo-{Guid.NewGuid()}"),
            config["value"]?.ToString() ?? "default",
            host.Ui));
        await runtime.LoadAsync(new HostComponent(host));
        return (new PluginStore(temp.Path("store"), HostVersion), registry, runtime, host);
    }

    private static async Task InstallDemoAsync(TempDir temp, PluginStore store, string version)
    {
        var source = temp.Path($"source-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), version);
        var package = temp.File($"demo-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("demo", version, "demo", "[1.0,3.0)"), package);
        await store.UpgradeAsync(package); // install-or-upgrade for fixture brevity
    }

    private sealed class HostComponent : Component
    {
        private readonly ConsoleHost _host;
        public HostComponent(ConsoleHost host) => _host = host;
        public override string Name => "console-host";
        public override IReadOnlyList<ICapabilityKey> Provide() => [UiContributionRegistry.Key, ApiGateway.Key];
        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _host.Ui);
            context.Provide(ApiGateway.Key, _host.Api);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class DemoComponent : Component
    {
        internal static string? LastValue;
        private readonly Key<string> _key;
        private readonly string _value;
        private readonly UiContributionRegistry _ui;

        public DemoComponent(Key<string> key, string value, UiContributionRegistry ui)
        {
            _key = key;
            _value = value;
            _ui = ui;
        }

        public override string Name => "demo-plugin";

        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            LastValue = _value;
            context.Provide(_key, _value);
            await context.RegisterUiPageAsync(ui, ContributionOwner.ForContext(context, "demo", Name),
                new UiPageDefinition("demo", "Demo", "/demo", "generic"));
            return null;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-pkg-{Guid.NewGuid():N}");
        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string File(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
