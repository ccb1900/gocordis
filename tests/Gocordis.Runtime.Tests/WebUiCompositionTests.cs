using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch A transport checks: the typed UI composition surfaces are exposed
/// over real HTTP, client modules are served from a guarded filesystem mount,
/// and the SSE stream announces composition changes without dropping the
/// pre-existing observation frames.
/// </summary>
public sealed class WebUiCompositionTests
{
    [Fact]
    public async Task PagesEndpointReturnsTypedUIPageContract()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        host.Ui.RegisterPage(ContributionOwner.For("test", "pages"), new UiPageDefinition(
            "overview", "Overview", "/overview", "dashboard",
            Icon: "dashboard",
            Description: "The overview page",
            Views: new JsonObject { ["kind"] = "blocks" },
            Actions: [new UiPageAction("Refresh", "test/refresh", DatePicker: true)]));

        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = NewClient(server);

        var data = JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!;
        var page = Assert.Single(data["pages"]!.AsArray())!;
        Assert.Equal("overview", page["id"]!.GetValue<string>());
        Assert.Equal("Overview", page["title"]!.GetValue<string>());
        Assert.Equal("/overview", page["route"]!.GetValue<string>());
        Assert.Equal("dashboard", page["renderer"]!.GetValue<string>());
        Assert.Equal("dashboard", page["icon"]!.GetValue<string>());
        Assert.Equal("The overview page", page["description"]!.GetValue<string>());
        Assert.Equal("blocks", page["views"]!["kind"]!.GetValue<string>());

        var action = Assert.Single(page["actions"]!.AsArray())!;
        Assert.Equal("Refresh", action["label"]!.GetValue<string>());
        Assert.Equal("test/refresh", action["command"]!.GetValue<string>());
        Assert.True(action["datePicker"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PanelsEndpointReturnsTypedUIPanelContract()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        host.Ui.RegisterPanel(ContributionOwner.For("test", "panels"), new UiPanelDefinition(
            "feed", "Events", "right", "event-feed",
            Pages: ["overview"],
            Views: new JsonArray(new JsonObject { ["kind"] = "list" })));

        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = NewClient(server);

        var data = JsonNode.Parse(await client.GetStringAsync("/api/ui/panels"))!["data"]!;
        var panel = Assert.Single(data["panels"]!.AsArray())!;
        Assert.Equal("feed", panel["id"]!.GetValue<string>());
        Assert.Equal("Events", panel["title"]!.GetValue<string>());
        Assert.Equal("right", panel["position"]!.GetValue<string>());
        Assert.Equal("event-feed", panel["renderer"]!.GetValue<string>());
        Assert.Equal("overview", Assert.Single(panel["pages"]!.AsArray())!.GetValue<string>());
        Assert.Equal("list", panel["views"]![0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClientModulesManifestReturnsVersionedUrlsAndRevision()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        host.Ui.RegisterClientModule(ContributionOwner.For("test", "modules"),
            new UiClientModuleDefinition("demo-ui", "/client-modules/demo-ui/ui.js", "v1", "ui.js"));

        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = NewClient(server);

        var data = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!;
        Assert.Equal(host.Ui.Revision, data["revision"]!.GetValue<long>());
        var module = Assert.Single(data["modules"]!.AsArray())!;
        Assert.Equal("demo-ui", module["name"]!.GetValue<string>());
        Assert.Matches("^/client-modules/demo-ui/ui\\.js\\?v=[0-9a-f]{8}$",
            module["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClientModuleStaticAssetsAreServedFromRegisteredModule()
    {
        var root = CreateModuleRoot(
            ("ui.js", "export default function register() { return () => {}; }"),
            ("lib/helper.js", "export const helper = 1;"));
        try
        {
            await using var runtime = new Gocordis.Core.Runtime();
            var host = new ConsoleHost(runtime);
            host.Ui.RegisterClientModule(ContributionOwner.For("test", "static"),
                new UiClientModuleDefinition("demo-ui", "/client-modules/demo-ui/ui.js", "v1", "ui.js"));

            await using var server = new WebUiServer(host, clientModuleRootResolver: _ => root);
            await server.StartAsync();
            using var client = NewClient(server);

            var data = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!;
            var url = Assert.Single(data["modules"]!.AsArray())!["url"]!.GetValue<string>();

            using var entry = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
            Assert.Equal("text/javascript", entry.Content.Headers.ContentType?.MediaType);
            Assert.Contains("register", await entry.Content.ReadAsStringAsync());

            using var nested = await client.GetAsync("/client-modules/demo-ui/lib/helper.js");
            Assert.Equal(HttpStatusCode.OK, nested.StatusCode);
            Assert.Contains("helper = 1", await nested.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClientModuleStaticAssetsRejectTraversalDotfilesAndSecrets()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"gocordis-webui-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "module");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ui.js"), "export default function register() {}");
            await File.WriteAllTextAsync(Path.Combine(parent, "secret.txt"), "outside the mount");
            await File.WriteAllTextAsync(Path.Combine(root, ".env"), "TOKEN=1");
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.toml"), "[component]");
            await File.WriteAllTextAsync(Path.Combine(root, "secrets.json"), "{}");

            var symlinkAvailable = true;
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "escape.js"), Path.Combine(parent, "secret.txt"));
            }
            catch (IOException)
            {
                symlinkAvailable = false;
            }
            catch (UnauthorizedAccessException)
            {
                symlinkAvailable = false;
            }

            await using var runtime = new Gocordis.Core.Runtime();
            var host = new ConsoleHost(runtime);
            host.Ui.RegisterClientModule(ContributionOwner.For("test", "guard"),
                new UiClientModuleDefinition("demo-ui", "/client-modules/demo-ui/ui.js", "v1", "ui.js"));

            await using var server = new WebUiServer(host, clientModuleRootResolver: _ => root);
            await server.StartAsync();
            using var client = NewClient(server);

            var blocked = new List<string>
            {
                "/client-modules/demo-ui/%2e%2e/secret.txt",
                "/client-modules/demo-ui/%252e%252e/secret.txt",
                "/client-modules/demo-ui/lib/%252e%252e/%252e%252e/secret.txt",
                "/client-modules/demo-ui/.env",
                "/client-modules/demo-ui/manifest.toml",
                "/client-modules/demo-ui/secrets.json",
                "/client-modules/demo-ui/missing.js",
                "/client-modules/unknown/ui.js",
            };
            if (symlinkAvailable)
            {
                blocked.Add("/client-modules/demo-ui/escape.js");
            }

            foreach (var path in blocked)
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using var allowed = await client.GetAsync("/client-modules/demo-ui/ui.js");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task ClientModuleUrlChangesWhenRegistryRevisionChanges()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        var owner = ContributionOwner.For("test", "revision");
        host.Ui.RegisterClientModule(owner,
            new UiClientModuleDefinition("demo-ui", "/client-modules/demo-ui/ui.js", "v1", "ui.js"));

        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = NewClient(server);

        var first = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!;
        var firstUrl = Assert.Single(first["modules"]!.AsArray())!["url"]!.GetValue<string>();

        host.Ui.RegisterPage(owner, new UiPageDefinition("later", "Later", "/later", "generic"));

        var second = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!;
        var secondUrl = Assert.Single(second["modules"]!.AsArray())!["url"]!.GetValue<string>();

        Assert.NotEqual(firstUrl, secondUrl);
        Assert.True(second["revision"]!.GetValue<long>() > first["revision"]!.GetValue<long>());
    }

    [Fact]
    public async Task StreamEmitsUiManifestFrameWhenRevisionChanges()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var server = new WebUiServer(host);
        await server.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = NewClient(server);
        using var response = await client.GetAsync("/api/stream",
            HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal(": connected", await ReadLineAsync(reader, cts.Token));

        // Wait for the first observation so the subscription loop is known live
        // before the composition mutation happens.
        Assert.Equal("observation", (await ReadFrameAsync(reader, cts.Token)).Event);

        host.Ui.RegisterPage(ContributionOwner.For("test", "sse"),
            new UiPageDefinition("live", "Live", "/live", "generic"));

        for (var i = 0; i < 20; i++)
        {
            var frame = await ReadFrameAsync(reader, cts.Token);
            if (frame.Event != "ui-manifest")
            {
                continue;
            }

            var revision = JsonNode.Parse(frame.Data)!["revision"]!.GetValue<long>();
            Assert.Equal(host.Ui.Revision, revision);
            return;
        }

        Assert.Fail("no ui-manifest frame arrived after the composition revision changed");
    }

    [Fact]
    public async Task DisabledPluginContributionsDisappearFromUiEndpoints()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var root = CreateModuleRoot(("ui.js", "export default function register() {}"));
        try
        {
            await using var server = new WebUiServer(host, clientModuleRootResolver: _ => root);
            await server.StartAsync();
            using var client = NewClient(server);

            var plugin = await runtime.LoadAsync(new UiFeature());

            var pages = JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray();
            Assert.Contains(pages, page => page!["id"]!.GetValue<string>() == "feature");
            var panels = JsonNode.Parse(await client.GetStringAsync("/api/ui/panels"))!["data"]!["panels"]!.AsArray();
            Assert.Contains(panels, panel => panel!["id"]!.GetValue<string>() == "feature-panel");
            var modules = JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray();
            Assert.Contains(modules, module => module!["name"]!.GetValue<string>() == "feature-ui");

            await runtime.DisposeAsync(plugin);

            Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray());
            Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/panels"))!["data"]!["panels"]!.AsArray());
            Assert.Empty(JsonNode.Parse(await client.GetStringAsync("/api/ui/client-modules"))!["data"]!["modules"]!.AsArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedBootstrapExposesDashboardGraphSnapshotAndEvents()
    {
        // A fresh checkout has no React dist; the embedded fallback must still
        // render the whole control surface, not only the plugin list.
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = NewClient(server);

        var script = await client.GetStringAsync("/app.js");
        Assert.Contains("/api/ui/pages", script, StringComparison.Ordinal);
        Assert.Contains("framework/dashboard", script, StringComparison.Ordinal);
        Assert.Contains("insight/graph", script, StringComparison.Ordinal);
        Assert.Contains("runtime/snapshot", script, StringComparison.Ordinal);
        Assert.Contains("runtime/events", script, StringComparison.Ordinal);

        var style = await client.GetStringAsync("/app.css");
        Assert.Contains(".graph", style, StringComparison.Ordinal);
    }

    private static HttpClient NewClient(WebUiServer server) =>
        new() { BaseAddress = new Uri(server.Address!) };

    private static string CreateModuleRoot(params (string Relative, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), $"gocordis-webui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return root;
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken token)
    {
        var line = reader.ReadLineAsync(token).AsTask();
        var finished = await Task.WhenAny(line, Task.Delay(Timeout.Infinite, token));
        Assert.Same(line, finished);
        return await line;
    }

    private static async Task<SseFrame> ReadFrameAsync(StreamReader reader, CancellationToken token)
    {
        string? name = null;
        string? data = null;
        for (var i = 0; i < 16; i++)
        {
            var line = await ReadLineAsync(reader, token);
            if (line is null)
            {
                throw new EndOfStreamException("SSE stream closed before a frame completed");
            }
            if (line.Length == 0)
            {
                return new SseFrame(name ?? string.Empty, data ?? string.Empty);
            }
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                name = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data = line[5..].Trim();
            }
        }

        throw new InvalidOperationException("unterminated SSE frame");
    }

    private sealed record SseFrame(string Event, string Data);

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

    private sealed class UiFeature : Component
    {
        public override string Name => "feature";

        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key, ApiGateway.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            var api = context.Require(ApiGateway.Key);
            var owner = ContributionOwner.ForContext(context, "ui-plugin", Name);

            await context.RegisterUiPageAsync(ui, owner,
                new UiPageDefinition(Name, "Feature", "/feature", "generic"));
            await context.RegisterUiPanelAsync(ui, owner,
                new UiPanelDefinition("feature-panel", "Feature Panel", "right", "event-feed"));
            await context.RegisterUiClientModuleAsync(ui, owner,
                new UiClientModuleDefinition("feature-ui", "/client-modules/feature-ui/ui.js", "v1", "ui.js"));
            await context.RegisterApiQueryAsync(api, owner, "feature/hello",
                () => new JsonObject { ["message"] = "hello" });
            return null;
        }
    }
}
