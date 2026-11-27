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
    private static readonly string[] Tokens =
        ["yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMM", "yyyy", "YYYY", "yy", "YY", "MM", "dd", "DD"];

    public static IReadOnlyList<DiscoveredFile> Apply(
        FileProfileDefinition? profile, IEnumerable<DiscoveredFile> files, string root)
    {
        if (profile is null) return files.ToArray();
        var rootPath = File.Exists(root)
            ? Path.GetDirectoryName(Path.GetFullPath(root)) ?? Path.GetFullPath(root)
            : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var namePattern = Glob(profile.FileNamePattern);
        var directoryPattern = DatePatternRegex(profile.DirectoryPattern);
        var datePattern = DatePatternRegex(profile.DatePattern);
        return files.Where(file =>
        {
            var full = Path.GetFullPath(file.FullPath);
            var relative = Path.GetRelativePath(rootPath, full).Replace('\\', '/');
            var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
            var name = Path.GetFileName(relative);
            if (profile.Flat && !string.IsNullOrEmpty(directory)) return false;
            if (namePattern is not null && !namePattern.IsMatch(name)) return false;
            if (directoryPattern is not null && !directoryPattern.IsMatch(directory)) return false;
            if (datePattern is not null && !datePattern.IsMatch(name)) return false;
            return true;
        }).Select(file => WithBusinessDate(file, profile, rootPath)).ToArray();
    }

    /// <summary>The date encoded in a path is stronger evidence than mtime:
    /// late files and backfills carry their business date in the directory or
    /// filename even when the file was written much later.</summary>
    private static DiscoveredFile WithBusinessDate(
        DiscoveredFile file, FileProfileDefinition profile, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(profile.DirectoryPattern) &&
            string.IsNullOrWhiteSpace(profile.DatePattern)) return file;
        var relative = Path.GetRelativePath(rootPath, Path.GetFullPath(file.FullPath))
            .Replace('\\', '/');
        var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
        var name = Path.GetFileName(relative);
        var date = ParseBusinessDate(name, profile.DatePattern)
                   ?? ParseBusinessDate(directory, profile.DirectoryPattern);
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
        var year = groups["year"].Success ? int.Parse(groups["year"].Value) : 2000;
        var month = groups["month"].Success ? int.Parse(groups["month"].Value) : 1;
        var day = groups["day"].Success ? int.Parse(groups["day"].Value) : 1;
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
        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase);
    }

    private static Regex? DatePatternRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern!.Length;)
        {
            var token = Tokens.FirstOrDefault(candidate =>
                string.CompareOrdinal(pattern, index, candidate, 0, candidate.Length) == 0);
            if (token is null)
            {
                builder.Append(pattern[index] == '*'
                    ? ".*" : Regex.Escape(pattern[index].ToString()));
                index++;
                continue;
            }
            builder.Append(token switch
            {
                "yyyyMMdd" => "(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})",
                "yyyy-MM-dd" => "(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})",
                "yyyy/MM/dd" => "(?<year>\\d{4})/(?<month>\\d{2})/(?<day>\\d{2})",
                "yyyyMM" => "(?<year>\\d{4})(?<month>\\d{2})",
                "yyyy" or "YYYY" => "(?<year>\\d{4})",
                "yy" or "YY" => "(?<year>\\d{2})",
                "MM" => "(?<month>\\d{2})",
                "dd" or "DD" => "(?<day>\\d{2})",
                _ => "(?<day>\\d{2})",
            });
            index += token.Length;
        }
        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase);
    }
}
