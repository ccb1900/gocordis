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

/// <summary>
/// Minimum operation audit: who initiated a control action and from which
/// surface, what was attempted, and what the control plane decided. Audit is
/// an in-memory operation record, not persisted runtime state.
/// </summary>
public sealed record ControlPlaneAudit(
    long Revision,
    DateTimeOffset Timestamp,
    string Source,
    string Action,
    string Id,
    string Outcome,
    string Code,
    string? Error);

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

    /// <summary>Operation audit is sourced from the one control plane; it is a
    /// read-only projection over in-memory operation records.</summary>
    Task<IReadOnlyList<ControlPlaneAudit>> AuditAsync(CancellationToken ct = default);

    /// <summary>Restores the declaration's last-known-good sidecar. This is the
    /// single recovery entry used by HTTP, CLI and the host's recovery page.</summary>
    Task<PluginMutationResult> RecoverManifestAsync(
        string source = "api", CancellationToken ct = default);

    Task<PluginUploadResult> UploadAsync(
        Stream packageStream, string fileName, CancellationToken ct = default, string source = "api");

    Task<PluginMutationResult> InstallAsync(
        string uploadKey, bool enable = true, CancellationToken ct = default, string source = "api");

    Task<PluginMutationResult> UpgradeAsync(
        string id, string uploadKey, string? versionRange = null, CancellationToken ct = default,
        string source = "api");

    Task<PluginMutationResult> UninstallAsync(
        string id, string? version = null, CancellationToken ct = default, string source = "api");

    Task<PluginMutationResult> SetEnabledAsync(
        string id, bool enabled, CancellationToken ct = default, string source = "api");

    Task<PluginMutationResult> SetConfigAsync(
        string id, IReadOnlyDictionary<string, object?> config, CancellationToken ct = default,
        string source = "api");
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
    private readonly ApplicationLoader _loader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _registeredTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _uploads = new(StringComparer.Ordinal);
    private readonly List<ControlPlaneAudit> _audit = [];
    private readonly object _auditSync = new();
    private long _revision;

    public PluginControlService(
        string ManifestPath,
        PluginStore Store,
        PluginRegistry Registry,
        UiContributionRegistry? Ui = null,
        Func<PluginManifest, Component>? ComponentFactory = null,
        Func<IReadOnlyList<string>, bool>? SharedAssemblyApproval = null,
        Func<IReadOnlyList<string>, Task<IDeclarationTransaction>>? DeclarationPlan = null)
    {
        _manifestPath = ManifestPath ?? throw new ArgumentNullException(nameof(ManifestPath));
        _store = Store ?? throw new ArgumentNullException(nameof(Store));
        _registry = Registry ?? throw new ArgumentNullException(nameof(Registry));
        _ui = Ui;
        // The host owns backend selection; Packaging never references a backend.
        _componentFactory = ComponentFactory;
        _sharedAssemblyApproval = SharedAssemblyApproval;
        _loader = new ApplicationLoader(Store, Registry)
        {
            DeclarationPlan = DeclarationPlan,
        };
        CleanStaleUploads();
    }

    /// <summary>
    /// Host-supplied read-only declaration fingerprint (Platform owns the
    /// catalog; Packaging never references a backend or the catalog directly).
    /// Included in the transaction baseline so a partial catalog rollback is
    /// reported, not silently accepted.
    /// </summary>
    public Func<string>? DeclarationFingerprint { get; init; }

    /// <summary>Host-configured package trust policy; defaults to local
    /// development mode (unsigned packages accepted).</summary>
    public PluginTrustPolicy Trust { get; init; } = PluginTrustPolicy.Development;

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
            var plan = await LoadPlanAsync(ct);
            var entries = new List<PluginControlEntry>(plan.Count);
            foreach (var declaration in plan.Declarations)
            {
                var package = declaration.Installed;
                _registry.Entries.TryGetValue(declaration.Id, out var entry);
                var fiber = entry?.Fiber;
                var component = fiber?.Component;
                var isProtected = _registry.ProtectedIds.Contains(declaration.Id);
                var state = fiber?.State.ToString() ?? (package is null ? "not_installed" : "disabled");
                entries.Add(new PluginControlEntry(
                    declaration.Id,
                    declaration.DisplayName ?? declaration.Id,
                    declaration.Type ?? entry?.Manifest.Type ?? declaration.Id,
                    declaration.Enabled,
                    state,
                    fiber?.Failure?.Message,
                    declaration.VersionRange,
                    declaration.InstalledVersion ?? package?.Version,
                    isProtected,
                    !isProtected,
                    true,
                    declaration.Config,
                    component is null ? Array.Empty<string>() : [component.Name],
                    component is null
                        ? Array.Empty<string>()
                        : component.Provide().Select(key => key.Name).ToArray(),
                    entry?.Manifest.Requires
                    ?? declaration.Dependencies.Select(dep => dep.Id).ToArray(),
                    entry?.Manifest.Provides
                    ?? declaration.Provides,
                    DeclaredCapabilities(
                        entry?.Manifest.CapabilitiesOrEmpty ?? declaration.Capabilities,
                        CapabilityDirection.Require),
                    DeclaredCapabilities(
                        entry?.Manifest.CapabilitiesOrEmpty ?? declaration.Capabilities,
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
            var declared = await LoadPlanOrEmptyAsync(ct);

            return installed.Select(package => new StorePluginEntry(
                package.Id,
                package.Version,
                package.Manifest.DisplayName ?? package.Id,
                package.Manifest.HostVersionRange,
                package.InstallPath,
                package.Manifest.DependenciesOrEmpty,
                package.Manifest.ProvidesOrEmpty,
                package.Manifest.Checksum ?? string.Empty,
                declared.Declarations.Any(declaration =>
                    string.Equals(declaration.PackageId, package.Id, StringComparison.Ordinal) ||
                    string.Equals(declaration.Id, package.Id, StringComparison.Ordinal)),
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
            var declaration = (await LoadPlanOrEmptyAsync(ct)).Find(id);
            // An Entry may name a different package id; the store is keyed by
            // that id while the control surface stays keyed by the plugin id.
            var storeId = declaration?.PackageId ?? id;
            var target = installed.FirstOrDefault(package =>
                string.Equals(package.Id, storeId, StringComparison.Ordinal));
            if (target is null)
            {
                return null;
            }

            var resolved = declaration?.Installed ?? target;

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
                .Where(package => package.Id != storeId &&
                                  package.Manifest.DependenciesOrEmpty.Any(dependency => dependency.Id == storeId))
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

    public Task<IReadOnlyList<ControlPlaneAudit>> AuditAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_auditSync)
        {
            return Task.FromResult<IReadOnlyList<ControlPlaneAudit>>(_audit.ToArray());
        }
    }

    public async Task<PluginMutationResult> RecoverManifestAsync(
        string source = "api", CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Restore writes declarations only; the sidecar survives so a failed
            // apply can be retried. No fiber table or runtime state is persisted.
            var backupPath = _manifestPath + ApplicationManifest.LastKnownGoodSuffix;
            if (!File.Exists(backupPath))
            {
                // Hand-written manifests have no sidecar yet. Plant one from the
                // current valid declaration so recovery is always available;
                // a corrupt primary with no sidecar remains an explicit error.
                var current = await ApplicationManifest.LoadAsync(_manifestPath, ct);
                await current.SaveAsync(_manifestPath, ct);
            }

            await ApplicationManifest.RestoreLastKnownGoodAsync(_manifestPath, ct);
            await ApplyAllCoreAsync(ct);

            Interlocked.Increment(ref _revision);
            var accepted = Accepted("manifest", [new PluginChange(PluginChangeKind.Updated, "manifest", string.Empty)]);
            RecordAudit("recover", "manifest", accepted, source);
            return accepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var rejected = Rejected("manifest", CodeFor(ex), ex.Message);
            RecordAudit("recover", "manifest", rejected, source);
            return rejected;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginUploadResult> UploadAsync(
        Stream packageStream, string fileName, CancellationToken ct = default, string source = "api")
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
                var uploaded = new PluginUploadResult(
                    key,
                    content.Manifest.Id,
                    content.Manifest.Version,
                    content.Manifest.Type,
                    content.Manifest.HostVersionRange,
                    content.Manifest.Checksum ?? string.Empty,
                    content.Manifest.DependenciesOrEmpty);
                RecordAudit("upload", uploaded.Id,
                    new PluginMutationResult(true, "ok", null, uploaded.Id, null, false, []), source);
                return uploaded;
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
        string uploadKey, bool enable = true, CancellationToken ct = default, string source = "api") =>
        MutateAsync("install", uploadKey ?? string.Empty, source, async (baseline, token) =>
        {
            var key = uploadKey ?? string.Empty;
            var path = await ResolveUploadAsync(key, token);
            try
            {
                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
                var id = content.Manifest.Id;
                var version = content.Manifest.Version;
                if (_registry.ProtectedIds.Contains(id))
                {
                    // The control plane is a host contribution, never a package.
                    // Check before any declaration lookup so a missing manifest
                    // entry can never downgrade this to "not_found".
                    return Rejected(id, "forbidden", $"plugin '{id}' is protected");
                }

                if (!SharedAssembliesApproved(content.Manifest.SharedAssembliesOrEmpty))
                {
                    return Rejected(id, "host_restart_required",
                        $"plugin '{id}' declares shared contract(s) " +
                        $"[{string.Join(", ", content.Manifest.SharedAssembliesOrEmpty)}] " +
                        "that the host has not sealed; restart the host to approve a new contract");
                }

                var application = await LoadApplicationAsync(token);
                if ((await ApplicationLoader.ResolveAsync(application, _store, token)).Find(id) is not null)
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
                var storeResult = await _store.InstallAsync(path, token, Trust);

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
        string id, string uploadKey, string? versionRange = null, CancellationToken ct = default,
        string source = "api") =>
        MutateAsync("upgrade", id, source, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (_registry.ProtectedIds.Contains(id))
            {
                // Protection wins over "not declared": the control plane is not
                // a manifest entry and must still return forbidden.
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var key = uploadKey ?? string.Empty;
            var path = await ResolveUploadAsync(key, token);
            try
            {
                var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
                var application = await LoadApplicationAsync(token);
                var effective = (await ApplicationLoader.ResolveAsync(application, _store, token)).Find(id);
                if (effective is null)
                {
                    return Rejected(id, "not_found", $"plugin '{id}' is not declared");
                }

                // Entries may declare a plugin id that differs from the package
                // id; the store and the uploaded package are keyed by PackageId.
                var storeId = effective.PackageId;
                if (!string.Equals(content.Manifest.Id, storeId, StringComparison.Ordinal))
                {
                    return Rejected(id, "invalid_request",
                        $"uploaded package '{content.Manifest.Id}' does not match '{storeId}'");
                }

                if (!SharedAssembliesApproved(content.Manifest.SharedAssembliesOrEmpty))
                {
                    return Rejected(id, "host_restart_required",
                        $"plugin '{id}' declares shared contract(s) " +
                        $"[{string.Join(", ", content.Manifest.SharedAssembliesOrEmpty)}] " +
                        "that the host has not sealed; restart the host to approve a new contract");
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
                var oldInstalled = effective.Installed;

                // An upgrade removes the old resolved version, not the new one.
                // Dependents are unsatisfied only when a dependency range matched
                // a removed version and no surviving version still matches.
                var removedVersions = oldInstalled is not null &&
                                      !string.Equals(oldInstalled.Version, newVersion, StringComparison.Ordinal)
                    ? new[] { NuGetVersion.Parse(oldInstalled.Version) }
                    : Array.Empty<NuGetVersion>();
                var survivingVersions = installedBefore
                    .Where(package => string.Equals(package.Id, storeId, StringComparison.Ordinal) &&
                                      (oldInstalled is null ||
                                       !string.Equals(package.Version, oldInstalled.Version, StringComparison.Ordinal)))
                    .Select(package => NuGetVersion.Parse(package.Version))
                    .Append(parsedNewVersion)
                    .Distinct()
                    .ToArray();
                // ONE reverse-dependency algorithm across upgrade / uninstall /
                // the store guard — use the already-loaded snapshot, no re-read.
                var dependents = PluginDependencyPlanner.FindUnsatisfiedDependents(
                    installedBefore, storeId, removedVersions, survivingVersions);
                if (dependents.Count > 0)
                {
                    return Rejected(id, "conflict",
                        $"installed plugins depend on '{storeId}' and would be unsatisfied: {string.Join(", ", dependents)}");
                }

                // Store.InstallAsync (not UpgradeAsync) adds v2 while v1 stays in
                // place. v1 is removed only after the new declaration is applied.
                var addedPackage = !installedBefore.Any(package =>
                    string.Equals(package.Id, storeId, StringComparison.Ordinal) &&
                    string.Equals(package.Version, newVersion, StringComparison.Ordinal));
                var storeResult = await _store.InstallAsync(path, token, Trust);
                var installedNew = (await _store.ListAsync(token)).FirstOrDefault(package =>
                    string.Equals(package.Id, storeId, StringComparison.Ordinal) &&
                    string.Equals(package.Version, newVersion, StringComparison.Ordinal));
                if (installedNew is null)
                {
                    if (addedPackage)
                    {
                        await TryUninstallPackageAsync(storeId, newVersion);
                    }

                    return Rejected(id, "invalid_request",
                        $"installed package '{storeId}' did not resolve to '{newVersion}'");
                }

                Func<Task>? undoStore = null;
                if (addedPackage)
                {
                    var installedVersion = installedNew.Version;
                    undoStore = () => _store.UninstallExactAsync(storeId, installedVersion, CancellationToken.None);
                }

                var inverses = Inverses(undoStore);
                var updated = await EditAsync(
                    application, effective,
                    plugin => plugin with { VersionRange = range },
                    node => node with { Url = $"{effective.PackageId}@{range}" },
                    token);
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
                        await _store.UninstallAsync(storeId, oldVersion, CancellationToken.None);
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
        string id, string? version = null, CancellationToken ct = default, string source = "api") =>
        MutateAsync("uninstall", id, source, async (baseline, token) =>
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
            var effective = (await ApplicationLoader.ResolveAsync(application, _store, token)).Find(id);
            var storeId = effective?.PackageId ?? id;
            var targets = await ResolveUninstallTargetsAsync(storeId, version, token);
            if (effective is null && targets.Length == 0)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared or installed");
            }

            // A package can be depended on even when its consumer is not declared;
            // check every installed package, not just the declaration set. Versions
            // that survive this uninstall still satisfy their dependents.
            var allInstalled = await _store.ListAsync(token);
            var targetVersions = targets.Select(package => NuGetVersion.Parse(package.Version)).ToArray();
            var survivingVersions = allInstalled
                .Where(package => string.Equals(package.Id, storeId, StringComparison.Ordinal) &&
                                  !targets.Any(target => string.Equals(
                                      target.Version, package.Version, StringComparison.Ordinal)))
                .Select(package => NuGetVersion.Parse(package.Version)).ToArray();
            var dependents = PluginDependencyPlanner.FindUnsatisfiedDependents(
                allInstalled, storeId, targetVersions, survivingVersions);
            if (dependents.Count > 0)
            {
                return Rejected(id, "conflict",
                    $"installed plugins depend on '{storeId}': {string.Join(", ", dependents)}");
            }

            // Even an orphan package has a fiber: reconcile the declaration first
            // so the fiber is retired before its bytes leave the store. An
            // entries-only plugin is removed by clearing its producing node.
            var updated = effective is not null
                ? await RemoveAsync(application, effective, token)
                : application;
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            try
            {
                var storeResult = await _store.UninstallAsync(storeId, version, token);
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
        string id, bool enabled, CancellationToken ct = default, string source = "api") =>
        MutateAsync("set-enabled", id, source, async (baseline, token) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (_registry.ProtectedIds.Contains(id))
            {
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var application = await LoadApplicationAsync(token);
            var effective = (await ApplicationLoader.ResolveAsync(application, _store, token)).Find(id);
            if (effective is null)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared");
            }

            var updated = await EditAsync(
                application, effective,
                plugin => plugin with { Enabled = enabled },
                node => node with { Disabled = !enabled },
                token);
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, effective.VersionRange)]);
        }, ct);

    public Task<PluginMutationResult> SetConfigAsync(
        string id, IReadOnlyDictionary<string, object?> config, CancellationToken ct = default,
        string source = "api") =>
        MutateAsync("set-config", id, source, async (baseline, token) =>
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

            if (_registry.ProtectedIds.Contains(id))
            {
                return Rejected(id, "forbidden", $"plugin '{id}' is protected");
            }

            var application = await LoadApplicationAsync(token);
            var effective = (await ApplicationLoader.ResolveAsync(application, _store, token)).Find(id);
            if (effective is null)
            {
                return Rejected(id, "not_found", $"plugin '{id}' is not declared");
            }

            var updated = await EditAsync(
                application, effective,
                plugin => plugin with { Config = values },
                node => node with { Config = values },
                token);
            var apply = await PersistAndApplyAsync(baseline, updated, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!, apply.CompensationError);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, [new PluginChange(PluginChangeKind.Updated, id, effective.VersionRange)]);
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
        string[] RuntimeFingerprint,
        string? DeclarationFingerprint,
        IReadOnlyDictionary<string, byte[]> DeclarationFiles);

    private static readonly StringComparer DeclarationPathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private async Task<MutationBaseline> CaptureBaselineAsync(CancellationToken ct)
    {
        var existed = File.Exists(_manifestPath);
        var bytes = existed ? await File.ReadAllBytesAsync(_manifestPath, ct) : [];
        var storeVersions = (await _store.ListAsync(ct))
            .Select(package => $"{package.Id}@{package.Version}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new MutationBaseline(
            existed, bytes, storeVersions, RuntimeFingerprint(), ReadDeclarationFingerprint(),
            await CaptureDeclarationFilesAsync(ct));
    }

    /// <summary>Entry mutations may edit an Include file as well as the manifest.
    /// Those bytes are part of the declaration baseline, so compensation can
    /// restore them instead of leaving a half-applied declaration.</summary>
    private async Task<IReadOnlyDictionary<string, byte[]>> CaptureDeclarationFilesAsync(CancellationToken ct)
    {
        var files = new Dictionary<string, byte[]>(DeclarationPathComparer);
        try
        {
            var application = await LoadApplicationAsync(ct);
            foreach (var path in await ApplicationLoader.DeclarationFilesAsync(application, ct))
            {
                if (DeclarationPathComparer.Equals(
                        Path.GetFullPath(path), Path.GetFullPath(_manifestPath)) ||
                    !File.Exists(path))
                {
                    continue;
                }

                files[path] = await File.ReadAllBytesAsync(path, ct);
            }
        }
        catch (FileNotFoundException) { }
        return files;
    }

    private string[] RuntimeFingerprint() =>
        _registry.Entries.Values
            .Select(entry => $"{entry.Manifest.Id}:{entry.Manifest.Enabled}:{entry.Fiber?.State}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private string? ReadDeclarationFingerprint() =>
        DeclarationFingerprint?.Invoke() is { } fingerprint ? fingerprint : null;

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

        try
        {
            await RestoreDeclarationFilesAsync(baseline);
        }
        catch (Exception ex)
        {
            errors.Add($"include files: {ex.Message}");
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

    private static async Task RestoreDeclarationFilesAsync(MutationBaseline baseline)
    {
        foreach (var (path, bytes) in baseline.DeclarationFiles)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory,
                $".{Path.GetFileName(path)}.rollback-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temp, bytes, CancellationToken.None);
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                TryDeleteFile(temp);
                throw;
            }
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

        foreach (var (path, bytes) in baseline.DeclarationFiles)
        {
            if (!File.Exists(path) ||
                !(await File.ReadAllBytesAsync(path, CancellationToken.None)).SequenceEqual(bytes))
            {
                errors.Add($"include file '{path}' differs after rollback");
            }
        }

        if (!RuntimeFingerprint().SequenceEqual(baseline.RuntimeFingerprint))
        {
            errors.Add("runtime declarations differ after rollback");
        }

        if (baseline.DeclarationFingerprint is { } expected &&
            !string.Equals(ReadDeclarationFingerprint(), expected, StringComparison.Ordinal))
        {
            errors.Add("capability declarations differ after rollback");
        }

        return errors;
    }

    // ---- internals ---------------------------------------------------------

    private async Task<ApplicationManifest> LoadApplicationAsync(CancellationToken ct) =>
        await ApplicationManifest.LoadAsync(_manifestPath, ct);

    private async Task<ResolvedDeclarationPlan> LoadPlanAsync(CancellationToken ct) =>
        await ApplicationLoader.ResolveAsync(await LoadApplicationAsync(ct), _store, ct);

    private async Task<ResolvedDeclarationPlan> LoadPlanOrEmptyAsync(CancellationToken ct)
    {
        try
        {
            return await LoadPlanAsync(ct);
        }
        catch (FileNotFoundException)
        {
            return new ResolvedDeclarationPlan([]);
        }
    }

    /// <summary>
    /// Writes a mutation to the authority that declared the plugin. Flat
    /// declarations edit Plugins; entry declarations edit their EntryNode (in
    /// the manifest tree or in the Include file that produced them). Either way
    /// the next ResolveAsync sees exactly one declaration for the id.
    /// </summary>
    private static async Task<ApplicationManifest> EditAsync(
        ApplicationManifest application,
        EffectiveDeclaration declaration,
        Func<ApplicationPlugin, ApplicationPlugin> editFlat,
        Func<EntryNode, EntryNode> editEntry,
        CancellationToken ct) =>
        await ApplicationLoader.EditDeclarationAsync(application, declaration, editFlat, editEntry, ct);

    private static async Task<ApplicationManifest> RemoveAsync(
        ApplicationManifest application, EffectiveDeclaration declaration, CancellationToken ct)
    {
        if (declaration.Source.Origin == DeclarationOrigin.FlatPlugin)
        {
            return application with
            {
                Plugins = application.Plugins
                    .Where(plugin => !string.Equals(plugin.Id, declaration.Id, StringComparison.Ordinal))
                    .ToArray(),
            };
        }

        // An Entry may also be a structural group: clear only the fields that
        // produce a declaration so its children survive the uninstall.
        return await ApplicationLoader.EditDeclarationAsync(
            application, declaration, plugin => plugin,
            node => node with { Url = null, Config = null, Isolate = null, Intercept = null, Disabled = false },
            ct);
    }

    private async Task ApplyAllCoreAsync(CancellationToken ct)
    {
        var application = await LoadApplicationAsync(ct);
        var plan = await ApplicationLoader.ResolveAsync(application, _store, ct);
        // Factories and schemas are registered from the SAME effective plan the
        // loader applies: entries-only plugins must be administrable too.
        foreach (var type in plan.Declarations
                     .Where(declaration => declaration.Installed is not null)
                     .Select(declaration => declaration.Type!)
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
        foreach (var declaration in plan.Declarations.Where(declaration =>
                     declaration.Installed?.Manifest.ConfigSchema is not null))
        {
            _registry.RegisterSchema(declaration.Type!, declaration.Installed!.Manifest.ConfigSchema!);
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
        string action,
        string id,
        string source,
        Func<MutationBaseline, CancellationToken, Task<PluginMutationResult>> operation,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        PluginMutationResult? result = null;
        try
        {
            var baseline = await CaptureBaselineAsync(ct);
            result = await operation(baseline, ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = Failure(id, ex);
            return result;
        }
        finally
        {
            if (result is not null)
            {
                RecordAudit(action, id, result, source);
            }

            _gate.Release();
        }
    }

    /// <summary>One append-only operation record per control attempt. The source
    /// is supplied by the caller (http/cli/host); the host never guesses an
    /// identity from a network address.</summary>
    private void RecordAudit(string action, string id, PluginMutationResult result, string source)
    {
        var who = string.IsNullOrWhiteSpace(source) ? "api" : source;
        lock (_auditSync)
        {
            _audit.Add(new ControlPlaneAudit(
                Interlocked.Read(ref _revision),
                DateTimeOffset.UtcNow,
                who,
                action,
                id,
                result.Accepted ? "accepted" : "rejected",
                result.Code,
                result.Error));
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
        ArgumentException or JsonException or InvalidDataException => "invalid_request",
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
