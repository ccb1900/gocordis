using Gocordis.Core;

namespace Gocordis.Extensions;

public static class RouteGuardKeys
{
    public static readonly Key<IRouteGuardService> Service = new("gocordis.extension.route-guard");
}

/// <summary>Route facts for a host ACL decision (no ASP.NET types in the contract).</summary>
public sealed record RouteSnapshot(string Method, string Path, string Host, string Pattern)
{
    // Keep the StripPort logic here so the contract does not reference implementation.
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

            if (text.Count(c => c == ':') > 1)
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

public interface IRouteGuardService : IAsyncDisposable
{
    IDisposable Add(Func<RouteSnapshot, bool> check, string? pattern = null);
    bool IsAllowed(string method, string path, string host, string? pattern = null);
    bool IsAllowed(RouteSnapshot route);
}
