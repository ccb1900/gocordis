using System.Diagnostics;

namespace Industrial.Core;

/// <summary>One ActivitySource for the real Elsa execution path. The old
/// coordinator does not own execution; telemetry belongs to the entry point
/// every product command converges on.</summary>
public static class IndustrialTelemetry
{
    public static readonly ActivitySource Source = new("Gocordis.Industrial.Collection");
}
