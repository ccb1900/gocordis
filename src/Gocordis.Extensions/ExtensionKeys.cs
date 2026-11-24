using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>
/// Stable capability keys for official extension services. Keys use reference
/// identity, so every provider and consumer must resolve them from this class,
/// never construct a same-named key. Packaged plugins must declare
/// <c>--shared-assembly Gocordis.Extensions</c> so all of them see this identity.
/// </summary>
public static class ExtensionKeys
{
    public static readonly Key<TimerService> Timer = new("gocordis.extension.timer");
    public static readonly Key<HttpService> Http = new("gocordis.extension.http");
    public static readonly Key<FileSystemService> FileSystem = new("gocordis.extension.fs");
    public static readonly Key<EnvService> Env = new("gocordis.extension.env");
    public static readonly Key<SocksRegistry> Socks = new("gocordis.extension.socks");
    public static readonly Key<DiagnosticsService> Diagnostics = new("gocordis.extension.devtools");
    public static readonly Key<ReversibleWatchService> Watch = new("gocordis.extension.watch");
    public static readonly Key<ServerRouteGuard> RouteGuard = new("gocordis.extension.route-guard");
}
