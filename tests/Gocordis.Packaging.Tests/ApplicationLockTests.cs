using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Packaging.Tests;

public sealed class ApplicationLockTests
{
    [Fact]
    public async Task LockedVersionAndPackageHashAreEnforcedAgainstTheStore()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("locked.gocordis-plugin");
        var manifest = new PluginPackageManifest(
            "locked", "1.0.0", "locked", "[1.0,)",
            Backend: "builtin", EntryAssembly: "plugin.dll");
        await PluginPackageWriter.CreateAsync(source, manifest, package);
        var store = new PluginStore(temp.At("store"), "1.0.0");
        await store.InstallAsync(package);

        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("locked", _ => new LockedComponent());
        var loader = new ApplicationLoader(store, registry);

        var good = new ApplicationManifest("app", "1.0.0", "1.0.0",
            [new ApplicationPlugin("locked", "[1.0,2.0)", true,
                LockedVersion: "1.0.0", PackageHash: manifest.Checksum)]);
        await loader.ApplyAsync(good);
        Assert.Equal("locked", Assert.Single(registry.Entries).Key);

        var badHash = good with
        {
            Plugins = [good.Plugins[0] with { PackageHash = "0000000000000000000000000000000000000000000000000000000000000000" }],
        };
        await Assert.ThrowsAsync<ChecksumMismatchException>(() => loader.ApplyAsync(badHash));

        var badVersion = good with
        {
            Plugins = [good.Plugins[0] with { LockedVersion = "9.9.9" }],
        };
        await Assert.ThrowsAsync<MissingPluginDependencyException>(
            () => loader.ApplyAsync(badVersion));
    }

    private sealed class LockedComponent : Component
    {
        public override string Name => "locked";
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-lock-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);
        public string At(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
