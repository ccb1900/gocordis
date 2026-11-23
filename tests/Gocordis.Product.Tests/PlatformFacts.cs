using Xunit;

namespace Gocordis.Product.Tests;

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
