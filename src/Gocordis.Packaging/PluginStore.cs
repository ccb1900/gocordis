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

internal sealed record DirectoryMove(string Source, string Backup);

/// <summary>
/// Directory-backed plugin store. Installation is serialized; every mutation
/// uses staging + backup directories so a failed commit restores both bytes
/// and lock state.
/// </summary>
public sealed class PluginStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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

    public Task<PluginStoreResult> UninstallAsync(
        string id, string? version = null, CancellationToken cancellationToken = default) =>
        UninstallCoreAsync(id, version, cancellationToken, exactOnly: false, skipDependents: false);

    /// <summary>
    /// Compensation-only inverse: removes one exact version without repeating
    /// the forward dependency check that rejected the original mutation.
    /// </summary>
    internal Task<PluginStoreResult> UninstallExactAsync(
        string id, string version, CancellationToken cancellationToken = default) =>
        UninstallCoreAsync(id, version, cancellationToken, exactOnly: true, skipDependents: true);

    private async Task<PluginStoreResult> UninstallCoreAsync(
        string id,
        string? version,
        CancellationToken cancellationToken,
        bool exactOnly,
        bool skipDependents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            var candidates = state.Plugins.Where(p =>
                string.Equals(p.Id, id, StringComparison.Ordinal)).ToArray();
            InstalledPlugin[] targets;
            if (version is null)
            {
                targets = candidates;
            }
            else
            {
                // Exact versions keep their old semantics; the UI also sends the
                // declaration's version range, which is resolved here instead.
                var exact = candidates.Where(p =>
                    string.Equals(p.Version, version, StringComparison.Ordinal)).ToArray();
                if (exact.Length > 0)
                {
                    targets = exact;
                }
                else if (!exactOnly && VersionRange.TryParse(version, out var range))
                {
                    targets = candidates.Where(p =>
                        NuGetVersion.TryParse(p.Version, out var parsed) && range.Satisfies(parsed)).ToArray();
                }
                else
                {
                    targets = [];
                }
            }

            if (targets.Length == 0)
            {
                return new PluginStoreResult([]);
            }

            // Dependents are still satisfied when another installed version of the
            // same plugin remains within their declared range. Compensation skips
            // this forward check so an inverse can always remove what it added.
            if (!skipDependents)
            {
                var survivors = state.Plugins.Where(p =>
                    !targets.Contains(p) && string.Equals(p.Id, id, StringComparison.Ordinal)).ToArray();
                var dependents = state.Plugins.Where(p => !targets.Contains(p))
                    .Where(p => p.Manifest.DependenciesOrEmpty.Any(d =>
                        string.Equals(d.Id, id, StringComparison.Ordinal) &&
                        targets.Any(t => VersionRange.Parse(d.VersionRange).Satisfies(NuGetVersion.Parse(t.Version))) &&
                        !survivors.Any(s => VersionRange.Parse(d.VersionRange).Satisfies(NuGetVersion.Parse(s.Version)))))
                    .Select(p => p.Id).Distinct(StringComparer.Ordinal).ToArray();
                if (dependents.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"cannot uninstall '{id}': required by {string.Join(", ", dependents)}");
                }
            }

            var backup = await ReadLockBytesAsync(cancellationToken);
            var transaction = Guid.NewGuid().ToString("N");
            var backupRoot = EnsureUnderRoot(_root, ".staging", transaction + "-backup");
            Directory.CreateDirectory(backupRoot);
            var moves = new List<DirectoryMove>();

            try
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    var source = EnsureUnderRoot(_root, targets[i].Id, targets[i].Version);
                    var destination = Path.Combine(backupRoot, i.ToString());
                    if (Directory.Exists(source))
                    {
                        Directory.Move(source, destination);
                        moves.Add(new DirectoryMove(source, destination));
                    }

                    state.Plugins.Remove(targets[i]);
                }

                await WriteStateAsync(state, cancellationToken);
                Directory.Delete(backupRoot, recursive: true);
                return new PluginStoreResult(targets
                    .Select(p => new PluginChange(PluginChangeKind.Removed, p.Id, p.Version)).ToArray());
            }
            catch
            {
                RestoreDirectories(moves);
                SafeDeleteDirectory(backupRoot);
                await WriteLockBytesAsync(backup, CancellationToken.None);
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
            var transaction = Guid.NewGuid().ToString("N");
            var staging = EnsureUnderRoot(_root, ".staging", transaction);
            var backupRoot = EnsureUnderRoot(_root, ".staging", transaction + "-backup");
            var finalDirectory = EnsureUnderRoot(_root, content.Manifest.Id, content.Manifest.Version);
            var moves = new List<DirectoryMove>();
            var finalMoved = false;

            try
            {
                Directory.CreateDirectory(staging);
                foreach (var (relative, bytes) in content.Files)
                {
                    PluginPackageWriter.EnsureSafeRelativePath(relative);
                    var target = EnsureUnderRoot(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                }

                Directory.CreateDirectory(backupRoot);
                var affected = new List<InstalledPlugin>();
                if (existing is not null)
                {
                    affected.Add(existing);
                }

                if (upgrade)
                {
                    affected.AddRange(state.Plugins.Where(p =>
                        string.Equals(p.Id, content.Manifest.Id, StringComparison.Ordinal) &&
                        NuGetVersion.Parse(p.Version) < NuGetVersion.Parse(content.Manifest.Version)));
                }

                var index = 0;
                foreach (var old in affected.Distinct())
                {
                    var source = EnsureUnderRoot(_root, old.Id, old.Version);
                    var destination = Path.Combine(backupRoot, index.ToString());
                    if (Directory.Exists(source))
                    {
                        Directory.Move(source, destination);
                        moves.Add(new DirectoryMove(source, destination));
                        index++;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
                Directory.Move(staging, finalDirectory);
                finalMoved = true;

                var changes = new List<PluginChange>();
                foreach (var old in affected.Distinct())
                {
                    state.Plugins.Remove(old);
                    changes.Add(new PluginChange(PluginChangeKind.Removed, old.Id, old.Version));
                }

                if (existing is not null)
                {
                    changes.Add(new PluginChange(PluginChangeKind.Updated, existing.Id, existing.Version));
                }

                var installed = new InstalledPlugin(content.Manifest, finalDirectory);
                state.Plugins.Add(installed);
                changes.Add(new PluginChange(existing is null ? PluginChangeKind.Added : PluginChangeKind.Updated,
                    installed.Id, installed.Version));

                await WriteStateAsync(state, cancellationToken);
                Directory.Delete(backupRoot, recursive: true);
                SafeDeleteDirectory(staging);
                return new PluginStoreResult(changes.Distinct().ToArray());
            }
            catch
            {
                if (finalMoved)
                {
                    SafeDeleteDirectory(finalDirectory);
                }

                RestoreDirectories(moves);
                SafeDeleteDirectory(backupRoot);
                SafeDeleteDirectory(staging);
                await WriteLockBytesAsync(backup, CancellationToken.None);
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

    private string EnsureUnderRoot(string root, params string[] segments)
    {
        var combined = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(normalizedRoot, PathComparison))
        {
            throw new InvalidOperationException($"path '{combined}' escapes store root '{root}'");
        }

        return combined;
    }

    private static void RestoreDirectories(IEnumerable<DirectoryMove> moves)
    {
        foreach (var move in moves.Reverse())
        {
            SafeDeleteDirectory(move.Source);
            if (Directory.Exists(move.Backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Source)!);
                Directory.Move(move.Backup, move.Source);
            }
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
