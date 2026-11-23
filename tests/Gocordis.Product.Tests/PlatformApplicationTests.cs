using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

public sealed class PlatformApplicationTests
{
    [Theory]
    [InlineData("winforms")]
    [InlineData("avalonia")]
    [InlineData("1")]
    public async Task PlatformRunnerRejectsNonWebHostOptions(string host)
    {
        var code = await PlatformRunner.RunAsync(
            ["--manifest", "missing.json", "--store", "unused-store", "--host", host]);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task ProductHostOptionsCarryTrustAndRemoteWritePolicy()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var trust = PluginTrustPolicy.TrustedOnly("pinned-key");
        ConsoleAccessPolicy policy = _ => true;
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Trust: trust, AccessPolicy: policy);

        Assert.Same(trust, options.Trust);
        Assert.Same(policy, options.AccessPolicy);
    }

    [Fact]
    public async Task PlatformRunnerParsesTrustedOnlyConfiguration()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var pem = temp.File("runner-key.pem");
        await File.WriteAllTextAsync(pem, "pem-public-key");

        string[] Base(string trust) =>
        [
            "--manifest", manifestPath, "--store", temp.Path("store"),
            "--host-version", "1.5.0", "--validate-only", "--trust", trust,
        ];

        Assert.Equal(1, await PlatformRunner.RunAsync([.. Base("bogus")]));
        Assert.Equal(1, await PlatformRunner.RunAsync([.. Base("trusted-only")]));
        Assert.Equal(0, await PlatformRunner.RunAsync(
            [.. Base("trusted-only"), "--public-key", pem]));
    }

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
    public async Task ValidateOnlyStartsTheRealAssemblyButOpensNoTransport()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0", ValidateOnly: true);

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        // The same declaration/effect path ran: the control plane fiber is
        // active and resolution is observable to the CLI renderer.
        Assert.Null(app.Address);
        Assert.NotNull(app.Control);
        Assert.Contains(app.Runtime.Snapshot().Fibers,
            fiber => fiber.Name == "plugin-control-plane" && fiber.State == FiberState.Active);
        Assert.NotNull(app.Manifest);
        Assert.Empty(app.ResolvedPlugins);

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task ControlPlaneIsAPluginContribution()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        // The control plane is reconciled by the one Core Runtime, not built by
        // the composition root: it is a fiber that provides a typed capability.
        var controlFiber = Assert.Single(app.Runtime.Snapshot().Fibers,
            fiber => fiber.Name == "plugin-control-plane");
        Assert.Equal(FiberState.Active, controlFiber.State);
        Assert.Contains("plugin-control-plane", controlFiber.Provides);
        Assert.NotNull(app.Control);
        Assert.Same(app.Control, app.Runtime.RequireCapability(ControlPlaneKeys.Service));
        var controlPage = Assert.Single(app.Console.Ui.Pages(), page => page.Id == "plugin-control-plane");
        Assert.Equal("plugin-explorer", controlPage.Renderer);

        // The existing HTTP contract still goes through the resolved capability.
        Assert.NotNull(app.Address);
        using var http = new HttpClient { BaseAddress = app.Address };
        var plugins = JsonNode.Parse(await http.GetStringAsync("/api/plugins"));
        Assert.NotNull(plugins?["data"]);
        var store = JsonNode.Parse(await http.GetStringAsync("/api/plugins/store"));
        Assert.NotNull(store?["data"]);
        using var control = await http.PostAsJsonAsync("/api/plugins/control",
            new { pluginId = "not-declared", enable = true });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, control.StatusCode);
        var rejection = JsonNode.Parse(await control.Content.ReadAsStringAsync());
        Assert.Equal("not_found", rejection?["code"]?.GetValue<string>());

        await app.StopAsync();
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers,
            fiber => fiber.Name == "plugin-control-plane");
        Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "plugin-control-plane");
        Assert.Throws<DependencyMissingException>(
            () => app.Runtime.RequireCapability(ControlPlaneKeys.Service));
    }

    [Fact]
    public async Task FailedWebHostStartReleasesRuntimeAndRegistry()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:99999", HostVersion: "1.5.0",
            AllowedHosts: ["console.example.com"]);

        var app = new PlatformApplication(options);
        await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync());
        await app.DisposeAsync();

        Assert.Empty(app.Runtime.Snapshot().Fibers);
        Assert.Empty(app.Registry.Entries);
        Assert.Null(app.WebUi);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => app.Runtime.LoadAsync(new ProbeComponent()));
    }

    [Fact]
    public async Task FailedApplyAllReleasesControlPlaneFiber()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("missing-plugin.app.json");
        await new ApplicationManifest("product-app", "1.0.0", "1.5.0",
            [new ApplicationPlugin("ghost", "1.0.0", true)]).SaveAsync(manifestPath);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            HostVersion: "1.5.0", AllowedHosts: ["console.example.com"]);

        var app = new PlatformApplication(options);
        await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync());
        await app.DisposeAsync();

        Assert.Empty(app.Runtime.Snapshot().Fibers);
        Assert.Empty(app.Registry.Entries);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => app.Runtime.LoadAsync(new ProbeComponent()));
    }

    [Fact]
    public async Task FailedManifestLoadReleasesRuntime()
    {
        using var temp = new TempDir();
        var options = new PlatformHostOptions(temp.Path("does-not-exist.json"), temp.Path("store"),
            HostVersion: "1.5.0", AllowedHosts: ["console.example.com"]);

        var app = new PlatformApplication(options);
        await Assert.ThrowsAsync<FileNotFoundException>(() => app.StartAsync());
        await app.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => app.Runtime.LoadAsync(new ProbeComponent()));
    }

    [Theory]
    [InlineData("webui", "webui dispose")]
    [InlineData("registry", "registry cleanup")]
    [InlineData("runtime", "runtime cleanup")]
    [InlineData("routeGuard", "route guard cleanup")]
    public async Task CleanupFailureIsIsolatedAndRetried(string fault, string expectedMessage)
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0",
            AllowedHosts: ["console.example.com"]);

        var app = new FaultyPlatformApplication(options, fault);
        await app.StartAsync();

        var failure = await Assert.ThrowsAsync<AggregateException>(() => app.StopAsync());
        var inner = Assert.Single(failure.InnerExceptions);
        Assert.Contains(expectedMessage, inner.Message, StringComparison.OrdinalIgnoreCase);

        // Every other resource was still attempted, and a failed step leaves the
        // host stoppable rather than marking cleanup as complete.
        Assert.Equal(1, app.WebUiDisposeCalls);
        Assert.Equal(1, app.RegistryDisposeCalls);
        Assert.Equal(1, app.RuntimeDisposeCalls);
        Assert.Equal(1, app.RouteGuardDisposeCalls);

        await app.StopAsync();
        Assert.Equal(fault == "webui" ? 2 : 1, app.WebUiDisposeCalls);
        Assert.Equal(2, app.RegistryDisposeCalls);
        Assert.Equal(2, app.RuntimeDisposeCalls);
        Assert.Equal(2, app.RouteGuardDisposeCalls);

        var callsBeforeDispose = app.WebUiDisposeCalls + app.RegistryDisposeCalls +
                                 app.RuntimeDisposeCalls + app.RouteGuardDisposeCalls;
        await app.DisposeAsync();
        Assert.Equal(callsBeforeDispose, app.WebUiDisposeCalls + app.RegistryDisposeCalls +
            app.RuntimeDisposeCalls + app.RouteGuardDisposeCalls);
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
        var page = Assert.Single(plan.Rendered, rendered => rendered.Id == "product-page");
        Assert.StartsWith("winforms:generic", page.Descriptor, StringComparison.Ordinal);
        Assert.Contains(plan.Rendered, rendered => rendered.Id == "plugin-control-plane");
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
    public async Task PackageDependencyInstalledButNotDeclaredIsReportedWithoutAutoAssembly()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        Directory.CreateDirectory(store);
        // A is installed; B's package depends on A. The application declares
        // ONLY B. Store-level dependency is satisfied, but assembly is
        // declaration-led — A must not be auto-declared or auto-activated.
        var state = new
        {
            Plugins = new object[]
            {
                new
                {
                    Manifest = new
                    {
                        Id = "provider", Version = "1.0.0", Type = "provider",
                        HostVersionRange = "[1.0,2.0)", DisplayName = "provider",
                        Dependencies = Array.Empty<object>(), Provides = new[] { "provider.capability" },
                        Checksum = (string?)null, Files = Array.Empty<object>(),
                    },
                    InstallPath = Path.Combine(store, "provider", "1.0.0"),
                },
                new
                {
                    Manifest = new
                    {
                        Id = "consumer", Version = "1.0.0", Type = "consumer",
                        HostVersionRange = "[1.0,2.0)", DisplayName = "consumer",
                        Dependencies = new[] { new { Id = "provider", VersionRange = "[1.0,2.0)" } },
                        Provides = Array.Empty<string>(),
                        Checksum = (string?)null, Files = Array.Empty<object>(),
                    },
                    InstallPath = Path.Combine(store, "consumer", "1.0.0"),
                },
            },
        };
        await File.WriteAllTextAsync(Path.Combine(store, "installed.json"), JsonSerializer.Serialize(state));

        var manifestPath = temp.File("gocordis.app.json");
        await new ApplicationManifest("decl-app", "1.0.0", "1.5.0",
            [new ApplicationPlugin("consumer", "[1.0,2.0)", true)]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: "1.5.0"));
        await app.StartAsync();

        // No auto-assembly: only the declared consumer has an entry/fiber.
        Assert.Single(app.Registry.Entries);
        Assert.True(app.Registry.Entries.ContainsKey("consumer"));
        Assert.False(app.Registry.Entries.ContainsKey("provider"));

        // Explicit diagnostic closes the "package satisfied but no runtime
        // provider" ambiguity — read-only, activation semantics unchanged.
        var diagnostics = app.Console.Api.Query("devtools/diagnostics")!["diagnostics"]!.AsArray();
        Assert.Contains(diagnostics, finding =>
            finding?["code"]?.GetValue<string>() == "PACKAGE_DEPENDENCY_NOT_DECLARED" &&
            finding?["severity"]?.GetValue<string>() == "warn" &&
            finding?["message"]?.GetValue<string>()?.Contains("provider") == true);
    }

    [Fact]
    public void PlatformCliUsageStatesNativeHostBoundary()
    {
        Assert.Contains("render-plan\nprofiles for future native hosts", PlatformRunner.UsageText, StringComparison.Ordinal);
        Assert.Contains("Web is the only runnable UI host", PlatformRunner.UsageText, StringComparison.Ordinal);
        Assert.Contains("--trust development|trusted-only", PlatformRunner.UsageText, StringComparison.Ordinal);
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

    private sealed class FaultyPlatformApplication : PlatformApplication
    {
        private readonly string _fault;
        private bool _faultInjected;

        public FaultyPlatformApplication(PlatformHostOptions options, string fault) : base(options) =>
            _fault = fault;

        public int WebUiDisposeCalls { get; private set; }
        public int RegistryDisposeCalls { get; private set; }
        public int RuntimeDisposeCalls { get; private set; }
        public int RouteGuardDisposeCalls { get; private set; }

        protected override ValueTask DisposeWebUiAsync()
        {
            WebUiDisposeCalls++;
            if (ShouldFail("webui"))
            {
                throw new IOException("injected webui dispose failure");
            }

            return base.DisposeWebUiAsync();
        }

        protected override ValueTask DisposeRegistryAsync()
        {
            RegistryDisposeCalls++;
            if (ShouldFail("registry"))
            {
                throw new IOException("injected registry dispose failure");
            }

            return base.DisposeRegistryAsync();
        }

        protected override ValueTask DisposeRuntimeAsync()
        {
            RuntimeDisposeCalls++;
            if (ShouldFail("runtime"))
            {
                throw new IOException("injected runtime dispose failure");
            }

            return base.DisposeRuntimeAsync();
        }

        protected override ValueTask DisposeRouteGuardAsync()
        {
            RouteGuardDisposeCalls++;
            if (ShouldFail("routeGuard"))
            {
                throw new IOException("injected route guard dispose failure");
            }

            return base.DisposeRouteGuardAsync();
        }

        private bool ShouldFail(string step)
        {
            if (_fault != step || _faultInjected)
            {
                return false;
            }

            _faultInjected = true;
            return true;
        }
    }

    private sealed class ProbeComponent : Component
    {
        public override string Name => "probe";
        public override ValueTask<Cleanup?> ApplyAsync(Context context) => ValueTask.FromResult<Cleanup?>(null);
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
