using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Koishi.ConsoleApp;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Paper §5.3 on the real product path: four packaged plugins compose a
/// Koishi-style server application; the web console is a SECOND application
/// with its own Runtime and UI contributions, talking over HTTP only.
/// </summary>
public sealed class KoishiPackagedProductTests
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task PackagedKoishiCompositionWithdrawsReactivatesAndServesIndependentConsole()
    {
        using var temp = new TempDir();
        await Stage(RunScriptAsync(temp.Root), "build/install packages");

        await using var app = new PlatformApplication(new PlatformHostOptions(
            temp.File("gocordis.app.json"), temp.Path("plugins"),
            Address: "http://127.0.0.1:0", Host: UiHostKind.Web, HostVersion: "1.0.0"));
        await Stage(app.StartAsync(), "start platform");

        Assert.Equal(4, app.Registry.Entries.Count);
        Assert.All(app.Registry.Entries.Values, entry => Assert.Equal(FiberState.Active, entry.Fiber!.State));
        // The query invokes the registered command, which calls the adapter,
        // which calls the database — a real typed capability chain across the
        // four separately packaged plugins, not a hardcoded "ready" literal.
        Assert.Equal("row:demo:ping", app.Console.Api.Query("koishi/echo")?["value"]?.GetValue<string>());

        // The database provider can be replaced independently; adapter and
        // feature withdraw and then reactivate without an error or restart.
        var disabled = await Stage(app.Control!.SetEnabledAsync("koishi.database", false), "disable database");
        Assert.True(disabled.Accepted, disabled.Error);
        Assert.Equal(FiberState.Pending, app.Registry.Entries["koishi.adapter"].Fiber!.State);
        Assert.Equal(FiberState.Pending, app.Registry.Entries["koishi.feature"].Fiber!.State);
        Assert.Equal(FiberState.Active, app.Registry.Entries["koishi.vocabulary"].Fiber!.State);
        Assert.Null(app.Console.Api.Query("koishi/echo"));

        var enabled = await Stage(app.Control.SetEnabledAsync("koishi.database", true), "enable database");
        Assert.True(enabled.Accepted, enabled.Error);
        Assert.All(app.Registry.Entries.Values, entry => Assert.Equal(FiberState.Active, entry.Fiber!.State));
        // The query invokes the registered command, which calls the adapter,
        // which calls the database — a real typed capability chain across the
        // four separately packaged plugins, not a hardcoded "ready" literal.
        Assert.Equal("row:demo:ping", app.Console.Api.Query("koishi/echo")?["value"]?.GetValue<string>());

        // Paper §5.2.2 product chain: the three-phase HMR engine is invoked
        // through a real HTTP endpoint of the running product host.
        var featureBeforeHmr = app.Registry.Entries["koishi.feature"].Fiber!;
        using var hostHttp = new HttpClient { BaseAddress = app.Address! };
        var hmrResponse = await Stage(hostHttp.PostAsJsonAsync("/api/hmr", new
        {
            stashed = new[] { "koishi.feature" },
            externals = new[] { "Gocordis.Core", "Gocordis.Plugins" },
            imports = new Dictionary<string, string[]> { ["koishi.feature"] = [] },
            version = "2.0.0",
        }), "hmr request");
        Assert.True(hmrResponse.IsSuccessStatusCode, await hmrResponse.Content.ReadAsStringAsync());
        var hmrPayload = JsonNode.Parse(await hmrResponse.Content.ReadAsStringAsync())!["data"]!;
        Assert.Contains("koishi.feature", hmrPayload["staleEntries"]!.AsArray()
            .Select(entry => entry!.GetValue<string>()));
        Assert.Contains("koishi.feature", hmrPayload["accepted"]!.AsArray()
            .Select(entry => entry!.GetValue<string>()));
        Assert.NotSame(featureBeforeHmr, app.Registry.Entries["koishi.feature"].Fiber);
        Assert.Equal(FiberState.Active, app.Registry.Entries["koishi.feature"].Fiber!.State);
        Assert.Equal("row:demo:ping", app.Console.Api.Query("koishi/echo")?["value"]?.GetValue<string>());

        // The product endpoint must not let a caller point a plugin factory at
        // an arbitrary host path.
        var rejectedSource = await Stage(hostHttp.PostAsJsonAsync("/api/hmr", new
        {
            stashed = new[] { "koishi.feature" },
            externals = Array.Empty<string>(),
            imports = new Dictionary<string, string[]> { ["koishi.feature"] = [] },
            source = "/etc/passwd",
        }), "hmr invalid source");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rejectedSource.StatusCode);

        var inStorePayload = Directory.GetFiles(temp.Path("plugins"), "Koishi.Feature.dll",
            SearchOption.AllDirectories).First();
        var acceptedSource = await Stage(hostHttp.PostAsJsonAsync("/api/hmr", new
        {
            stashed = new[] { "koishi.feature" },
            externals = Array.Empty<string>(),
            imports = new Dictionary<string, string[]> { ["koishi.feature"] = [] },
            source = System.IO.Path.GetDirectoryName(inStorePayload),
        }), "hmr in-store source");
        Assert.True(acceptedSource.IsSuccessStatusCode, await acceptedSource.Content.ReadAsStringAsync());

        // Independent console application: its own Runtime and Registry, its
        // own UI contribution, server state only via the typed HTTP client.
        await using var consoleApp = new KoishiConsoleApp();
        await Stage(consoleApp.StartAsync(app.Address!.ToString(), "http://127.0.0.1:0"), "start console app");
        Assert.NotSame(app.Runtime, consoleApp.Runtime);
        Assert.Equal(2, consoleApp.Registry.Entries.Count);
        var serverFibers = app.Registry.Entries.Values.Select(entry => entry.Fiber).ToHashSet();
        Assert.All(consoleApp.Registry.Entries.Values,
            entry => Assert.DoesNotContain(entry.Fiber, serverFibers));
        // The second application's contribution exists in its OWN ApiGateway,
        // not in the server's one.
        Assert.NotNull(consoleApp.Console.Api.Query("koishi/server"));
        Assert.Null(app.Console.Api.Query("koishi/server"));
        using var http = new HttpClient { BaseAddress = new Uri(consoleApp.WebUi!.Address!) };
        var pages = await Stage(http.GetFromJsonAsync<JsonNode>("/api/ui/pages"), "read console pages");
        Assert.Contains("koishi-console",
            pages?["data"]?["pages"]?.AsArray().Select(page => page?["id"]?.GetValue<string>()) ?? []);
        var server = await Stage(http.GetFromJsonAsync<JsonNode>("/api/query/koishi/server"), "read console server");
        Assert.Equal("gocordis-console", server?["data"]?["server"]?.GetValue<string>());

        await Stage(app.StopAsync(), "stop platform");
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    private static async Task<T> Stage<T>(Task<T> task, string stage)
    {
        await Stage((Task)task, stage);
        return await task;
    }

    private static async Task Stage(Task task, string stage)
    {
        try
        {
            await task.WaitAsync(StageTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Koishi product stage timed out: {stage}", new InvalidOperationException("stage timeout"));
        }
    }

    private static async Task RunScriptAsync(string target)
    {
        var root = FindRepositoryRoot();
        var script = Path.Combine(root, "samples", "KoishiShowcase", "build-and-run.sh");
        var stdoutPath = System.IO.Path.Combine(target, "build.stdout.log");
        var stderrPath = System.IO.Path.Combine(target, "build.stderr.log");
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"\"$0\" \"$1\" > \"$2\" 2> \"$3\"");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(stdoutPath);
        start.ArgumentList.Add(stderrPath);
        // Long-lived MSBuild worker nodes inherit redirected pipes; use files
        // so waiting for the process never waits for an unrelated node's EOF.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["HTTP_PROXY"] = string.Empty;
        start.Environment["HTTPS_PROXY"] = string.Empty;
        start.Environment["http_proxy"] = string.Empty;
        start.Environment["https_proxy"] = string.Empty;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        await process.WaitForExitAsync(timeout.Token);
        // Only read after exit: an in-flight build can leave either file empty.
        var stdout = await File.ReadAllTextAsync(stdoutPath);
        var stderr = await File.ReadAllTextAsync(stderrPath);
        Assert.True(process.ExitCode == 0, $"Koishi build script failed: {stdout}\n{stderr}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-koishi-product-" + Guid.NewGuid().ToString("N"));
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
