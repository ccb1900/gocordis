using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>Repeated dynamic changes must not accumulate fibers or break the
/// declaration ledger (Task 04 stability evidence).</summary>
public sealed class StabilityTests
{
    [Fact]
    public async Task RepeatedEnableDisableCyclesKeepTheDeclarationLedgerStable()
    {
        var assembly = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/fixtures/CatalogPlugin/bin/Debug/net10.0/CatalogPlugin.dll"));
        Assert.True(File.Exists(assembly), $"missing fixture '{assembly}'");

        using var temp = new TempDir();
        var store = temp.At("store");
        Directory.CreateDirectory(store);
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        File.Copy(assembly, Path.Combine(source, "CatalogPlugin.dll"));
        var package = temp.At("stable.gocordis-plugin");
        await Packaging.PluginPackageWriter.CreateAsync(source, new Packaging.PluginPackageManifest(
            "stable", "1.0.0", "stable", "[1.0,)",
            Backend: "managed", EntryAssembly: "CatalogPlugin.dll",
            EntryType: "CatalogPlugin.CatalogComponent",
            Capabilities: ["provide|org.example.catalog/ping|endpoint|1.0.0|text/plain|"]), package);
        await new Packaging.PluginStore(store, "1.0.0").InstallAsync(package);

        var manifestPath = temp.At("app.json");
        await new Packaging.ApplicationManifest("stable-app", "1.0.0", "1.0.0",
            [new Packaging.ApplicationPlugin("stable", "1.0.0", true)]).SaveAsync(manifestPath);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: "1.0.0"));
        await app.StartAsync();

        for (var cycle = 0; cycle < 20; cycle++)
        {
            Assert.True((await app.Control!.SetEnabledAsync("stable", false)).Accepted);
            Assert.True((await app.Control!.SetEnabledAsync("stable", true)).Accepted);
        }

        Assert.Equal(1, app.Runtime.Snapshot().Fibers.Count(fiber =>
            fiber.Name == "catalog-plugin" && fiber.State == FiberState.Active));
        Assert.Equal(0, app.Runtime.PendingCommands);
        await app.StopAsync();
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, fiber => fiber.Name == "catalog-plugin");
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-stable-" + Guid.NewGuid().ToString("N"));

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
