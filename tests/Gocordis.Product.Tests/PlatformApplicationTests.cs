using System.Net;
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

// The host-runner tests capture process-global stderr; never interleave them
// with other test collections that might do the same.
[CollectionDefinition("PlatformConsole", DisableParallelization = true)]
public sealed class PlatformConsoleCollection;

[Collection("PlatformConsole")]
public sealed class PlatformApplicationTests
{
    [Theory]
    [InlineData("winforms")]
    [InlineData("avalonia")]
    [InlineData("1")]
    public async Task PlatformRunnerRejectsNonWebHostOptions(string host)
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        string[] Args(string value) =>
        [
            "--manifest", manifestPath, "--store", temp.Path("store"),
            "--host-version", "1.5.0", "--validate-only", "--host", value,
        ];

        // A real manifest proves the only reason for failure is the host value.
        Assert.Equal(0, await PlatformRunner.RunAsync(Args("web")));

        var original = System.Console.Error;
        using var errors = new StringWriter();
        System.Console.SetError(errors);
        try
        {
            Assert.Equal(1, await PlatformRunner.RunAsync(Args(host)));
        }
        finally
        {
            System.Console.SetError(original);
        }

        Assert.Contains("is not runnable", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("web is the only product UI host", errors.ToString(), StringComparison.Ordinal);
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
    public async Task ProductRootDeniesRemoteApiWithoutAnAccessPolicy()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        string Base() => temp.Path("store");

        await using (var locked = new PlatformApplication(new PlatformHostOptions(
            manifestPath, Base(), Address: "http://127.0.0.1:0",
            AllowRemote: true, HostVersion: "1.5.0")))
        {
            await locked.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = locked.Address };
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/plugins")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/api/query/devtools/diagnostics")).StatusCode);
            var denied = await client.PostAsJsonAsync("/api/plugins/control",
                new JsonObject { ["pluginId"] = "absent", ["enable"] = false });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        await using (var permitted = new PlatformApplication(new PlatformHostOptions(
            manifestPath, Base(), Address: "http://127.0.0.1:0",
            AllowRemote: true, HostVersion: "1.5.0",
            AccessPolicy: _ => true)))
        {
            await permitted.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = permitted.Address };
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/plugins")).StatusCode);
            var accepted = await client.PostAsJsonAsync("/api/plugins/control",
                new JsonObject { ["pluginId"] = "absent", ["enable"] = false });
            Assert.NotEqual(HttpStatusCode.Forbidden, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task ProductHostTrustPolicyRejectsControlPlaneInstallOfUnsignedPackage()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var source = temp.Path("unsigned-source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "payload");
        var package = temp.File("unsigned.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "unsigned-demo", "1.0.0", "unsigned-demo", "[1.0,)"), package);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), HostVersion: "1.5.0",
            Trust: PluginTrustPolicy.TrustedOnly("pinned-public-key")));
        await app.StartAsync();

        await using var stream = File.OpenRead(package);
        var upload = await app.Control!.UploadAsync(stream, "unsigned.gocordis-plugin");
        var rejected = await app.Control.InstallAsync(upload.UploadKey);
        Assert.False(rejected.Accepted);
        Assert.Contains("signature", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await app.Store.ListAsync());
    }

    [Fact]
    public async Task PlatformRunnerTrustFlagsAreEnforcedByTheRunningControlPlane()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("runner-trust-store");
        var source = temp.Path("trust-source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "payload");
        var unsigned = temp.File("runner-unsigned.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "runner-trust-demo", "1.0.0", "runner-trust-demo", "[1.0,)"), unsigned);

        var (privateKey, publicKey) = PluginPackageSigner.CreateKeyPair();
        var pem = temp.File("runner-trust.pem");
        await File.WriteAllTextAsync(pem, publicKey);
        var signed = await PluginPackageSigner.SignAsync(unsigned, privateKey);

        PlatformApplication? started = null;
        var code = await PlatformRunner.RunAsync(
        [
            "--manifest", manifestPath, "--store", store, "--host-version", "1.5.0",
            "--validate-only", "--trust", "trusted-only", "--public-key", pem,
        ], new PlatformRunHooks
        {
            WriteStartupBanner = false,
            AfterStartAsync = async app =>
            {
                started = app;
                await using var stream = File.OpenRead(unsigned);
                var upload = await app.Control!.UploadAsync(stream, "unsigned.gocordis-plugin");
                var rejected = await app.Control.InstallAsync(upload.UploadKey);
                Assert.False(rejected.Accepted);
                Assert.Contains("signature", rejected.Error, StringComparison.OrdinalIgnoreCase);

                await using var signedStream = File.OpenRead(signed);
                var signedUpload = await app.Control.UploadAsync(signedStream, "signed.gocordis-plugin");
                var accepted = await app.Control.InstallAsync(signedUpload.UploadKey);
                Assert.True(accepted.Accepted, accepted.Error);
            },
        });

        Assert.Equal(0, code);
        Assert.NotNull(started);
        Assert.Single(await started.Store.ListAsync());
        Assert.Empty(started.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task PlatformRunnerAllowedHostFlagsReachTheRouteGuard()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var code = await PlatformRunner.RunAsync(
        [
            "--manifest", manifestPath, "--store", temp.Path("allowed-host-store"),
            "--host-version", "1.5.0", "--allowed-host", "console.example.com",
        ], new PlatformRunHooks
        {
            WriteStartupBanner = false,
            WaitForShutdown = false,
            AfterStartAsync = async app =>
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
                {
                    BaseAddress = app.Address,
                };
                using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/meta");
                allowed.Headers.Host = "console.example.com";
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(allowed)).StatusCode);

                using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/meta");
                denied.Headers.Host = "evil.example.com";
                Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(denied)).StatusCode);
            },
        });
        Assert.Equal(0, code);
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
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = app.Address };
        var meta = JsonNode.Parse(await http.GetStringAsync("/api/meta"));
        Assert.Equal("gocordis-console", meta?["data"]?["kind"]?.GetValue<string>());
        Assert.Equal(UiHostKind.Web, app.RenderPlan().Host);

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task RepeatedStartStopLeavesNoFiberOrListenerResidue()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0");

        for (var round = 0; round < 4; round++)
        {
            var app = new PlatformApplication(options);
            await app.StartAsync();
            Assert.NotEmpty(app.Runtime.Snapshot().Fibers);
            Assert.NotNull(app.Address);

            await app.StopAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
            await app.DisposeAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
        }
    }

    [Fact]
    public async Task RuntimeSnapshotQueryExposesInspectorFields()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), HostVersion: "1.5.0"));
        await app.StartAsync();

        var snapshot = app.Console.Api.Query("runtime/snapshot")!;
        Assert.NotNull(snapshot["sequence"]);
        Assert.Equal("runtime:1", snapshot["runtimeID"]?.GetValue<string>());

        var fiber = snapshot["fibers"]!.AsArray()[0]!;
        Assert.False(string.IsNullOrWhiteSpace(fiber["component"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(fiber["intent"]?.GetValue<string>()));
        Assert.Equal(fiber["generation"]!.GetValue<int>(), fiber["activationID"]!.GetValue<int>());
        Assert.NotNull(fiber["parentFiberIDs"]);
        Assert.NotNull(fiber["childFiberIDs"]);
        Assert.NotEmpty(snapshot["providers"]!.AsArray());
        Assert.NotEmpty(snapshot["dependencies"]!.AsArray());
        Assert.NotNull(snapshot["scopes"]);
        Assert.NotNull(snapshot["effects"]);
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
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = app.Address };
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

        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = app.Address };
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
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = app.Address };
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
        Assert.Matches(@"render-plan\s+profiles for future native hosts", PlatformRunner.UsageText);
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

    [Fact]
    public async Task HmrRejectsSourceBehindASymlinkedIntermediateDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Directory symlinks/junctions need developer mode or elevation on
            // Windows; the Windows-specific reparse test below covers that case
            // when the privilege is available.
            return;
        }

        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("store");
        var plugins = Path.Combine(store, "plugins");
        Directory.CreateDirectory(plugins);
        var outside = temp.Path("outside");
        Directory.CreateDirectory(outside);
        var payload = Path.Combine(outside, "plugin.dll");
        await File.WriteAllTextAsync(payload, "outside payload");

        // The final file is NOT a link: its lexical path looks inside the
        // store, but an intermediate directory redirects it outside.
        Directory.CreateSymbolicLink(Path.Combine(plugins, "external"), outside);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, HostVersion: "1.5.0"));
        await app.StartAsync();

        var rejection = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReloadWithSourceAsync(app, Path.Combine(plugins, "external", "plugin.dll")));
        Assert.Contains("inside the configured plugin store", rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HmrRejectsDotDotTraversalThroughASymlinkedDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Directory symlinks need developer mode or elevation on Windows;
            // see the conditional reparse-point test below.
            return;
        }

        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("store");
        var plugins = Path.Combine(store, "plugins");
        Directory.CreateDirectory(plugins);
        var outside = temp.Path("outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(plugins, "external"), outside);

        // OS semantics: external/../payload.dll resolves against the LINK TARGET
        // (temp/payload.dll), while lexical normalization would collapse it to
        // an innocuous-looking in-store path.
        var payload = temp.Path("payload.dll");
        await File.WriteAllTextAsync(payload, "outside payload");

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, HostVersion: "1.5.0"));
        await app.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReloadWithSourceAsync(app, Path.Combine(plugins, "external", "..", "payload.dll")));
    }

    [Fact]
    public async Task HmrRejectsSourceWhoseFinalEntryIsASymlinkToOutside()
    {
        if (OperatingSystem.IsWindows())
        {
            // File symlinks need developer mode or elevation on Windows.
            return;
        }

        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("store");
        var plugins = Path.Combine(store, "plugins");
        Directory.CreateDirectory(plugins);
        var outside = temp.Path("outside");
        Directory.CreateDirectory(outside);
        var payload = Path.Combine(outside, "plugin.dll");
        await File.WriteAllTextAsync(payload, "outside payload");
        File.CreateSymbolicLink(Path.Combine(plugins, "linked.dll"), payload);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, HostVersion: "1.5.0"));
        await app.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReloadWithSourceAsync(app, Path.Combine(plugins, "linked.dll")));
    }

    [Fact]
    public async Task HmrAcceptsARealPayloadInsideTheResolvedStoreRoot()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("store");
        var plugins = Path.Combine(store, "plugins");
        Directory.CreateDirectory(plugins);
        var payload = Path.Combine(plugins, "plugin.dll");
        await File.WriteAllTextAsync(payload, "in-store payload");

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, HostVersion: "1.5.0"));
        await app.StartAsync();

        // On macOS the temp root itself lives behind /var -> /private/var.
        // Both roots must be resolved the same way or this legitimate source
        // would be rejected.
        var plan = await ReloadWithSourceAsync(app, payload);
        Assert.Empty(plan.StaleEntryIds);
    }

    [Fact]
    public async Task HmrRejectsSourceBehindAWindowsReparsePoint()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Windows-only reparse-point/junction fixture; Unix symlinks are
            // exercised by the two tests above.
            return;
        }

        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var store = temp.Path("store");
        var plugins = Path.Combine(store, "plugins");
        Directory.CreateDirectory(plugins);
        var outside = temp.Path("outside");
        Directory.CreateDirectory(outside);
        var payload = Path.Combine(outside, "plugin.dll");
        await File.WriteAllTextAsync(payload, "outside payload");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(plugins, "external"), outside);
        }
        catch (UnauthorizedAccessException)
        {
            // Creating reparse points requires developer mode or elevation.
            // Skipped conditionally; the resolver uses FileSystemInfo.LinkTarget
            // (which covers Windows junctions) for the same containment check.
            return;
        }

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, HostVersion: "1.5.0"));
        await app.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReloadWithSourceAsync(app, Path.Combine(plugins, "external", "plugin.dll")));
    }

    private static Task<HmrPlan> ReloadWithSourceAsync(PlatformApplication app, string source)
    {
        var request = new HmrRequest(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            []);
        return app.HmrAsync(request, new JsonObject { ["source"] = source });
    }

    [Fact]
    public async Task EntryOnlyPluginIsPreparedByHostAndVisibleToControlPlane()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        await WritePackageAsync(temp, store, "entry-provider", "1.0.0", "entry-provider", []);
        await WritePackageAsync(temp, store, "entry-only", "1.0.0", "entry-only",
        [
            new PluginDependency("entry-provider", "[1.0,)"),
        ],
        capabilities: ["provide|entry/capability|endpoint|1.0.0|application/json|"],
        schema: JsonNode.Parse("""{"type":"object","properties":{"value":{"type":"string"}}}"""));

        var manifestPath = temp.File("entry-only.app.json");
        await new ApplicationManifest("entry-app", "1.0.0", "1.5.0", [],
        [
            new EntryNode("plugins", Children:
            [
                new EntryNode("entry-provider", Url: "entry-provider@1.0.0"),
                new EntryNode("entry-only", Url: "entry-only@1.0.0",
                    Config: new Dictionary<string, object?> { ["value"] = "from-entry" }),
            ]),
        ]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: "1.5.0"));
        await app.StartAsync();

        // The host's pre-scan uses the SAME effective declarations as the loader:
        // an entries-only plugin is resolved, activated and administrable.
        Assert.Contains(app.ResolvedPlugins,
            item => item.Id == "entry-only" && item.Enabled && item.Version == "1.0.0");
        Assert.True(app.Registry.Entries.ContainsKey("entry-only"));
        Assert.Equal(FiberState.Active, app.Registry.Entries["entry-only"].Fiber!.State);
        var controlEntries = await app.Control!.ListAsync();
        Assert.Contains(controlEntries, item => item.Id == "entry-only");
        var controlEntry = controlEntries.First(item => item.Id == "entry-only");
        Assert.True(controlEntry.Declared);
        Assert.True(controlEntry.Enabled);

        // capabilityWire, schema registration and declaredIds all come from the
        // same plan — not from a separate manifest.Plugins walk.
        Assert.Contains(app.Capabilities.Declarations,
            declaration => declaration.Id.ToString() == "entry/capability");
        var schemas = app.Console.Api.Query("config/schema")?["components"]!.AsArray();
        Assert.NotNull(schemas);
        Assert.Contains(schemas, item => item?["type"]?.GetValue<string>() == "entry-only");
        var diagnostics = app.Console.Api.Query("devtools/diagnostics")!["diagnostics"]!.AsArray();
        Assert.DoesNotContain(diagnostics, finding =>
            finding?["code"]?.GetValue<string>() == "PACKAGE_DEPENDENCY_NOT_DECLARED" &&
            finding?["message"]?.GetValue<string>()?.Contains("entry-provider") == true);
    }

    [Fact]
    public async Task ControlPlaneCannotBeInstalledDisabledUpgradedUninstalledOrReconfigured()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.WinForms, HostVersion: "1.5.0"));
        await app.StartAsync();

        const string controlId = "plugin-control-plane";
        // Protection is established by loading the control plane fiber, not by
        // accidentally finding its id in the application manifest.
        Assert.Contains(controlId, app.Registry.ProtectedIds);

        var source = temp.Path("control-plane-source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "payload.txt"), "payload");
        var package = temp.File("control-plane-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            controlId, "1.0.0", controlId, "[1.0,)"), package);

        var manifestBefore = await File.ReadAllTextAsync(manifestPath);
        await using (var stream = File.OpenRead(package))
        {
            var upload = await app.Control!.UploadAsync(stream, "control-plane-1.0.0.gocordis-plugin");
            var install = await app.Control.InstallAsync(upload.UploadKey);
            Assert.False(install.Accepted);
            Assert.Equal("forbidden", install.Code);
        }

        await using (var stream = File.OpenRead(package))
        {
            var upload = await app.Control!.UploadAsync(stream, "control-plane-1.0.0.gocordis-plugin");
            var upgrade = await app.Control.UpgradeAsync(controlId, upload.UploadKey);
            Assert.False(upgrade.Accepted);
            Assert.Equal("forbidden", upgrade.Code);
        }

        var disabled = await app.Control!.SetEnabledAsync(controlId, false);
        Assert.False(disabled.Accepted);
        Assert.Equal("forbidden", disabled.Code);
        var removed = await app.Control.UninstallAsync(controlId);
        Assert.False(removed.Accepted);
        Assert.Equal("forbidden", removed.Code);
        var reconfigured = await app.Control.SetConfigAsync(controlId,
            new Dictionary<string, object?> { ["value"] = "blocked" });
        Assert.False(reconfigured.Accepted);
        Assert.Equal("forbidden", reconfigured.Code);

        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(manifestPath));
        Assert.Empty(app.Registry.Entries);
        Assert.NotNull(app.Runtime.RequireCapability(ControlPlaneKeys.Service));
    }

    private static async Task WritePackageAsync(
        TempDir temp, string store, string id, string version, string type,
        IReadOnlyList<PluginDependency> dependencies,
        IReadOnlyList<string>? capabilities = null,
        JsonNode? schema = null)
    {
        var source = temp.Path($"source-{id}-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "payload.txt"), id);
        var package = temp.File($"{id}-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            id, version, type, "[1.0,)", Dependencies: dependencies,
            ConfigSchema: schema, Capabilities: capabilities), package);
        await new PluginStore(store, "1.5.0").InstallAsync(package);
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
