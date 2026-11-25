using System.Text.Json.Nodes;
using Gocordis.Client;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// The typed .NET client exercises the same IPluginControlPlane HTTP endpoints
/// as the web console and the CLI. No direct store/manifest access here.
/// </summary>
public sealed class ConsoleClientControlTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task TypedClientInstallsControlsRecoversAndReadsAudit()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        Directory.CreateDirectory(store);
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "builtin fixture");
        var package = temp.File("client-demo-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("client-demo", "1.0.0", "client-demo", "[1.0,)",
                Backend: "builtin", EntryAssembly: "entry.txt"), package);

        var manifest = temp.File("gocordis.app.json");
        await new ApplicationManifest("client-app", "1.0.0", HostVersion, []).SaveAsync(manifest);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifest, store, HostVersion: HostVersion));
        await app.StartAsync();

        var owner = ContributionOwner.For("client-test", "ui");
        app.Console.Ui.RegisterRoute(owner, new UiRouteDefinition("client-route", "/client", "plugin-client-demo"));
        app.Console.Ui.RegisterNavigation(owner, new UiNavigationDefinition("client-nav", "Client", "/client"));

        using var client = new ConsoleClient(app.Address!);

        var contributions = await client.GetUiContributionsAsync();
        var clientRoute = Assert.Single(contributions.Routes, route => route.Id == "client-route");
        var clientNav = Assert.Single(contributions.Navigation, nav => nav.Id == "client-nav");
        Assert.Equal("/client", clientRoute.Path);
        Assert.Equal("Client", clientNav.Label);

        await using var stream = File.OpenRead(package);
        var upload = await client.UploadPackageAsync(stream, "client-demo-1.0.0.gocordis-plugin", "client-test");
        Assert.Equal("client-demo", upload.Id);

        var installed = await client.InstallPluginAsync(upload.UploadKey, source: "client-test");
        Assert.True(installed.Accepted, installed.Error);
        Assert.Contains(await client.GetPluginsAsync(), entry => entry.Id == "client-demo");

        var disabled = await client.SetPluginEnabledAsync("client-demo", false, "client-test");
        Assert.True(disabled.Accepted, disabled.Error);

        var recovered = await client.RecoverHostAsync("client-test");
        Assert.True(recovered.Accepted, recovered.Error);

        var audit = await client.GetAuditAsync();
        foreach (var action in new[] { "upload", "install", "set-enabled", "recover" })
        {
            Assert.Contains(audit, entry => entry.Action == action && entry.Source == "client-test");
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-client-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        private string Root { get; }

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
