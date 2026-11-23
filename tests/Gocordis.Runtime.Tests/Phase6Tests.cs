using System.Net;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Runtime.Managed;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class Phase6Tests
{
    [Fact]
    public void CompositionAndPatchesKeepDeclarationsDeterministic()
    {
        var composer = new DeclarationComposer();
        var first = new[] { Manifest("a", "1"), Manifest("b", "1") };
        var second = new[] { Manifest("b", "2") };

        var merged = composer.Compose(first, second);
        Assert.Equal(2, merged.Count);
        Assert.Equal("2", Assert.Single(merged, entry => entry.Id == "b").Config["value"]);

        var patched = composer.ApplyPatches(
            merged,
            [new DeclarationPatch("remove", "a"), new DeclarationPatch("add", "c", Manifest("c", "3"))]);
        Assert.Equal(new[] { "b", "c" }, patched.Select(entry => entry.Id));
        Assert.Throws<InvalidOperationException>(() =>
            composer.ApplyPatches(merged, [new DeclarationPatch("replace", "missing", Manifest("x", "1"))]));
    }

    [Fact]
    public async Task ConfigWatcherCoalescesFileChangesIntoOneRevision()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "manifest.toml");
        await File.WriteAllTextAsync(path, Toml("a", "1"));
        var observed = new List<int>();

        try
        {
            using var watcher = new ConfigWatcher(path, manifests =>
            {
                observed.Add(manifests.Count);
                return Task.CompletedTask;
            });
            await watcher.SyncAsync();
            watcher.Start();
            await File.WriteAllTextAsync(path, Toml("a", "2"));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (watcher.Revision == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(30);
            }

            Assert.True(watcher.Revision > 0);
            Assert.Equal(1, observed.First());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StaleGenerationCannotReleaseANewerGeneration()
    {
        var tracker = new GenerationTracker();
        var first = tracker.Begin();
        var second = tracker.Begin();

        Assert.False(first.Release());
        Assert.True(second.Release());
        Assert.False(second.Release());
    }

    [Fact]
    public async Task ConsoleExposesFrameworkQueries()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);

        var dashboard = host.Api.Query("framework/dashboard")!;
        Assert.Equal(0, dashboard["counts"]!["fibers"]!.GetValue<int>());
        Assert.NotNull(host.Api.Query("runtime/snapshot"));
        Assert.NotNull(host.Api.Query("runtime/events"));
        var graph = host.Api.Query("insight/graph")!;
        Assert.NotNull(graph["declared"]);
        Assert.NotNull(graph["runtime"]);
        // config/schema must be registered (no 404) with the frontend's DTO shape.
        var schemas = host.Api.Query("config/schema")!;
        Assert.NotNull(schemas["components"]);
    }

    [Fact]
    public void ListenGuardRejectsRemoteBindingByDefault()
    {
        ListenGuard.Validate("http://127.0.0.1:5099", false);
        Assert.Throws<InvalidOperationException>(() => ListenGuard.Validate("http://0.0.0.0:5099", false));
        ListenGuard.Validate("http://0.0.0.0:5099", allowRemote: true);
    }

    [Fact]
    public async Task WebUiServesQueriesOverHttp()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new EchoComponent());
        await registry.ApplyAsync([Manifest("p1", "x")]);
        await using var server = new WebUiServer(host, registry: registry);

        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        var html = await client.GetStringAsync("/");
        Assert.Contains("<div id=\"root\">", html);
        Assert.Contains("fetch(", await client.GetStringAsync("/app.js"));
        Assert.Contains("body", await client.GetStringAsync("/app.css"));

        var meta = await client.GetStringAsync("/api/meta");
        Assert.Contains("frameworkVersion", meta);
        var snapshot = await client.GetStringAsync("/api/query/runtime/snapshot");
        Assert.Contains("fibers", snapshot);
        var plugins = await client.GetStringAsync("/api/plugins");
        Assert.Contains("p1", plugins);
        var fleet = await client.GetStringAsync("/api/fleet");
        Assert.Contains("\"plugins\":1", fleet);
        await server.StopAsync();
    }

    [Fact]
    public async Task WebUiUsesDataAndErrorEnvelopesWithBodyLimit()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        var meta = JsonNode.Parse(await client.GetStringAsync("/api/meta"))!;
        Assert.Equal("1", meta["data"]!["frameworkVersion"]!.GetValue<string>());
        Assert.NotNull(JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]);

        var unknownQuery = await client.GetAsync("/api/query/no/such/query");
        Assert.Equal(HttpStatusCode.NotFound, unknownQuery.StatusCode);
        var queryError = JsonNode.Parse(await unknownQuery.Content.ReadAsStringAsync())!;
        Assert.Equal("not_found", queryError["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(queryError["message"]!.GetValue<string>()));

        var unknownRoute = await client.GetAsync("/api/not-a-route");
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
        var routeError = JsonNode.Parse(await unknownRoute.Content.ReadAsStringAsync())!;
        Assert.Equal("not_found", routeError["code"]!.GetValue<string>());

        using var oversized = new StringContent(new string('x', (1 << 20) + 2));
        var tooLarge = await client.PostAsync("/api/command/anything", oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        var bodyError = JsonNode.Parse(await tooLarge.Content.ReadAsStringAsync())!;
        Assert.Equal("invalid_request", bodyError["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task WebUiRejectsUnpersistedLifecycleMutationsWithoutControl()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new EchoComponent());
        await registry.ApplyAsync([Manifest("p1", "1")]);
        await using var server = new WebUiServer(host, registry: registry);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        var disabled = await PostJsonAsync(client, "/api/plugins/control",
            new JsonObject { ["pluginId"] = "p1", ["enable"] = false });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        Assert.Equal("unavailable",
            JsonNode.Parse(await disabled.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());

        var changed = await PostJsonAsync(client, "/api/plugins/p1/config",
            new JsonObject { ["config"] = new JsonObject { ["value"] = "2" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, changed.StatusCode);

        // Reads remain available; no mutation happened.
        var config = JsonNode.Parse(await client.GetStringAsync("/api/plugins/p1/config"))!["data"]!;
        Assert.Equal("1", config["value"]!.GetValue<string>());
        Assert.Equal("Active", await PluginStateAsync(client, "p1"));
    }

    [Fact]
    public async Task WebUiStreamPushesObservationsUntilDisconnect()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var server = new WebUiServer(host);
        await server.StartAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };
        using var response = await client.GetAsync("/api/stream",
            HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        Assert.Equal(": connected", await ReadLineWithTimeoutAsync(reader));
        Assert.Equal("event: observation", await ReadLineWithTimeoutAsync(reader));
        var dataLine = await ReadLineWithTimeoutAsync(reader);
        Assert.StartsWith("data: ", dataLine);
        var observation = JsonNode.Parse(dataLine![6..])!;
        Assert.NotNull(observation["fibers"]);

        cts.Cancel();
        var ended = reader.ReadLineAsync();
        var finished = await Task.WhenAny(ended, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(ended, finished);
        try
        {
            await ended;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Client disconnect is the expected end of an SSE subscription.
        }
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string path, JsonNode body) =>
        await client.PostAsync(path,
            new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));

    private static async Task<string> PluginStateAsync(HttpClient client, string id)
    {
        var plugins = JsonNode.Parse(await client.GetStringAsync("/api/plugins"))!["data"]!.AsArray();
        return plugins.Single(plugin => plugin!["id"]!.GetValue<string>() == id)!["state"]!.GetValue<string>();
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader)
    {
        var read = reader.ReadLineAsync();
        var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(read, completed);
        return await read;
    }

    [Fact]
    public async Task ConfigSchemaQueryReturnsRegisteredSchemaMatchingSharedWireFixture()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var schema = new ConfigSchema
        {
            Type = "object",
            Required = ["batch"],
            Properties = new Dictionary<string, ConfigSchema>(StringComparer.Ordinal)
            {
                ["batch"] = new ConfigSchema { Type = "integer", Default = 8 },
                ["mode"] = new ConfigSchema { Type = "string", Enum = ["fast", "slow"], Default = "fast" },
                ["enabled"] = new ConfigSchema { Type = "boolean", Default = true },
            },
        };
        registry.RegisterValidatedFactory("echo", schema, _ => new EchoComponent());

        var host = new ConsoleHost(runtime);
        host.AttachRegistry(registry);
        var result = host.Api.Query("config/schema")!;

        // The wire payload is a shared fixture also consumed by the frontend
        // schema-form test; deep-equality locks the backend→frontend contract.
        var fixture = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "config-schema-wire.json")))!;
        Assert.True(JsonNode.DeepEquals(fixture, result),
            $"config/schema drift: got {result.ToJsonString()}, want {fixture.ToJsonString()}");
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gocordis.sln"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private sealed class EchoComponent : Component
    {
        private readonly Key<int> _key = new($"echo-{Guid.NewGuid():N}");

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, 1);
            return null;
        }
    }

    private static PluginManifest Manifest(string id, string value) =>
        new(id, "echo", true, new Dictionary<string, object?> { ["value"] = value }, [], []);

    private static string Toml(string id, string value) =>
        $$"""
        [[components]]
        id = "{{id}}"
        type = "echo"
        enabled = true
        [components.config]
        value = "{{value}}"
        """;
}
