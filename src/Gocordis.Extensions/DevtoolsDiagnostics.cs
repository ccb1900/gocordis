using Gocordis.Plugins;

namespace Gocordis.Extensions;

/// <summary>Seriousness of a diagnostic finding.</summary>
public enum DiagnosticSeverity
{
    Error,
    Warn,
    Info,
}

/// <summary>One stable, machine-readable finding.</summary>
public sealed record Diagnostic(DiagnosticSeverity Severity, string Code, string Message)
{
    public override string ToString() => $"[{Severity.ToString().ToLowerInvariant()}] {Code}: {Message}";
}

/// <summary>Stable diagnostic codes emitted by <see cref="DiagnosticsService"/>.</summary>
public static class DiagnosticCode
{
    public const string InvalidVersion = "INVALID_VERSION";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string UnknownDependency = "UNKNOWN_DEPENDENCY";
    public const string UnknownProvider = "UNKNOWN_PROVIDER";
    public const string DuplicateProvider = "DUPLICATE_PROVIDER";
}

/// <summary>
/// One module in the declaration set. Dependencies are module identities;
/// Provides are capability keys. HostVersionConstraint accepts &gt;=, &lt;=,
/// == or ^ (minimum) forms.
/// </summary>
public sealed record ModuleDeclaration(
    string Id,
    string Version,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? Provides = null,
    string? HostVersionConstraint = null);

/// <summary>
/// Pure descriptive diagnosis over a module/plugin declaration set and a host
/// version. Nothing is loaded, registered or mutated; findings are returned in
/// a deterministic order (modules first, then plugins, declaration order within
/// each group).
/// </summary>
public sealed class DiagnosticsService
{
    private readonly string _hostVersion;

    public DiagnosticsService(string hostVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);
        _hostVersion = hostVersion;
    }

    public IReadOnlyList<Diagnostic> Diagnose(
        IReadOnlyList<ModuleDeclaration> modules,
        IReadOnlyList<PluginManifest> plugins) =>
        Diagnose(modules, plugins, _hostVersion);

    public static IReadOnlyList<Diagnostic> Diagnose(
        IReadOnlyList<ModuleDeclaration> modules,
        IReadOnlyList<PluginManifest> plugins,
        string hostVersion)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostVersion);

        var findings = new List<Diagnostic>();
        if (!Version.TryParse(hostVersion, out _))
        {
            findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InvalidVersion,
                $"host version '{hostVersion}' is not a valid version"));
            return findings;
        }

        var moduleIds = new HashSet<string>(modules.Select(module => module.Id), StringComparer.Ordinal);
        var providers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            DiagnoseVersion(module, hostVersion, findings);

            foreach (var dependency in module.Dependencies ?? Array.Empty<string>())
            {
                if (!moduleIds.Contains(dependency))
                {
                    findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.UnknownDependency,
                        $"module '{module.Id}' depends on unknown module '{dependency}'"));
                }
            }

            foreach (var capability in module.Provides ?? Array.Empty<string>())
            {
                AddProvider(capability, $"module '{module.Id}'", providers, findings);
            }
        }

        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Provides)
            {
                AddProvider(capability, $"plugin '{plugin.Id}'", providers, findings);
            }
        }

        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Requires)
            {
                if (string.IsNullOrWhiteSpace(capability) || providers.Contains(capability))
                {
                    continue;
                }

                findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.UnknownProvider,
                    $"plugin '{plugin.Id}' requires capability '{capability}' with no provider"));
            }
        }

        return findings;
    }

    private static void DiagnoseVersion(ModuleDeclaration module, string hostVersion, List<Diagnostic> findings)
    {
        if (string.IsNullOrWhiteSpace(module.HostVersionConstraint))
        {
            return;
        }

        if (!TrySatisfies(module.HostVersionConstraint, hostVersion, out var satisfied))
        {
            findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InvalidVersion,
                $"module '{module.Id}' has invalid host version constraint '{module.HostVersionConstraint}'"));
            return;
        }

        if (!satisfied)
        {
            findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.VersionMismatch,
                $"module '{module.Id}' requires host version '{module.HostVersionConstraint}' but host is '{hostVersion}'"));
        }
    }

    private static void AddProvider(string capability, string owner, HashSet<string> providers,
        List<Diagnostic> findings)
    {
        if (string.IsNullOrWhiteSpace(capability))
        {
            return;
        }

        if (!providers.Add(capability))
        {
            findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.DuplicateProvider,
                $"capability '{capability}' is provided more than once (second: {owner})"));
        }
    }

    private static bool TrySatisfies(string constraint, string hostVersion, out bool satisfied)
    {
        satisfied = false;
        var text = constraint.Trim();
        var comparison = ">=";
        if (text.StartsWith(">=", StringComparison.Ordinal))
        {
            text = text[2..].Trim();
        }
        else if (text.StartsWith("<=", StringComparison.Ordinal))
        {
            comparison = "<=";
            text = text[2..].Trim();
        }
        else if (text.StartsWith("==", StringComparison.Ordinal))
        {
            comparison = "==";
            text = text[2..].Trim();
        }
        else if (text.StartsWith('^'))
        {
            text = text[1..].Trim();
        }

        if (!Version.TryParse(text, out var expected) || !Version.TryParse(hostVersion, out var host))
        {
            return false;
        }

        satisfied = comparison switch
        {
            "<=" => host <= expected,
            "==" => host == expected,
            _ => host >= expected,
        };
        return true;
    }
}
