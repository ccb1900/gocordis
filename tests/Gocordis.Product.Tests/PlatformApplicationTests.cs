using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

public sealed class PlatformApplicationTests
{
    [Fact]
    public async Task WebHostStartsServesConsoleAndStopsCleanly()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        Assert.NotNull(app.Address);
        using var http = new HttpClient { BaseAddress = app.Address };
        var meta = JsonNode.Parse(await http.GetStringAsync("/api/meta"));
        Assert.Equal("gocordis-console", meta?["data"]?["kind"]?.GetValue<string>());
        Assert.Equal(UiHostKind.Web, app.RenderPlan().Host);

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task ProductHostHonorsAllowedHostPolicyWhenConfigured()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0",
            AllowedHosts: ["console.example.com"]);

        await using var app = new PlatformApplication(options);
        await app.StartAsync();
        Assert.NotNull(app.Address);

        using var http = new HttpClient { BaseAddress = app.Address };
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/meta");
        allowed.Headers.Host = "console.example.com";
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.SendAsync(allowed)).StatusCode);

        using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/meta");
        denied.Headers.Host = "evil.example.com";
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.SendAsync(denied)).StatusCode);
    }

    [Fact]
    public async Task NativeProfileStartsBackendWithoutWebServerAndRendersByProfile()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Host: UiHostKind.WinForms, HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        Assert.Null(app.Address);
        app.Console.Ui.RegisterPage(ContributionOwner.For("test", "platform"),
            new UiPageDefinition("product-page", "Product", "/product", "generic"));
        var plan = app.RenderPlan();
        Assert.Equal(UiHostKind.WinForms, plan.Host);
        var page = Assert.Single(plan.Rendered);
        Assert.StartsWith("winforms:", page.Descriptor, StringComparison.Ordinal);
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, f => f.Name == "webui");
    }

    [Fact]
    public async Task DiagnosticsQueryReportsVersionAndProviderFindings()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        // Store state is declaration data only: a package that requires a newer
        // host and names a dependency no installed module provides.
        await WriteInstalledPluginAsync(temp, "stale-plugin", "1.0.0",
            hostRange: "[2.0.0,)", dependencies: ["missing-module"], provides: ["stale.capability"]);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        Assert.Contains("devtools/diagnostics", app.Console.Api.Queries);
        using var http = new HttpClient { BaseAddress = app.Address };
        var payload = JsonNode.Parse(await http.GetStringAsync("/api/query/devtools/diagnostics"));
        var findings = payload?["data"]?["diagnostics"]?.AsArray()
                       ?? throw new InvalidOperationException("diagnostics query returned no array");

        Assert.Contains(findings, finding =>
            finding?["code"]?.GetValue<string>() == "VERSION_MISMATCH" &&
            finding?["severity"]?.GetValue<string>() == "error");
        Assert.Contains(findings, finding =>
            finding?["code"]?.GetValue<string>() == "UNKNOWN_DEPENDENCY" &&
            finding?["severity"]?.GetValue<string>() == "error");
        Assert.All(findings, finding =>
        {
            Assert.NotNull(finding?["severity"]);
            Assert.NotNull(finding?["message"]);
        });

        // A consistent declaration set has nothing to report, and the same
        // query is reachable without the web host.
        using var clean = new TempDir();
        var cleanManifest = await WriteManifestAsync(clean);
        await WriteInstalledPluginAsync(clean, "compatible-plugin", "1.0.0",
            hostRange: "[1.0.0,)", dependencies: [], provides: ["sample.capability"]);
        await using var cleanApp = new PlatformApplication(new PlatformHostOptions(
            cleanManifest, clean.Path("store"), HostVersion: "1.5.0"));
        await cleanApp.StartAsync();
        var cleanResult = cleanApp.Console.Api.Query("devtools/diagnostics");
        Assert.Empty(cleanResult?["diagnostics"]!.AsArray() ?? throw new InvalidOperationException("missing diagnostics"));
    }

    [Fact]
    public void OptionsRejectRemoteBindWithoutOptIn()
    {
        Assert.Throws<InvalidOperationException>(() => new PlatformHostOptions(
            "app.json", "store", "http://0.0.0.0:8080", AllowRemote: false));
        var allowed = new PlatformHostOptions(
            "app.json", "store", "http://0.0.0.0:8080", AllowRemote: true);
        Assert.Equal("http://0.0.0.0:8080", allowed.Address);
    }

    private static async Task<string> WriteManifestAsync(TempDir temp)
    {
        var path = temp.File("gocordis.app.json");
        await new ApplicationManifest("product-app", "1.0.0", "1.5.0", []).SaveAsync(path);
        return path;
    }

    private static async Task WriteInstalledPluginAsync(
        TempDir temp, string id, string version, string hostRange,
        string[] dependencies, string[] provides)
    {
        var store = temp.Path("store");
        Directory.CreateDirectory(store);
        var state = new
        {
            Plugins = new[]
            {
                new
                {
                    Manifest = new
                    {
                        Id = id,
                        Version = version,
                        Type = id,
                        HostVersionRange = hostRange,
                        DisplayName = id,
                        Dependencies = dependencies
                            .Select(dependency => new { Id = dependency, VersionRange = ">=1.0.0" })
                            .ToArray(),
                        Provides = provides,
                        Checksum = (string?)null,
                        Files = Array.Empty<object>(),
                    },
                    InstallPath = Path.Combine(store, id, version),
                },
            },
        };
        await File.WriteAllTextAsync(Path.Combine(store, "installed.json"), JsonSerializer.Serialize(state));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-platform-{Guid.NewGuid():N}");
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
