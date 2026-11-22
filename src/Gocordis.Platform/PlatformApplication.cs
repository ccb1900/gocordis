using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;

namespace Gocordis.Platform;

/// <summary>Options for the product application host (not a sample).</summary>
public sealed class PlatformHostOptions
{
    public PlatformHostOptions(
        string ManifestPath,
        string StorePath,
        string Address = "http://127.0.0.1:0",
        bool AllowRemote = false,
        UiHostKind Host = UiHostKind.Web,
        string HostVersion = "1.0.0")
    {
        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            throw new ArgumentException("manifest path is required", nameof(ManifestPath));
        }

        if (string.IsNullOrWhiteSpace(StorePath))
        {
            throw new ArgumentException("store path is required", nameof(StorePath));
        }

        ListenGuard.Validate(Address, AllowRemote);
        this.ManifestPath = ManifestPath;
        this.StorePath = StorePath;
        this.Address = Address;
        this.AllowRemote = AllowRemote;
        this.Host = Host;
        this.HostVersion = HostVersion;
    }

    public string ManifestPath { get; }
    public string StorePath { get; }
    public string Address { get; }
    public bool AllowRemote { get; }
    public UiHostKind Host { get; }
    public string HostVersion { get; }
}

/// <summary>
/// Product application host. It composes the one Runtime, one PluginRegistry,
/// the package store and a host-specific UI renderer. It is not a second
/// plugin lifecycle authority.
/// </summary>
public sealed class PlatformApplication : IAsyncDisposable
{
    private readonly PlatformHostOptions _options;
    private bool _started;
    private bool _stopped;

    public PlatformApplication(PlatformHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Runtime = new Runtime();
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        Store = new PluginStore(options.StorePath, options.HostVersion);
    }

    public Runtime Runtime { get; }
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public PluginStore Store { get; }
    public WebUiServer? WebUi { get; private set; }
    public Uri? Address => WebUi?.Address is { } address ? new Uri(address) : null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        var manifest = await ApplicationManifest.LoadAsync(_options.ManifestPath, cancellationToken);
        if (manifest.HostVersion != Store.HostVersion)
        {
            throw new InvalidOperationException(
                $"application host version '{manifest.HostVersion}' does not match store host '{Store.HostVersion}'");
        }

        var registeredTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in manifest.Plugins)
        {
            var installed = await Store.ResolveAsync(plugin.Id, plugin.VersionRange, cancellationToken)
                            ?? throw new MissingPluginDependencyException(
                                $"'{plugin.Id}' {plugin.VersionRange} is not installed");
            if (registeredTypes.Add(installed.Manifest.Type))
            {
                var type = installed.Manifest.Type;
                Registry.RegisterFactory(type, _ => new ProductPluginComponent(type, Console.Ui));
            }
        }

        await new ApplicationLoader(Store, Registry).ApplyAsync(manifest, cancellationToken);
        if (_options.Host == UiHostKind.Web)
        {
            WebUi = new WebUiServer(
                Console,
                address: _options.Address,
                allowRemote: _options.AllowRemote,
                registry: Registry);
            await WebUi.StartAsync(cancellationToken);
        }

        _started = true;
    }

    public UiRenderPlan RenderPlan() => UiHostProfiles.Get(_options.Host).Plan(Console.Ui);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped || !_started)
        {
            return;
        }

        if (WebUi is not null)
        {
            await WebUi.StopAsync(cancellationToken);
            await WebUi.DisposeAsync();
            WebUi = null;
        }

        await Registry.DisposeAsync();
        await Runtime.DisposeAsync();
        _stopped = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await StopAsync();
        }
    }

    /// <summary>
    /// Adapter that turns an installed package declaration into a real Core
    /// fiber. The actual backend payload is still loaded by Managed/Process/WASM
    /// adapters; the product host only supplies the declaration adapter here.
    /// </summary>
    private sealed class ProductPluginComponent : Component
    {
        private readonly Key<string> _key = new($"product-plugin-{Guid.NewGuid():N}");
        private readonly string _type;
        private readonly UiContributionRegistry _ui;

        public ProductPluginComponent(string type, UiContributionRegistry ui)
        {
            _type = type;
            _ui = ui;
        }

        public override string Name => $"product-plugin:{_type}";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _type);
            await context.RegisterUiPageAsync(
                _ui,
                ContributionOwner.ForContext(context, _type, Name),
                new UiPageDefinition(
                    $"plugin-{_type}", _type, $"/plugins/{_type}", "generic",
                    Description: "Installed plugin contribution"));
            return null;
        }
    }
}
