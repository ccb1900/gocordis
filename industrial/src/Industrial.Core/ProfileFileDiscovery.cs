using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Shared discovery order for local and UNC sources. The profile's
/// date-directory/date-filename rules decide the business date BEFORE the
/// caller's date range is applied, so late files with stale mtimes remain
/// discoverable.</summary>
internal static class ProfileFileDiscovery
{
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(200);

    public static async ValueTask<IReadOnlyList<DiscoveredFile>> FindAsync(
        string root,
        string resourceId,
        Func<IReadOnlyList<string>>? extensions,
        SourceDateRange range,
        FileProfileDefinition? profile,
        IReadOnlySet<DateOnly>? businessDates,
        TimeSpan quietPeriod,
        Action<string>? afterQuietPeriod,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(root)) return [];
        if (File.Exists(root))
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(root)) ?? root;
            var date = FileProfileDiscovery.BusinessDateFor(root, parent, profile);
            var sourceDate = BusinessDate(date, root);
            if (!Included(sourceDate, businessDates, range)) return [];
            if (profile is not null && !FileProfileDiscovery.MatchesPath(profile, root, parent)) return [];
            var identity = await FileStability.CreateStableIdentityAsync(
                root, parent, resourceId, quietPeriod, afterQuietPeriod, ct);
            return identity is null
                ? []
                : [new DiscoveredFile(identity, root, sourceDate, resourceId)];
        }
        if (!Directory.Exists(root)) return [];

        var result = new List<DiscoveredFile>();
        foreach (var path in DiscoveredFilePatterns.Enumerate(root, extensions, ct))
        {
            var date = FileProfileDiscovery.BusinessDateFor(path, root, profile);
            var sourceDate = BusinessDate(date, path);
            if (!Included(sourceDate, businessDates, range)) continue;
            if (profile is not null && !FileProfileDiscovery.MatchesPath(profile, path, root)) continue;
            var identity = await FileStability.CreateStableIdentityAsync(
                path, root, resourceId, quietPeriod, afterQuietPeriod, ct);
            if (identity is null) continue;
            result.Add(new DiscoveredFile(identity, path, sourceDate, resourceId));
        }
        return result.OrderBy(file => file.SourceDate)
            .ThenBy(file => file.FullPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static DateTimeOffset BusinessDate(DateOnly? date, string path) =>
        date is { } parsed
            ? new DateTimeOffset(parsed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            : FileIdentityFactory.ParseSourceDate(Path.GetFileName(path))
              ?? new DateTimeOffset(new FileInfo(path).LastWriteTimeUtc);

    private static bool Included(
        DateTimeOffset sourceDate, IReadOnlySet<DateOnly>? businessDates, SourceDateRange range)
    {
        if (businessDates is not null)
            return businessDates.Contains(DateOnly.FromDateTime(sourceDate.UtcDateTime));
        return sourceDate >= range.FromInclusive && sourceDate < range.ToExclusive;
    }
}
