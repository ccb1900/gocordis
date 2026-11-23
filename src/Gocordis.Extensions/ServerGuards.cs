using Microsoft.AspNetCore.Http;

namespace Gocordis.Extensions;

/// <summary>The route facts a guard decision is based on.</summary>
/// <param name="Method">HTTP method (uppercase, e.g. GET).</param>
/// <param name="Path">Request path, e.g. /assets/app.js.</param>
/// <param name="Host">Raw Host header (port included).</param>
/// <param name="Pattern">Registration key, e.g. "GET /assets/app.js".</param>
public sealed record RouteSnapshot(string Method, string Path, string Host, string Pattern)
{
    public string NormalizedHost => HostAcl.StripPort(Host);

    public static RouteSnapshot Static(string path, string host) => Create("GET", path, host);

    public static RouteSnapshot WebSocket(string path, string host) => Create("GET", path, host);

    private static RouteSnapshot Create(string method, string path, string host) =>
        new(method, path, host, $"{method} {path}");
}

/// <summary>
/// Pure host access-control decisions. Deny wins; loopback is always allowed;
/// an empty allow list means loopback-only; a leading dot matches the apex and
/// all subdomains ("*.example.com" is an equivalent spelling).
/// </summary>
public static class HostAcl
{
    private static readonly HashSet<string> LoopbackHosts =
        new(StringComparer.Ordinal) { "localhost", "127.0.0.1", "::1", "[::1]" };

    /// <summary>Lowercases a Host header, keeps bracketed IPv6 and removes a trailing port.</summary>
    public static string StripPort(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var text = host.Trim().ToLowerInvariant();
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            return (close >= 0 ? text[..(close + 1)] : text).TrimEnd('.');
        }

        // A bare IPv6 literal (more than one colon) has no port to remove.
        if (text.Count(character => character == ':') > 1)
        {
            return text.TrimEnd('.');
        }

        var colon = text.IndexOf(':');
        return (colon >= 0 ? text[..colon] : text).TrimEnd('.');
    }

    public static bool IsAllowed(
        string host, IReadOnlyCollection<string>? allow = null, IReadOnlyCollection<string>? deny = null)
    {
        var normalized = StripPort(host);
        if (MatchesAny(normalized, deny))
        {
            return false;
        }

        if (LoopbackHosts.Contains(normalized))
        {
            return true;
        }

        return allow is { Count: > 0 } && MatchesAny(normalized, allow);
    }

    /// <summary>
    /// Builds a pure route policy. A route key in <paramref name="routeAllow"/>
    /// ("METHOD /path" or "/path") replaces the global allow list for that route;
    /// deny still applies everywhere.
    /// </summary>
    public static Func<RouteSnapshot, bool> Policy(
        IReadOnlyCollection<string>? allow,
        IReadOnlyCollection<string>? deny,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? routeAllow = null) => route =>
    {
        var effective = allow;
        if (routeAllow is not null)
        {
            if (routeAllow.TryGetValue(route.Pattern, out var byPattern))
            {
                effective = byPattern;
            }
            else
            {
                var separator = route.Pattern.IndexOf(' ', StringComparison.Ordinal);
                if (separator >= 0 &&
                    routeAllow.TryGetValue(route.Pattern[(separator + 1)..], out var byPath))
                {
                    effective = byPath;
                }
            }
        }

        return IsAllowed(route.NormalizedHost, effective, deny);
    };

    private static bool MatchesAny(string host, IReadOnlyCollection<string>? rules)
    {
        if (rules is null)
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (MatchHost(rule, host))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchHost(string pattern, string host)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var rule = pattern.Trim().TrimEnd('.').ToLowerInvariant();
        var target = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (rule.StartsWith("*.", StringComparison.Ordinal))
        {
            rule = "." + rule[2..];
        }

        if (rule.StartsWith('.'))
        {
            var suffix = rule[1..];
            return suffix.Length > 0 &&
                (target == suffix || target.EndsWith(rule, StringComparison.Ordinal));
        }

        return rule.Length > 0 && target == rule;
    }
}

/// <summary>
/// Reversible route guards for static resources, WebSockets and ordinary
/// handlers. Denied routes are answered with 404, matching a route that was
/// never registered. Each registration is withdrawn by identity only.
/// </summary>
public sealed class ServerRouteGuard : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<GuardRegistration> _checks = [];
    private int _disposed;

    /// <summary>Adds a check. A null pattern makes it global; otherwise it is a route key.</summary>
    public IDisposable Add(Func<RouteSnapshot, bool> check, string? pattern = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        var registration = new GuardRegistration(this, pattern, check);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _checks.Add(registration);
        }

        return registration;
    }

    public bool IsAllowed(string method, string path, string host, string? pattern = null)
    {
        var normalizedMethod = method.ToUpperInvariant();
        return IsAllowed(new RouteSnapshot(
            normalizedMethod, path, host, pattern ?? $"{normalizedMethod} {path}"));
    }

    public bool IsAllowed(RouteSnapshot route)
    {
        GuardRegistration[] checks;
        lock (_sync)
        {
            checks = _checks.ToArray();
        }

        foreach (var check in checks)
        {
            if (PatternMatches(check.Pattern, route) && !check.Check(route))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ASP.NET Core middleware that turns a denial into a 404.</summary>
    public RequestDelegate Middleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return context =>
        {
            var request = context.Request;
            var method = request.Method.ToUpperInvariant();
            var path = request.Path.Value ?? string.Empty;
            var route = new RouteSnapshot(method, path, request.Host.Value ?? string.Empty, $"{method} {path}");
            if (!IsAllowed(route))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return next(context);
        };
    }

    /// <summary>Route key covering a static prefix and its exact mount root.</summary>
    public static string StaticRoute(string pathPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathPrefix);
        return pathPrefix.TrimEnd('/') + "/**";
    }

    /// <summary>Route key for a WebSocket endpoint (a GET handshake).</summary>
    public static string WebSocketRoute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Trim();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            lock (_sync)
            {
                _checks.Clear();
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Remove(GuardRegistration registration)
    {
        lock (_sync)
        {
            var index = _checks.FindIndex(candidate => ReferenceEquals(candidate, registration));
            if (index >= 0)
            {
                _checks.RemoveAt(index);
            }
        }
    }

    private static bool PatternMatches(string? key, RouteSnapshot route)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        var trimmed = key.Trim();
        var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (separator >= 0)
        {
            var method = trimmed[..separator];
            if (!route.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return PathMatches(trimmed[(separator + 1)..], route);
        }

        return PathMatches(trimmed, route);
    }

    private static bool PathMatches(string path, RouteSnapshot route)
    {
        if (path.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = path[..^2]; // "/assets/**" -> "/assets/"
            return route.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                route.Path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        return route.Pattern.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            route.Path.Equals(path, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GuardRegistration : IDisposable
    {
        private readonly ServerRouteGuard _owner;
        private int _disposed;

        public GuardRegistration(ServerRouteGuard owner, string? pattern, Func<RouteSnapshot, bool> check)
        {
            _owner = owner;
            Pattern = pattern;
            Check = check;
        }

        public string? Pattern { get; }
        public Func<RouteSnapshot, bool> Check { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Remove(this);
            }
        }
    }
}
