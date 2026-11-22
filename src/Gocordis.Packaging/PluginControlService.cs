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
    IReadOnlyList<string> Provides);

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
    private readonly ApplicationLoader _loader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _registeredTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _uploads = new(StringComparer.Ordinal);
    private long _revision;

    public PluginControlService(
        string ManifestPath,
        PluginStore Store,
        PluginRegistry Registry,
        UiContributionRegistry? Ui = null)
    {
        _manifestPath = ManifestPath ?? throw new ArgumentNullException(nameof(ManifestPath));
        _store = Store ?? throw new ArgumentNullException(nameof(Store));
        _registry = Registry ?? throw new ArgumentNullException(nameof(Registry));
        _ui = Ui;
        _loader = new ApplicationLoader(Store, Registry);
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
                    package?.Version,
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
                    ?? Array.Empty<string>()));
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

            var dependencies = new List<PackageDependencyView>();
            var diagnostics = new List<string>();
            foreach (var dependency in target.Manifest.DependenciesOrEmpty)
            {
                var match = installed.FirstOrDefault(package =>
                    package.Id == dependency.Id &&
                    VersionRange.TryParse(dependency.VersionRange, out var range) &&
                    NuGetVersion.TryParse(package.Version, out var version) &&
                    range.Satisfies(version));
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
                target.Version,
                dependencies,
                dependents,
                entry?.Manifest.Requires ?? Array.Empty<string>(),
                entry?.Manifest.Provides ?? target.Manifest.ProvidesOrEmpty,
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
        MutateAsync(uploadKey ?? string.Empty, async token =>
        {
            var key = uploadKey ?? string.Empty;
            var path = await ResolveUploadAsync(key, token);
            try
            {
                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
                var id = content.Manifest.Id;
                var version = content.Manifest.Version;
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
                    undoStore = () => _store.UninstallAsync(id, version, CancellationToken.None);
                }

                var apply = await PersistAndApplyAsync(application, updated, token, undoStore);
                if (!apply.Success)
                {
                    return Failure(id, apply.Failure!);
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
        MutateAsync(id, async token =>
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
                var dependents = await FindInstalledDependentsAsync(id, new[] { parsedNewVersion }, token);
                if (dependents.Length > 0)
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
                    undoStore = () => _store.UninstallAsync(id, installedVersion, CancellationToken.None);
                }

                var replacement = declaration with { VersionRange = range };
                var updated = application with
                {
                    Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
                };
                var apply = await PersistAndApplyAsync(application, updated, token, undoStore);
                if (!apply.Success)
                {
                    return Failure(id, apply.Failure!);
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
                        try
                        {
                            await TryRestoreAsync(application, undoStore);
                        }
                        catch
                        {
                            // The original error is the one worth reporting.
                        }

                        return Failure(id, ex);
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
        MutateAsync(id, async token =>
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
            var dependents = await FindInstalledDependentsAsync(
                id, targetVersions, token, survivingVersions);
            if (dependents.Length > 0)
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
            var apply = await PersistAndApplyAsync(application, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            try
            {
                var storeResult = await _store.UninstallAsync(id, version, token);
                Interlocked.Increment(ref _revision);
                return Accepted(id, storeResult.Changes);
            }
            catch (Exception ex)
            {
                try
                {
                    await TryRestoreAsync(application, null);
                }
                catch
                {
                    // Best effort: report the store failure.
                }

                return Failure(id, ex);
            }
        }, ct);

    public Task<PluginMutationResult> SetEnabledAsync(
        string id, bool enabled, CancellationToken ct = default) =>
        MutateAsync(id, async token =>
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
            var apply = await PersistAndApplyAsync(application, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, declaration.VersionRange)]);
        }, ct);

    public Task<PluginMutationResult> SetConfigAsync(
        string id, IReadOnlyDictionary<string, object?> config, CancellationToken ct = default) =>
        MutateAsync(id, async token =>
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
            var apply = await PersistAndApplyAsync(application, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, declaration.VersionRange)]);
        }, ct);

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
                _registry.RegisterFactory(type, _ => new ProductPluginComponent(type, _ui));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already registered", StringComparison.Ordinal))
            {
                // A host-provided factory for this type already exists; keep it.
            }
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

    private async Task<string[]> FindInstalledDependentsAsync(
        string id,
        IReadOnlyCollection<NuGetVersion> targetVersions,
        CancellationToken ct,
        IReadOnlyCollection<NuGetVersion>? survivingVersions = null)
    {
        if (targetVersions.Count == 0)
        {
            return [];
        }

        var installed = await _store.ListAsync(ct);
        var dependents = new List<string>();
        foreach (var package in installed)
        {
            if (string.Equals(package.Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var dependency in package.Manifest.DependenciesOrEmpty)
            {
                if (!string.Equals(dependency.Id, id, StringComparison.Ordinal) ||
                    !VersionRange.TryParse(dependency.VersionRange, out var range) ||
                    !targetVersions.Any(version => range.Satisfies(version)) ||
                    (survivingVersions is not null && survivingVersions.Any(version => range.Satisfies(version))))
                {
                    continue;
                }

                dependents.Add(package.Id);
                break;
            }
        }

        return dependents.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task TryUninstallPackageAsync(string id, string version)
    {
        try
        {
            await _store.UninstallAsync(id, version, CancellationToken.None);
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
    /// Persists a declaration edit and reconciles it. On failure the previous
    /// declaration and any store change made by this mutation are restored before
    /// the error is reported. Revision is advanced by the caller only after the
    /// whole mutation has succeeded.
    /// </summary>
    private async Task<(bool Success, Exception? Failure)> PersistAndApplyAsync(
        ApplicationManifest previous,
        ApplicationManifest updated,
        CancellationToken ct,
        Func<Task>? undoStore = null)
    {
        try
        {
            await updated.SaveAsync(_manifestPath, ct);
            await ApplyAllCoreAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            try
            {
                await TryRestoreAsync(previous, undoStore);
            }
            catch
            {
                // Best effort: the reported failure is the original one.
            }

            return (false, ex);
        }
    }

    private async Task TryRestoreAsync(ApplicationManifest previous, Func<Task>? undoStore)
    {
        await previous.SaveAsync(_manifestPath, CancellationToken.None);
        if (undoStore is not null)
        {
            await undoStore();
        }

        await ApplyAllCoreAsync(CancellationToken.None);
    }

    private async Task<PluginMutationResult> MutateAsync(
        string id,
        Func<CancellationToken, Task<PluginMutationResult>> operation,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await operation(ct);
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

    private static PluginMutationResult Rejected(
        string id, string code, string error, bool persisted = false) =>
        new(false, code, error, id, null, persisted, []);

    private static PluginMutationResult Failure(string id, Exception ex, bool persisted = false) =>
        Rejected(id, CodeFor(ex), ex.Message, persisted);

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
