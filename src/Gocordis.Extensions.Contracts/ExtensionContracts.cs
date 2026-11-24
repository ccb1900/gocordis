using System.Net.Http;
using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Extensions;

// ---- Shared DTOs (moved out of implementation assembly) -------------------

public sealed record FileWriteOptions(bool Exclusive = false, bool Atomic = false);

public sealed record FileCopyOptions(
    bool Overwrite = false, bool Exclusive = false, bool Atomic = false);

/// <summary>Linearizable, read-only view of the HTTP service's ownership.</summary>
public sealed record HttpState(bool Closed, int Active, int OpenStreams, int Interceptors);

/// <summary>The route facts a guard decision is based on.</summary>
public sealed record RouteSnapshot(string Method, string Path, string Host, string Pattern)
{
    // Contract must not reference the implementation's HostAcl.
    public string NormalizedHost
    {
        get
        {
            var text = Host.Trim().ToLowerInvariant();
            if (text.StartsWith('['))
            {
                var close = text.IndexOf(']');
                return (close >= 0 ? text[..(close + 1)] : text).TrimEnd('.');
            }

            if (text.Count(character => character == ':') > 1)
            {
                return text.TrimEnd('.');
            }

            var colon = text.IndexOf(':');
            return (colon >= 0 ? text[..colon] : text).TrimEnd('.');
        }
    }

    public static RouteSnapshot Static(string path, string host) => Create("GET", path, host);

    public static RouteSnapshot WebSocket(string path, string host) => Create("GET", path, host);

    private static RouteSnapshot Create(string method, string path, string host) =>
        new(method, path, host, $"{method} {path}");
}

/// <summary>Three observations a watcher can report.</summary>
public enum WatchEventKind { Change, Ready, Error }

public sealed record WatchEvent(WatchEventKind Kind, string Path, long Revision = 0, string? Error = null);

public sealed record WatchOptions(bool Recursive = false, string? Filter = null);

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

/// <summary>Dials a target host through a registered SOCKS proxy.</summary>
public delegate Task<Stream> SocksDialer(string host, int port, CancellationToken cancellationToken);

/// <summary>A SOCKS5 CONNECT failure carrying the REP code from the proxy.</summary>
public sealed class SocksConnectException : InvalidOperationException
{
    public SocksConnectException(byte replyCode)
        : base($"SOCKS5 CONNECT failed: {Describe(replyCode)}") => ReplyCode = replyCode;

    public byte ReplyCode { get; }

    public static string Describe(byte replyCode) => replyCode switch
    {
        1 => "general failure",
        2 => "connection not allowed by ruleset",
        3 => "network unreachable",
        4 => "host unreachable",
        5 => "connection refused",
        6 => "TTL expired",
        7 => "command not supported",
        8 => "address type not supported",
        _ => $"unknown REP code {replyCode}",
    };
}

// ---- Public capability contracts ------------------------------------------

/// <summary>Timer capability. Owner-bound variants return a disposable handle.</summary>
public interface ITimerService : IAsyncDisposable
{
    Task TimeoutAsync(TimeSpan delay, Func<ValueTask> action, CancellationToken token = default);
    Task IntervalAsync(TimeSpan period, Func<ValueTask> action, CancellationToken token = default);
    Task DebounceAsync(TimeSpan window, Func<ValueTask> action, CancellationToken token = default);
    Task<bool> ThrottleAsync(TimeSpan window, Func<ValueTask> action);
    IDisposable TimeoutOwned(Context owner, TimeSpan delay, Func<ValueTask> action);
    IDisposable IntervalOwned(Context owner, TimeSpan period, Func<ValueTask> action);
    Task TimeoutWait(Context owner, TimeSpan delay);
    IAsyncEnumerable<DateTimeOffset> Ticks(Context owner, TimeSpan period);
}

/// <summary>HTTP capability with reversible proxy contributions.</summary>
public interface IHttpService : IAsyncDisposable
{
    bool IsDisposed { get; }
    int ProxyCount { get; }
    void SetBaseAddress(Uri uri);
    IDisposable AddInterceptor(Func<HttpRequestMessage, ValueTask> interceptor);
    IDisposable AddInterceptorChain(
        Func<HttpRequestMessage, Func<Task<HttpResponseMessage>>, Task<HttpResponseMessage>> interceptor);
    IDisposable RegisterProxyDialer(IEnumerable<string> schemes, SocksDialer dialer);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token = default);
    Task<string> GetStringAsync(string uri);
    HttpState State();
}

/// <summary>Mount-routed filesystem capability.</summary>
public interface IFileSystemService : IAsyncDisposable
{
    void Mount(string prefix, string root);
    IDisposable MountScoped(string prefix, string root);
    bool Unmount(string prefix);
    Task WriteAllTextAsync(string path, string content);
    Task WriteAllTextAsync(string path, string content, FileWriteOptions options);
    Task WriteAsync(string path, byte[] content);
    Task WriteAsync(string path, byte[] content, FileWriteOptions options);
    Task<string> ReadAllTextAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task<bool> ExistsAsync(string path);
    Task<IReadOnlyList<string>> ListAsync(string path);
    IDisposable Watch(string path, Action<string> onChange);
}

/// <summary>dotenv capability; layers are unwound by the provider's effect.</summary>
public interface IEnvService : IAsyncDisposable
{
    void Apply(IReadOnlyDictionary<string, string> values, bool processWins = true);
}

/// <summary>SOCKS registration capability composed with an HTTP provider.</summary>
public interface ISocksRegistry : IAsyncDisposable
{
    IHttpService? Http { get; }
    IDisposable Register(string scheme, string address);
    SocksDialer? Resolve(string scheme);
    Task<Stream> ConnectAsync(string scheme, string host, int port, CancellationToken token = default);
}

/// <summary>Pure declaration diagnostics capability.</summary>
public interface IDiagnosticsService
{
    IReadOnlyList<Diagnostic> Diagnose(
        IReadOnlyList<ModuleDeclaration> modules,
        IReadOnlyList<PluginManifest> plugins);
}

/// <summary>Reversible file-watch capability.</summary>
public interface IWatchService : IAsyncDisposable
{
    long Revision(string path);
    IDisposable Watch(string path, Action<WatchEvent> onEvent, WatchOptions? options = null);
    IDisposable WatchOwned(Context owner, string path, Action<WatchEvent> onEvent, WatchOptions? options = null);
}

/// <summary>Route guard contribution capability (no ASP.NET types in the contract).</summary>
public interface IRouteGuardService : IAsyncDisposable
{
    IDisposable Add(Func<RouteSnapshot, bool> check, string? pattern = null);
    bool IsAllowed(string method, string path, string host, string? pattern = null);
    bool IsAllowed(RouteSnapshot route);
}
