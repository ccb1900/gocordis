using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Transport-level write authorization and declaration-authority checks for
/// the console adapter. Remote writes need an explicit policy; a registry with
/// no control service is read-only, because changing a fiber without its
/// declaration would break the single declaration authority.
/// </summary>
public sealed class WebUiPolicyTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task AllowRemoteWriteEndpointsRequireAccessPolicy()
    {
        await using var fx = await PolicyFixture.CreateAsync();
        var disable = new JsonObject { ["pluginId"] = "demo", ["enable"] = false };

        // Remote listener with no policy: every mutation is forbidden, and so
        // is every read/observation of host state. A read-enabled viewer below
        // verifies nothing was changed behind the 403.
        await using var locked = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control);
        await locked.StartAsync();
        using var lockedClient = NewClient(locked);

        using var denied = await PostJsonAsync(lockedClient, "/api/plugins/control", disable);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("forbidden",
            JsonNode.Parse(await denied.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        using var read = await lockedClient.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        await locked.StopAsync();

        await using var viewer = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control,
            accessPolicy: scope => scope is ConsoleAccessScope.Read);
        await viewer.StartAsync();
        using var viewerClient = NewClient(viewer);
        Assert.True((await PluginAsync(viewerClient, "demo"))["enabled"]!.GetValue<bool>());
        await viewer.StopAsync();

        // An explicit policy opts into writes and is consulted per scope.
        await using var allowed = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control,
            accessPolicy: _ => true);
        await allowed.StartAsync();
        using var allowedClient = NewClient(allowed);

        using var accepted = await PostJsonAsync(allowedClient, "/api/plugins/control", disable);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = JsonNode.Parse(await accepted.Content.ReadAsStringAsync())!["data"]!;
        Assert.True(result["accepted"]!.GetValue<bool>());
        Assert.False((await PluginAsync(allowedClient, "demo"))["enabled"]!.GetValue<bool>());
        await allowed.StopAsync();
    }

    /// <summary>Remote state reads and the SSE observation stream are a
    /// separate authority from query/command. Loopback keeps its open default;
    /// an explicit policy remains authoritative on either listener.</summary>
    [Fact]
    public async Task RemoteReadAndObserveEndpointsRequireAccessPolicy()
    {
        await using var fx = await PolicyFixture.CreateAsync();
        string[] readRoutes =
        [
            "/api/plugins", "/api/plugins/store", "/api/plugins/demo/config",
            "/api/plugins/demo/dependencies", "/api/stream",
        ];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Remote without a policy: all five routes are forbidden before any
        // state (or SSE header/body) is emitted.
        await using var locked = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control);
        await locked.StartAsync();
        using var lockedClient = NewClient(locked);
        foreach (var path in readRoutes)
        {
            using var denied = await GetWithHostAsync(lockedClient, path, null, cts.Token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var body = await denied.Content.ReadAsStringAsync();
            Assert.Equal("forbidden", JsonNode.Parse(body)!["code"]!.GetValue<string>());
            Assert.DoesNotContain("text/event-stream", denied.Content.Headers.ContentType?.MediaType ?? "");
            Assert.DoesNotContain(": connected", body);
        }
        await locked.StopAsync();

        // Loopback default is unchanged: same routes, no policy, still open.
        await using var loopback = new WebUiServer(fx.Host, registry: fx.Registry, control: fx.Control);
        await loopback.StartAsync();
        using var loopbackClient = NewClient(loopback);
        foreach (var path in readRoutes)
        {
            using var ok = await GetWithHostAsync(loopbackClient, path, null, cts.Token);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        await loopback.StopAsync();

        // A supplied policy is consulted per scope; Read covers the four
        // state routes and Observe is a distinct scope for the stream.
        var seen = new List<ConsoleAccessScope>();
        await using var scoped = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control,
            accessPolicy: scope =>
            {
                lock (seen) seen.Add(scope);
                return scope is ConsoleAccessScope.Query or ConsoleAccessScope.Command
                    or ConsoleAccessScope.Lifecycle or ConsoleAccessScope.Config
                    or ConsoleAccessScope.Package;
            });
        await scoped.StartAsync();
        using var scopedClient = NewClient(scoped);
        foreach (var path in readRoutes)
        {
            using var denied = await GetWithHostAsync(scopedClient, path, null, cts.Token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        lock (seen)
        {
            Assert.Contains(ConsoleAccessScope.Read, seen);
            Assert.Contains(ConsoleAccessScope.Observe, seen);
        }
        await scoped.StopAsync();
    }

    [Fact]
    public async Task QueryAndWriteScopesKeepTheirExistingPolicySemantics()
    {
        await using var fx = await PolicyFixture.CreateAsync();

        // Query stays open in remote-without-policy mode (404 for an unknown
        // query, never 403); lifecycle writes still need a policy.
        await using var locked = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control);
        await locked.StartAsync();
        using var lockedClient = NewClient(locked);
        using var query = await lockedClient.GetAsync("/api/query/no-such-query");
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);

        // A policy that rejects only the new scopes leaves every legacy
        // scope usable, including the write path.
        await using var legacy = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control,
            accessPolicy: scope => scope is not (ConsoleAccessScope.Read or ConsoleAccessScope.Observe));
        await legacy.StartAsync();
        using var legacyClient = NewClient(legacy);
        using var accepted = await PostJsonAsync(legacyClient, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "demo", ["enable"] = false });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var read = await legacyClient.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task SensitiveConfigAndInstallPathAreRedactedOnReads()
    {
        await using var fx = await PolicyFixture.CreateAsync(new Dictionary<string, object?>
        {
            ["value"] = "visible",
            ["apiKey"] = "super-secret-key",
            ["dbPassword"] = "hunter2",
            ["token"] = "tok-xyz",
            ["credential"] = "cred-xyz",
            ["connectionString"] = "Server=db;Password=hunter2;",
            ["auth"] = new Dictionary<string, object?>
            {
                ["password"] = "nested-secret",
                ["realm"] = "local",
            },
        });
        await using var server = new WebUiServer(fx.Host, registry: fx.Registry, control: fx.Control);
        await server.StartAsync();
        using var client = NewClient(server);

        var listJson = await client.GetStringAsync("/api/plugins");
        var list = JsonNode.Parse(listJson)!["data"]!.AsArray();
        AssertMaskedConfig(list.OfType<JsonObject>()
            .Single(item => item["id"]!.GetValue<string>() == "demo")["config"]!.AsObject(), listJson);

        var configJson = await client.GetStringAsync("/api/plugins/demo/config");
        AssertMaskedConfig(JsonNode.Parse(configJson)!["data"]!.AsObject(), configJson);

        // A registry-only config read goes through a different code path.
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("demo", _ => new DemoComponent());
        await registry.ApplyAsync([
            new PluginManifest("p1", "demo", true,
                new Dictionary<string, object?> { ["apiKey"] = "registry-secret", ["value"] = "1" }, [], []),
        ]);
        await using var registryServer = new WebUiServer(host, registry: registry);
        await registryServer.StartAsync();
        using var registryClient = NewClient(registryServer);
        var registryJson = await registryClient.GetStringAsync("/api/plugins/p1/config");
        var registryConfig = JsonNode.Parse(registryJson)!["data"]!.AsObject();
        Assert.DoesNotContain("registry-secret", registryJson);
        Assert.Equal("***", registryConfig["apiKey"]!.GetValue<string>());
        Assert.Equal("1", registryConfig["value"]!.GetValue<string>());

        // The store DTO exposes only the package file name, never its directory.
        var storeJson = await client.GetStringAsync("/api/plugins/store");
        var displayed = JsonNode.Parse(storeJson)!["data"]!.AsArray()
            .OfType<JsonObject>().Single()["installPath"]!.GetValue<string>();
        var fullPath = (await fx.Control.StoreAsync())[0].InstallPath;
        Assert.Equal(Path.GetFileName(fullPath), displayed);
        Assert.NotEqual(fullPath, displayed);
        Assert.DoesNotContain(Path.GetDirectoryName(fullPath)!, storeJson);
    }

    private static void AssertMaskedConfig(JsonObject config, string rawResponse)
    {
        Assert.DoesNotContain("super-secret-key", rawResponse);
        Assert.DoesNotContain("hunter2", rawResponse);
        Assert.DoesNotContain("tok-xyz", rawResponse);
        Assert.DoesNotContain("cred-xyz", rawResponse);
        Assert.DoesNotContain("nested-secret", rawResponse);
        Assert.Equal("visible", config["value"]!.GetValue<string>());
        Assert.Equal("***", config["apiKey"]!.GetValue<string>());
        Assert.Equal("***", config["dbPassword"]!.GetValue<string>());
        Assert.Equal("***", config["token"]!.GetValue<string>());
        Assert.Equal("***", config["credential"]!.GetValue<string>());
        Assert.Equal("***", config["connectionString"]!.GetValue<string>());
        Assert.Equal("local", config["auth"]!["realm"]!.GetValue<string>());
        Assert.Equal("***", config["auth"]!["password"]!.GetValue<string>());
    }

    [Fact]
    public async Task RegistryOnlyWebUiMutationIsRejected()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("demo", _ => new DemoComponent());
        await registry.ApplyAsync([
            new PluginManifest("p1", "demo", true,
                new Dictionary<string, object?> { ["value"] = "1" }, [], []),
        ]);

        await using var server = new WebUiServer(host, registry: registry);
        await server.StartAsync();
        using var client = NewClient(server);

        using var control = await PostJsonAsync(client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "p1", ["enable"] = false });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, control.StatusCode);
        Assert.Equal("unavailable",
            JsonNode.Parse(await control.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());

        using var config = await PostJsonAsync(client, "/api/plugins/p1/config",
            new JsonObject { ["config"] = new JsonObject { ["value"] = "2" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, config.StatusCode);

        // Both failures must leave the declaration and the fiber untouched.
        Assert.True((await PluginAsync(client, "p1"))["enabled"]!.GetValue<bool>());
        Assert.Equal("Active", (await PluginAsync(client, "p1"))["state"]!.GetValue<string>());
        var configRead = JsonNode.Parse(await client.GetStringAsync("/api/plugins/p1/config"))!["data"]!;
        Assert.Equal("1", configRead["value"]!.GetValue<string>());
    }

    /// <summary>
    /// Inbound Host ACL is the existing extensions-level guard, wired in as an
    /// opt-in middleware rather than a second ACL inside WebUiServer. A denied
    /// host sees the 404 of a route that was never registered; allow-listed
    /// hosts and loopback keep their normal behavior.
    /// </summary>
    [Fact]
    public async Task HostAclDeniesNonAllowlistedHostAs404AndKeepsLoopback()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);

        await using var guard = new ServerRouteGuard();
        var allowConsole = HostAcl.Policy(["console.example.com"], []);
        guard.Add(allowConsole, "GET /api/meta");
        guard.Add(allowConsole, "GET /api/stream");

        await using var server = new WebUiServer(host, routeGuard: guard);
        await server.StartAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = NewClient(server);

        // Two distinct route keys: a plain API route and the SSE stream route.
        foreach (var path in new[] { "/api/meta", "/api/stream" })
        {
            using var denied = await GetWithHostAsync(client, path, "evil.example", cts.Token);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            // A port on the allow-listed host is stripped before matching.
            using var listed = await GetWithHostAsync(client, path, "console.example.com:8443", cts.Token);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

            using var loopback = await GetWithHostAsync(client, path, null, cts.Token);
            Assert.Equal(HttpStatusCode.OK, loopback.StatusCode);
        }

        // Guard registrations stay route-key scoped: an unregistered key is
        // untouched, so the ACL never silently becomes a blanket policy.
        using var unguarded = await GetWithHostAsync(client, "/api/fleet", "evil.example", cts.Token);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);

        // Opt-in: with no guard supplied the existing transport behavior stands,
        // even for the host the policy above rejects.
        await using var plain = new WebUiServer(host);
        await plain.StartAsync();
        using var plainClient = NewClient(plain);
        using var withoutGuard = await GetWithHostAsync(plainClient, "/api/meta", "evil.example", cts.Token);
        Assert.Equal(HttpStatusCode.OK, withoutGuard.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetWithHostAsync(
        HttpClient client, string path, string? host, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (host is not null)
        {
            request.Headers.Host = host;
        }
        // The stream route never completes its body; headers are all we assert.
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    // Loopback tests must not inherit an ambient proxy: a spoofed Host header
    // would otherwise be routed to the proxy and never reach Kestrel.
    private static HttpClient NewClient(WebUiServer server) =>
        new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(server.Address!) };

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, JsonNode body) =>
        await client.PostAsync(path, new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

    private static async Task<JsonObject> PluginAsync(HttpClient client, string id)
    {
        var plugins = JsonNode.Parse(await client.GetStringAsync("/api/plugins"))!["data"]!.AsArray();
        return plugins.OfType<JsonObject>().Single(item => item["id"]!.GetValue<string>() == id);
    }

    private sealed class DemoComponent : Component
    {
        public override string Name => "demo";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    /// <summary>A real control service over a temporary store and manifest, so
     /// an allow-policy write can be observed as a persisted declaration.</summary>
    private sealed class PolicyFixture : IAsyncDisposable
    {
        private readonly string _root;

        private PolicyFixture(string root, Gocordis.Core.Runtime runtime, ConsoleHost host, PluginRegistry registry,
            PluginControlService control)
        {
            _root = root;
            Runtime = runtime;
            Host = host;
            Registry = registry;
            Control = control;
        }

        public Gocordis.Core.Runtime Runtime { get; }
        public ConsoleHost Host { get; }
        public PluginRegistry Registry { get; }
        public PluginControlService Control { get; }

        public static async Task<PolicyFixture> CreateAsync(IReadOnlyDictionary<string, object?>? config = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"gocordis-policy-{Guid.NewGuid():N}");
            try
            {
                var runtime = new Gocordis.Core.Runtime();
                try
                {
                    var host = new ConsoleHost(runtime);
                    var registry = new PluginRegistry(runtime);
                    registry.RegisterFactory("demo", _ => new DemoComponent());

                    var source = Path.Combine(root, "source");
                    Directory.CreateDirectory(source);
                    await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "demo");
                    var package = Path.Combine(root, "demo-1.0.0.gocordis-plugin");
                    await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
                        Id: "demo",
                        Version: "1.0.0",
                        Type: "demo",
                        HostVersionRange: "[1.0,2.0)",
                        DisplayName: "demo",
                        Dependencies: [],
                        Provides: []), package);

                    var store = new PluginStore(Path.Combine(root, "store"), HostVersion);
                    await store.InstallAsync(package);
                    var manifest = Path.Combine(root, "app.json");
                    await new ApplicationManifest("policy-tests", "1.0.0", HostVersion,
                        [new ApplicationPlugin("demo", "[1.0,2.0)", true,
                            config ?? new Dictionary<string, object?> { ["value"] = "1" })]).SaveAsync(manifest);

                    var control = new PluginControlService(manifest, store, registry);
                    await control.ApplyAllAsync();
                    return new PolicyFixture(root, runtime, host, registry, control);
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

        public async ValueTask DisposeAsync()
        {
            await Registry.DisposeAsync();
            await Runtime.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
