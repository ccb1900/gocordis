using NuGet.Versioning;

namespace Gocordis.Packaging;

/// <summary>
/// Shared reverse-dependency decision for every upgrade/uninstall entry point.
/// A dependent is only broken when its range matched a version that will be
/// removed and no version surviving the mutation still matches it.
/// </summary>
public static class PluginDependencyPlanner
{
    public static IReadOnlyList<string> FindUnsatisfiedDependents(
        IReadOnlyList<InstalledPlugin> installed,
        string id,
        IReadOnlyCollection<NuGetVersion> removedVersions,
        IReadOnlyCollection<NuGetVersion> survivingVersions)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (removedVersions.Count == 0)
        {
            return [];
        }

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
                    !removedVersions.Any(range.Satisfies) ||
                    survivingVersions.Any(range.Satisfies))
                {
                    continue;
                }

                dependents.Add(package.Id);
                break;
            }
        }

        return dependents.Distinct(StringComparer.Ordinal).ToArray();
    }
}
