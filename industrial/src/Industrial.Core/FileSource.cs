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
        string root, Func<IReadOnlyList<string>>? extensions, CancellationToken ct = default)
    {
        var patterns = (extensions?.Invoke() ?? [".csv", ".xlsx"])
            .Select(extension => "*" + extension.TrimStart('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
        };
        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(root, pattern, options))
            {
                ct.ThrowIfCancellationRequested();
                files.Add(path);
            }
        }
        // Enumeration order is filesystem-dependent; a full-path tie-breaker
        // makes discovery reproducible across machines and runs.
        return files.Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>Size + last-write-time quiet period shared by discovery and the
/// collection loop. A file is stable only if two observations bracketed by
/// the quiet period agree.</summary>
public static class FileStability
{
    public static async ValueTask<bool> IsStableAsync(
        string path, TimeSpan quietPeriod, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return false;
        var beforeInfo = new FileInfo(path);
        // FileInfo caches lazily; capture the observations now so a write
        // during the quiet period cannot rewrite the "before" snapshot.
        var beforeLength = beforeInfo.Length;
        var beforeWriteUtc = beforeInfo.LastWriteTimeUtc;
        if (quietPeriod > TimeSpan.Zero)
        {
            await Task.Delay(quietPeriod, ct);
        }
        else
        {
            ct.ThrowIfCancellationRequested();
        }

        if (!File.Exists(path)) return false;
        var after = new FileInfo(path);
        return beforeLength == after.Length && beforeWriteUtc == after.LastWriteTimeUtc;
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
    private readonly Action<string>? _afterStabilityCheck;

    public LocalFileSource(
        IResourceRegistry resources,
        Func<IReadOnlyList<string>>? extensions = null,
        Action<string>? afterStabilityCheck = null)
    {
        _resources = resources;
        _extensions = extensions;
        // Optional seam for tests that simulate a write landing between the
        // pre-hash stat gate and the hash; production callers leave it null.
        _afterStabilityCheck = afterStabilityCheck;
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
            var flatDate = CheapSourceDate(root);
            if (flatDate < range.FromInclusive || flatDate >= range.ToExclusive)
            {
                return [];
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(root)) ?? root;
            var identity = await StableIdentityAsync(root, parent, resource.Id, ct);
            return identity is null
                ? []
                : [new DiscoveredFile(identity, root, flatDate, resource.Id)];
        }
        if (!Directory.Exists(root)) return [];

        var result = new List<DiscoveredFile>();
        foreach (var path in DiscoveredFilePatterns.Enumerate(root, _extensions, ct))
        {
            // Prefilter by date/name with one cheap stat before paying for a
            // hash. Business date never enters the technical identity key.
            var sourceDate = CheapSourceDate(path);
            if (sourceDate < range.FromInclusive || sourceDate >= range.ToExclusive)
            {
                continue;
            }

            var identity = await StableIdentityAsync(path, root, resource.Id, ct);
            if (identity is null) continue;
            result.Add(new DiscoveredFile(identity, path, sourceDate, resource.Id));
        }

        return result.OrderBy(file => file.SourceDate)
            .ThenBy(file => file.FullPath, StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<FileIdentity?> StableIdentityAsync(
        string path, string root, string resourceId, CancellationToken ct)
    {
        // Stability comes before hashing: a file that is still being written
        // must not receive a content hash it will not keep.
        if (!await FileStability.IsStableAsync(path, TimeSpan.Zero, ct)) return null;
        _afterStabilityCheck?.Invoke(path);
        FileIdentity identity;
        try
        {
            identity = FileIdentityFactory.Create(path, root, resourceId);
        }
        catch (IOException)
        {
            return null;
        }
        // A write that landed between the stat gate and the hash invalidates
        // the candidate; let the next discovery see the settled file.
        var after = new FileInfo(path);
        return after.Exists && after.Length == identity.Length &&
               after.LastWriteTimeUtc == identity.LastWriteUtc
            ? identity
            : null;
    }

    private static DateTimeOffset CheapSourceDate(string path) =>
        FileIdentityFactory.ParseSourceDate(Path.GetFileName(path))
        ?? new DateTimeOffset(new FileInfo(path).LastWriteTimeUtc);

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        FileStability.IsStableAsync(file.FullPath, quietPeriod, ct);
}

// UNC sources acquire a credentialed SMB session in DiscoverAsync
// (UncConnectionLease.cs). IFileSource exposes no lease handle, so the session
// cannot be kept alive across the caller's stability checks and reads without
// a second session mechanism; those later accesses use the ambient client
// mapping. Do not present UNC reads as session-bound beyond discovery.
