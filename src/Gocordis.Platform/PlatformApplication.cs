using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using Gocordis.Builtin.ControlPlaneUi;
using Gocordis.Builtin.InsightUi;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Runtime.Managed;
using Gocordis.Runtime.Process;
using Gocordis.Runtime.Wasm;
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
        IReadOnlyList<string>? AllowedHosts = null,
        bool ValidateOnly = false,
        PluginTrustPolicy? Trust = null,
        ConsoleAccessPolicy? AccessPolicy = null,
        bool WebConsoleEnabled = true)
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
        this.ValidateOnly = ValidateOnly;
        this.Trust = Trust ?? PluginTrustPolicy.Development;
        // No CLI flag fabricates an authentication decision. Remote writes stay
        // denied until an embedding host supplies an explicit policy here.
        this.AccessPolicy = AccessPolicy;
        // degradation/test seam: starts only the minimal host recovery surface
        this.WebConsoleEnabled = WebConsoleEnabled;
    }

    public string ManifestPath { get; }
    public string StorePath { get; }
    public string Address { get; }
    public bool AllowRemote { get; }
    public UiHostKind Host { get; }
    public string HostVersion { get; }
    public IReadOnlyList<string>? AllowedHosts { get; }

    /// <summary>
    /// Declaration/activation preflight: declarations are fully reconciled and
    /// every enabled component is activated, but no transport is opened. The
    /// caller is expected to stop the application immediately after StartAsync.
    /// </summary>
    public bool ValidateOnly { get; }

    /// <summary>Package trust policy used by the control plane installer.</summary>
    public PluginTrustPolicy Trust { get; }

    /// <summary>
    /// Remote-write authorization seam. Null means the console default:
    /// loopback management is open, remote writes are denied. The product CLI
    /// never guesses an identity; an embedding host supplies the policy.
    /// </summary>
    public ConsoleAccessPolicy? AccessPolicy { get; }

    /// <summary>Whether the builtin Web Console fiber may open its transport.
    /// False yields a degraded host with only /recover; it is not a second
    /// Console implementation.</summary>
    public bool WebConsoleEnabled { get; }
}

/// <summary>One resolved declaration as seen by the product composition root.</summary>
public sealed record PlatformResolvedPlugin(string Id, string Version, bool Enabled);

/// <summary>
/// Product application host. It composes the one Runtime, one PluginRegistry,
/// the package store and a host-specific UI renderer. It is not a second
/// plugin lifecycle authority.
/// </summary>
public class PlatformApplication : IAsyncDisposable
{
    private readonly PlatformHostOptions _options;
    private IDiagnosticsService? _diagnostics;
    // WebUi needs the ASP.NET middleware surface, which is intentionally not
    // part of the public capability contract. The host knows the implementation.
    private ServerRouteGuard? _routeGuard;
    private MinimalRecoveryHost? _recoveryHost;
    private bool _recoveryDisposed;
    private Exception? _webConsoleFailure;
    private ApplicationManifest? _manifest;
    private readonly List<PlatformResolvedPlugin> _resolved = [];
    private bool _started;
    private bool _stopped;

    public PlatformApplication(PlatformHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Runtime = new Gocordis.Core.Runtime();
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        // Paper §5.2.1 managed realms: a shared name maps to one realm for all
        // entries; "local" is private to an entry id and follows that entry if
        // it moves between groups. The host owns the identity table.
        var realms = new ConcurrentDictionary<string, Realm>(StringComparer.Ordinal);
        Registry.RealmFor = (isolate, entryId) =>
        {
            if (string.IsNullOrWhiteSpace(isolate) ||
                string.Equals(isolate, "default", StringComparison.OrdinalIgnoreCase))
            {
                return Realm.Default;
            }

            var key = string.Equals(isolate, "local", StringComparison.OrdinalIgnoreCase)
                ? $"local:{entryId}"
                : $"shared:{isolate}";
            return realms.GetOrAdd(key, static name => new Realm(name));
        };
        // Give the console's insight/graph query access to the declaration set
        // so its "declared" view is populated (runtime view uses Snapshot).
        Console.AttachRegistry(Registry);
        Managed = new ManagedPluginHost(Runtime);
        Store = new PluginStore(options.StorePath, options.HostVersion);
        // One host-owned Id → Key allocator for every backend. It is assembled
        // from declarations before any fiber activates and holds no lifecycle state.
        Capabilities = new CapabilityCatalog();
        // Diagnostics and route guard are NOT constructed here. They are
        // published by official extension components loaded into the one
        // Runtime in StartAsync, then resolved through capability keys.

        // Read-only query: it observes declarations and never touches a fiber.
        Console.Api.RegisterQuery("devtools/diagnostics", Diagnostics,
            ContributionOwner.For("framework", "devtools"));
    }

    public Gocordis.Core.Runtime Runtime { get; }
    public ConsoleHost Console { get; }
    public PluginRegistry Registry { get; }
    public ManagedPluginHost Managed { get; }
    public CapabilityCatalog Capabilities { get; }
    public PluginStore Store { get; }
    public IPluginControlPlane? Control { get; private set; }
    /// <summary>Read-only view of the builtin Web Console fiber's transport.
    /// Platform never constructs or starts it.</summary>
    public WebUiServer? WebUi =>
        Runtime.TryRequireCapability(WebConsoleKeys.Service)?.Server;

    public Uri? Address =>
        WebUi?.Address is { } address ? new Uri(address)
        : _recoveryHost?.Address is { } recovery ? new Uri(recovery) : null;

    /// <summary>Non-null when the builtin Web Console failed to activate and the
    /// host entered degraded recovery mode.</summary>
    public Exception? WebConsoleFailure => _webConsoleFailure;

    /// <summary>The application declaration loaded by StartAsync (read-only).</summary>
    public ApplicationManifest? Manifest => _manifest;

    /// <summary>Resolved declarations captured by the one product startup path.</summary>
    public IReadOnlyList<PlatformResolvedPlugin> ResolvedPlugins => _resolved;

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
            _resolved.Clear();

            // Framework keys are capabilities like any other: one Core fiber
            // provides them, and the registry treats them as host-supplied when
            // validating an injected dependency at Prepare time.
            Registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
            Registry.ExternalCapabilities.Add(ApiGateway.Key);
            Registry.ExternalCapabilities.Add(ConsoleHost.Key);
            Registry.ExternalCapabilities.Add(PluginRegistry.Key);
            Registry.ExternalCapabilities.Add(ControlPlaneKeys.Service);
            Registry.ExternalCapabilities.Add(ControlPlaneKeys.Dependencies);
            Registry.ExternalCapabilities.Add(ControlPlaneKeys.Recovery);
            Registry.ExternalCapabilities.Add(WebConsoleKeys.Options);
            Registry.ExternalCapabilities.Add(WebConsoleKeys.Service);
            await Runtime.LoadAsync(new ConsoleCapabilitiesComponent(Console));

            // Official extensions are ordinary provider fibers in the same
            // Runtime, not private host objects. Dependency order is expressed
            // by Inject(): SOCKS waits for HTTP. Env is opt-in and is NOT
            // registered as host-supplied: a plugin that declares it must load
            // EnvExtensionComponent explicitly rather than sit Pending forever.
            foreach (var key in new ICapabilityKey[]
            {
                TimerKeys.Service, HttpKeys.Service, FsKeys.Service,
                SocksKeys.Service, DiagnosticsKeys.Service,
                WatchKeys.Service, RouteGuardKeys.Service,
            })
            {
                Registry.ExternalCapabilities.Add(key);
            }

            await Runtime.LoadAsync(new FileSystemExtensionComponent());
            await Runtime.LoadAsync(new HttpExtensionComponent());
            await Runtime.LoadAsync(new TimerExtensionComponent());
            await Runtime.LoadAsync(new WatchExtensionComponent());
            await Runtime.LoadAsync(new SocksExtensionComponent());
            await Runtime.LoadAsync(new DiagnosticsExtensionComponent(_options.HostVersion));
            await Runtime.LoadAsync(new RouteGuardExtensionComponent(_options.AllowedHosts));
            // Env is opt-in: loading it by default would mutate the process
            // environment from an undeclared .env in the current directory.

            _diagnostics = Runtime.RequireCapability(DiagnosticsKeys.Service);
            _routeGuard = Runtime.RequireCapability(RouteGuardKeys.Service) as ServerRouteGuard
                ?? throw new InvalidOperationException("route guard capability has no ASP.NET middleware implementation");

            // Framework inputs become capabilities too. The control plane and
            // builtin plugins resolve them from Context; the host never passes a
            // concrete Console/WebUi object into their constructors.
            Func<PluginManifest, Component> componentFactory = CreateComponent;
            await Runtime.LoadAsync(new FrameworkCapabilitiesComponent(
                Registry,
                new ControlPlaneDependencies(
                    _options.ManifestPath,
                    Store,
                    Registry,
                    componentFactory,
                    names => names.All(SharedAssemblyRegistry.IsApproved),
                    ApplyDeclarationPlanAsync,
                    () => string.Join("|", Capabilities.Declarations
                        .Select(declaration => declaration.ToWireString())
                        .OrderBy(value => value, StringComparer.Ordinal)),
                    _options.Trust),
                new HostRecovery((source, token) =>
                    (Control ?? throw new InvalidOperationException("control plane is not loaded"))
                        .RecoverManifestAsync(source, token)),
                new WebConsoleOptions(
                    _options.Address,
                    _options.AllowRemote,
                    _options.AccessPolicy,
                    _routeGuard,
                    HmrAsync,
                    StartupDisabled: _options.Host != UiHostKind.Web || !_options.WebConsoleEnabled)));

            // Backend selection belongs to the composition root. The control
            // plane receives the same factory and registers it for every declared
            // type, decorating the result with UiPackageBinder so a package's
            // gocordis-ui.json is installed/undone with the plugin. Registering
            // factories HERE would bypass that wrapper, so we only resolve the
            // declaration report; ApplyAllAsync owns factory registration.
            // Collect ALL candidate copies of every declared shared contract
            // from installed (checksum-verified) packages. The HOST seals one
            // identity before any plugin fiber activates — no plugin can decide
            // which contract version lands in the Default ALC.
            var contractCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var capabilityWire = new HashSet<string>(StringComparer.Ordinal);
            // ONE effective declaration plan for the whole composition root.
            // Entries/Group/Include expansion, flat Plugins overrides and package
            // resolution are ApplicationLoader's job; this loop never walks
            // manifest.Plugins directly.
            var plan = await ApplicationLoader.ResolveAsync(manifest, Store, cancellationToken);
            foreach (var effective in plan.Declarations)
            {
                if (effective.Installed is not { } installed)
                {
                    continue; // ApplicationLoader reports the missing dependency.
                }

                _resolved.Add(new PlatformResolvedPlugin(
                    effective.Id, installed.Version, effective.Enabled));
                capabilityWire.UnionWith(effective.Capabilities);

                // A package-declared config schema belongs to the plugin, not
                // to console code. Register it with the registry so the
                // config/schema query can drive the ConfigEditor's typed form.
                if (effective.ConfigSchema is { } schema)
                {
                    Registry.RegisterSchema(effective.Type!, schema);
                }

                foreach (var contract in effective.SharedAssemblies)
                {
                    if (!contractCandidates.TryGetValue(contract, out var paths))
                    {
                        paths = [];
                        contractCandidates[contract] = paths;
                    }

                    paths.Add(Path.Combine(Path.GetFullPath(installed.InstallPath), contract + ".dll"));
                }
            }

            SharedAssemblyRegistry.Seal(contractCandidates.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal));

            // The control plane is a plugin contribution: load its fiber first,
            // then read its capability. Runtime.Close ends this fiber with all
            // the others, including on a partially failed start.
            // The control plane is a plugin contribution: load its fiber first,
            // then read its capability. Runtime.Close ends this fiber with all
            // the others, including on a partially failed start.
            await Runtime.LoadAsync(new PluginControlPlaneComponent(
                Runtime.RequireCapability(ControlPlaneKeys.Dependencies)));
            var control = Runtime.RequireCapability(ControlPlaneKeys.Service);
            Control = control;

            // Builtin UI features are ordinary protected plugin entries in the
            // SAME Registry/Fiber lifecycle. Each owns its page declarations,
            // client module and teardown; Platform only registers factories.
            if (!_options.ValidateOnly)
            {
                // Builtin UI is host-profile independent; only the HTTP
                // transport is Web-specific. Native render profiles still get
                // the same product pages and control surface.
                await LoadBuiltinPluginsAsync(cancellationToken);
                if (_options.Host == UiHostKind.Web && !_options.WebConsoleEnabled)
                {
                    await StartRecoveryHostAsync(cancellationToken);
                }
            }

            await control.ApplyAllAsync(cancellationToken);

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
    /// Load every builtin UI feature as its own protected Fiber. Failure of
    /// the Web Console transport degrades the host to the minimal recovery
    /// surface; UI-only builtins can still be inspected through render plans.
    /// </summary>
    private async Task LoadBuiltinPluginsAsync(CancellationToken cancellationToken)
    {
        Registry.RegisterComponentFactory(WebConsolePlugin.Type, _ => new WebConsoleComponent());
        Registry.RegisterComponentFactory(ControlPlaneUiPlugin.Type, _ => new ControlPlaneUiComponent());
        Registry.RegisterComponentFactory(InsightUiPlugin.Type, _ => new InsightUiComponent());

        var declarations = new[]
        {
            BuiltinDeclaration(WebConsolePlugin.Id, WebConsolePlugin.Type, WebConsolePlugin.Version),
            BuiltinDeclaration(ControlPlaneUiPlugin.Id, ControlPlaneUiPlugin.Type, ControlPlaneUiPlugin.Version),
            BuiltinDeclaration(InsightUiPlugin.Id, InsightUiPlugin.Type, InsightUiPlugin.Version),
        };
        try
        {
            await Registry.ApplyAsync(declarations);
        }
        catch (Exception ex)
        {
            _webConsoleFailure = ex;
            await StartRecoveryHostAsync(cancellationToken);
        }
    }

    private static PluginManifest BuiltinDeclaration(string id, string type, string version) =>
        new(id, type, true, new Dictionary<string, object?>(), [], [], version,
            string.Empty, null, "builtin");

    private async Task StartRecoveryHostAsync(CancellationToken cancellationToken)
    {
        if (_recoveryHost is not null || _options.ValidateOnly || _options.Host != UiHostKind.Web)
        {
            return;
        }

        _recoveryHost = new MinimalRecoveryHost(
            _options.Address,
            _options.AllowRemote,
            Runtime.RequireCapability(ControlPlaneKeys.Recovery));
        await _recoveryHost.StartAsync(cancellationToken);
    }

    /// <summary>
    /// The one product component factory. Backend selection comes from the
    /// resolved package declaration; every branch returns an ordinary Core
    /// component driven by the same PluginRegistry/effect lifecycle. No second
    /// registry, no separate lifetime authority.
    /// </summary>
    public Component CreateComponent(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.Enabled)
        {
            // Prepare constructs components even for disabled declarations.
            // Returning a zero-capability component keeps disabled plugins from
            // opening an AssemblyLoadContext or spawning a child until enabled.
            return new DisabledPluginComponent();
        }

        var backend = string.IsNullOrWhiteSpace(manifest.Backend) ? "builtin" : manifest.Backend!;
        if (string.Equals(backend, "managed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                throw new InvalidOperationException(
                    $"managed plugin '{manifest.Id}' is missing entry type");
            }

            var assemblyPath = ResolvePayload(manifest);
            // Prepare stages a new component before Commit retires the old
            // fiber, so the loader key must include everything that changes the
            // component: version AND config. A config-only declaration change
            // otherwise collides with the still-loaded ALC ("already loaded").
            // Empty-config plugins keep the historical key (id@version); only
            // configured declarations add a fingerprint suffix. A config-only
            // change still stages a distinct ALC while the old fiber retires.
            var loaderKey = manifest.Config.Count == 0
                ? $"{manifest.Id}@{manifest.Version}"
                : $"{manifest.Id}@{manifest.Version}:{ConfigFingerprint(manifest.Config)}";
            // Capabilities and shared assemblies also change what a staged
            // component means. Keep the historical key when both are absent.
            var sharedAssemblies = manifest.SharedAssemblies ?? [];
            if (manifest.CapabilitiesOrEmpty.Count > 0 || sharedAssemblies.Count > 0)
            {
                loaderKey += ":" + DeclarationFingerprint(
                    manifest.CapabilitiesOrEmpty, sharedAssemblies);
            }
            // HMR may replace the payload at the same id/version/config. The
            // loader key must therefore include the resolved source directory,
            // otherwise Prepare stages a component while the old ALC is still
            // loaded and ManagedPluginLoader rejects the duplicate identity.
            if (!string.IsNullOrWhiteSpace(manifest.Source))
            {
                loaderKey += ":" + SourceFingerprint(manifest.Source);
            }
            return Managed.CreateComponent(
                loaderKey, assemblyPath, manifest.EntryType, manifest.SharedAssemblies, Capabilities);
        }

        if (string.Equals(backend, "process", StringComparison.OrdinalIgnoreCase))
        {
            var executable = ResolvePayload(manifest);
            var declarations = PluginManifest.ParseCapabilities(manifest.Capabilities);
            if (declarations.Count == 0)
            {
                throw new InvalidOperationException(
                    $"process plugin '{manifest.Id}' declares no capabilities; the legacy echo/error " +
                    "demo path is not a product backend. Declare capabilities with plugin pack " +
                    "--capability '<direction>|<ns>/<name>|<kind>|<version-or-range>|<media-type>|<schema>' " +
                    "(or the package manifest's capabilities field) and bind them with Inject()/Provide().");
            }

            // The child only gets handles for capabilities it declared.
            return new ProcessCapabilityComponent(executable, Capabilities, declarations);
        }

        if (string.Equals(backend, "wasm", StringComparison.OrdinalIgnoreCase))
        {
            var modulePath = ResolvePayload(manifest);
            var isWat = string.Equals(
                Path.GetExtension(modulePath), ".wat", StringComparison.OrdinalIgnoreCase);
            var fromManifest = PluginManifest.ParseCapabilities(manifest.Capabilities);
            var fromModule = isWat
                ? WasmCapabilityDeclarations.ReadFromWat(File.ReadAllText(modulePath))
                : WasmCapabilityDeclarations.Read(File.ReadAllBytes(modulePath));
            // Bidirectional consistency: a WASM module cannot add, drop or
            // change a declaration relative to its package manifest. Merge by
            // group-first used to hide conflicting versions/media types.
            if (fromManifest.Count == 0 && fromModule.Count == 0)
            {
                throw new InvalidOperationException(
                    $"WASM plugin '{manifest.Id}' declares no capabilities; the legacy value demo path " +
                    "is not a product backend. Declare capabilities in the package manifest and in the " +
                    "gocordis.capabilities custom section, then bind them with Inject()/Provide().");
            }

            if (fromManifest.Count != fromModule.Count ||
                fromManifest.Any(declaration => !fromModule.Any(declaration.Equals)))
            {
                throw new InvalidOperationException(
                    $"WASM capability declarations for plugin '{manifest.Id}' differ from its package " +
                    $"manifest: manifest=[{string.Join(" | ", fromManifest.Select(d => d.ToWireString()))}] " +
                    $"module=[{string.Join(" | ", fromModule.Select(d => d.ToWireString()))}]");
            }

            var declarations = fromManifest;
            if (declarations.Count > 0)
            {
                Capabilities.EnsureConsistent(declarations);
                return isWat
                    ? new WasmCapabilityComponent(
                        File.ReadAllText(modulePath), manifest.Id, Capabilities, declarations)
                    : new WasmCapabilityComponent(
                        File.ReadAllBytes(modulePath), manifest.Id, Capabilities, declarations);
            }

        }

        if (string.Equals(backend, "builtin", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy declaration-only packages remain loadable without executing
            // package code: the same placeholder adapter as before P2.
            return new ProductPluginComponent(manifest.Type, Console.Ui);
        }

        throw new InvalidOperationException(
            $"unsupported backend '{backend}' declared by plugin '{manifest.Id}'");
    }

    /// <summary>
    /// Self-contained transaction: captures its own baseline and restores it,
    /// never a shared field that a later operation could overwrite.
    /// </summary>
    private sealed class DeclarationPlanTransaction : IDeclarationTransaction
    {
        private readonly CapabilityCatalog _catalog;
        private readonly IReadOnlyList<CapabilityDeclaration> _previous;

        public DeclarationPlanTransaction(
            CapabilityCatalog catalog, IReadOnlyList<CapabilityDeclaration> previous)
        {
            _catalog = catalog;
            _previous = previous;
        }

        public Task RollbackAsync()
        {
            _catalog.Replace(_previous);
            return Task.CompletedTask;
        }
    }

    private async Task<IDeclarationTransaction> ApplyDeclarationPlanAsync(
        IReadOnlyList<string> capabilityWire)
    {
        var previous = Capabilities.Declarations;
        try
        {
            Capabilities.Replace(capabilityWire.Select(CapabilityDeclaration.Parse));
            Capabilities.ValidateCompatibility();
        }
        catch
        {
            Capabilities.Replace(previous); // a rejected plan never leaks
            throw;
        }

        return new DeclarationPlanTransaction(Capabilities, previous);
    }

    /// <summary>Stable, order-independent fingerprint of a declaration's config.</summary>
    private static string ConfigFingerprint(IReadOnlyDictionary<string, object?> config)
    {
        var ordered = config.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(ordered));
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes))[..12].ToLowerInvariant();
    }

    /// <summary>
    /// Stable, order-independent fingerprint of the declaration fields that
    /// change how a managed component is staged. Derived state, never persisted.
    /// </summary>
    private static string DeclarationFingerprint(
        IReadOnlyList<string> capabilities, IReadOnlyList<string> sharedAssemblies)
    {
        var canonical = new
        {
            Capabilities = capabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Shared = sharedAssemblies.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(canonical));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12].ToLowerInvariant();
    }

    /// <summary>Stable fingerprint of the payload root; HMR can replace bytes
    /// without changing the declared version.</summary>
    private static string SourceFingerprint(string source)
    {
        var normalized = Path.GetFullPath(source);
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12].ToLowerInvariant();
    }

    private static string ResolvePayload(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Source) ||
            string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            throw new InvalidOperationException(
                $"{manifest.Backend} plugin '{manifest.Id}' is missing source or entry metadata");
        }

        var entry = manifest.EntryAssembly;
        if (Path.IsPathRooted(entry) ||
            entry.Split('/', '\\').Any(segment => segment is "." or ".." or ""))
        {
            throw new InvalidOperationException(
                $"unsafe entry assembly '{entry}' for plugin '{manifest.Id}'");
        }

        var root = Path.GetFullPath(manifest.Source);
        var payload = Path.GetFullPath(Path.Combine(root, entry));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!payload.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
            !File.Exists(payload))
        {
            throw new FileNotFoundException(
                $"entry assembly for '{manifest.Id}' not found in package: '{entry}'");
        }

        return payload;
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
        // Declaration authority is the SAME effective set the loader and the
        // control plane use — entries-only ids are declared too.
        var declaredIds = new HashSet<string>(StringComparer.Ordinal);
        if (_manifest is { } declApp)
        {
            foreach (var item in Task.Run(
                         async () => await ApplicationLoader.EffectivePluginsAsync(declApp))
                         .GetAwaiter().GetResult())
            {
                declaredIds.Add(item.Id);
            }
        }

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
            // Protected framework plugins (control plane, builtin Web Console)
            // are not application declarations and never participate in
            // package/contract diagnostics.
            if (Registry.ProtectedIds.Contains(id))
            {
                continue;
            }

            live.TryGetValue(id, out var fiber);
            plugins.Add(entry.Manifest with
            {
                Requires = fiber?.Requires ?? Array.Empty<string>(),
                Provides = fiber?.Provides ?? Array.Empty<string>(),
            });
        }

        var findings = new JsonArray();
        // The query is registered in the constructor; before StartAsync the
        // runtime-owned diagnostics provider has not published its capability.
        if (_diagnostics is not null)
        {
            foreach (var finding in _diagnostics.Diagnose(modules, plugins))
            {
                findings.Add(new JsonObject
                {
                    ["severity"] = finding.Severity.ToString().ToLowerInvariant(),
                    ["code"] = finding.Code,
                    ["message"] = finding.Message,
                });
            }
        }

        // Package dependency vs application assembly: the store checks that a
        // dependency is INSTALLED, but only the application declaration decides
        // what is ASSEMBLED. A declared plugin whose package requires a provider
        // that is installed yet not declared would otherwise look "satisfied"
        // to the package manager while having no runtime provider. Report it
        // explicitly (read-only diagnostic — activation stays declaration-led).
        foreach (var entry in Registry.Entries.Values.Where(entry => !Registry.ProtectedIds.Contains(entry.Manifest.Id)))
        {
            foreach (var requirement in entry.Manifest.Requires.Distinct(StringComparer.Ordinal))
            {
                if (declaredIds.Contains(requirement))
                {
                    continue;
                }

                findings.Add(new JsonObject
                {
                    ["severity"] = "warn",
                    ["code"] = "PACKAGE_DEPENDENCY_NOT_DECLARED",
                    ["message"] = $"plugin '{entry.Manifest.Id}' declares package dependency '{requirement}', " +
                                  $"but '{requirement}' is not in the application manifest; it will not be " +
                                  "assembled or activated (declare it explicitly if it provides a runtime capability)",
                });
            }
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
        _recoveryHost?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    protected virtual ValueTask DisposeWebUiAsync() =>
        _recoveryHost?.DisposeAsync() ?? ValueTask.CompletedTask;

    protected virtual ValueTask DisposeRegistryAsync() => Registry.DisposeAsync();

    protected virtual ValueTask DisposeManagedAsync() => Managed.DisposeAsync();

    protected virtual ValueTask DisposeRuntimeAsync() => Runtime.DisposeAsync();

    protected virtual ValueTask DisposeRouteGuardAsync() =>
        _routeGuard?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Paper §5.2.2 product entry: run the three-phase HMR engine against the
    /// CURRENT declarations of this host. Callers name changed modules and the
    /// replacement fields; they cannot inject an arbitrary declaration set.
    /// The declaration revision seen by the read is pinned into the commit.
    /// </summary>
    public async Task<HmrPlan> HmrAsync(
        HmrRequest request, JsonObject? replacement = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_started)
        {
            throw new InvalidOperationException("HMR requires a started platform host");
        }

        // Declarations and their revision come from ONE gate hold. Carrying the
        // revision into the request makes the registry reject the commit if an
        // enable/disable/config change lands while this HMR is being planned.
        var snapshot = Registry.ReadDeclarations();
        var declarations = snapshot.Entries.Values.Select(entry => entry.Manifest).ToArray();
        var effective = request with
        {
            Entries = declarations,
            ExpectedDeclarationVersion = snapshot.DeclarationVersion,
        };
        var source = replacement?["source"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(source))
        {
            // A remote HMR caller must not be able to point a plugin factory at
            // an arbitrary host path. Only payloads inside this host's store are
            // valid replacement sources. Resolve the store AND every existing
            // segment of the source (Unix symlinks, Windows junctions/reparse
            // points) before deciding containment: checking only the final
            // entry lets store/plugins/external -> /outside look in-store while
            // actually loading from outside.
            source = ResolveHmrSource(source, ResolveRealPath(_options.StorePath));
        }

        var version = replacement?["version"]?.GetValue<string>();
        return await HmrEngine.ReloadAsync(
            Registry,
            effective,
            manifest =>
            {
                var next = manifest;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    next = next with { Source = source };
                }

                if (!string.IsNullOrWhiteSpace(version))
                {
                    next = next with { Version = version };
                }

                return next;
            },
            cancellationToken);
    }

    /// <summary>
    /// Validate twice: the filesystem may change between the containment
    /// decision and the eventual open. A retargeted link or a vanished payload
    /// must fail closed instead of being loaded under a stale decision. The
    /// string-based plugin factory API has no openat-style handle to pin the
    /// file, so the second observation and passing the RESOLVED path (not the
    /// caller's lexical path) are the available mitigations.
    /// </summary>
    private static string ResolveHmrSource(string source, string realStoreRoot)
    {
        var realSource = ResolveRealPath(source);
        EnsureInsideStore(realStoreRoot, realSource);

        if (!File.Exists(realSource) && !Directory.Exists(realSource))
        {
            throw new InvalidOperationException("HMR replacement source does not exist");
        }

        var rechecked = ResolveRealPath(source);
        EnsureInsideStore(realStoreRoot, rechecked);
        if (!string.Equals(rechecked, realSource, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "HMR replacement source changed while it was being validated");
        }

        return rechecked;
    }

    private static void EnsureInsideStore(string realStoreRoot, string candidate)
    {
        var relative = Path.GetRelativePath(realStoreRoot, candidate);
        var outside = Path.IsPathRooted(relative) ||
                      relative == ".." ||
                      relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                      relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        if (outside)
        {
            throw new InvalidOperationException(
                "HMR replacement source must be inside the configured plugin store");
        }
    }

    /// <summary>
    /// Canonicalize as the OS would: walk each segment and follow links when
    /// they are encountered, so a later ".." applies to the link's target and
    /// not to the lexical spelling. Path.GetFullPath alone never resolves links.
    /// </summary>
    private static string ResolveRealPath(string path, int depth = 0)
    {
        if (depth > 40)
        {
            throw new InvalidOperationException("HMR replacement source has a symlink cycle");
        }

        var full = Path.IsPathRooted(path)
            ? path
            : Path.Combine(System.Environment.CurrentDirectory, path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return Path.GetFullPath(full);
        }

        // Windows callers may spell paths with '/'; normalize before walking
        // so no segment hides a separator (and a link) from the resolver.
        var normalized = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var real = root;
        foreach (var segment in normalized[root.Length..].Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                real = Path.GetFullPath(Path.Combine(real, ".."));
                continue;
            }

            var candidate = Path.Combine(real, segment);
            var link = LinkTarget(candidate);
            real = link is null ? candidate : ResolveFinalTarget(candidate, link, depth);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(real));
    }

    /// <summary>
    /// Read the link target even for a dangling link (Exists is false there)
    /// and for Windows junctions, which are reparse points on directories.
    /// </summary>
    private static string? LinkTarget(string candidate)
    {
        try
        {
            return new FileInfo(candidate).LinkTarget ?? new DirectoryInfo(candidate).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException)
        {
            // Fail closed: an unreadable segment cannot be proven in-store.
            throw new InvalidOperationException(
                $"cannot inspect HMR path segment '{candidate}'", ex);
        }
    }

    private static string ResolveFinalTarget(string linkPath, string target, int depth)
    {
        try
        {
            FileSystemInfo info = new FileInfo(linkPath);
            info = info.LinkTarget is null ? new DirectoryInfo(linkPath) : info;
            var final = info.ResolveLinkTarget(returnFinalTarget: true);
            if (final is not null && !string.IsNullOrWhiteSpace(final.FullName))
            {
                return ResolveRealPath(final.FullName, depth + 1);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException)
        {
            // A dangling link has no resolvable final target; fall back to its
            // lexical target and resolve that segment by segment so a relative
            // target or a linked parent directory cannot bypass containment.
        }

        var lexical = Path.IsPathRooted(target)
            ? target
            : Path.Combine(Path.GetDirectoryName(linkPath)!, target);
        return ResolveRealPath(lexical, depth + 1);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // _stopped means all resources were released, not merely that a stop
        // was requested. A failed step leaves it false so callers can retry.
        if (_stopped)
        {
            return;
        }

        var errors = new List<Exception>();
        // The Web Console transport stops as an Effect inverse when Registry is
        // disposed. Only the degraded recovery host is owned directly here.
        await AttemptAsync(errors, "recovery host stop", async () =>
            await StopWebUiAsync(cancellationToken));
        if (!_recoveryDisposed)
        {
            await AttemptAsync(errors, "recovery host dispose", async () =>
            {
                await DisposeWebUiAsync();
                _recoveryDisposed = true;
                _recoveryHost = null;
            });
        }
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
