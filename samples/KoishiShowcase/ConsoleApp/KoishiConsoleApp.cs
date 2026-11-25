using System.Text.Json.Nodes;
using Gocordis.Client;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.Plugins;

namespace Koishi.ConsoleApp;

/// <summary>
/// Paper §5.3: the web console is a SECOND, independent Cordis application.
/// It owns its Runtime, Registry and UI contributions; the server Runtime is
/// reached only through the typed HTTP client, never by sharing fibers.
/// </summary>
public sealed class KoishiConsoleApp : IAsyncDisposable
{
    public Runtime Runtime { get; } = new();
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public WebUiServer? WebUi { get; private set; }
    private bool _disposed;
    private readonly SemaphoreSlim _disposeGate = new(1, 1);

    public KoishiConsoleApp()
    {
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        Console.AttachRegistry(Registry);
    }

    public async Task StartAsync(string serverAddress, string listenAddress)
    {
        Registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        Registry.ExternalCapabilities.Add(ApiGateway.Key);
        // The second application composes through its OWN registry, so its
        // fibers and declaration table are independent of the server host.
        Registry.RegisterComponentFactory(
            "console.capabilities", _ => new ConsoleCapabilitiesComponent(Console));
        Registry.RegisterComponentFactory(
            "koishi.console.bridge", _ => new ServerBridgeComponent(serverAddress));
        await Registry.ApplyAsync([
            Manifest("console.capabilities"),
            Manifest("koishi.console.bridge"),
        ]);

        WebUi = new WebUiServer(Console, listenAddress, registry: Registry);
        await WebUi.StartAsync();
    }

    private static PluginManifest Manifest(string id) => new(
        id, id, true, new Dictionary<string, object?>(), [], []);

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            var errors = new List<Exception>();
            await AttemptAsync(errors, "webui stop", async () =>
            {
                if (WebUi is not null)
                {
                    await WebUi.StopAsync();
                }
            });
            await AttemptAsync(errors, "webui dispose", async () =>
            {
                if (WebUi is not null)
                {
                    await WebUi.DisposeAsync();
                    WebUi = null;
                }
            });
            await AttemptAsync(errors, "registry", async () => await Registry.DisposeAsync());
            // The registry retires fibers; the Runtime owns the command channel
            // and decision loop, so it must be completed as well.
            await AttemptAsync(errors, "runtime", async () => await Runtime.DisposeAsync());

            if (errors.Count > 0)
            {
                throw new AggregateException("console cleanup was incomplete", errors);
            }

            _disposed = true;
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private static async Task AttemptAsync(List<Exception> errors, string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            errors.Add(new InvalidOperationException($"{step} cleanup failed", ex));
        }
    }

    private sealed class ServerBridgeComponent : Component
    {
        private readonly string _serverAddress;

        public ServerBridgeComponent(string serverAddress) =>
            _serverAddress = serverAddress;

        public override string Name => "koishi-console-bridge";

        public override IReadOnlyList<ICapabilityKey> Inject() =>
            [UiContributionRegistry.Key, ApiGateway.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            var api = context.Require(ApiGateway.Key);
            var owner = ContributionOwner.For("koishi-console", "bridge");
            ConsoleClient? client = null;
            await context.EffectAsync(
                () => { client = new ConsoleClient(_serverAddress); return ValueTask.CompletedTask; },
                () => { client?.Dispose(); return ValueTask.CompletedTask; });

            await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
                "koishi-console", "Koishi Console", "/koishi", "generic",
                Description: "Second independent Cordis application"));
            await context.RegisterApiQueryAsync(api, owner, "koishi/server", () =>
            {
                var meta = client!.GetMetaAsync().GetAwaiter().GetResult();
                return new JsonObject { ["server"] = meta.Kind };
            });
            return null;
        }
    }
}
