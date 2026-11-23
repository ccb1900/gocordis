using System.Text.Json;
using Gocordis.Core;
using Gocordis.Plugins;
using NuGet.Versioning;

namespace Gocordis.Packaging;

// ---- control DTOs ---------------------------------------------------------
// These records are transport-neutral. WebUiServer hand-writes their lowercase
// JSON shape so the wire contract does not depend on System.Text.Json naming.

public sealed record PluginControlEntry(
    string Id,
    string Name,
    string Type,
    bool Enabled,
    string State,
    string? Error,
    string Version,
    string? InstalledVersion,
    bool Protected,
    bool Controllable,
    bool Declared,
    IReadOnlyDictionary<string, object?> Config,
    IReadOnlyList<string> Components,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> DeclaredCapabilityRequires,
    IReadOnlyList<string> DeclaredCapabilityProvides);

public sealed record StorePluginEntry(
    string Id,
    string Version,
    string DisplayName,
    string HostVersionRange,
    string InstallPath,
    IReadOnlyList<PluginDependency> Dependencies,
    IReadOnlyList<string> Provides,
    string Checksum,
    bool Declared,
    bool Running);

public sealed record PackageDependencyView(
    string Id,
    string VersionRange,
    string? InstalledVersion,
    bool Satisfied);

public sealed record PluginDependencyView(
    string Id,
    string Version,
    IReadOnlyList<PackageDependencyView> PackageDependencies,
    IReadOnlyList<string> Dependents,
    IReadOnlyList<string> CapabilityRequires,
    IReadOnlyList<string> CapabilityProvides,
    IReadOnlyList<string> DeclaredCapabilityRequires,
    IReadOnlyList<string> DeclaredCapabilityProvides,
    IReadOnlyList<string> Diagnostics);

public sealed record PluginUploadResult(
    string UploadKey,
    string Id,
    string Version,
    string Type,
    string HostVersionRange,
    string Checksum,
    IReadOnlyList<PluginDependency> Dependencies);

public sealed record PluginMutationResult(
    bool Accepted,
    string Code,
    string? Error,
    string Id,
    string? State,
    bool Persisted,
    IReadOnlyList<PluginChange> Changes);

/// <summary>An upload exceeded the shared package size limit (HTTP 413).</summary>
public sealed class PluginUploadTooLargeException : InvalidPluginPackageException
{
    public PluginUploadTooLargeException(string message) : base(message) { }
}

internal sealed class InvalidPluginConfigException : Exception
{
    public InvalidPluginConfigException(string message) : base(message) { }
}

/// <summary>
/// The control surface WebUiServer drives. It is a capability contribution of
/// the plugin-control-plane fiber; DTOs and HTTP wire shapes stay unchanged.
/// </summary>
public interface IPluginControlPlane
{
    long Revision { get; }

    Task ApplyAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PluginControlEntry>> ListAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StorePluginEntry>> StoreAsync(CancellationToken ct = default);

    Task<PluginDependencyView?> DependenciesAsync(string id, CancellationToken ct = default);

    Task<PluginUploadResult> UploadAsync(Stream packageStream, string fileName, CancellationToken ct = default);

    Task<PluginMutationResult> InstallAsync(
        string uploadKey, bool enable = true, CancellationToken ct = default);

    Task<PluginMutationResult> UpgradeAsync(
        string id, string uploadKey, string? versionRange = null, CancellationToken ct = default);

    Task<PluginMutationResult> UninstallAsync(
        string id, string? version = null, CancellationToken ct = default);

    Task<PluginMutationResult> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);

    Task<PluginMutationResult> SetConfigAsync(
        string id, IReadOnlyDictionary<string, object?> config, CancellationToken ct = default);
}

public static class ControlPlaneKeys
{
    public static readonly Key<IPluginControlPlane> Service = new("plugin-control-plane");
}

/// <summary>
/// Declaration-aware plugin control. It owns no fibers: every lifecycle change
/// is expressed as a manifest edit followed by ApplicationLoader.ApplyAsync, so
/// PluginRegistry remains the single lifecycle authority. All operations are
/// serialized and the declaration store is restored if reconciliation fails.
/// </summary>
public sealed class PluginControlService : IPluginControlPlane
{
    private readonly string _manifestPath;
    private readonly PluginStore _store;
    private readonly PluginRegistry _registry;
    private readonly UiContributionRegistry? _ui;
    private readonly Func<PluginManifest, Component>? _componentFactory;
    private readonly Func<IReadOnlyList<string>, bool>? _sharedAssemblyApproval;
    private readonly Func<IReadOnlyList<string>, Task>? _declarationPlan;
    private readonly Func<Task>? _declarationRollback;
    private readonly ApplicationLoader _loader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _registeredTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _uploads = new(StringComparer.Ordinal);
    private long _revision;

    public PluginControlService(
        string ManifestPath,
        PluginStore Store,
        PluginRegistry Registry,
        UiContributionRegistry? Ui = null,
        Func<PluginManifest, Component>? ComponentFactory = null,
        Func<IReadOnlyList<string>, bool>? SharedAssemblyApproval = null,
        Func<IReadOnlyList<string>, Task>? DeclarationPlan = null,
        Func<Task>? DeclarationRollback = null)
    {
        _manifestPath = ManifestPath ?? throw new ArgumentNullException(nameof(ManifestPath));
        _store = Store ?? throw new ArgumentNullException(nameof(Store));
        _registry = Registry ?? throw new ArgumentNullException(nameof(Registry));
        _ui = Ui;
        // The host owns backend selection; Packaging never references a backend.
        _componentFactory = ComponentFactory;
        _sharedAssemblyApproval = SharedAssemblyApproval;
        _declarationPlan = DeclarationPlan;
        _declarationRollback = DeclarationRollback;
        _loader = new ApplicationLoader(Store, Registry)
        {
            DeclarationPlan = DeclarationPlan,
            DeclarationRollback = DeclarationRollback,
        };
        CleanStaleUploads();
    }

    public long Revision => Interlocked.Read(ref _revision);

    public async Task ApplyAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ApplyAllCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PluginControlEntry>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var application = await LoadApplicationAsync(ct);
            var installed = await _store.ListAsync(ct);
            var entries = new List<PluginControlEntry>(application.Plugins.Count);
            foreach (var declaration in application.Plugins)
            {
                var package = installed.FirstOrDefault(p => p.Id == declaration.Id);
                var resolved = await _store.ResolveAsync(declaration.Id, declaration.VersionRange, ct);
                _registry.Entries.TryGetValue(declaration.Id, out var entry);
                var fiber = entry?.Fiber;
                var component = fiber?.Component;
                var isProtected = _registry.ProtectedIds.Contains(declaration.Id);
                var state = fiber?.State.ToString() ?? (package is null ? "not_installed" : "disabled");
                entries.Add(new PluginControlEntry(
                    declaration.Id,
                    package?.Manifest.DisplayName ?? declaration.Id,
                    package?.Manifest.Type ?? entry?.Manifest.Type ?? declaration.Id,
                    declaration.Enabled,
                    state,
                    fiber?.Failure?.Message,
                    declaration.VersionRange,
                    resolved?.Version ?? package?.Version,
                    isProtected,
                    !isProtected,
                    true,
                    declaration.Config ?? new Dictionary<string, object?>(),
                    component is null ? Array.Empty<string>() : [component.Name],
                    component is null
                        ? Array.Empty<string>()
                        : component.Provide().Select(key => key.Name).ToArray(),
                    entry?.Manifest.Requires
                    ?? package?.Manifest.DependenciesOrEmpty.Select(dep => dep.Id).ToArray()
                    ?? Array.Empty<string>(),
                    entry?.Manifest.Provides
                    ?? package?.Manifest.ProvidesOrEmpty
                    ?? Array.Empty<string>(),
                    DeclaredCapabilities(
                        entry?.Manifest.CapabilitiesOrEmpty ?? package?.Manifest.CapabilitiesOrEmpty,
                        CapabilityDirection.Require),
                    DeclaredCapabilities(
                        entry?.Manifest.CapabilitiesOrEmpty ?? package?.Manifest.CapabilitiesOrEmpty,
                        CapabilityDirection.Provide)));
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StorePluginEntry>> StoreAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var installed = await _store.ListAsync(ct);
            IReadOnlyList<ApplicationPlugin> declarations;
            try
            {
                declarations = (await LoadApplicationAsync(ct)).Plugins;
            }
            catch (FileNotFoundException)
            {
                declarations = [];
            }

            return installed.Select(package => new StorePluginEntry(
                package.Id,
                package.Version,
                package.Manifest.DisplayName ?? package.Id,
                package.Manifest.HostVersionRange,
                package.InstallPath,
                package.Manifest.DependenciesOrEmpty,
                package.Manifest.ProvidesOrEmpty,
                package.Manifest.Checksum ?? string.Empty,
                declarations.Any(declaration => declaration.Id == package.Id),
                _registry.Entries.TryGetValue(package.Id, out var entry) &&
                entry.Fiber?.State == FiberState.Active)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginDependencyView?> DependenciesAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var installed = await _store.ListAsync(ct);
            var target = installed.FirstOrDefault(package => package.Id == id);
            if (target is null)
            {
                return null;
            }

            var application = await LoadApplicationAsync(ct);
            var declaration = application.Plugins.FirstOrDefault(plugin => plugin.Id == id);
            var resolved = declaration is null
                ? target
                : await _store.ResolveAsync(id, declaration.VersionRange, ct) ?? target;

            var dependencies = new List<PackageDependencyView>();
            var diagnostics = new List<string>();
            foreach (var dependency in target.Manifest.DependenciesOrEmpty)
            {
                // Resolve the highest satisfying version, matching Store.ResolveAsync;
                // the first match in ListAsync order would display an older version.
                var match = installed
                    .Where(package => package.Id == dependency.Id &&
                                      VersionRange.TryParse(dependency.VersionRange, out var range) &&
                                      NuGetVersion.TryParse(package.Version, out var version) &&
                                      range.Satisfies(version))
                    .OrderByDescending(package => NuGetVersion.Parse(package.Version))
                    .FirstOrDefault();
                dependencies.Add(new PackageDependencyView(
                    dependency.Id,
                    dependency.VersionRange,
                    match?.Version,
                    match is not null));
                if (match is null)
                {
                    diagnostics.Add($"missing package dependency '{dependency.Id}' {dependency.VersionRange}");
                }
            }

            var dependents = installed
                .Where(package => package.Id != id &&
                                  package.Manifest.DependenciesOrEmpty.Any(dependency => dependency.Id == id))
                .Select(package => package.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            _registry.Entries.TryGetValue(id, out var entry);
            if (entry?.Fiber?.Failure is { } failure)
            {
                diagnostics.Add(failure.Message);
            }

            foreach (var diagnostic in _registry.Diagnostics)
            {
                diagnostics.Add($"{diagnostic.Path}: {diagnostic.Error.Message}");
            }

            return new PluginDependencyView(
                target.Id,
                resolved.Version,
                dependencies,
                dependents,
                entry?.Manifest.Requires ?? Array.Empty<string>(),
                entry?.Manifest.Provides ?? target.Manifest.ProvidesOrEmpty,
                DeclaredCapabilities(
                    entry?.Manifest.CapabilitiesOrEmpty ?? target.Manifest.CapabilitiesOrEmpty,
                    CapabilityDirection.Require),
                DeclaredCapabilities(
                    entry?.Manifest.CapabilitiesOrEmpty ?? target.Manifest.CapabilitiesOrEmpty,
                    CapabilityDirection.Provide),
                diagnostics);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginUploadResult> UploadAsync(
        Stream packageStream, string fileName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ArgumentNullException.ThrowIfNull(packageStream);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            // The client-supplied name is never a key or a path. Keys are server
            // generated so two uploads of the same file cannot overwrite each other.
            var suppliedName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(suppliedName))
            {
                throw new ArgumentException("invalid package file name", nameof(fileName));
            }

            var key = Guid.NewGuid().ToString("N");

            var uploadDirectory = Path.Combine(_store.Root, ".uploads");
            Directory.CreateDirectory(uploadDirectory);
            var path = Path.Combine(uploadDirectory, $"{key}.gocordis-plugin");
            try
            {
                await using (var output = File.Create(path))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    while (true)
                    {
                        var read = await packageStream.ReadAsync(buffer, ct);
                        if (read == 0)
                        {
                            break;
                        }

                        total += read;
                        if (total > PluginPackageLimits.MaxPackageBytes)
                        {
                            throw new PluginUploadTooLargeException(
                                $"package exceeds {PluginPackageLimits.MaxPackageBytes} bytes");
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, ct);
                _uploads[key] = path;
                return new PluginUploadResult(
                    key,
                    content.Manifest.Id,
                    content.Manifest.Version,
                    content.Manifest.Type,
                    content.Manifest.HostVersionRange,
                    content.Manifest.Checksum ?? string.Empty,
                    content.Manifest.DependenciesOrEmpty);
            }
            catch
            {
                TryDeleteFile(path);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PluginMutationResult> InstallAsync(
        string uploadKey, bool enable = true, CancellationToken ct = default) =>
        MutateAsync(uploadKey ?? string.Empty, async (baseline, token) =>
        {
            var key = uploadKey ?? string.Empty;
            var path = await ResolveUploadAsync(key, token);
            try
            {
                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
                var id = content.Manifest.Id;
                var version = content.Manifest.Version;
                if (!SharedAssembliesApproved(content.Manifest.SharedAssembliesOrEmpty))
                {
                    return Rejected(id, "host_restart_required",
                        $"plugin '{id}' declares shared contract(s) " +
                        $"[{string.Join(", ", content.Manifest.SharedAssembliesOrEmpty)}] " +
                        "that the host has not sealed; restart the host to approve a new contract");
                }

                var application = await LoadApplicationAsync(token);
                if (application.Plugins.Any(declaration => declaration.Id == id))
                {
                    return Rejected(id, "conflict", $"plugin '{id}' is already declared");
                }

                // The store validates the package dependencies before it writes
                // anything; remember whether this call actually adds a package so
                // rollback never removes a version that predated the mutation.
                var installedBefore = await _store.ListAsync(token);
                var addedPackage = !installedBefore.Any(package =>
                    string.Equals(package.Id, id, StringComparison.Ordinal) &&
                    string.Equals(package.Version, version, StringComparison.Ordinal));
                var storeResult = await _store.InstallAsync(path, token);

                var updated = application with
                {
                    Plugins = [.. application.Plugins, new ApplicationPlugin(id, version, enable)],
                };
                Func<Task>? undoStore = null;
                if (addedPackage)
                {
                    undoStore = () => _store.UninstallExactAsync(id, version, CancellationToken.None);
                }

                var apply = await PersistAndApplyAsync(baseline, updated, token, Inverses(undoStore));
                if (!apply.Success)
                {
                    return Failure(id, apply.Failure!, apply.CompensationError);
                }

                Interlocked.Increment(ref _revision);
                return Accepted(id, storeResult.Changes);
            }
            finally
            {
                ConsumeUpload(key);
            }
        }, ct);

    public Task<PluginMutationResult> UpgradeAsync(
        string id, string uploadKey, string? versionRange = null, CancellationToken ct = default) =>
        MutateAsync(id, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var key = uploadKey ?? string.Empty;
            var path = await ResolveUploadAsync(key, token);
            try
            {
                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
                if (!string.Equals(content.Manifest.Id, id, StringComparison.Ordinal))
                {
                    return Rejected(id, "invalid_request",
                        $"uploaded package '{content.Manifest.Id}' does not match '{id}'");
                }

                if (!SharedAssembliesApproved(content.Manifest.SharedAssembliesOrEmpty))
                {
                    return Rejected(id, "host_restart_required",
                        $"plugin '{id}' declares shared contract(s) " +
                        $"[{string.Join(", ", content.Manifest.SharedAssembliesOrEmpty)}] " +
                        "that the host has not sealed; restart the host to approve a new contract");
                }

                var application = await LoadApplicationAsync(token);
                var declaration = application.Plugins.FirstOrDefault(item => item.Id == id);
                if (declaration is null)
                {
                    return Rejected(id, "not_found", $"plugin '{id}' is not declared");
                }

                if (_registry.ProtectedIds.Contains(id))
                {
                    return Rejected(id, "forbidden", $"plugin '{id}' is protected");
                }

                var newVersion = content.Manifest.Version;
                var parsedNewVersion = NuGetVersion.Parse(newVersion);
                var range = versionRange;
                if (string.IsNullOrWhiteSpace(range))
                {
                    range = newVersion;
                }
                else if (!VersionRange.TryParse(range, out var parsedRange) ||
                         !parsedRange.Satisfies(parsedNewVersion))
                {
                    return Rejected(id, "invalid_request",
                        $"version range '{range}' does not satisfy uploaded version '{newVersion}'");
                }

                var installedBefore = await _store.ListAsync(token);
                var oldInstalled = await _store.ResolveAsync(id, declaration.VersionRange, token);

                // An upgrade removes the old resolved version, not the new one.
                // Dependents are unsatisfied only when a dependency range matched
                // a removed version and no surviving version still matches.
                var removedVersions = oldInstalled is not null &&
                                      !string.Equals(oldInstalled.Version, newVersion, StringComparison.Ordinal)
                    ? new[] { NuGetVersion.Parse(oldInstalled.Version) }
                    : Array.Empty<NuGetVersion>();
                var survivingVersions = installedBefore
                    .Where(package => string.Equals(package.Id, id, StringComparison.Ordinal) &&
                                      (oldInstalled is null ||
                                       !string.Equals(package.Version, oldInstalled.Version, StringComparison.Ordinal)))
                    .Select(package => NuGetVersion.Parse(package.Version))
                    .Append(parsedNewVersion)
                    .Distinct()
                    .ToArray();
                // ONE reverse-dependency algorithm across upgrade / uninstall /
                // the store guard — use the already-loaded snapshot, no re-read.
                var dependents = PluginDependencyPlanner.FindUnsatisfiedDependents(
                    installedBefore, id, removedVersions, survivingVersions);
                if (dependents.Count > 0)
                {
                    return Rejected(id, "conflict",
                        $"installed plugins depend on '{id}' and would be unsatisfied: {string.Join(", ", dependents)}");
                }

                // Store.InstallAsync (not UpgradeAsync) adds v2 while v1 stays in
                // place. v1 is removed only after the new declaration is applied.
                var addedPackage = !installedBefore.Any(package =>
                    string.Equals(package.Id, id, StringComparison.Ordinal) &&
                    string.Equals(package.Version, newVersion, StringComparison.Ordinal));
                var storeResult = await _store.InstallAsync(path, token);
                var installedNew = (await _store.ListAsync(token)).FirstOrDefault(package =>
                    string.Equals(package.Id, id, StringComparison.Ordinal) &&
                    string.Equals(package.Version, newVersion, StringComparison.Ordinal));
                if (installedNew is null)
                {
                    if (addedPackage)
                    {
                        await TryUninstallPackageAsync(id, newVersion);
                    }

                    return Rejected(id, "invalid_request",
                        $"installed package '{id}' did not resolve to '{newVersion}'");
                }

                Func<Task>? undoStore = null;
                if (addedPackage)
                {
                    var installedVersion = installedNew.Version;
                    undoStore = () => _store.UninstallExactAsync(id, installedVersion, CancellationToken.None);
                }

                var inverses = Inverses(undoStore);
                var replacement = declaration with { VersionRange = range };
                var updated = application with
                {
                    Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
                };
                var apply = await PersistAndApplyAsync(baseline, updated, token, inverses);
                if (!apply.Success)
                {
                    return Failure(id, apply.Failure!, apply.CompensationError);
                }

                var oldVersion = oldInstalled?.Version;
                if (oldVersion is not null &&
                    !string.Equals(oldVersion, installedNew.Version, StringComparison.Ordinal))
                {
                    try
                    {
                        await _store.UninstallAsync(id, oldVersion, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Removing v1 is part of the transaction: undo v2 and the
                        // declaration before reporting the failure.
                        var compensation = await CompensateAsync(baseline, inverses);
                        return Failure(id, ex, compensation);
                    }
                }

                Interlocked.Increment(ref _revision);
                return Accepted(id, storeResult.Changes);
            }
            finally
            {
                ConsumeUpload(key);
            }
        }, ct);

    public Task<PluginMutationResult> UninstallAsync(
        string id, string? version = null, CancellationToken ct = default) =>
        MutateAsync(id, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            // The UI sends the declaration's versionRange verbatim, which may be
            // an exact version or a NuGet range. Both are accepted here.
            if (version is not null && !VersionRange.TryParse(version, out _))
            {
                return Rejected(id, "invalid_request", $"invalid version '{version}'");
            }

            if (_registry.ProtectedIds.Contains(id))
            {
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var application = await LoadApplicationOrEmptyAsync(token);
            var declaration = application.Plugins.FirstOrDefault(item => item.Id == id);
            var targets = await ResolveUninstallTargetsAsync(id, version, token);
            if (declaration is null && targets.Length == 0)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared or installed");
            }

            // A package can be depended on even when its consumer is not declared;
            // check every installed package, not just the declaration set. Versions
            // that survive this uninstall still satisfy their dependents.
            var allInstalled = await _store.ListAsync(token);
            var targetVersions = targets.Select(package => NuGetVersion.Parse(package.Version)).ToArray();
            var survivingVersions = allInstalled
                .Where(package => package.Id == id &&
                                  !targets.Any(target => string.Equals(
                                      target.Version, package.Version, StringComparison.Ordinal)))
                .Select(package => NuGetVersion.Parse(package.Version)).ToArray();
            var dependents = PluginDependencyPlanner.FindUnsatisfiedDependents(
                allInstalled, id, targetVersions, survivingVersions);
            if (dependents.Count > 0)
            {
                return Rejected(id, "conflict",
                    $"installed plugins depend on '{id}': {string.Join(", ", dependents)}");
            }

            // Even an orphan package has a fiber: reconcile the declaration first
            // so the fiber is retired before its bytes leave the store.
            var updated = application with
            {
                Plugins = application.Plugins.Where(item => item.Id != id).ToArray(),
            };
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            try
            {
                var storeResult = await _store.UninstallAsync(id, version, token);
                Interlocked.Increment(ref _revision);
                return Accepted(id, storeResult.Changes);
            }
            catch (Exception ex)
            {
                var compensation = await CompensateAsync(baseline, []);
                return Failure(id, ex, compensation);
            }
        }, ct);

    public Task<PluginMutationResult> SetEnabledAsync(
        string id, bool enabled, CancellationToken ct = default) =>
        MutateAsync(id, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var application = await LoadApplicationAsync(token);
            var declaration = application.Plugins.FirstOrDefault(item => item.Id == id);
            if (declaration is null)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared");
            }

            if (!enabled && _registry.ProtectedIds.Contains(id))
            {
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var replacement = declaration with { Enabled = enabled };
            var updated = application with
            {
                Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
            };
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, declaration.VersionRange)]);
        }, ct);

    public Task<PluginMutationResult> SetConfigAsync(
        string id, IReadOnlyDictionary<string, object?> config, CancellationToken ct = default) =>
        MutateAsync(id, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (config is null)
            {
                return Rejected(id, "invalid_config", "config is required");
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in config)
            {
                if (string.IsNullOrWhiteSpace(key) || !IsConfigValue(value))
                {
                    return Rejected(id, "invalid_config", $"unsupported config value for '{key}'");
                }

                values[key] = value;
            }

            var application = await LoadApplicationAsync(token);
            var declaration = application.Plugins.FirstOrDefault(item => item.Id == id);
            if (declaration is null)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared");
            }

            if (_registry.ProtectedIds.Contains(id))
            {
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var replacement = declaration with { Config = values };
            var updated = application with
            {
                Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
            };
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, declaration.VersionRange)]);
        }, ct);

    // ---- transaction baseline ----------------------------------------------

    /// <summary>
    /// State captured before a control mutation touches disk or runtime. The
    /// raw manifest bytes, not a re-serialized copy, are the declaration
    /// baseline; store versions and runtime declarations are compared after
    /// compensation so a silent partial rollback is reported instead of hidden.
    /// </summary>
    private sealed record MutationBaseline(
        bool ManifestExisted,
        byte[] ManifestBytes,
        string[] StoreVersions,
        string[] RuntimeFingerprint);

    private async Task<MutationBaseline> CaptureBaselineAsync(CancellationToken ct)
    {
        var existed = File.Exists(_manifestPath);
        var bytes = existed ? await File.ReadAllBytesAsync(_manifestPath, ct) : [];
        var storeVersions = (await _store.ListAsync(ct))
            .Select(package => $"{package.Id}@{package.Version}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new MutationBaseline(existed, bytes, storeVersions, RuntimeFingerprint());
    }

    private string[] RuntimeFingerprint() =>
        _registry.Entries.Values
            .Select(entry => $"{entry.Manifest.Id}:{entry.Manifest.Enabled}:{entry.Fiber?.State}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private async Task<string?> CompensateAsync(
        MutationBaseline baseline, IReadOnlyList<Func<Task>> inverses)
    {
        var errors = new List<string>();

        try
        {
            await RestoreManifestAsync(baseline);
        }
        catch (Exception ex)
        {
            errors.Add($"manifest: {ex.Message}");
        }

        for (var i = inverses.Count - 1; i >= 0; i--)
        {
            try
            {
                await inverses[i]();
            }
            catch (Exception ex)
            {
                errors.Add($"store[{i}]: {ex.Message}");
            }
        }

        try
        {
            await ApplyAllCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            errors.Add($"runtime: {ex.Message}");
        }

        try
        {
            errors.AddRange(await VerifyBaselineAsync(baseline));
        }
        catch (Exception ex)
        {
            errors.Add($"verification: {ex.Message}");
        }

        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private async Task RestoreManifestAsync(MutationBaseline baseline)
    {
        if (!baseline.ManifestExisted)
        {
            if (File.Exists(_manifestPath))
            {
                File.Delete(_manifestPath);
            }

            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_manifestPath))!;
        Directory.CreateDirectory(directory);
        // Never reuse the forward write's .tmp path: a fault there must not
        // disable the inverse as well.
        var temp = Path.Combine(directory,
            $".{Path.GetFileName(_manifestPath)}.rollback-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, baseline.ManifestBytes, CancellationToken.None);
            File.Move(temp, _manifestPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> VerifyBaselineAsync(MutationBaseline baseline)
    {
        var errors = new List<string>();
        var exists = File.Exists(_manifestPath);
        if (exists != baseline.ManifestExisted)
        {
            errors.Add(exists ? "unexpected manifest after rollback" : "manifest missing after rollback");
        }
        else if (exists &&
                 !(await File.ReadAllBytesAsync(_manifestPath, CancellationToken.None))
                     .SequenceEqual(baseline.ManifestBytes))
        {
            errors.Add("manifest bytes differ after rollback");
        }

        var storeVersions = (await _store.ListAsync(CancellationToken.None))
            .Select(package => $"{package.Id}@{package.Version}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!storeVersions.SequenceEqual(baseline.StoreVersions))
        {
            errors.Add("store versions differ after rollback");
        }

        if (!RuntimeFingerprint().SequenceEqual(baseline.RuntimeFingerprint))
        {
            errors.Add("runtime declarations differ after rollback");
        }

        return errors;
    }

    // ---- internals ---------------------------------------------------------

    private async Task<ApplicationManifest> LoadApplicationAsync(CancellationToken ct) =>
        await ApplicationManifest.LoadAsync(_manifestPath, ct);

    private async Task ApplyAllCoreAsync(CancellationToken ct)
    {
        var application = await LoadApplicationAsync(ct);
        var installed = await _store.ListAsync(ct);
        var declaredIds = application.Plugins.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var type in installed
                     .Where(package => declaredIds.Contains(package.Id))
                     .Select(package => package.Manifest.Type)
                     .Distinct(StringComparer.Ordinal))
        {
            if (!_registeredTypes.Add(type))
            {
                continue;
            }

            try
            {
                if (_componentFactory is { } factory)
                {
                    // The host still selects the backend; the control plane
                    // only decorates the resulting component with UI
                    // contributions derived from the installed package.
                    _registry.RegisterComponentFactory(type, manifest =>
                    {
                        var inner = factory(manifest);
                        return _ui is null ? inner : UiPackageBinder.Wrap(inner, manifest);
                    });
                }
                else
                {
                    _registry.RegisterComponentFactory(type, manifest =>
                    {
                        var inner = new ProductPluginComponent(type, _ui);
                        return _ui is null ? inner : UiPackageBinder.Wrap(inner, manifest);
                    });
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already registered", StringComparison.Ordinal))
            {
                // A host-provided factory for this type already exists; keep it.
            }
        }

        // A schema belongs to the package, so runtime installs/upgrades must
        // register it on every apply, not only during host startup. A conflicting
        // document for the same type is rejected by RegisterSchema.
        foreach (var package in installed.Where(package =>
                     declaredIds.Contains(package.Id) && package.Manifest.ConfigSchema is not null))
        {
            _registry.RegisterSchema(package.Manifest.Type, package.Manifest.ConfigSchema!);
        }

        await _loader.ApplyAsync(application, ct);
    }

    private async Task<string> ResolveUploadAsync(string uploadKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadKey);
        ct.ThrowIfCancellationRequested();
        if (_uploads.TryGetValue(uploadKey, out var path) && File.Exists(path))
        {
            return path;
        }

        throw new KeyNotFoundException($"unknown upload '{uploadKey}'");
    }

    private async Task<ApplicationManifest> LoadApplicationOrEmptyAsync(CancellationToken ct)
    {
        try
        {
            return await LoadApplicationAsync(ct);
        }
        catch (FileNotFoundException)
        {
            // An orphan package can be uninstalled before any declaration exists.
            return new ApplicationManifest("gocordis", "0.0.0", _store.HostVersion, []);
        }
    }

    private async Task<InstalledPlugin[]> ResolveUninstallTargetsAsync(
        string id, string? version, CancellationToken ct)
    {
        var candidates = (await _store.ListAsync(ct))
            .Where(package => string.Equals(package.Id, id, StringComparison.Ordinal))
            .ToArray();
        if (version is null)
        {
            return candidates;
        }

        var exact = candidates.Where(package =>
            string.Equals(package.Version, version, StringComparison.Ordinal)).ToArray();
        if (exact.Length > 0)
        {
            return exact;
        }

        if (!VersionRange.TryParse(version, out var range))
        {
            return [];
        }

        return candidates.Where(package =>
            NuGetVersion.TryParse(package.Version, out var parsed) && range.Satisfies(parsed)).ToArray();
    }

    private async Task TryUninstallPackageAsync(string id, string version)
    {
        try
        {
            await _store.UninstallExactAsync(id, version, CancellationToken.None);
        }
        catch
        {
            // Best effort: the package was never exposed through a declaration.
        }
    }

    private void ConsumeUpload(string uploadKey)
    {
        if (_uploads.Remove(uploadKey, out var path))
        {
            TryDeleteFile(path);
        }
    }

    private void CleanStaleUploads()
    {
        var directory = Path.Combine(_store.Root, ".uploads");
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Persists a declaration edit and reconciles it. On failure every inverse
    /// is attempted independently in LIFO order, then all three layers are
    /// verified against the captured baseline. Compensation errors are returned
    /// to the caller instead of being swallowed. Revision is advanced only after
    /// the whole mutation has succeeded.
    /// </summary>
    private async Task<(bool Success, Exception? Failure, string? CompensationError)> PersistAndApplyAsync(
        MutationBaseline baseline,
        ApplicationManifest updated,
        CancellationToken ct,
        IReadOnlyList<Func<Task>>? inverses = null)
    {
        try
        {
            await updated.SaveAsync(_manifestPath, ct);
            await ApplyAllCoreAsync(ct);
            return (true, null, null);
        }
        catch (Exception ex)
        {
            var compensation = await CompensateAsync(baseline, inverses ?? []);
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }

            return (false, ex, compensation);
        }
    }

    private static IReadOnlyList<Func<Task>> Inverses(Func<Task>? inverse) =>
        inverse is null ? [] : [inverse];

    private async Task<PluginMutationResult> MutateAsync(
        string id,
        Func<MutationBaseline, CancellationToken, Task<PluginMutationResult>> operation,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var baseline = await CaptureBaselineAsync(ct);
            return await operation(baseline, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(id, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string StateOf(string id) =>
        _registry.Entries.TryGetValue(id, out var entry)
            ? entry.Fiber?.State.ToString() ?? "disabled"
            : "not_installed";

    private PluginMutationResult Accepted(string id, IEnumerable<PluginChange> changes) =>
        new(true, "ok", null, id, StateOf(id), true, changes.ToArray());

    /// <summary>
    /// Shared CLR contracts are sealed once by the host and have process
    /// lifetime. A runtime install may only reuse an approved name; otherwise
    /// it is rejected with an explicit restart requirement instead of failing
    /// later inside the loader with a generic conflict.
    /// </summary>
    private static IReadOnlyList<string> DeclaredCapabilities(
        IReadOnlyList<string>? wire, CapabilityDirection direction) =>
        PluginManifest.ParseCapabilities(wire)
            .Where(declaration => declaration.Direction == direction)
            .Select(declaration => declaration.ToWireString())
            .ToArray();

    private bool SharedAssembliesApproved(IReadOnlyList<string> names) =>
        _sharedAssemblyApproval is null || names.Count == 0 || _sharedAssemblyApproval(names);

    private static PluginMutationResult Rejected(
        string id, string code, string error, bool persisted = false) =>
        new(false, code, error, id, null, persisted, []);

    private static PluginMutationResult Failure(string id, Exception ex, string? compensationError = null)
    {
        var message = string.IsNullOrWhiteSpace(compensationError)
            ? ex.Message
            : $"{ex.Message}; compensation incomplete: {compensationError}";
        return Rejected(id, CodeFor(ex), message);
    }

    private static string CodeFor(Exception ex) => ex switch
    {
        PluginUploadTooLargeException => "invalid_request",
        InvalidPluginPackageException => "invalid_package",
        InvalidPluginConfigException => "invalid_config",
        ProtectedPluginException => "forbidden",
        KeyNotFoundException => "not_found",
        FileNotFoundException or DirectoryNotFoundException => "not_found",
        ArgumentException or JsonException => "invalid_request",
        IOException or UnauthorizedAccessException => "unavailable",
        _ => "conflict",
    };

    private static bool IsConfigValue(object? value) => value switch
    {
        null or string or bool or char or byte or sbyte or short or ushort or
            int or uint or long or ulong or float or double or decimal or
            DateTime or DateTimeOffset or JsonElement => true,
        IEnumerable<object?> sequence => sequence.All(IsConfigValue),
        IReadOnlyDictionary<string, object?> map => map.Values.All(IsConfigValue),
        _ => false,
    };

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Adapter from an installed package declaration to a real Core fiber. It
/// provides a typed capability and contributes one UI page through the owning
/// activation's reversible effect; it never touches fiber state directly.
/// </summary>
public sealed class ProductPluginComponent : Component
{
    private readonly Key<string> _key = new($"product-plugin-{Guid.NewGuid():N}");
    private readonly string _type;
    private readonly UiContributionRegistry? _ui;

    public ProductPluginComponent(string type, UiContributionRegistry? ui)
    {
        _type = type;
        _ui = ui;
    }

    public override string Name => $"product-plugin:{_type}";

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(_key, _type);
        if (_ui is not null)
        {
            await context.RegisterUiPageAsync(
                _ui,
                ContributionOwner.ForContext(context, _type, Name),
                new UiPageDefinition(
                    $"plugin-{_type}", _type, $"/plugins/{_type}", "generic",
                    Description: "Installed plugin contribution"));
        }

        return null;
    }
}
