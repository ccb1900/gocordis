using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Explicit platform scoping: a skipped test is REPORTED as skipped, never
/// counted as a silent pass (review P1-06: bare `return` masquerades as green
/// on the excluded platform).
/// </summary>
public sealed class PosixOnlyFactAttribute : FactAttribute
{
    public PosixOnlyFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "POSIX-only fixture (process/permission); Windows parity is tracked in docs/migration/acceptance-baseline.md";
        }
    }
}

/// <summary>Author-side executable marking that is safe on every platform.</summary>
public static class PlatformFiles
{
    public static void MarkExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}

public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only fixture; parity is tracked in docs/migration/acceptance-baseline.md";
        }
    }
}
