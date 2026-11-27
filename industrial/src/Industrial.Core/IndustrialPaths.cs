namespace Industrial.Core;

/// <summary>Single root resolution/normalization for every industrial
/// persistence component (ledger, stages, Elsa instances). Production must
/// never silently fall back to a temporary directory.</summary>
public static class IndustrialPaths
{
    public static string NormalizeRoot(string root, bool allowDevelopmentKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.GetFullPath(root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var temporary = full.StartsWith(temp, StringComparison.Ordinal);
        var optedIn = allowDevelopmentKey || string.Equals(
            Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_TEMP_ROOT"),
            "true", StringComparison.OrdinalIgnoreCase);
        if (temporary && !optedIn)
            throw new InvalidOperationException(
                "GOCORDIS_INDUSTRIAL_ROOT must point at a persistent directory; " +
                "temporary roots are only allowed for development/test hosts");
        return full;
    }
}
