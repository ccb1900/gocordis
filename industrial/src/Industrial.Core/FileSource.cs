using System.Globalization;
using System.Security.Cryptography;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>File discovery is driven by the SAME reader broker that parses the
/// files. A Reader plugin extending .json only registers a broker route; both
/// discovery and parsing see it without a Core or designer edit.</summary>
internal static class DiscoveredFilePatterns
{
    public static IReadOnlyList<string> Enumerate(
        string root, Func<IReadOnlyList<string>>? extensions)
    {
        var patterns = (extensions?.Invoke() ?? [".csv", ".xlsx"])
            .Select(extension => "*" + extension.TrimStart('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>Stable technical file identity. Mapping rules and business date
/// parsing never contribute to this key.</summary>
public static class FileIdentityFactory
{
    public static FileIdentity Create(string fullPath, string rootPath, DateTimeOffset? sourceDate = null) =>
        Create(fullPath, rootPath, string.Empty, sourceDate);

    public static FileIdentity Create(
        string fullPath, string rootPath, string resourceId, DateTimeOffset? sourceDate = null)
    {
        var root = Path.GetFullPath(rootPath);
        var full = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        using var stream = File.OpenRead(full);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var info = new FileInfo(full);
        return new FileIdentity(
            $"{resourceId}|{relative}|{info.Length}|{hash}",
            relative,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            hash,
            resourceId);
    }

    public static DateTimeOffset? ParseSourceDate(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length >= 8 && DateTimeOffset.TryParseExact(
                   stem[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>Local filesystem source. UNC shares reuse the Windows SMB client
/// through the resource; no second credential/session implementation.</summary>
public sealed class LocalFileSource : IFileSource
{
    private readonly IResourceRegistry _resources;
    private readonly Func<IReadOnlyList<string>>? _extensions;

    public LocalFileSource(IResourceRegistry resources, Func<IReadOnlyList<string>>? extensions = null)
    {
        _resources = resources;
        _extensions = extensions;
    }

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value)
            ? value?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(root)) return [];
        // Flat mode: the resource root is the file itself (Go's single-file
        // source), not a directory that happens to contain one file.
        if (File.Exists(root))
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(root)) ?? root;
            var identity = FileIdentityFactory.Create(root, parent, resource.Id);
            var flatDate = FileIdentityFactory.ParseSourceDate(Path.GetFileName(root))
                           ?? identity.LastWriteUtc;
            return flatDate < range.FromInclusive || flatDate >= range.ToExclusive
                ? []
                : [new DiscoveredFile(identity, root, flatDate, resource.Id)];
        }
        if (!Directory.Exists(root)) return [];

        var result = new List<DiscoveredFile>();
        var files = DiscoveredFilePatterns.Enumerate(root, _extensions);
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var identity = FileIdentityFactory.Create(path, root, resource.Id);
            var sourceDate = FileIdentityFactory.ParseSourceDate(Path.GetFileName(path)) ?? identity.LastWriteUtc;
            if (sourceDate < range.FromInclusive || sourceDate >= range.ToExclusive)
            {
                continue;
            }

            result.Add(new DiscoveredFile(identity, path, sourceDate, resource.Id));
        }

        return result.OrderBy(file => file.SourceDate).ToArray();
    }

    public async ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default)
    {
        if (!File.Exists(file.FullPath))
        {
            return false;
        }

        var before = new FileInfo(file.FullPath);
        if (quietPeriod > TimeSpan.Zero)
        {
            await Task.Delay(quietPeriod, ct);
        }

        if (!File.Exists(file.FullPath))
        {
            return false;
        }

        var after = new FileInfo(file.FullPath);
        return before.Length == after.Length && before.LastWriteTimeUtc == after.LastWriteTimeUtc;
    }
}
