using NuGet.Versioning;

namespace Gocordis.Packaging;

/// <summary>
/// Resolution boundary for package locations. A remote registry can implement
/// this later without changing PluginStore, ApplicationLoader or the CLI.
/// </summary>
public interface IPluginPackageSource
{
    Task<string> ResolveAsync(string id, string versionRange, CancellationToken cancellationToken = default);
}

/// <summary>Immutable local directory source for .gocordis-plugin files.</summary>
public sealed class LocalDirectoryPackageSource : IPluginPackageSource
{
    private readonly string _directory;
    private readonly string _hostVersion;

    public LocalDirectoryPackageSource(string directory, string hostVersion)
    {
        _directory = Path.GetFullPath(directory);
        _hostVersion = hostVersion;
    }

    public async Task<string> ResolveAsync(string id, string versionRange, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var range = VersionRange.Parse(versionRange);
        if (!Directory.Exists(_directory))
        {
            throw new FileNotFoundException($"package source directory not found: {_directory}");
        }

        var matches = new List<(NuGetVersion Version, string Path)>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.gocordis-plugin", SearchOption.TopDirectoryOnly))
        {
            PluginPackageManifest manifest;
            try
            {
                manifest = (await PluginPackageReader.ReadAsync(path, _hostVersion, cancellationToken)).Manifest;
            }
            catch (InvalidPluginPackageException)
            {
                continue; // an unrelated or corrupt candidate is not a resolution error
            }

            if (string.Equals(manifest.Id, id, StringComparison.Ordinal) &&
                NuGetVersion.TryParse(manifest.Version, out var version) && range.Satisfies(version))
            {
                matches.Add((version, path));
            }
        }

        if (matches.Count == 0)
        {
            throw new MissingPluginDependencyException($"no local package matches '{id}' {versionRange}");
        }

        return matches.OrderByDescending(m => m.Version).First().Path;
    }
}
