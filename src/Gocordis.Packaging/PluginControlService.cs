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
/// Declaration-aware plugin control. It owns no fibers: every lifecycle change
/// is expressed as a manifest edit followed by ApplicationLoader.ApplyAsync, so
/// PluginRegistry remains the single lifecycle authority. All operations are
/// serialized and the declaration store is restored if reconciliation fails.
/// </summary>
public sealed class PluginControlService
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
            // The client-supplied name is a key only; it never becomes a path.
            var key = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("invalid package file name", nameof(fileName));
            }

            var uploadDirectory = Path.Combine(_store.Root, ".uploads");
            Directory.CreateDirectory(uploadDirectory);
            var path = Path.Combine(uploadDirectory, $"{Guid.NewGuid():N}.gocordis-plugin");
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
            var content = await PluginPackageReader.ReadAsync(path, _store.HostVersion, token);
            var id = content.Manifest.Id;
            var application = await LoadApplicationAsync(token);
            if (application.Plugins.Any(declaration => declaration.Id == id))
            {
                return Rejected(id, "conflict", $"plugin '{id}' is already declared");
            }

            var storeResult = await _store.InstallAsync(path, token);
            var updated = application with
            {
                Plugins = [.. application.Plugins,
                    new ApplicationPlugin(id, content.Manifest.Version, enable)],
            };
            await updated.SaveAsync(_manifestPath, token);
            var apply = await ApplyWithRollbackAsync(application, id, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, storeResult.Changes);
        }, ct);

    public Task<PluginMutationResult> UpgradeAsync(
        string id, string uploadKey, string? versionRange = null, CancellationToken ct = default) =>
        MutateAsync(id, async token =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var path = await ResolveUploadAsync(uploadKey, token);
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

            var range = versionRange;
            if (string.IsNullOrWhiteSpace(range))
            {
                range = content.Manifest.Version;
            }
            else if (!VersionRange.TryParse(range, out _))
            {
                return Rejected(id, "invalid_request", $"invalid version range '{range}'");
            }

            var storeResult = await _store.UpgradeAsync(path, token);
            var replacement = declaration with { VersionRange = range };
            var updated = application with
            {
                Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
            };
            await updated.SaveAsync(_manifestPath, token);
            var apply = await ApplyWithRollbackAsync(application, id, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            Interlocked.Increment(ref _revision);
            return Accepted(id, storeResult.Changes);
        }, ct);

    public Task<PluginMutationResult> UninstallAsync(
        string id, string? version = null, CancellationToken ct = default) =>
        MutateAsync(id, async token =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
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

            var dependents = await FindDeclaredDependentsAsync(application, id, version, token);
            if (dependents.Length > 0)
            {
                return Rejected(id, "conflict",
                    $"declared plugins depend on '{id}': {string.Join(", ", dependents)}");
            }

            var updated = application with
            {
                Plugins = application.Plugins.Where(item => item.Id != id).ToArray(),
            };
            await updated.SaveAsync(_manifestPath, token);
            var apply = await ApplyWithRollbackAsync(application, id, token);
            if (!apply.Success)
            {
                return Failure(id, apply.Failure!);
            }

            var storeResult = await _store.UninstallAsync(id, version, token);
            Interlocked.Increment(ref _revision);
            return Accepted(id, storeResult.Changes);
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
            await updated.SaveAsync(_manifestPath, token);
            var apply = await ApplyWithRollbackAsync(application, id, token);
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

            var replacement = declaration with { Config = values };
            var updated = application with
            {
                Plugins = application.Plugins.Select(item => item.Id == id ? replacement : item).ToArray(),
            };
            await updated.SaveAsync(_manifestPath, token);
            var apply = await ApplyWithRollbackAsync(application, id, token);
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

    private async Task<string[]> FindDeclaredDependentsAsync(
        ApplicationManifest application, string id, string? version, CancellationToken ct)
    {
        var installed = await _store.ListAsync(ct);
        NuGetVersion.TryParse(version, out var targetVersion);
        var dependents = new List<string>();
        foreach (var declaration in application.Plugins.Where(item => item.Id != id))
        {
            var package = installed.FirstOrDefault(item => item.Id == declaration.Id);
            var depends = package?.Manifest.DependenciesOrEmpty.Any(dependency =>
                dependency.Id == id &&
                (targetVersion is null ||
                 (VersionRange.TryParse(dependency.VersionRange, out var range) && range.Satisfies(targetVersion))));
            if (depends == true)
            {
                dependents.Add(declaration.Id);
            }
        }

        return dependents.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Reconcile after a persisted declaration edit. On failure the previous
    /// declaration is written back and reapplied before the error is reported.
    /// </summary>
    private async Task<(bool Success, Exception? Failure)> ApplyWithRollbackAsync(
        ApplicationManifest previous, string id, CancellationToken ct)
    {
        try
        {
            await ApplyAllCoreAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            try
            {
                await previous.SaveAsync(_manifestPath, CancellationToken.None);
                await ApplyAllCoreAsync(CancellationToken.None);
            }
            catch
            {
                // Best effort: the reported failure is the original one.
            }

            return (false, ex);
        }
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
