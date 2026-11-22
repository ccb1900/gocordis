using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Gocordis.Packaging;

/// <summary>
/// Read-only local registry over a directory of .gocordis-plugin files.
/// It publishes a static index and serves only the package files named in that
/// index; it never writes packages, installs anything or authenticates callers.
/// Loopback binding is the default; a wildcard/remote bind needs explicit
/// opt-in, mirroring the console's ListenGuard policy.
/// </summary>
public sealed class PluginSourceServer : IAsyncDisposable
{
    private static readonly Regex AddressPattern = new(
        @"^http://(\[[0-9a-fA-F:.]+\]|[^:/?#]+)(?::(\d+))?/?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string _directory;
    private readonly string _host;
    private readonly bool _allowRemote;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, IndexEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _skipped = new(StringComparer.Ordinal);
    private Task? _serveTask;
    private int _port;
    private int _disposed;

    public PluginSourceServer(string directory, string address = "http://127.0.0.1:0", bool allowRemote = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        if (!Directory.Exists(_directory))
        {
            throw new DirectoryNotFoundException(_directory);
        }

        (_host, _port, _allowRemote) = ParseAddress(address, allowRemote);
    }

    /// <summary>The bound address, including the concrete port when port 0 was requested.</summary>
    public string Address => $"http://{_host}:{_port}/";

    public string IndexUrl => Address.TrimEnd('/') + "/index.json";

    /// <summary>Package file names that could not be read, with their reasons.</summary>
    public IReadOnlyDictionary<string, string> SkippedPackages
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, string>(_skipped);
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_serveTask is not null)
        {
            throw new InvalidOperationException("registry server is already started");
        }

        if (_port == 0)
        {
            _port = ReservePort();
        }

        var prefix = $"http://{_host}:{_port}/";
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        try
        {
            await RefreshIndexAsync(cancellationToken);
        }
        catch
        {
            _listener.Stop();
            throw;
        }

        _serveTask = Task.Run(ServeAsync);
    }

    private (string Host, int Port, bool AllowRemote) ParseAddress(string address, bool allowRemote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var match = AddressPattern.Match(address.Trim());
        if (!match.Success)
        {
            throw new ArgumentException(
                $"invalid registry address '{address}' (expected http://host[:port])", nameof(address));
        }

        var host = match.Groups[1].Value;
        var port = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
            : 80;
        if (port is < 0 or > 65535)
        {
            throw new ArgumentException($"invalid registry port {port}", nameof(address));
        }

        var bare = host.Trim('[', ']');
        if (!allowRemote)
        {
            if (bare.Length == 0 || host is "*" or "+")
            {
                throw new InvalidOperationException("wildcard listen requires explicit opt-in");
            }

            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                !(IPAddress.TryParse(bare, out var ip) && IPAddress.IsLoopback(ip)))
            {
                throw new InvalidOperationException($"remote listen '{host}' requires explicit opt-in");
            }
        }

        return (host, port, allowRemote);
    }

    private int ReservePort()
    {
        IPAddress bind;
        if (_host is "*" or "+")
        {
            bind = IPAddress.Any;
        }
        else if (IPAddress.TryParse(_host.Trim('[', ']'), out var parsed))
        {
            bind = parsed;
        }
        else
        {
            bind = _allowRemote ? IPAddress.Any : IPAddress.Loopback;
        }

        var probe = new TcpListener(bind, 0);
        try
        {
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task RefreshIndexAsync(CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(_directory, "*.gocordis-plugin", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            try
            {
                var content = await PackageSourceReader.ReadIgnoringHostAsync(path, cancellationToken);
                var sha256 = await PackageSourceReader.ComputeSha256Async(path, cancellationToken);
                entries[name] = new IndexEntry(
                    content.Manifest.Id,
                    content.Manifest.Version,
                    "/packages/" + Uri.EscapeDataString(name),
                    sha256);
            }
            catch (Exception ex) when (ex is InvalidPluginPackageException or IOException or UnauthorizedAccessException)
            {
                skipped[name] = ex.Message;
                Trace.TraceWarning($"registry skipped invalid package '{name}': {ex.Message}");
            }
        }

        lock (_sync)
        {
            _entries.Clear();
            _skipped.Clear();
            foreach (var (name, entry) in entries)
            {
                _entries.Add(name, entry);
            }

            foreach (var (name, reason) in skipped)
            {
                _skipped.Add(name, reason);
            }
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return; // listener closed by DisposeAsync
                }

                await HandleAsync(context);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"registry request loop stopped: {ex}");
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.NotFound;
                return;
            }

            var rawTarget = context.Request.RawUrl ?? "/";
            var path = rawTarget.Split('?')[0];
            // Reject encoded separators/dots up front: a decoded or double-encoded
            // traversal must never reach the index lookup, let alone the disk.
            if (ContainsEncodedTraversal(rawTarget))
            {
                context.Response.StatusCode = StatusCodes.NotFound;
                return;
            }

            if (string.Equals(path, "/index.json", StringComparison.Ordinal))
            {
                await RefreshIndexAsync(_shutdown.Token);
                IndexEntry[] snapshot;
                lock (_sync)
                {
                    snapshot = _entries.Values.ToArray();
                }

                var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                context.Response.StatusCode = StatusCodes.OK;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.LongLength;
                await context.Response.OutputStream.WriteAsync(bytes, _shutdown.Token);
                return;
            }

            const string prefix = "/packages/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.NotFound;
                return;
            }

            if (!TryDecodePackageName(path[prefix.Length..], out var name))
            {
                context.Response.StatusCode = StatusCodes.NotFound;
                return;
            }

            lock (_sync)
            {
                if (!_entries.ContainsKey(name))
                {
                    context.Response.StatusCode = StatusCodes.NotFound;
                    return;
                }
            }

            var file = Path.Combine(_directory, name);
            if (!File.Exists(file))
            {
                context.Response.StatusCode = StatusCodes.NotFound;
                return;
            }

            context.Response.StatusCode = StatusCodes.OK;
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength64 = new FileInfo(file).Length;
            await using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await stream.CopyToAsync(context.Response.OutputStream, _shutdown.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException or
                                       OperationCanceledException)
        {
            // Client disconnected or shutdown started: nothing left to answer.
        }
        catch (Exception ex)
        {
            Trace.TraceError($"registry request failed: {ex}");
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    context.Response.StatusCode = StatusCodes.InternalServerError;
                }
            }
            catch (Exception inner) when (inner is InvalidOperationException or ObjectDisposedException or HttpListenerException)
            {
                // Headers were already sent; the response is simply closed below.
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or IOException)
            {
                // The peer went away; closing is best effort.
            }
        }
    }

    private static bool ContainsEncodedTraversal(string rawTarget)
    {
        foreach (var token in new[] { "%2e", "%2f", "%5c", "%00", "%25" })
        {
            if (rawTarget.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Accepts a single file name only. Percent decoding is repeated to reject
    /// double-encoded traversal, and the decoded name must also be in the
    /// published index (checked by the caller), so no other file can be served.
    /// </summary>
    private static bool TryDecodePackageName(string raw, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 1024)
        {
            return false;
        }

        try
        {
            var decoded = raw;
            for (var i = 0; i < 3; i++)
            {
                var next = Uri.UnescapeDataString(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }

            if (decoded.Length == 0 || decoded.Length > 255 ||
                decoded is "." or ".." ||
                decoded.IndexOfAny(['/', '\\', ':', '\0']) >= 0 ||
                decoded.Contains("..", StringComparison.Ordinal) ||
                !string.Equals(Path.GetFileName(decoded), decoded, StringComparison.Ordinal))
            {
                return false;
            }

            name = decoded;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException) { }

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // Best effort: the serve loop exits on any of these as well.
        }

        if (_serveTask is not null)
        {
            try
            {
                await _serveTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"registry shutdown: {ex.Message}");
            }
        }

        _shutdown.Dispose();
    }

    // Lowercase wire shape is the registry contract, matching index.json.
    internal sealed record IndexEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("sha256")] string Sha256);

    private static class StatusCodes
    {
        public const int OK = 200;
        public const int NotFound = 404;
        public const int InternalServerError = 500;
    }
}
