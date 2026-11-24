using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Extensions;

public static class DiagnosticsKeys
{
    public static readonly Key<IDiagnosticsService> Service = new("gocordis.extension.devtools");
}

public enum DiagnosticSeverity { Error, Warn, Info }

public sealed record Diagnostic(DiagnosticSeverity Severity, string Code, string Message)
{
    public override string ToString() => $"[{Severity.ToString().ToLowerInvariant()}] {Code}: {Message}";
}

public static class DiagnosticCode
{
    public const string InvalidVersion = "INVALID_VERSION";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string UnknownDependency = "UNKNOWN_DEPENDENCY";
    public const string UnknownProvider = "UNKNOWN_PROVIDER";
    public const string DuplicateProvider = "DUPLICATE_PROVIDER";
}

public sealed record ModuleDeclaration(
    string Id,
    string Version,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? Provides = null,
    string? HostVersionConstraint = null);

public interface IDiagnosticsService
{
    IReadOnlyList<Diagnostic> Diagnose(
        IReadOnlyList<ModuleDeclaration> modules,
        IReadOnlyList<PluginManifest> plugins);
}
