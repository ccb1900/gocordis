using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// The Console is a product-owned, self-contained plugin package: a real
/// managed Component, its own TSX-built ESM and all UI declarations. The host
/// contributes only ApiGateway and web infrastructure; this test drives the
/// complete declaration -> package -> backend -> fiber -> UI/API -> teardown
/// loop through the same control plane a user calls.
/// </summary>
public sealed class BuiltInConsolePluginTests
{
    private const string HostVersion = "1.5.0";
    private const string PluginId = "gocordis-console";
    private const string ModuleId = "gocordis-console-ui";
    private const string StatusQuery = "gocordis-console/status";
    private const string PingCommand = "gocordis-console/ping";

    [Fact]
    public async Task BuiltInPackageContainsManagedComponentFrontendAndAllUiKinds()
    {
        var packagePath = BuiltInPackages.ConsolePackagePath;
        Assert.True(File.Exists(packagePath), packagePath);

        var content = await PluginPackageReader.ReadAsync(packagePath, HostVersion);
        Assert.Equal(PluginId, content.Manifest.Id);
        Assert.Equal("managed", content.Manifest.Backend);
        Assert.Equal("Gocordis.Console.Plugin.dll", content.Manifest.EntryAssembly);
        Assert.Equal("Gocordis.Console.Plugin.ConsolePluginComponent", content.Manifest.EntryType);
        Assert.Contains("Gocordis.Console.Plugin.dll", content.Files.Keys);
        Assert.Contains("ui.js", content.Files.Keys);

        // The product build uses the package's gocordis-ui.json payload file;
        // the store derives .gocordis-ui.json from it at install time.
        var declaration = JsonNode.Parse(
            System.Text.Encoding.UTF8.GetString(content.Files["gocordis-ui.json"]))!;
        var kinds = declaration["resources"]!.AsArray()
            .Select(resource => resource!["kind"]!.GetValue<string>())
            .ToArray();
        Assert.Contains("client-module", kinds);
        Assert.Contains("page", kinds);
        Assert.Contains("panel", kinds);
        Assert.Contains("route", kinds);
        Assert.Contains("navigation", kinds);
        Assert.Contains("extension", kinds);
        Assert.Contains("resource", kinds);
        Assert.Contains("gocordis-console-overview", System.Text.Encoding.UTF8.GetString(content.Files["ui.js"]));
    }

    [Fact]
    public async Task BuiltInConsoleLoadsServesItsOwnUiAndUnwindsThroughControlPlane()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        var installed = await new PluginStore(store, HostVersion)
            .InstallAsync(BuiltInPackages.ConsolePackagePath);
        Assert.Contains(installed.Changes, change =>
            change.Kind == PluginChangeKind.Added && change.Id == PluginId);

        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("builtin-console-app", "1.0.0", HostVersion,
            [new ApplicationPlugin(PluginId, "[1.0,2.0)", true)]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.Web, HostVersion: HostVersion,
            Address: "http://127.0.0.1:0"));
        await app.StartAsync();

        var fiber = app.Registry.Entries[PluginId].Fiber!;
        Assert.Equal(FiberState.Active, fiber.State);

        var status = app.Console.Api.Query(StatusQuery) as JsonObject;
        Assert.NotNull(status);
        Assert.Equal(PluginId, status!["plugin"]?.GetValue<string>());
        Assert.True(status["installed"]?.GetValue<bool>());
        Assert.Equal("gocordis-console-ui", status["frontend"]?.GetValue<string>());
        Assert.True(await app.Console.Api.ExecuteAsync(PingCommand, null));
        Assert.Equal(1, app.Console.Api.Query(StatusQuery)?["pings"]?.GetValue<int>());

        Assert.Contains(app.Console.Ui.ClientModules(), module => module.Name == ModuleId);
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "gocordis-console-page");
        Assert.Contains(app.Console.Ui.Panels(), panel => panel.Id == "gocordis-console-status");
        Assert.Contains(app.Console.Ui.Routes(), route => route.Id == "gocordis-console-route");
        Assert.Contains(app.Console.Ui.Navigations(), nav => nav.Id == "gocordis-console-nav");
        Assert.Contains(app.Console.Ui.Extensions(), extension => extension.Id == "gocordis-console-footer");
        Assert.Contains(app.Console.Ui.Resources(), resource => resource.Id == "gocordis-console-assets");

        using var client = new HttpClient { BaseAddress = app.Address };
        var moduleResponse = await client.GetAsync($"/client-modules/{ModuleId}/ui.js");
        Assert.Equal(HttpStatusCode.OK, moduleResponse.StatusCode);
        Assert.Contains("gocordis-console-overview", await moduleResponse.Content.ReadAsStringAsync());

        var disabled = await app.Control!.SetEnabledAsync(PluginId, false, source: "builtin-test");
        Assert.True(disabled.Accepted, disabled.Error);
        Assert.Null(app.Console.Api.Query(StatusQuery));
        Assert.Empty(app.Console.Ui.ClientModules());
        Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "gocordis-console-page");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/client-modules/{ModuleId}/ui.js")).StatusCode);
        // Host infrastructure survives the plugin's teardown.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/recover")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/meta")).StatusCode);

        var enabled = await app.Control.SetEnabledAsync(PluginId, true, source: "builtin-test");
        Assert.True(enabled.Accepted, enabled.Error);
        Assert.Contains(app.Console.Ui.ClientModules(), module => module.Name == ModuleId);
        Assert.Equal(PluginId, app.Console.Api.Query(StatusQuery)?["plugin"]?.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/client-modules/{ModuleId}/ui.js")).StatusCode);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gocordis-builtin-console-" + Guid.NewGuid().ToString("N"));
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
