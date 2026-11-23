using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Regression test for a product-host defect: PlatformApplication used to
/// pre-register raw component factories before the control plane could wrap
/// them with UiPackageBinder. That silently dropped every package-declared
/// gocordis-ui.json contribution — pages/panels/client modules in real
/// packages never appeared, even though the same package worked through the
/// control plane directly (PluginUiPackageProductTests).
///
/// This test proves the product path now binds package UI through the plugin's
/// own fiber: install => page present, disable => page gone, enable => back.
/// </summary>
public sealed class PackageUiProductHostTests
{
    private const string HostVersion = "1.5.0";
    private const string PluginId = "pkg-ui-host";
    private const string PageId = "pkg-ui-host-page";

    [Fact]
    public async Task ProductHostBindsPackageUiThroughThePluginFiber()
    {
        var root = FindRepositoryRoot();
        using var temp = new TempDir();
        var store = temp.Path("store");
        var source = temp.Path("source");
        Directory.CreateDirectory(Path.Combine(source, "ui"));

        // A minimal component package (no compiled code needed): the UI comes
        // entirely from the package's own gocordis-ui.json declaration.
        await File.WriteAllTextAsync(Path.Combine(source, "gocordis-ui.json"), """
            { "resources": [ { "kind": "page", "id": "pkg-ui-host-page", "path": "ui/page.json" } ] }
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "ui", "page.json"), """
            { "id": "pkg-ui-host-page", "title": "Package UI", "route": "/pkg-ui-host", "renderer": "generic" }
            """);

        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "package fixture");

        var package = temp.File("pkg-ui-host-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(PluginId, "1.0.0", PluginId, "[1.0,)",
                Backend: "builtin", EntryAssembly: "entry.txt"),
            package);
        await new PluginStore(store, HostVersion).InstallAsync(package);

        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("pkg-ui-host-app", "1.0.0", HostVersion,
            [new ApplicationPlugin(PluginId, "[1.0,2.0)", true)]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
        await app.StartAsync();

        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == PageId);

        var control = Assert.IsAssignableFrom<Gocordis.Packaging.IPluginControlPlane>(app.Control!);
        var disabled = await control.SetEnabledAsync(PluginId, false);
        Assert.True(disabled.Accepted, disabled.Error);
        Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == PageId);

        var enabled = await control.SetEnabledAsync(PluginId, true);
        Assert.True(enabled.Accepted, enabled.Error);
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == PageId);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-pkg-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

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
