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
        Control = new PluginControlService(options.ManifestPath, Store, Registry, Console.Ui);
    }

    public Runtime Runtime { get; }
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public PluginStore Store { get; }
    public PluginControlService Control { get; }
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

        await Control.ApplyAllAsync(cancellationToken);
        if (_options.Host == UiHostKind.Web)
        {
            WebUi = new WebUiServer(
                Console,
                address: _options.Address,
                allowRemote: _options.AllowRemote,
                registry: Registry,
                control: Control);
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
}
