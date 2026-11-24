using System.Diagnostics;

namespace Gocordis.Runtime.Process;

/// <summary>
/// OS launch policy for Process-backed plugins (paper §6.3). This is NOT a
/// sandbox: the child still runs with the host's credentials. The policy only
/// constrains HOW a child is launched — no shell, absolute executable, scrubbed
/// secret environment variables and an explicit working directory.
/// </summary>
public sealed record ProcessPermissionPolicy(
    IReadOnlySet<string>? DeniedEnvironmentVariables = null,
    string? WorkingDirectory = null,
    bool RequireAbsoluteExecutable = true)
{
    public static ProcessPermissionPolicy Default { get; } = new();

    private static bool IsOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory, executable)))
            {
                return true;
            }
        }

        return false;
    }

    public void ApplyTo(ProcessStartInfo start, string executable)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (RequireAbsoluteExecutable && !Path.IsPathRooted(executable) && !IsOnPath(executable))
        {
            throw new InvalidOperationException(
                $"process plugin executable must be an absolute path or resolvable on PATH: '{executable}'");
        }

        if (start.UseShellExecute)
        {
            throw new InvalidOperationException("process plugins must not be launched through a shell");
        }

        if (DeniedEnvironmentVariables is { } denied)
        {
            foreach (var name in denied)
            {
                start.Environment.Remove(name);
            }
        }

        if (WorkingDirectory is not null)
        {
            start.WorkingDirectory = WorkingDirectory;
        }
    }
}
