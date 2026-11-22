using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
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

        // Remote listener with no policy: every mutation is forbidden, reads
        // remain available and nothing is changed behind the 403.
        await using var locked = new WebUiServer(
            fx.Host, allowRemote: true, registry: fx.Registry, control: fx.Control);
        await locked.StartAsync();
        using var lockedClient = NewClient(locked);

        using var denied = await PostJsonAsync(lockedClient, "/api/plugins/control", disable);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("forbidden",
            JsonNode.Parse(await denied.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        using var read = await lockedClient.GetAsync("/api/plugins");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True((await PluginAsync(lockedClient, "demo"))["enabled"]!.GetValue<bool>());
        await locked.StopAsync();

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

    private static HttpClient NewClient(WebUiServer server) =>
        new() { BaseAddress = new Uri(server.Address!) };

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

        public static async Task<PolicyFixture> CreateAsync()
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
                        [new ApplicationPlugin("demo", "[1.0,2.0)", true)]).SaveAsync(manifest);

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
