using System.Text.Json;
using NuGet.Versioning;

namespace Gocordis.Packaging;

public sealed record InstalledPlugin(PluginPackageManifest Manifest, string InstallPath)
{
    public string Id => Manifest.Id;
    public string Version => Manifest.Version;
}

internal sealed record StoreState(List<InstalledPlugin> Plugins)
{
    public static StoreState Empty() => new([]);
}

/// <summary>
/// Directory-backed plugin store. Installation is serialized, staged outside
/// the final location, and the lock file is restored on any failure.
/// </summary>
public sealed class PluginStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private readonly string _hostVersion;

    public PluginStore(string root, string hostVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _hostVersion = hostVersion;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string HostVersion => _hostVersion;

    public async Task<IReadOnlyList<InstalledPlugin>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            return state.Plugins.OrderBy(p => p.Id, StringComparer.Ordinal)
                .ThenBy(p => NuGetVersion.Parse(p.Version)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InstalledPlugin?> ResolveAsync(
        string id, string versionRange, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var range = VersionRange.Parse(versionRange);
        return (await ListAsync(cancellationToken))
            .Where(p => string.Equals(p.Id, id, StringComparison.Ordinal) && range.Satisfies(NuGetVersion.Parse(p.Version)))
            .OrderByDescending(p => NuGetVersion.Parse(p.Version))
            .FirstOrDefault();
    }

    public async Task<PluginStoreResult> InstallAsync(string packagePath, CancellationToken cancellationToken = default) =>
        await InstallCoreAsync(packagePath, upgrade: false, cancellationToken);

    public async Task<PluginStoreResult> UpgradeAsync(string packagePath, CancellationToken cancellationToken = default) =>
        await InstallCoreAsync(packagePath, upgrade: true, cancellationToken);

    public async Task<PluginStoreResult> UninstallAsync(
        string id, string? version = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            var targets = state.Plugins.Where(p =>
                string.Equals(p.Id, id, StringComparison.Ordinal) &&
                (version is null || string.Equals(p.Version, version, StringComparison.Ordinal))).ToArray();
            if (targets.Length == 0)
            {
                return new PluginStoreResult([]);
            }

            var dependents = state.Plugins.Where(p =>
                p.Manifest.DependenciesOrEmpty.Any(d =>
                    string.Equals(d.Id, id, StringComparison.Ordinal) &&
                    targets.Any(t => VersionRange.Parse(d.VersionRange).Satisfies(NuGetVersion.Parse(t.Version)))))
                .Select(p => p.Id).Distinct(StringComparer.Ordinal).ToArray();
            if (dependents.Length > 0)
            {
                throw new InvalidOperationException(
                    $"cannot uninstall '{id}': required by {string.Join(", ", dependents)}");
            }

            var backup = await ReadLockBytesAsync(cancellationToken);
            try
            {
                foreach (var target in targets)
                {
                    var directory = Path.Combine(_root, target.Id, target.Version);
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }

                    state.Plugins.Remove(target);
                }

                await WriteStateAsync(state, cancellationToken);
                return new PluginStoreResult(targets
                    .Select(p => new PluginChange(PluginChangeKind.Removed, p.Id, p.Version)).ToArray());
            }
            catch
            {
                await WriteLockBytesAsync(backup, cancellationToken);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PluginStoreResult> InstallCoreAsync(
        string packagePath, bool upgrade, CancellationToken cancellationToken)
    {
        // Validation happens before taking the gate: a bad package never touches state.
        var content = await PluginPackageReader.ReadAsync(packagePath, _hostVersion, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            CheckDependencies(content.Manifest, state);

            var existing = state.Plugins.FirstOrDefault(p =>
                string.Equals(p.Id, content.Manifest.Id, StringComparison.Ordinal) &&
                string.Equals(p.Version, content.Manifest.Version, StringComparison.Ordinal));
            if (existing is not null && !upgrade)
            {
                return new PluginStoreResult([new PluginChange(PluginChangeKind.Unchanged, existing.Id, existing.Version)]);
            }

            var backup = await ReadLockBytesAsync(cancellationToken);
            var staging = Path.Combine(_root, ".staging", Guid.NewGuid().ToString("N"));
            var finalDirectory = Path.Combine(_root, content.Manifest.Id, content.Manifest.Version);
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var (relative, bytes) in content.Files)
                {
                    var target = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
                if (Directory.Exists(finalDirectory))
                {
                    Directory.Delete(finalDirectory, recursive: true);
                }

                Directory.Move(staging, finalDirectory);

                var installed = new InstalledPlugin(content.Manifest, finalDirectory);
                var changes = new List<PluginChange>();
                if (existing is not null)
                {
                    state.Plugins.Remove(existing);
                    changes.Add(new PluginChange(PluginChangeKind.Updated, existing.Id, existing.Version));
                }

                if (upgrade)
                {
                    var superseded = state.Plugins.Where(p =>
                        string.Equals(p.Id, content.Manifest.Id, StringComparison.Ordinal) &&
                        NuGetVersion.Parse(p.Version) < NuGetVersion.Parse(content.Manifest.Version)).ToArray();
                    foreach (var old in superseded)
                    {
                        var oldDirectory = Path.Combine(_root, old.Id, old.Version);
                        if (Directory.Exists(oldDirectory))
                        {
                            Directory.Delete(oldDirectory, recursive: true);
                        }

                        state.Plugins.Remove(old);
                        changes.Add(new PluginChange(PluginChangeKind.Removed, old.Id, old.Version));
                    }
                }

                state.Plugins.Add(installed);
                changes.Add(new PluginChange(
                    existing is null ? PluginChangeKind.Added : PluginChangeKind.Updated,
                    installed.Id, installed.Version));
                await WriteStateAsync(state, cancellationToken);
                return new PluginStoreResult(changes.Distinct().ToArray());
            }
            catch
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }

                await WriteLockBytesAsync(backup, cancellationToken);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void CheckDependencies(PluginPackageManifest manifest, StoreState state)
    {
        foreach (var dependency in manifest.DependenciesOrEmpty)
        {
            var found = state.Plugins.Any(p =>
                string.Equals(p.Id, dependency.Id, StringComparison.Ordinal) &&
                VersionRange.Parse(dependency.VersionRange).Satisfies(NuGetVersion.Parse(p.Version)));
            if (!found)
            {
                throw new MissingPluginDependencyException(
                    $"plugin '{manifest.Id}' requires '{dependency.Id}' {dependency.VersionRange}");
            }
        }
    }

    private string LockPath => Path.Combine(_root, "installed.json");

    private static async Task<StoreState> ParseStateAsync(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return StoreState.Empty();
        }

        return JsonSerializer.Deserialize<StoreState>(bytes, JsonOptions) ?? StoreState.Empty();
    }

    private async Task<StoreState> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LockPath))
        {
            return StoreState.Empty();
        }

        return await ParseStateAsync(await File.ReadAllBytesAsync(LockPath, cancellationToken));
    }

    private async Task<byte[]> ReadLockBytesAsync(CancellationToken cancellationToken) =>
        File.Exists(LockPath) ? await File.ReadAllBytesAsync(LockPath, cancellationToken) : [];

    private async Task WriteStateAsync(StoreState state, CancellationToken cancellationToken) =>
        await WriteLockBytesAsync(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), cancellationToken);

    private async Task WriteLockBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var temp = LockPath + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        File.Move(temp, LockPath, overwrite: true);
    }
}
