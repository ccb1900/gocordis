using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Product-level red test for the P3 plugin UI package loop: an independent
/// package carries its own UI resource declaration, installing it is enough
/// for the host to contribute pages/panels/modules from the Store install
/// directory, and Fiber effects unwind every contribution on disable/upgrade/
/// uninstall. The WebUiServer is deliberately created WITHOUT a
/// clientModuleRootResolver: the install directory must be enough.
/// </summary>
public sealed class PluginUiPackageProductTests
{
    private const string HostVersion = "1.5.0";
    private const string PluginId = "ui-demo";

    [Fact]
    public async Task InstalledUiPackageDrivesConsoleCompositionAcrossTheWholeLifecycle()
    {
        await using var fx = await Fixture.CreateAsync();
        await using var server = new WebUiServer(fx.Host, registry: fx.Registry, control: fx.Control);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        // Install v1. No host code is changed for this plugin: the package is
        // the only source of the page, panel and client-module declarations.
        var upload1 = await fx.UploadAsync(await fx.BuildPackageAsync("1.0.0", "First UI"));
        var installed = await fx.Control.InstallAsync(upload1);
        Assert.True(installed.Accepted, installed.Error);

        var pages = await client.GetStringAsync("/api/ui/pages");
        var panelJson = await client.GetStringAsync("/api/ui/panels");
        var modulesJson = await client.GetStringAsync("/api/ui/client-modules");
        var page = Assert.Single(
            JsonNode.Parse(pages)!["data"]!["pages"]!.AsArray().OfType<JsonObject>(),
            item => item["id"]!.GetValue<string>() == "ui-demo-page");
        Assert.Equal("First UI", page["title"]!.GetValue<string>());
        Assert.Single(JsonNode.Parse(panelJson)!["data"]!["panels"]!.AsArray());
        var module = Assert.Single(JsonNode.Parse(modulesJson)!["data"]!["modules"]!.AsArray())!;
        Assert.Equal("ui-demo-client", module["name"]!.GetValue<string>());
        var url1 = module["url"]!.GetValue<string>();
        Assert.Matches("^/client-modules/ui-demo-client/ui\\.js\\?v=[0-9a-f]{8}$", url1);
        Assert.Equal("ui-demo", module["packageId"]!.GetValue<string>());
        Assert.Matches("^[0-9a-f]{64}$", module["contentHash"]!.GetValue<string>());

        // The combined manifest exposes the same composition without polling
        // three separate endpoints.
        var combined = JsonNode.Parse(await client.GetStringAsync("/api/ui/manifest"))!["data"]!;
        Assert.Contains(combined["pages"]!.AsArray().OfType<JsonObject>(),
            item => item["id"]!.GetValue<string>() == "ui-demo-page");
        Assert.Single(combined["panels"]!.AsArray());
        Assert.Single(combined["modules"]!.AsArray());

        // The contribution owner is the plugin's own Fiber, not the console host.
        var fiber = fx.Registry.Entries[PluginId].Fiber;
        Assert.NotNull(fiber);
        Assert.Equal(fiber.Id, fx.Host.Ui.Pages().Single(p => p.Id == "ui-demo-page").Owner.FiberId);
        Assert.Equal(fiber.Id, fx.Host.Ui.Panels().Single().Owner.FiberId);
        Assert.Equal(fiber.Id, fx.Host.Ui.ClientModules().Single().Owner.FiberId);

        // Served bytes come from the version directory in the Store, not from
        // AppContext.BaseDirectory/client-modules or an injected resolver.
        using var served = await client.GetAsync(url1);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("FIRST-UI", await served.Content.ReadAsStringAsync());

        // Disable: all UI contributions unwind with the Fiber's effects.
        var disabled = await fx.Control.SetEnabledAsync(PluginId, false);
        Assert.True(disabled.Accepted, disabled.Error);
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray());
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/panels"))!["data"]!["panels"]!.AsArray());
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray());

        // Re-enable restores the same declarations from the installed directory.
        var enabled = await fx.Control.SetEnabledAsync(PluginId, true);
        Assert.True(enabled.Accepted, enabled.Error);
        Assert.Single(JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray());

        // Upgrade replaces the old module: the URL (content cache key) changes
        // and the old version directory is removed from the Store.
        var upload2 = await fx.UploadAsync(await fx.BuildPackageAsync("2.0.0", "Second UI"));
        var upgraded = await fx.Control.UpgradeAsync(PluginId, upload2, "2.0.0");
        Assert.True(upgraded.Accepted, upgraded.Error);
        var modulesAfterUpgrade = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray();
        var upgradedModule = Assert.Single(modulesAfterUpgrade)!;
        var url2 = upgradedModule["url"]!.GetValue<string>();
        Assert.NotEqual(url1, url2);
        Assert.NotEqual(module["contentHash"]!.GetValue<string>(),
            upgradedModule["contentHash"]!.GetValue<string>());
        using var served2 = await client.GetAsync(url2);
        Assert.Contains("SECOND-UI", await served2.Content.ReadAsStringAsync());
        var upgradedPages = JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray();
        var upgradedPage = Assert.Single(upgradedPages.OfType<JsonObject>(),
            item => item["id"]!.GetValue<string>() == "ui-demo-page");
        Assert.Equal("Second UI", upgradedPage["title"]!.GetValue<string>());
        Assert.Single(await fx.Store.ListAsync());
        Assert.False(Directory.Exists(Path.Combine(fx.Store.Root, PluginId, "1.0.0")));

        // Uninstall removes every contribution and the installed directory.
        var uninstalled = await fx.Control.UninstallAsync(PluginId, "2.0.0");
        Assert.True(uninstalled.Accepted, uninstalled.Error);
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray());
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/panels"))!["data"]!["panels"]!.AsArray());
        Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray());
        Assert.Empty(await fx.Store.ListAsync());
        Assert.False(Directory.Exists(Path.Combine(fx.Store.Root, PluginId)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;

        private Fixture(string root, Gocordis.Core.Runtime runtime, ConsoleHost host,
            PluginRegistry registry, PluginStore store, PluginControlService control)
        {
            _root = root;
            Runtime = runtime;
            Host = host;
            Registry = registry;
            Store = store;
            Control = control;
        }

        public Gocordis.Core.Runtime Runtime { get; }
        public ConsoleHost Host { get; }
        public PluginRegistry Registry { get; }
        public PluginStore Store { get; }
        public PluginControlService Control { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"gocordis-ui-package-{Guid.NewGuid():N}");
            try
            {
                var runtime = new Gocordis.Core.Runtime();
                try
                {
                    var host = new ConsoleHost(runtime);
                    var registry = new PluginRegistry(runtime);
                    registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
                    registry.ExternalCapabilities.Add(ApiGateway.Key);
                    await runtime.LoadAsync(new HostComponent(host));

                    var store = new PluginStore(Path.Combine(root, "store"), HostVersion);
                    var application = Path.Combine(root, "app.json");
                    await new ApplicationManifest("ui-package-product", "1.0.0", HostVersion, [])
                        .SaveAsync(application);

                    var control = new PluginControlService(application, store, registry, host.Ui);
                    await control.ApplyAllAsync();
                    return new Fixture(root, runtime, host, registry, store, control);
                }
                catch
                {
                    await runtime.DisposeAsync();
                    throw;
                }
            }
            catch
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                throw;
            }
        }

        public async Task<string> BuildPackageAsync(string version, string title)
        {
            var source = Path.Combine(_root, $"source-{version}");
            Directory.CreateDirectory(Path.Combine(source, "ui"));
            await File.WriteAllTextAsync(Path.Combine(source, "gocordis-ui.json"),
                """
                {
                  "resources": [
                    { "kind": "page", "id": "ui-demo-page", "path": "ui/page.json" },
                    { "kind": "panel", "id": "ui-demo-panel", "path": "ui/panel.json" },
                    { "kind": "client-module", "id": "ui-demo-client", "path": "ui.js" }
                  ]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(source, "ui", "page.json"),
                $$"""{"id":"ui-demo-page","title":"{{title}}","route":"/ui-demo","renderer":"generic"}""");
            await File.WriteAllTextAsync(Path.Combine(source, "ui", "panel.json"),
                """{"id":"ui-demo-panel","title":"UI Panel","position":"right","renderer":"event-feed"}""");
            var marker = version.StartsWith('1') ? "FIRST-UI" : "SECOND-UI";
            await File.WriteAllTextAsync(Path.Combine(source, "ui.js"),
                $"export default function register() {{ /* {marker} */ }}");

            var package = Path.Combine(_root, $"ui-demo-{version}.gocordis-plugin");
            await PluginPackageWriter.CreateAsync(source,
                new PluginPackageManifest(PluginId, version, PluginId, "[1.0,)",
                    DisplayName: "UI Demo", Dependencies: [], Provides: []), package);
            return package;
        }

        public async Task<string> UploadAsync(string package)
        {
            await using var stream = File.OpenRead(package);
            var result = await Control.UploadAsync(stream, Path.GetFileName(package));
            return result.UploadKey;
        }

        public async ValueTask DisposeAsync()
        {
            await Registry.DisposeAsync();
            await Runtime.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
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
}
