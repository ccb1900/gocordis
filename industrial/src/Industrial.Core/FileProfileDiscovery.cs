using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>One discovery filter shared by the source activity and the
/// binding preview. Date-directory and date-filename layouts use the small
/// token vocabulary already used by industrial operators: yyyy/yy/MM/dd.
/// </summary>
public static class FileProfileDiscovery
{
    // Canonical lowercase vocabulary; token matching is case-insensitive so
    // it agrees with the generated RegexOptions.IgnoreCase patterns.
    private static readonly string[] Tokens =
        ["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMM", "yyyy", "yy", "MM", "dd"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<DiscoveredFile> Apply(
        FileProfileDefinition? profile, IEnumerable<DiscoveredFile> files, string root)
    {
        if (profile is null) return files.ToArray();
        var rootPath = NormalizeRoot(root);
        return files.Where(file => MatchesPath(profile, file.FullPath, rootPath))
            .Select(file => WithBusinessDate(file, profile, rootPath)).ToArray();
    }

    public static string NormalizeRoot(string root) => File.Exists(root)
        ? Path.GetDirectoryName(Path.GetFullPath(root)) ?? Path.GetFullPath(root)
        : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>Name/directory layout gate shared by discovery and preview.</summary>
    public static bool MatchesPath(FileProfileDefinition profile, string fullPath, string root)
    {
        var rootPath = NormalizeRoot(root);
        var relative = Path.GetRelativePath(rootPath, Path.GetFullPath(fullPath)).Replace('\\', '/');
        var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileName(relative);
        if (profile.Flat && !string.IsNullOrEmpty(directory)) return false;
        var namePattern = Glob(profile.FileNamePattern);
        var directoryPattern = DatePatternRegex(profile.DirectoryPattern);
        var datePattern = DatePatternRegex(profile.DatePattern);
        if (namePattern is not null && !namePattern.IsMatch(name)) return false;
        if (directoryPattern is not null && !directoryPattern.IsMatch(directory)) return false;
        if (datePattern is not null && !datePattern.IsMatch(name)) return false;
        return true;
    }

    /// <summary>The date encoded in the path wins over mtime. This is the
    /// single authority used by source discovery, before any range filter.</summary>
    public static DateOnly? BusinessDateFor(
        string fullPath, string root, FileProfileDefinition? profile)
    {
        var rootPath = NormalizeRoot(root);
        var relative = Path.GetRelativePath(rootPath, Path.GetFullPath(fullPath)).Replace('\\', '/');
        var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileName(relative);
        if (profile is not null)
        {
            var date = ParseBusinessDate(name, profile.DatePattern)
                       ?? ParseBusinessDate(directory, profile.DirectoryPattern);
            if (date.HasValue) return date;
        }
        return FileIdentityFactory.ParseSourceDate(name) is { } parsed
            ? DateOnly.FromDateTime(parsed.UtcDateTime)
            : null;
    }

    /// <summary>The date encoded in a path is stronger evidence than mtime:
    /// late files and backfills carry their business date in the directory or
    /// filename even when the file was written much later.</summary>
    private static DiscoveredFile WithBusinessDate(
        DiscoveredFile file, FileProfileDefinition profile, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(profile.DirectoryPattern) &&
            string.IsNullOrWhiteSpace(profile.DatePattern)) return file;
        var date = BusinessDateFor(file.FullPath, rootPath, profile);
        return date is null ? file : file with
        {
            SourceDate = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
    }

    public static DateOnly? ParseBusinessDate(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var match = DatePatternRegex(pattern)?.Match(text);
        if (match is null || !match.Success) return null;
        var groups = match.Groups;
        var year = 2000;
        if (groups["year"].Success &&
            int.TryParse(groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear))
        {
            // Two-digit operator tokens pivot around 69: 25 means 2025, 99
            // means 1999. Four-digit tokens stay literal.
            year = groups["year"].Value.Length != 2
                ? parsedYear
                : parsedYear >= 69 ? 1900 + parsedYear : 2000 + parsedYear;
        }
        var month = groups["month"].Success ? int.Parse(groups["month"].Value, CultureInfo.InvariantCulture) : 1;
        var day = groups["day"].Success ? int.Parse(groups["day"].Value, CultureInfo.InvariantCulture) : 1;
        return DateOnly.TryParseExact($"{year:0000}-{month:00}-{day:00}", "yyyy-MM-dd", out var date)
            ? date : null;
    }

    private static Regex? Glob(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*") return null;
        var builder = new StringBuilder("^");
        foreach (var character in pattern!)
        {
            builder.Append(character switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(character.ToString()),
            });
        }
        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase, RegexTimeout);
    }

    private static Regex? DatePatternRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern!.Length;)
        {
            var token = Tokens.FirstOrDefault(candidate =>
                string.Compare(pattern, index, candidate, 0, candidate.Length,
                    StringComparison.OrdinalIgnoreCase) == 0);
            if (token is null)
            {
                builder.Append(pattern[index] == '*'
                    ? ".*" : Regex.Escape(pattern[index].ToString()));
                index++;
                continue;
            }
            // Switch on the canonical spelling: an operator may write YYYY,
            // Yy, DD or dd without changing the generated group semantics.
            builder.Append(token.ToLowerInvariant() switch
            {
                "yyyymmdd" => "(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})",
                "yyyy-mm-dd" => "(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})",
                "yyyy/mm/dd" => "(?<year>\\d{4})/(?<month>\\d{2})/(?<day>\\d{2})",
                "yyyymm" => "(?<year>\\d{4})(?<month>\\d{2})",
                "yyyy" => "(?<year>\\d{4})",
                "yy" => "(?<year>\\d{2})",
                "mm" => "(?<month>\\d{2})",
                "dd" => "(?<day>\\d{2})",
                _ => "(?<day>\\d{2})",
            });
            index += token.Length;
        }
        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase, RegexTimeout);
    }
}
