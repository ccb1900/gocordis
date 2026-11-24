using System.Diagnostics;

namespace Gocordis.Runtime.Process;

/// <summary>
/// OS launch policy for Process-backed plugins (paper §6.3). This is NOT a
/// sandbox: the child still runs with the host's credentials and can do
/// anything the host account can do. The policy only constrains HOW a child is
/// launched — no shell, absolute (or PATH-resolved) executable, an explicit
/// working directory and a MINIMAL environment.
/// <para>
/// <see cref="ProcessStartInfo"/> inherits the whole parent environment by
/// default (PATH, proxy settings, cloud credentials, CI tokens). A secret
/// blacklist is never complete, so the default policy clears the child
/// environment and admits only what the host explicitly authorizes: names in
/// <see cref="AllowedEnvironmentVariables"/> (current parent values) and the
/// explicit name/value pairs in <see cref="EnvironmentVariables"/>.
/// </para>
/// </summary>
public sealed record ProcessPermissionPolicy(
    IEnumerable<string>? AllowedEnvironmentVariables = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    string? WorkingDirectory = null,
    bool RequireAbsoluteExecutable = true)
{
    public static ProcessPermissionPolicy Default { get; } = new();

    /// <summary>
    /// The smallest parent-variable allowlist a .NET child needs to start when
    /// the host itself is running on a rolled-forward runtime. Everything else
    /// stays out unless the host explicitly authorizes it.
    /// </summary>
    private static readonly string[] RuntimeEnvironmentVariables =
        ["DOTNET_ROOT", "DOTNET_ROLL_FORWARD"];

    /// <summary>
    /// Alias for hosts that prefer to describe the injected variables as an
    /// explicit dictionary; both names refer to the same name/value map.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExplicitEnvironmentVariables
    {
        get => EnvironmentVariables;
        init => EnvironmentVariables = value;
    }

    /// <summary>Authorize one parent variable by name; its current value is passed through.</summary>
    public ProcessPermissionPolicy AllowEnvironmentVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        if (AllowedEnvironmentVariables is not null)
        {
            foreach (var existing in AllowedEnvironmentVariables)
            {
                allowed.Add(existing);
            }
        }

        allowed.Add(name);
        return this with { AllowedEnvironmentVariables = allowed };
    }

    /// <summary>Inject one explicit value; the parent's value is never consulted.</summary>
    public ProcessPermissionPolicy WithEnvironmentVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        var injected = new Dictionary<string, string>(StringComparer.Ordinal);
        if (EnvironmentVariables is not null)
        {
            foreach (var (existingName, existingValue) in EnvironmentVariables)
            {
                injected[existingName] = existingValue;
            }
        }

        injected[name] = value;
        return this with { EnvironmentVariables = injected };
    }

    public void ApplyTo(ProcessStartInfo start, string executable)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (start.UseShellExecute)
        {
            throw new InvalidOperationException("process plugins must not be launched through a shell");
        }

        if (!Path.IsPathRooted(executable))
        {
            if (!TryResolveOnPath(executable, out var resolved))
            {
                if (RequireAbsoluteExecutable)
                {
                    throw new InvalidOperationException(
                        $"process plugin executable must be an absolute path or resolvable on PATH: '{executable}'");
                }
            }
            else
            {
                // The child no longer inherits PATH. Resolve against the HOST's
                // PATH now and hand the launcher an absolute file name instead.
                start.FileName = resolved;
            }
        }

        // Start from an empty environment: inheritance is the leak, and an
        // incomplete deny-list cannot close it.
        start.Environment.Clear();
        foreach (var name in RuntimeEnvironmentVariables)
        {
            CopyParentValue(start, name);
        }

        if (AllowedEnvironmentVariables is not null)
        {
            foreach (var name in AllowedEnvironmentVariables)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("allowed environment variable names must not be empty");
                }

                CopyParentValue(start, name);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows process creation needs SystemRoot even for a child whose
            // environment is otherwise empty. It is an OS path, not a secret.
            CopyParentValue(start, "SystemRoot");
        }

        if (EnvironmentVariables is { Count: > 0 } injected)
        {
            foreach (var (name, value) in injected)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("injected environment variable names must not be empty");
                }

                // Explicit host values take precedence over inherited ones.
                start.Environment[name] = value;
            }
        }

        if (WorkingDirectory is not null)
        {
            start.WorkingDirectory = WorkingDirectory;
        }
    }

    private static void CopyParentValue(ProcessStartInfo start, string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        if (value is not null)
        {
            start.Environment[name] = value;
        }
    }

    private static bool TryResolveOnPath(string executable, out string resolved)
    {
        var path = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                {
                    resolved = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        resolved = executable;
        return false;
    }
}
