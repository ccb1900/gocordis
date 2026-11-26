using Gocordis.Plugins;

namespace Gocordis.Extensions;

/// <summary>
/// Pure descriptive diagnosis over a module/plugin declaration set and a host
/// version. Nothing is loaded, registered or mutated; findings are returned in
/// a deterministic order (modules first, then plugins, declaration order within
/// each group).
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
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

        // Pure diagnostics must never throw on malformed input: report a
        // duplicate id instead of failing ToDictionary.
        var pluginsById = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            if (!pluginsById.TryAdd(plugin.Id, plugin))
            {
                findings.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.DuplicatePluginId,
                    $"duplicate plugin declaration '{plugin.Id}'"));
            }
        }

        var capabilityOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            foreach (var capability in plugin.Provides.Where(capability => !string.IsNullOrWhiteSpace(capability)))
            {
                capabilityOwners.TryAdd(capability, plugin.Id);
            }
        }

        var reportedPairs = new HashSet<string>(StringComparer.Ordinal);
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

            foreach (var required in plugin.Requires.Where(capability =>
                         !string.IsNullOrWhiteSpace(capability) &&
                         capabilityOwners.TryGetValue(capability, out var owner) &&
                         !string.Equals(owner, plugin.Id, StringComparison.Ordinal)))
            {
                var ownerId = capabilityOwners[required];
                var other = pluginsById[ownerId];
                if (!other.Requires.Any(candidate => plugin.Provides.Contains(candidate, StringComparer.Ordinal)))
                {
                    continue;
                }

                var pair = string.Join('|', new[] { plugin.Id, ownerId }
                    .OrderBy(id => id, StringComparer.Ordinal));
                if (reportedPairs.Add(pair))
                {
                    findings.Add(new Diagnostic(
                        DiagnosticSeverity.Warn,
                        DiagnosticCode.MutualCapabilityDependency,
                        $"plugins '{plugin.Id}' and '{ownerId}' require each other's capabilities; " +
                        "extract an integration component (paper §6.5) instead of relying on the mount-time cycle rejection"));
                }
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

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

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

        // NuGet normalizes "1.0" to "1.0.0.0"; System.Version treats an
        // unspecified Build/Revision as -1, which would make 1.0.0 < 1.0.0.0.
        var normalizedExpected = Normalize(expected);
        var normalizedHost = Normalize(host);
        satisfied = comparison switch
        {
            "<=" => normalizedHost <= normalizedExpected,
            "==" => normalizedHost == normalizedExpected,
            _ => normalizedHost >= normalizedExpected,
        };
        return true;
    }
}
