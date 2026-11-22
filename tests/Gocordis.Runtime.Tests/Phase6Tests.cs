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
        Assert.Equal("2", Assert.Single(merged.Where(entry => entry.Id == "b")).Config["value"]);

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
        Assert.Equal(0, dashboard["total"]!.GetValue<int>());
        Assert.NotNull(host.Api.Query("runtime/snapshot"));
        Assert.NotNull(host.Api.Query("insight/graph"));
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
        Assert.Contains("Gocordis Console", html);
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
