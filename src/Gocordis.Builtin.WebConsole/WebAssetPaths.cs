using Microsoft.AspNetCore.StaticFiles;

namespace Gocordis.Builtin.WebConsole;

/// <summary>
/// Path guard shared by the React console assets and the plugin client modules.
/// Every segment is resolved in turn; a symlink that points outside the mount
/// is refused just like an explicit ".." segment.
/// </summary>
internal static class WebAssetPaths
{
    private const long MaxFileBytes = 64L << 20;

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> WebExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".cjs", ".css", ".html", ".htm", ".svg",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".webm",
    };

    // Data-ish assets are permitted, but never manifest/config/secret names.
    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".map", ".txt", ".wasm",
    };

    private static readonly HashSet<string> DeniedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".toml", ".yaml", ".yml", ".env", ".key", ".pem", ".pfx", ".p12",
        ".config", ".db", ".sqlite", ".sqlite3", ".sh", ".bat", ".cmd", ".exe",
    };

    private static readonly string[] DeniedNameFragments =
        ["secret", "credential", "password", "private-key", "id_rsa"];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Kestrel decodes the URI once before routing; a second encoded form
    /// (%252e) or an encoded separator must never reach the filesystem.
    /// </summary>
    public static bool ContainsEncodedTraversal(string? rawTarget)
    {
        if (string.IsNullOrEmpty(rawTarget))
        {
            return false;
        }

        foreach (var token in new[] { "%2e", "%2f", "%5c", "%00", "%25" })
        {
            if (rawTarget.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string[] SplitSegments(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return [];
        }

        return relativePath.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Resolves <paramref name="segments"/> under <paramref name="root"/> after
    /// checking every hop for traversal, dotfiles, sensitive names and symlinks.
    /// </summary>
    public static bool TryResolve(string root, IReadOnlyList<string> segments, out string fullPath)
    {
        fullPath = string.Empty;
        if (segments.Count == 0 || !Directory.Exists(root))
        {
            return false;
        }

        var resolvedRoot = ResolvePath(Path.GetFullPath(root));
        var current = resolvedRoot;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Count - 1;
            if (!IsSafeSegment(segment, isLast))
            {
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(current, segment));
            if (!IsUnder(resolvedRoot, candidate))
            {
                return false;
            }

            var resolved = ResolvePath(candidate);
            if (!IsUnder(resolvedRoot, resolved))
            {
                return false;
            }

            current = resolved;
        }

        if (!File.Exists(current) || new FileInfo(current).Length > MaxFileBytes)
        {
            return false;
        }

        fullPath = current;
        return true;
    }

    public static string ContentTypeOf(string path, bool forceJavaScript = false)
    {
        if (forceJavaScript)
        {
            return "text/javascript; charset=utf-8";
        }

        return ContentTypes.TryGetContentType(path, out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    private static bool IsSafeSegment(string segment, bool isFile)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        {
            return false;
        }

        // Dotfiles and dot-directories (.env, .git, .ssh, ...) are never web assets.
        if (segment.StartsWith('.'))
        {
            return false;
        }

        var lower = segment.ToLowerInvariant();
        foreach (var fragment in DeniedNameFragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (lower is "manifest.toml" or "manifest.json" or "appsettings.json")
        {
            return false;
        }

        if (!isFile)
        {
            return true; // directories need no extension
        }

        var extension = Path.GetExtension(segment);
        if (extension.Length == 0 || DeniedExtensions.Contains(extension))
        {
            return false; // extensionless files are typically backend executables
        }

        return WebExtensions.Contains(extension) || DataExtensions.Contains(extension);
    }

    /// <summary>
    /// Resolves every component of an absolute path, so a symlinked ancestor
    /// cannot move the mount point outside its declared boundary.
    /// </summary>
    private static string ResolvePath(string absolutePath)
    {
        var anchor = Path.GetPathRoot(absolutePath) ?? string.Empty;
        var current = anchor;
        var remainder = absolutePath[anchor.Length..];
        foreach (var segment in remainder.Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var target = TryResolveLink(current);
            if (target is not null)
            {
                current = Path.GetFullPath(target);
            }
        }

        return Path.GetFullPath(current);
    }

    private static string? TryResolveLink(string path)
    {
        try
        {
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return target?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUnder(string root, string candidate)
    {
        if (string.Equals(root, candidate, PathComparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }
}
