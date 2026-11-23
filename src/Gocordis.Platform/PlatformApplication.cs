using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using Gocordis.Console.WebUi;
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
        PluginTrustPolicy? Trust = null)
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
        Trust = Trust ?? PluginTrustPolicy.Development;
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
}

/// <summary>One resolved declaration as seen by the product composition root.</summary>
public sealed record PlatformResolvedPlugin(string Id, string Version, bool Enabled);

/// <summary>
/// Stable capability keys for backend-provided values. Key identity is
/// reference identity, so consumers must resolve keys through this contract
/// rather than constructing a key with the same name.
/// </summary>
public static class PlatformBackendKeys
{
    private static readonly ConcurrentDictionary<string, Key<string>> ProcessEchoKeys = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Key<string>> ProcessErrorKeys = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Key<ProcessDiagnostics>> ProcessDiagnosticKeys = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Key<int>> WasmValueKeys = new(StringComparer.Ordinal);

    public static Key<string> ProcessEcho(string pluginId) =>
        ProcessEchoKeys.GetOrAdd(pluginId, id => new Key<string>($"{id}.process.echo"));

    public static Key<string> ProcessError(string pluginId) =>
        ProcessErrorKeys.GetOrAdd(pluginId, id => new Key<string>($"{id}.process.error"));

    public static Key<ProcessDiagnostics> ProcessDiagnostics(string pluginId) =>
        ProcessDiagnosticKeys.GetOrAdd(pluginId, id => new Key<ProcessDiagnostics>($"{id}.process.diagnostics"));

    public static Key<int> WasmValue(string pluginId) =>
        WasmValueKeys.GetOrAdd(pluginId, id => new Key<int>($"{id}.wasm.value"));
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
    private readonly List<PlatformResolvedPlugin> _resolved = [];
    private bool _started;
    private bool _stopped;

    public PlatformApplication(PlatformHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Runtime = new Gocordis.Core.Runtime();
        Console = new ConsoleHost(Runtime);
        Registry = new PluginRegistry(Runtime);
        // Give the console's insight/graph query access to the declaration set
        // so its "declared" view is populated (runtime view uses Snapshot).
        Console.AttachRegistry(Registry);
        Managed = new ManagedPluginHost(Runtime);
        Store = new PluginStore(options.StorePath, options.HostVersion);
        // One host-owned Id → Key allocator for every backend. It is assembled
        // from declarations before any fiber activates and holds no lifecycle state.
        Capabilities = new CapabilityCatalog();
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
    public CapabilityCatalog Capabilities { get; }
    public PluginStore Store { get; }
    public IPluginControlPlane? Control { get; private set; }
    public WebUiServer? WebUi { get; private set; }
    public Uri? Address => WebUi?.Address is { } address ? new Uri(address) : null;

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
            await Runtime.LoadAsync(new ConsoleCapabilitiesComponent(Console));

            // Backend selection belongs to the composition root. The control
            // plane receives the same factory and registers it for every declared
            // type, decorating the result with UiPackageBinder so a package's
            // gocordis-ui.json is installed/undone with the plugin. Registering
            // factories HERE would bypass that wrapper, so we only resolve the
            // declaration report; ApplyAllAsync owns factory registration.
            Func<PluginManifest, Component> componentFactory = CreateComponent;
            // Collect ALL candidate copies of every declared shared contract
            // from installed (checksum-verified) packages. The HOST seals one
            // identity before any plugin fiber activates — no plugin can decide
            // which contract version lands in the Default ALC.
            var contractCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var capabilityWire = new HashSet<string>(StringComparer.Ordinal);
            foreach (var plugin in manifest.Plugins)
            {
                var installed = await Store.ResolveAsync(plugin.Id, plugin.VersionRange, cancellationToken);
                if (installed is null)
                {
                    continue; // ApplicationLoader reports the missing dependency.
                }

                _resolved.Add(new PlatformResolvedPlugin(plugin.Id, installed.Version, plugin.Enabled));
                capabilityWire.UnionWith(installed.Manifest.CapabilitiesOrEmpty);

                // A package-declared config schema belongs to the plugin, not
                // to console code. Register it with the registry so the
                // config/schema query can drive the ConfigEditor's typed form.
                if (installed.Manifest.ConfigSchema is { } schema)
                {
                    Registry.RegisterSchema(installed.Manifest.Type, schema);
                }

                foreach (var contract in installed.Manifest.SharedAssembliesOrEmpty)
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
            await Runtime.LoadAsync(new PluginControlPlaneComponent(
                _options.ManifestPath, Store, Registry, Console.Ui, componentFactory,
                names => names.All(SharedAssemblyRegistry.IsApproved),
                ApplyDeclarationPlanAsync,
                () => string.Join("|", Capabilities.Declarations
                    .Select(declaration => declaration.ToWireString())
                    .OrderBy(value => value, StringComparer.Ordinal)),
                _options.Trust));
            var control = Runtime.RequireCapability(ControlPlaneKeys.Service);
            Control = control;

            await control.ApplyAllAsync(cancellationToken);
            // Validate-only stops after the declaration set is genuinely active:
            // no transport is opened, so a preflight cannot leave a listener behind.
            if (_options.Host == UiHostKind.Web && !_options.ValidateOnly)
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
            return Managed.CreateComponent(
                loaderKey, assemblyPath, manifest.EntryType, manifest.SharedAssemblies, Capabilities);
        }

        if (string.Equals(backend, "process", StringComparison.OrdinalIgnoreCase))
        {
            var executable = ResolvePayload(manifest);
            var declarations = PluginManifest.ParseCapabilities(manifest.Capabilities);
            if (declarations.Count > 0)
            {
                // Declared capabilities replace the host-invented echo/error keys:
                // the child only gets handles for capabilities it declared.
                return new ProcessCapabilityComponent(executable, Capabilities, declarations);
            }

            return new ProcessPluginComponent(
                executable,
                PlatformBackendKeys.ProcessEcho(manifest.Id),
                PlatformBackendKeys.ProcessDiagnostics(manifest.Id),
                PlatformBackendKeys.ProcessError(manifest.Id));
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
                var legacyKey = PlatformBackendKeys.WasmValue(manifest.Id);
                return isWat
                    ? new WasmComponent(File.ReadAllText(modulePath), legacyKey)
                    : new WasmComponent(File.ReadAllBytes(modulePath), legacyKey, name: manifest.Id);
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

        // Package dependency vs application assembly: the store checks that a
        // dependency is INSTALLED, but only the application declaration decides
        // what is ASSEMBLED. A declared plugin whose package requires a provider
        // that is installed yet not declared would otherwise look "satisfied"
        // to the package manager while having no runtime provider. Report it
        // explicitly (read-only diagnostic — activation stays declaration-led).
        var declaredIds = _manifest?.Plugins
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Registry.Entries.Values)
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
