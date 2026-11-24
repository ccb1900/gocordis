using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>
/// Stable capability keys for official extension services. Keys use reference
/// identity, so every provider and consumer must resolve them from this class,
/// never construct a same-named key. Packaged plugins must declare
/// <c>--shared-assembly Gocordis.Extensions.Contracts</c> so all of them see
/// this identity. Keys bind public interfaces, not implementation classes: a
/// provider can be replaced without changing consumers.
/// </summary>
public static class ExtensionKeys
{
    public static readonly Key<ITimerService> Timer = new("gocordis.extension.timer");
    public static readonly Key<IHttpService> Http = new("gocordis.extension.http");
    public static readonly Key<IFileSystemService> FileSystem = new("gocordis.extension.fs");
    public static readonly Key<IEnvService> Env = new("gocordis.extension.env");
    public static readonly Key<ISocksRegistry> Socks = new("gocordis.extension.socks");
    public static readonly Key<IDiagnosticsService> Diagnostics = new("gocordis.extension.devtools");
    public static readonly Key<IWatchService> Watch = new("gocordis.extension.watch");
    public static readonly Key<IRouteGuardService> RouteGuard = new("gocordis.extension.route-guard");
}
