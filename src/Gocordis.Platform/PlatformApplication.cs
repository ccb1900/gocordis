using System.IO;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Runtime.Managed;
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
public class PlatformApplication : IAsyncDisposable
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
        Runtime = new Gocordis.Core.Runtime();
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        Managed = new ManagedPluginHost(Runtime);
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

    public Gocordis.Core.Runtime Runtime { get; }
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public ManagedPluginHost Managed { get; }
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

        try
        {
            var manifest = await ApplicationManifest.LoadAsync(_options.ManifestPath, cancellationToken);
            if (manifest.HostVersion != Store.HostVersion)
            {
                throw new InvalidOperationException(
                    $"application host version '{manifest.HostVersion}' does not match store host '{Store.HostVersion}'");
            }

            _manifest = manifest;

            // Framework keys are capabilities like any other: one Core fiber
            // provides them, and the registry treats them as host-supplied when
            // validating an injected dependency at Prepare time.
            Registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
            Registry.ExternalCapabilities.Add(ApiGateway.Key);
            await Runtime.LoadAsync(new ConsoleCapabilitiesComponent(Console));

            // Backend selection belongs to the composition root. The control
            // plane receives the same factory so packages installed later use
            // the same dispatch path as declarations known at startup.
            Func<PluginManifest, Component> componentFactory = CreateComponent;
            foreach (var plugin in manifest.Plugins)
            {
                var installed = await Store.ResolveAsync(plugin.Id, plugin.VersionRange, cancellationToken);
                if (installed is null)
                {
                    continue; // ApplicationLoader reports the missing dependency.
                }

                try
                {
                    Registry.RegisterComponentFactory(installed.Manifest.Type, componentFactory);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already registered", StringComparison.Ordinal))
                {
                    // A type can be shared by several declarations; one factory
                    // serves all of them, so a duplicate registration is benign.
                }
            }

            // The control plane is a plugin contribution: load its fiber first,
            // then read its capability. Runtime.Close ends this fiber with all
            // the others, including on a partially failed start.
            await Runtime.LoadAsync(new PluginControlPlaneComponent(
                _options.ManifestPath, Store, Registry, Console.Ui, componentFactory));
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
        catch
        {
            // Start failure keeps raise semantics: clean what was constructed,
            // then propagate the original error. StopAsync is idempotent and no
            // longer waits for _started to become true.
            try
            {
                await StopAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original startup failure.
            }

            throw;
        }
    }

    /// <summary>
    /// Selects a backend from the resolved package declaration. Managed plugins
    /// get a real collectible ALC; legacy declarations keep the placeholder
    /// adapter, so older packages remain loadable without executing code.
    /// </summary>
    private Component CreateComponent(PluginManifest manifest)
    {
        if (!manifest.Enabled)
        {
            // Prepare constructs components even for disabled declarations.
            // Returning a zero-capability component keeps disabled plugins from
            // opening an AssemblyLoadContext until they are actually enabled.
            return new DisabledPluginComponent();
        }

        if (string.Equals(manifest.Backend, "managed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(manifest.Source) ||
                string.IsNullOrWhiteSpace(manifest.EntryAssembly) ||
                string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                throw new InvalidOperationException(
                    $"managed plugin '{manifest.Id}' is missing source or entry metadata");
            }

            var assemblyPath = Path.Combine(manifest.Source, manifest.EntryAssembly);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"managed entry assembly for '{manifest.Id}' not found: '{assemblyPath}'");
            }

            // Prepare stages a new component before Commit retires the old
            // fiber, so the loader key must include the version: otherwise an
            // upgrade would fail with "already loaded" on the same id.
            var loaderKey = $"{manifest.Id}@{manifest.Version}";
            return Managed.CreateComponent(loaderKey, assemblyPath, manifest.EntryType);
        }

        return new ProductPluginComponent(manifest.Type, Console.Ui);
    }

    private sealed class DisabledPluginComponent : Component
    {
        public override string Name => "disabled-plugin";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
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

    /// <summary>Test seams for disposal faults; production uses the real resources.</summary>
    protected virtual Task StopWebUiAsync(CancellationToken cancellationToken) =>
        WebUi?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    protected virtual ValueTask DisposeWebUiAsync() =>
        WebUi?.DisposeAsync() ?? ValueTask.CompletedTask;

    protected virtual ValueTask DisposeRegistryAsync() => Registry.DisposeAsync();

    protected virtual ValueTask DisposeManagedAsync() => Managed.DisposeAsync();

    protected virtual ValueTask DisposeRuntimeAsync() => Runtime.DisposeAsync();

    protected virtual ValueTask DisposeRouteGuardAsync() =>
        _routeGuard?.DisposeAsync() ?? ValueTask.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // _stopped means all resources were released, not merely that a stop
        // was requested. A failed step leaves it false so callers can retry.
        if (_stopped)
        {
            return;
        }

        var errors = new List<Exception>();
        await AttemptAsync(errors, "webui stop", async () => await StopWebUiAsync(cancellationToken));
        await AttemptAsync(errors, "webui dispose", async () =>
        {
            if (WebUi is not null)
            {
                await DisposeWebUiAsync();
                WebUi = null;
            }
        });
        await AttemptAsync(errors, "registry", async () => await DisposeRegistryAsync());
        await AttemptAsync(errors, "managed host", async () => await DisposeManagedAsync());
        await AttemptAsync(errors, "runtime", async () => await DisposeRuntimeAsync());
        await AttemptAsync(errors, "route guard", async () => await DisposeRouteGuardAsync());

        if (errors.Count > 0)
        {
            throw new AggregateException("platform cleanup was incomplete", errors);
        }

        _stopped = true;
    }

    private static async Task AttemptAsync(
        List<Exception> errors, string step, Func<Task> action)
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

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await StopAsync();
        }
    }
}
