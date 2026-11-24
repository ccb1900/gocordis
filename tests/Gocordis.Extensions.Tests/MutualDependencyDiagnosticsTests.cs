using Gocordis.Extensions;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>Paper §6.5: diagnostics should advise splitting a mutual capability
/// dependency into an integration component instead of only rejecting the cycle
/// at mount time.</summary>
public sealed class MutualDependencyDiagnosticsTests
{
    [Fact]
    public void MutualCapabilityRequirementsProduceAnIntegrationComponentAdvice()
    {
        var plugins = new[]
        {
            Manifest("alpha", requires: ["beta.capability"], provides: ["alpha.capability"]),
            Manifest("beta", requires: ["alpha.capability"], provides: ["beta.capability"]),
        };

        var findings = DiagnosticsService.Diagnose([], plugins, "1.0.0");

        var advice = Assert.Single(findings, finding =>
            finding.Code == DiagnosticCode.MutualCapabilityDependency);
        Assert.Equal(DiagnosticSeverity.Warn, advice.Severity);
        Assert.Contains("alpha", advice.Message, StringComparison.Ordinal);
        Assert.Contains("beta", advice.Message, StringComparison.Ordinal);
        Assert.Contains("integration component", advice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneWayDependencyIsNotReportedAsMutual()
    {
        var plugins = new[]
        {
            Manifest("alpha", requires: [], provides: ["alpha.capability"]),
            Manifest("beta", requires: ["alpha.capability"], provides: []),
        };

        var findings = DiagnosticsService.Diagnose([], plugins, "1.0.0");

        Assert.DoesNotContain(findings, finding =>
            finding.Code == DiagnosticCode.MutualCapabilityDependency);
        Assert.DoesNotContain(findings, finding => finding.Severity == DiagnosticSeverity.Error);
    }

    private static PluginManifest Manifest(
        string id, IReadOnlyList<string> requires, IReadOnlyList<string> provides) =>
        new(id, id, true, new Dictionary<string, object?>(), requires, provides);
}
