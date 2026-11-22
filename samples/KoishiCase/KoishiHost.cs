using Gocordis.Core;
using Gocordis.Plugins;

namespace KoishiCase;

/// <summary>
/// Server-side Koishi application: the host supplies only domain vocabulary;
/// every feature is a plugin composed through Core primitives.
/// </summary>
public sealed class KoishiHost : IAsyncDisposable
{
    private readonly PluginRegistry _registry;

    public KoishiHost()
    {
        Runtime = new Gocordis.Core.Runtime();
        Console = new ConsoleHost(Runtime);
        Commands = new CommandRegistry();
        LiveDatabases = [];
        LiveAdapters = [];
        _registry = new PluginRegistry(Runtime);

        // Host vocabulary is supplied outside the declaration batch.
        _registry.ExternalCapabilities.Add(K.Commands);
        _registry.ExternalCapabilities.Add(K.Message);
        _registry.RegisterFactory("database", _ => new DatabasePlugin("sqlite", LiveDatabases));
        _registry.RegisterFactory("adapter", config =>
            new AdapterPlugin(LiveAdapters) { PlatformTag = config["tag"]?.ToString() });
        _registry.RegisterFactory("feature", _ => new FeaturePlugin());
    }

    public Gocordis.Core.Runtime Runtime { get; }

    public ConsoleHost Console { get; }

    public CommandRegistry Commands { get; }

    public List<LiveDatabase> LiveDatabases { get; }

    public List<LiveAdapter> LiveAdapters { get; }

    public PluginRegistry Registry => _registry;

    public async Task StartAsync()
    {
        await Runtime.LoadAsync(new HostVocabulary(Commands));
        await _registry.ApplyAsync([
            Manifest("db", "database", true),
            Manifest("adapter", "adapter", true),
            Manifest("feature", "feature", true),
        ]);
    }

    public Task SetEnabledAsync(string id, bool enabled) => _registry.ApplyAsync([
        Manifest("db", "database", true),
        Manifest("adapter", "adapter", true),
        Manifest("feature", "feature", enabled),
    ]);

    public Task ReplaceAdapterAsync(string tag) => _registry.ApplyAsync([
        Manifest("db", "database", true),
        Manifest("adapter", "adapter", true, tag),
        Manifest("feature", "feature", true),
    ]);

    public async ValueTask DisposeAsync()
    {
        await _registry.DisposeAsync();
        await Runtime.DisposeAsync();
    }

    private static PluginManifest Manifest(string id, string type, bool enabled, string? tag = null)
    {
        var config = new Dictionary<string, object?> { ["tag"] = tag ?? "v1" };
        return new PluginManifest(id, type, enabled, config, [], []);
    }

    private sealed class HostVocabulary : Component
    {
        private readonly CommandRegistry _commands;

        public HostVocabulary(CommandRegistry commands) => _commands = commands;

        public override string Name => "koishi-host-vocabulary";

        public override IReadOnlyList<ICapabilityKey> Provide() => [K.Commands, K.Message];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(K.Commands, _commands);
            context.Provide(K.Message, "channel");
            return null;
        }
    }
}

/// <summary>
/// Koishi's web console is a second, independent Cordis application using the
/// same primitives, here represented by its own runtime and contribution registry.
/// </summary>
public sealed class BrowserConsoleApp : IAsyncDisposable
{
    public BrowserConsoleApp()
    {
        Runtime = new Gocordis.Core.Runtime();
        Console = new ConsoleHost(Runtime);
    }

    public Gocordis.Core.Runtime Runtime { get; }

    public ConsoleHost Console { get; }

    public async Task StartAsync()
    {
        await Runtime.LoadAsync(new UiHost(Console.Ui));
        await Runtime.LoadAsync(new PagePlugin(Console.Ui));
    }

    public async ValueTask DisposeAsync() => await Runtime.DisposeAsync();

    private sealed class UiHost : Component
    {
        private readonly UiContributionRegistry _ui;
        public UiHost(UiContributionRegistry ui) => _ui = ui;
        public override string Name => "browser-ui-host";
        public override IReadOnlyList<ICapabilityKey> Provide() => [UiContributionRegistry.Key];
        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _ui);
            return null;
        }
    }

    private sealed class PagePlugin : Component
    {
        private readonly UiContributionRegistry _ui;
        private UiContribution? _page;

        public PagePlugin(UiContributionRegistry ui) => _ui = ui;

        public override string Name => "browser-page";

        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () =>
                {
                    if (_page is not null)
                    {
                        ui.Unregister(_page);
                    }

                    return ValueTask.CompletedTask;
                });
            _page = ui.Register("page", "koishi-console", ContributionOwner.For("browser", Name));
            return null;
        }
    }
}
