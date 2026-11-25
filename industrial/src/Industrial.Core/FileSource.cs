using System.Globalization;
using System.Security.Cryptography;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Stable technical file identity. Mapping rules and business date
/// parsing never contribute to this key.</summary>
public static class FileIdentityFactory
{
    public static FileIdentity Create(string fullPath, string rootPath, DateTimeOffset? sourceDate = null)
    {
        var root = Path.GetFullPath(rootPath);
        var full = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        using var stream = File.OpenRead(full);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var info = new FileInfo(full);
        return new FileIdentity(
            $"{relative}|{info.Length}|{hash}",
            relative,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            hash);
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

    public LocalFileSource(IResourceRegistry resources) => _resources = resources;

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value)
            ? value?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var result = new List<DiscoveredFile>();
        var files = Directory.EnumerateFiles(root, "*.csv", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.xlsx", SearchOption.AllDirectories));
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var identity = FileIdentityFactory.Create(path, root);
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
