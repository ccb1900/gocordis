using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Red tests for the final host/console split. The web transport, static
/// assets, control API and recovery page belong to the host; the console is an
/// ordinary UI plugin whose contributions live and die with its fiber.
/// </summary>
public sealed class UiHostSeparationTests
{
    private const string HostVersion = "1.5.0";
    private const string ConsolePlugin = "gocordis-console";
    private const string BusinessPlugin = "business-ui-plugin";

    [Fact]
    public async Task UninstallingConsolePluginLeavesHostRecoveryApiAndOtherPluginsIntact()
    {
        using var temp = new TempDir();
        await InstallUiPackageAsync(temp, ConsolePlugin, "1.0.0",
            UiResourceSpec.Page("console-plugin-page", "/console", "console-plugin-page"),
            UiResourceSpec.Route("console-route", "/console", "console-plugin-page"),
            UiResourceSpec.Navigation("console-nav", "插件", "/console", "运营"));
        await InstallUiPackageAsync(temp, BusinessPlugin, "1.0.0",
            UiResourceSpec.Page("business-page", "/business", "business-page"));
        var manifest = temp.File("gocordis.app.json");
        await new ApplicationManifest("host-separation-app", "1.0.0", HostVersion,
        [
            new ApplicationPlugin(ConsolePlugin, "[1.0,2.0)", true),
            new ApplicationPlugin(BusinessPlugin, "[1.0,2.0)", true),
        ]).SaveAsync(manifest);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifest, temp.Path("store"), Host: UiHostKind.Web, HostVersion: HostVersion,
            Address: "http://127.0.0.1:0"));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = app.Address };

        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "console-plugin-page");
        Assert.Contains(app.Console.Ui.Routes(), route => route.Id == "console-route");
        Assert.Contains(app.Console.Ui.Navigations(), nav => nav.Id == "console-nav");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/recover")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/meta")).StatusCode);

        var control = app.Control!;
        var removed = await control.UninstallAsync(ConsolePlugin, source: "test");
        Assert.True(removed.Accepted, removed.Error);

        Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "console-plugin-page");
        Assert.DoesNotContain(app.Console.Ui.Routes(), route => route.Id == "console-route");
        Assert.DoesNotContain(app.Console.Ui.Navigations(), nav => nav.Id == "console-nav");
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "business-page");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/recover")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/plugins")).StatusCode);

        var audit = JsonNode.Parse(await client.GetStringAsync("/api/plugins/audit"))!["data"]!["audit"]!.AsArray();
        Assert.Contains(audit, entry => entry!["action"]!.GetValue<string>() == "uninstall" &&
                                        entry["id"]!.GetValue<string>() == ConsolePlugin &&
                                        entry["source"]!.GetValue<string>() == "test" &&
                                        entry["outcome"]!.GetValue<string>() == "accepted");
    }

    [Fact]
    public async Task CorruptManifestFallsBackToLastKnownGoodAndApiCanRestoreIt()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        var manifest = new ApplicationManifest("recovery-app", "1.0.0", HostVersion, []);
        await manifest.SaveAsync(manifestPath);
        var backupPath = manifestPath + ApplicationManifest.LastKnownGoodSuffix;
        var goodBytes = await File.ReadAllBytesAsync(backupPath);

        await File.WriteAllTextAsync(manifestPath, "{ this is not json");
        var recovered = await ApplicationManifest.LoadAsync(manifestPath);
        Assert.Equal("recovery-app", recovered.Id);
        Assert.Equal(backupPath, recovered.RecoveredFrom);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.Web, HostVersion: HostVersion));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = app.Address };

        using var response = await client.PostAsync("/api/host/recover",
            new StringContent("{ \"source\": \"cli\" }", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(goodBytes, await File.ReadAllBytesAsync(manifestPath));

        var audit = JsonNode.Parse(await client.GetStringAsync("/api/plugins/audit"))!["data"]!["audit"]!.AsArray();
        Assert.Contains(audit, entry => entry!["action"]!.GetValue<string>() == "recover" &&
                                        entry["source"]!.GetValue<string>() == "cli");
    }

    [Fact]
    public async Task CorruptManifestWithoutSidecarReportsClearDiagnostic()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        await File.WriteAllTextAsync(manifestPath, "{ not json");

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => ApplicationManifest.LoadAsync(manifestPath));
        Assert.Contains("last-known-good backup", error.Message, StringComparison.Ordinal);
        Assert.Contains(manifestPath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverySeedsSidecarForHandWrittenManifest()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        // A hand-written manifest has no last-known-good sidecar.
        await File.WriteAllTextAsync(manifestPath, """
            { "Id": "hand-written", "Version": "1.0.0", "HostVersion": "1.5.0", "Plugins": [] }
            """);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), HostVersion: HostVersion));
        await app.StartAsync();

        var recovered = await app.Control!.RecoverManifestAsync("cli");
        Assert.True(recovered.Accepted, recovered.Error);
        Assert.True(File.Exists(manifestPath + ApplicationManifest.LastKnownGoodSuffix));
        Assert.Equal("hand-written", (await ApplicationManifest.LoadAsync(manifestPath)).Id);
        Assert.Contains(await app.Control.AuditAsync(),
            entry => entry.Action == "recover" && entry.Source == "cli" && entry.Outcome == "accepted");
    }

    [Fact]
    public async Task FailedConsoleUpgradeKeepsOldPageModuleAndLeavesNoNewPage()
    {
        using var temp = new TempDir();
        await InstallUiPackageAsync(temp, ConsolePlugin, "1.0.0",
            UiResourceSpec.Page("console-plugin-page", "/console", "console-plugin-page"),
            UiResourceSpec.ClientModule("console-module", "ui.js"));
        await InstallUiPackageAsync(temp, BusinessPlugin, "1.0.0",
            UiResourceSpec.Page("business-page", "/business", "business-page"));
        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("upgrade-app", "1.0.0", HostVersion,
        [
            new ApplicationPlugin(ConsolePlugin, "[1.0,2.0)", true),
            new ApplicationPlugin(BusinessPlugin, "[1.0,2.0)", true),
        ]).SaveAsync(manifestPath);
        var manifestBefore = await File.ReadAllTextAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), HostVersion: HostVersion));
        await app.StartAsync();

        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "console-plugin-page");
        Assert.Contains(app.Console.Ui.ClientModules(), module => module.Name == "console-module");

        // v2 declares a page id already owned by another plugin. Its Apply fails,
        // so the whole install/upgrade transaction must roll back.
        await PackUiPackageAsync(temp, ConsolePlugin, "2.0.0", install: false,
            UiResourceSpec.Page("console-v2-page", "/console-v2", "console-v2-page"),
            UiResourceSpec.Page("business-page", "/collision", "business-page"));
        var v2 = temp.File($"{ConsolePlugin}-2.0.0.gocordis-plugin");
        await using var upload = File.OpenRead(v2);
        var uploaded = await app.Control!.UploadAsync(upload, $"{ConsolePlugin}-2.0.0.gocordis-plugin", source: "test");
        var upgraded = await app.Control.UpgradeAsync(ConsolePlugin, uploaded.UploadKey, "[2.0,3.0)", source: "test");

        Assert.False(upgraded.Accepted, upgraded.Error ?? "duplicate page id must fail the upgrade");
        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(manifestPath));
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "console-plugin-page");
        Assert.Contains(app.Console.Ui.ClientModules(), module => module.Name == "console-module");
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "business-page");
        Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "console-v2-page");

        var store = await app.Control.StoreAsync();
        Assert.Contains(store, entry => entry.Id == ConsolePlugin && entry.Version == "1.0.0");
        Assert.DoesNotContain(store, entry => entry.Id == ConsolePlugin && entry.Version == "2.0.0");

        var audit = await app.Control.AuditAsync();
        Assert.Contains(audit, entry => entry.Action == "upgrade" && entry.Outcome == "rejected" &&
                                        entry.Id == ConsolePlugin && entry.Source == "test");
    }

    /// <summary>Packs a builtin UI-only package whose UI comes entirely from its
    /// own gocordis-ui.json declaration. No compiled plugin code is needed.</summary>
    private static async Task InstallUiPackageAsync(
        TempDir temp, string id, string version, params UiResourceSpec[] resources) =>
        await PackUiPackageAsync(temp, id, version, install: true, resources);

    private static async Task PackUiPackageAsync(
        TempDir temp, string id, string version, bool install, params UiResourceSpec[] resources)
    {
        var source = temp.Path($"source-{id}-{version}");
        Directory.CreateDirectory(Path.Combine(source, "ui"));
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "ui-only builtin package");

        var declarations = new JsonArray();
        foreach (var resource in resources)
        {
            var path = resource.Kind == "client-module"
                ? resource.Path
                : $"ui/{resource.Id}.json";
            if (resource.Kind == "client-module")
            {
                await File.WriteAllTextAsync(Path.Combine(source, resource.Path),
                    "export default function register() {}");
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(source, "ui", $"{resource.Id}.json"),
                    resource.Definition.ToJsonString());
            }

            declarations.Add(new JsonObject
            {
                ["kind"] = resource.Kind,
                ["id"] = resource.Id,
                ["path"] = path,
            });
        }

        await File.WriteAllTextAsync(Path.Combine(source, "gocordis-ui.json"),
            new JsonObject { ["resources"] = declarations }.ToJsonString());

        var package = temp.File($"{id}-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(id, version, id, "[1.0,)",
                Backend: "builtin", EntryAssembly: "entry.txt"), package);
        if (install)
        {
            await new PluginStore(temp.Path("store"), HostVersion).InstallAsync(package);
        }
    }

    private sealed record UiResourceSpec(string Kind, string Id, string Path, JsonObject Definition)
    {
        public static UiResourceSpec Page(string id, string route, string definitionId) => new(
            "page", id, route, new JsonObject
            {
                ["id"] = definitionId,
                ["title"] = id,
                ["route"] = route,
                ["renderer"] = "generic",
            });

        public static UiResourceSpec Route(string id, string path, string pageId) => new(
            "route", id, path, new JsonObject
            {
                ["id"] = id,
                ["path"] = path,
                ["pageId"] = pageId,
                ["layout"] = "shell",
            });

        public static UiResourceSpec Navigation(string id, string label, string route, string group) => new(
            "navigation", id, route, new JsonObject
            {
                ["id"] = id,
                ["label"] = label,
                ["route"] = route,
                ["group"] = group,
            });

        public static UiResourceSpec ClientModule(string id, string path) => new(
            "client-module", id, path, new JsonObject());
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-ui-host-" + Guid.NewGuid().ToString("N"));
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
