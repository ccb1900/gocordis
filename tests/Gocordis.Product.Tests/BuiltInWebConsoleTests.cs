using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Web Console is a protected builtin Fiber, not a Platform-owned subsystem:
/// its Component owns the HTTP transport, the React shell, the plugin ESM and
/// its UI/API contributions. Platform only bootstraps the capability set.
/// </summary>
public sealed class BuiltInWebConsoleTests
{
    private const string HostVersion = "1.5.0";
    private const string ModulePath = "/client-modules/gocordis-console-ui/ui.js";

    [Fact]
    public async Task WebConsoleRunsAsAProtectedBuiltinFiberWithItsOwnAssets()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("builtin-console-app", "1.0.0", HostVersion, [])
            .SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.Web, HostVersion: HostVersion,
            Address: "http://127.0.0.1:0"));
        await app.StartAsync();

        var fiber = app.Registry.Entries[WebConsolePlugin.Id].Fiber!;
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Contains(WebConsolePlugin.Id, app.Registry.ProtectedIds);
        Assert.NotNull(app.WebUi);
        Assert.NotNull(app.Address);

        Assert.True(File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "web-dist", "index.html")));
        Assert.True(File.Exists(System.IO.Path.Combine(
            AppContext.BaseDirectory, "client-modules", "gocordis-console-ui", "ui.js")));

        using var client = new HttpClient { BaseAddress = app.Address };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ModulePath)).StatusCode);
        Assert.Contains("gocordis-console-overview", await client.GetStringAsync(ModulePath));

        var status = JsonNode.Parse(await client.GetStringAsync("/api/query/gocordis-console/status"))!;
        Assert.Equal(WebConsolePlugin.Id, status["data"]!["plugin"]!.GetValue<string>());
        Assert.True(status["data"]!["installed"]!.GetValue<bool>());

        // Each builtin UI feature is its OWN protected plugin with its own
        // client module and owner-bound contributions.
        foreach (var (moduleName, packageId) in new[]
                 {
                     ("gocordis-console-ui", "gocordis-web-console"),
                     ("gocordis-control-plane-ui", "gocordis-control-plane-ui"),
                     ("gocordis-insight-ui", "gocordis-insight-ui"),
                 })
        {
            var module = Assert.Single(app.Console.Ui.ClientModules(), item => item.Name == moduleName);
            Assert.Equal(packageId, module.PackageId);
        }

        string OwnerOf(string pageId) =>
            Assert.Single(app.Console.Ui.Pages(), page => page.Id == pageId).Owner.PluginId;
        Assert.Equal("gocordis-web-console", OwnerOf("gocordis-console-page"));
        Assert.Equal("gocordis-control-plane-ui", OwnerOf("plugin-control-plane"));
        Assert.Equal("gocordis-insight-ui", OwnerOf("gocordis-framework"));
        Assert.Equal("gocordis-insight-ui", OwnerOf("gocordis-dependencies"));
        Assert.Equal("gocordis-insight-ui", OwnerOf("gocordis-runtime"));

        foreach (var pageId in new[]
                 {
                     "gocordis-console-page", "gocordis-framework", "gocordis-dependencies",
                     "gocordis-runtime", "plugin-control-plane",
                 })
        {
            Assert.Contains(app.Console.Ui.Pages(), page => page.Id == pageId);
            Assert.Contains(app.Console.Ui.Routes(), route => route.PageId == pageId);
            Assert.Contains(app.Console.Ui.Navigations(), nav => nav.PageId == pageId);
        }

        Assert.Equal("framework-dashboard",
            Assert.Single(app.Console.Ui.Pages(), page => page.Id == "gocordis-framework").Renderer);
        Assert.Equal("insight-graph",
            Assert.Single(app.Console.Ui.Pages(), page => page.Id == "gocordis-dependencies").Renderer);
        Assert.Equal("runtime-inspector",
            Assert.Single(app.Console.Ui.Pages(), page => page.Id == "gocordis-runtime").Renderer);
        Assert.Equal("plugin-explorer",
            Assert.Single(app.Console.Ui.Pages(), page => page.Id == "plugin-control-plane").Renderer);

        var uninstall = await app.Control!.UninstallAsync(WebConsolePlugin.Id, source: "test");
        Assert.False(uninstall.Accepted);
        Assert.Equal("forbidden", uninstall.Code);
        var disable = await app.Control.SetEnabledAsync(WebConsolePlugin.Id, false, source: "test");
        Assert.False(disable.Accepted);
        Assert.Equal("forbidden", disable.Code);
        Assert.Equal(FiberState.Active, fiber.State);
    }

    [Fact]
    public async Task DegradedHostKeepsOnlyRecoveryWhenWebConsoleIsDisabled()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("recovery-app", "1.0.0", HostVersion, [])
            .SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.Web, HostVersion: HostVersion,
            Address: "http://127.0.0.1:0", WebConsoleEnabled: false));
        await app.StartAsync();

        Assert.Null(app.WebUi);
        Assert.NotNull(app.Address);
        using var client = new HttpClient { BaseAddress = app.Address };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/recover")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/meta")).StatusCode);

        var recovered = await client.PostAsync("/api/host/recover",
            new StringContent("{ \"source\": \"recovery-test\" }"));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Contains(await app.Control!.AuditAsync(),
            entry => entry.Action == "recover" && entry.Source == "recovery-test");
    }

    [Fact]
    public async Task BuiltinUiContributionsExistInNativeRenderProfilesWithoutHttp()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("native-render-app", "1.0.0", HostVersion, [])
            .SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.WinForms, HostVersion: HostVersion));
        await app.StartAsync();

        Assert.Null(app.WebUi);
        Assert.Null(app.Address);
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "gocordis-dependencies");
        Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "plugin-control-plane");
        Assert.Contains(app.RenderPlan().Rendered, rendered => rendered.Id == "plugin-control-plane");
    }

    [Fact]
    public void PlatformDoesNotOwnWebConsoleConstructionAndBuiltinDoesNotReferencePlatform()
    {
        var root = FindRepositoryRoot();
        var platform = File.ReadAllText(System.IO.Path.Combine(root, "src", "Gocordis.Platform", "PlatformApplication.cs"));
        Assert.DoesNotContain("new WebUiServer", platform);
        Assert.DoesNotContain("await WebUi.StartAsync", platform);

        var controlPlane = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Gocordis.Packaging", "PluginControlPlaneComponent.cs"));
        Assert.Contains("ControlPlaneKeys.Dependencies", controlPlane);
        Assert.Contains("UiContributionRegistry.Key", controlPlane);
        Assert.DoesNotContain("UiContributionRegistry? Ui", controlPlane);
        Assert.DoesNotContain("RegisterUiPageAsync", controlPlane);

        var pluginDir = System.IO.Path.Combine(root, "src", "Gocordis.Builtin.WebConsole");
        var project = File.ReadAllText(System.IO.Path.Combine(pluginDir, "Gocordis.Builtin.WebConsole.csproj"));
        Assert.DoesNotContain("Gocordis.Platform", project);
        foreach (var source in Directory.EnumerateFiles(pluginDir, "*.cs"))
        {
            Assert.DoesNotContain("Gocordis.Platform", File.ReadAllText(source), StringComparison.Ordinal);
        }

        // The transport plugin no longer owns other builtin UI features.
        var webConsole = File.ReadAllText(System.IO.Path.Combine(pluginDir, "WebConsoleComponent.cs"));
        Assert.DoesNotContain("framework-dashboard", webConsole, StringComparison.Ordinal);
        Assert.DoesNotContain("insight-graph", webConsole, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-explorer", webConsole, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Gocordis.sln"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-webconsole-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string File(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
