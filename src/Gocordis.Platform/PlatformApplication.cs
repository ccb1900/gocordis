using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using NuGet.Versioning;

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
        string HostVersion = "1.0.0",
        IReadOnlyList<string>? AllowedHosts = null)
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
        this.AllowedHosts = AllowedHosts;
    }

    public string ManifestPath { get; }
    public string StorePath { get; }
    public string Address { get; }
    public bool AllowRemote { get; }
    public UiHostKind Host { get; }
    public string HostVersion { get; }
    public IReadOnlyList<string>? AllowedHosts { get; }
}

/// <summary>
/// Product application host. It composes the one Runtime, one PluginRegistry,
/// the package store and a host-specific UI renderer. It is not a second
/// plugin lifecycle authority.
/// </summary>
public sealed class PlatformApplication : IAsyncDisposable
{
    private readonly PlatformHostOptions _options;
    private readonly DiagnosticsService _diagnostics;
    private readonly ServerRouteGuard? _routeGuard;
    private ApplicationManifest? _manifest;
    private bool _started;
    private bool _stopped;

    public PlatformApplication(PlatformHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Runtime = new Runtime();
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        Store = new PluginStore(options.StorePath, options.HostVersion);
        _diagnostics = new DiagnosticsService(options.HostVersion);
        if (options.AllowedHosts is { Count: > 0 } allowedHosts)
        {
            _routeGuard = new ServerRouteGuard();
            _routeGuard.Add(HostAcl.Policy(allowedHosts, []));
        }

        // Read-only query: it observes declarations and never touches a fiber.
        Console.Api.RegisterQuery("devtools/diagnostics", Diagnostics,
            ContributionOwner.For("framework", "devtools"));
    }

    public Runtime Runtime { get; }
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public PluginStore Store { get; }
    public IPluginControlPlane? Control { get; private set; }
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

        _manifest = manifest;

        // The control plane is a plugin contribution: load its fiber first, then
        // read its capability. Runtime.Close ends this fiber with all the others.
        await Runtime.LoadAsync(new PluginControlPlaneComponent(
            _options.ManifestPath, Store, Registry, Console.Ui));
        var control = Runtime.RequireCapability(ControlPlaneKeys.Service);
        Control = control;

        await control.ApplyAllAsync(cancellationToken);
        if (_options.Host == UiHostKind.Web)
        {
            WebUi = new WebUiServer(
                Console,
                address: _options.Address,
                allowRemote: _options.AllowRemote,
                registry: Registry,
                control: control,
                routeGuard: _routeGuard);
            await WebUi.StartAsync(cancellationToken);
        }

        _started = true;
    }

    /// <summary>
    /// Read-only diagnosis of the current declaration set. It composes store
    /// packages, the application declaration and live plugin capabilities; it
    /// never loads, disposes or otherwise changes a fiber.
    /// </summary>
    private JsonObject Diagnostics()
    {
        // The query surface is synchronous. Move the store's gated IO off the
        // caller's context so no UI/test SynchronizationContext can be captured.
        var installed = Task.Run(() => Store.ListAsync()).GetAwaiter().GetResult();

        var modules = new List<ModuleDeclaration>();
        if (_manifest is { } application)
        {
            // Plugin ids in an application manifest are descriptions, not
            // capability dependencies, so they are not injected into Requires.
            modules.Add(new ModuleDeclaration(application.Id, application.Version));
        }

        // Live fibers are the authority for capability names. An active plugin
        // is represented once, as a plugin entry below; a module only carries
        // declared Provides while it has no running fiber, so the service does
        // not report the same live capability as a duplicate provider.
        var live = new Dictionary<string, FiberSnapshot>(StringComparer.Ordinal);
        var fibers = Runtime.Snapshot().Fibers;
        foreach (var entry in Registry.Entries.Values)
        {
            if (entry.Fiber is { } fiber)
            {
                var snapshot = fibers.FirstOrDefault(candidate => candidate.Id == fiber.Id);
                if (snapshot is not null)
                {
                    live[entry.Manifest.Id] = snapshot;
                }
            }
        }

        foreach (var plugin in installed)
        {
            live.TryGetValue(plugin.Id, out var fiber);
            modules.Add(new ModuleDeclaration(
                plugin.Id,
                plugin.Version,
                plugin.Manifest.DependenciesOrEmpty.Select(dependency => dependency.Id).ToArray(),
                fiber is null ? plugin.Manifest.ProvidesOrEmpty.ToArray() : Array.Empty<string>(),
                ToHostConstraint(plugin.Manifest.HostVersionRange)));
        }

        var plugins = new List<PluginManifest>();
        foreach (var (id, entry) in Registry.Entries)
        {
            live.TryGetValue(id, out var fiber);
            plugins.Add(entry.Manifest with
            {
                Requires = fiber?.Requires ?? Array.Empty<string>(),
                Provides = fiber?.Provides ?? Array.Empty<string>(),
            });
        }

        var findings = new JsonArray();
        foreach (var finding in _diagnostics.Diagnose(modules, plugins))
        {
            findings.Add(new JsonObject
            {
                ["severity"] = finding.Severity.ToString().ToLowerInvariant(),
                ["code"] = finding.Code,
                ["message"] = finding.Message,
            });
        }

        return new JsonObject
        {
            ["hostVersion"] = _options.HostVersion,
            ["diagnostics"] = findings,
        };
    }

    /// <summary>
    /// NuGet package ranges (e.g. "[1.5.0,)") are expressed as the single
    /// comparison DiagnosticsService understands. An unparsable range is passed
    /// through so the service can report it as an invalid version constraint.
    /// </summary>
    private static string? ToHostConstraint(string? hostVersionRange)
    {
        if (string.IsNullOrWhiteSpace(hostVersionRange))
        {
            return null;
        }

        if (!VersionRange.TryParse(hostVersionRange, out var range))
        {
            return hostVersionRange;
        }

        if (range.HasLowerBound && range.MinVersion is { } minimum)
        {
            return $">={minimum.Version}";
        }

        if (range.HasUpperBound && range.MaxVersion is { } maximum)
        {
            return $"<={maximum.Version}";
        }

        return null;
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
        if (_routeGuard is not null)
        {
            await _routeGuard.DisposeAsync();
        }

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
