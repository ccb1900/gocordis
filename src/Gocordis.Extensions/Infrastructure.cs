using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace Gocordis.Extensions;

/// <summary>
/// Mount router over mount-owned filesystem providers. Path policy (longest
/// prefix, boundary matching, case rules) stays in the service; each provider
/// owns its resource semantics (local disk or in-memory tree).
/// </summary>
public sealed class FileSystemService : IAsyncDisposable, IFileSystemService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MountEntry> _mounts = new(StringComparer.Ordinal);

    private sealed record MountEntry(IFileSystemProvider Provider, bool Owns, Guid Token);

    public void Mount(string prefix, string root) =>
        MountProvider(prefix, new LocalFileSystemProvider(root), owns: true);

    /// <summary>Mounts a provider under a prefix; the service owns it when owns is true.</summary>
    public void MountProvider(string prefix, IFileSystemProvider provider, bool owns = true) =>
        MountProvider(prefix, provider, owns, Guid.NewGuid());

    private void MountProvider(string prefix, IFileSystemProvider provider, bool owns, Guid token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(provider);
        MountEntry? evicted = null;
        lock (_sync)
        {
            if (_mounts.TryGetValue(prefix, out var existing) && existing.Owns)
            {
                evicted = existing;
            }

            _mounts[prefix] = new MountEntry(provider, owns, token);
        }

        evicted?.Provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Mounts an in-memory tree; seeds are (path, bytes) pairs.</summary>
    public void MountMemory(string prefix, IEnumerable<KeyValuePair<string, byte[]>>? seeds = null) =>
        MountProvider(prefix, new MemoryFileSystemProvider(seeds), owns: true);

    public IDisposable MountScoped(string prefix, string root)
    {
        var token = Guid.NewGuid();
        MountProvider(prefix, new LocalFileSystemProvider(root), owns: true, token);
        return new Subscription(() => Unmount(prefix, token));
    }

    public bool Unmount(string prefix) => Unmount(prefix, null);

    private bool Unmount(string prefix, Guid? token)
    {
        MountEntry? removed = null;
        lock (_sync)
        {
            if (_mounts.TryGetValue(prefix, out var entry) &&
                (token is null || entry.Token == token.Value))
            {
                removed = entry;
                _mounts.Remove(prefix);
            }
        }

        if (removed is { Owns: true })
        {
            removed.Provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return removed is not null;
    }

    private static bool PrefixMatches(string path, string prefix, StringComparison comparison) =>
        path.Equals(prefix, comparison) ||
        path.StartsWith(prefix + "/", comparison) ||
        path.StartsWith(prefix + "\\", comparison);

    private (IFileSystemProvider Provider, string Relative) Resolve(string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        KeyValuePair<string, MountEntry> match;
        lock (_sync)
        {
            match = _mounts
                .Where(pair => PrefixMatches(path, pair.Key, comparison))
                .OrderByDescending(pair => pair.Key.Length)
                .FirstOrDefault();
        }

        if (match.Key is null)
        {
            throw new InvalidOperationException($"no mount matches '{path}'");
        }

        return (match.Value.Provider, path[match.Key.Length..].TrimStart('/', '\\'));
    }

    public Task WriteAsync(string path, byte[] content) =>
        WriteAsync(path, content, new FileWriteOptions());

    public Task WriteAsync(string path, byte[] content, FileWriteOptions options)
    {
        var (provider, relative) = Resolve(path);
        return provider.WriteAsync(relative, content, options);
    }

    public Task<byte[]> ReadAsync(string path)
    {
        var (provider, relative) = Resolve(path);
        return provider.ReadAsync(relative);
    }

    public Task<byte[]> ReadAllBytesAsync(string path) => ReadAsync(path);

    public async Task<string> ReadAllTextAsync(string path) => Encoding.UTF8.GetString(await ReadAsync(path));

    public Task WriteAllTextAsync(string path, string content) =>
        WriteAsync(path, Encoding.UTF8.GetBytes(content));

    public Task WriteAllTextAsync(string path, string content, FileWriteOptions options) =>
        WriteAsync(path, Encoding.UTF8.GetBytes(content), options);

    public Task<byte[]> ReadRangeAsync(string path, long offset, int count)
    {
        var (provider, relative) = Resolve(path);
        return provider.ReadRangeAsync(relative, offset, count);
    }

    public Task RemoveAsync(string path, bool recursive = false)
    {
        var (provider, relative) = Resolve(path);
        return provider.RemoveAsync(relative, recursive);
    }

    public Task MkdirAsync(string path, bool recursive = false)
    {
        var (provider, relative) = Resolve(path);
        return provider.MkdirAsync(relative, recursive);
    }

    public Task CopyAsync(string source, string destination, bool overwrite = false) =>
        CopyAsync(source, destination, new FileCopyOptions(Overwrite: overwrite));

    public Task CopyAsync(string source, string destination, FileCopyOptions options)
    {
        var (fromProvider, fromRelative) = Resolve(source);
        var (toProvider, toRelative) = Resolve(destination);
        if (!ReferenceEquals(fromProvider, toProvider))
        {
            throw new InvalidOperationException("copy across different filesystem providers is not supported");
        }

        return fromProvider.CopyAsync(fromRelative, toRelative, options);
    }

    public Task MoveAsync(string source, string destination)
    {
        var (fromProvider, fromRelative) = Resolve(source);
        var (toProvider, toRelative) = Resolve(destination);
        if (!ReferenceEquals(fromProvider, toProvider))
        {
            throw new InvalidOperationException("move across different filesystem providers is not supported");
        }

        return fromProvider.MoveAsync(fromRelative, toRelative);
    }

    public Task<bool> ExistsAsync(string path)
    {
        var (provider, relative) = Resolve(path);
        return provider.ExistsAsync(relative);
    }

    public Task<IReadOnlyList<string>> ListAsync(string path)
    {
        var (provider, relative) = Resolve(path);
        return provider.ListAsync(relative);
    }

    public IDisposable Watch(string path, Action<string> onChange)
    {
        var (provider, relative) = Resolve(path);
        return provider.Watch(relative, onChange);
    }

    public async ValueTask DisposeAsync()
    {
        MountEntry[] entries;
        lock (_sync)
        {
            entries = _mounts.Values.ToArray();
            _mounts.Clear();
        }

        foreach (var entry in entries.Where(item => item.Owns))
        {
            await entry.Provider.DisposeAsync();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;
        public Subscription(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>
/// HttpClient with ordered, reversible next-chain interceptors. Stream bodies are
/// service-owned until closed: request completion and stream closure are distinct.
/// </summary>
public sealed class HttpService : IAsyncDisposable, IHttpService
{
    private readonly SocketsHttpHandler _handler = new();
    private readonly HttpClient _client;
    private readonly object _sync = new();
    private readonly List<Func<HttpRequestMessage, ValueTask>> _interceptors = [];
    private readonly List<ChainRegistration> _chain = [];
    private readonly Dictionary<long, ProxyRegistration> _proxies = [];
    private readonly Dictionary<long, (HashSet<string> Schemes, Func<Uri?> Resolve)> _proxyUrls = [];
    private readonly HashSet<StreamLease> _leases = [];
    private long _nextProxyId;
    private bool _closed;
    private int _active;
    private int _openStreams;
    private int _disposed;

    public HttpService()
    {
        // Proxy selection happens per connection, not per HttpClient: a
        // withdrawn contribution must not affect an existing in-flight call,
        // and a new one must take effect immediately.
        // Proxying is owned by this service's reversible registrations; ambient
        // HTTP_PROXY must not silently bypass the Core-owned proxy lifecycle.
        _handler.UseProxy = false;
        _handler.ConnectCallback = ConnectAsync;
        _client = new HttpClient(_handler);
    }

    /// <summary>Count of active proxy contributions (diagnostics and tests).</summary>
    public int ProxyCount
    {
        get
        {
            lock (_sync)
            {
                return _proxies.Count;
            }
        }
    }

    /// <summary>
    /// Registers a dialer-based proxy for the given schemes. The returned handle
    /// removes ONLY this registration; last registration wins per scheme, and
    /// removal reveals the previous winner (Go HTTP semantics).
    /// </summary>
    public IDisposable RegisterProxyDialer(IEnumerable<string> schemes, SocksDialer dialer)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(dialer);
        var normalized = schemes.Select(NormalizeHttpScheme).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("at least one scheme is required", nameof(schemes));
        }

        long id;
        lock (_sync)
        {
            EnsureOpen();
            id = ++_nextProxyId;
            _proxies[id] = new ProxyRegistration(id, normalized, dialer);
        }

        return new Subscription(() => RemoveProxy(id));
    }

    private static string NormalizeHttpScheme(string scheme)
    {
        var normalized = scheme.Trim().ToLowerInvariant();
        return normalized is "" or "*" ? "*" : normalized;
    }

    private static async Task<Stream> ConnectDirectAsync(string host, int port, CancellationToken token)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, token);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed record ProxyRegistration(long Id, HashSet<string> Schemes, SocksDialer Dialer);

    private void RemoveProxy(long id)
    {
        lock (_sync)
        {
            _proxies.Remove(id);
        }
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var scheme = context.InitialRequestMessage.RequestUri?.Scheme ?? "http";
        SocksDialer? dialer = null;
        lock (_sync)
        {
            // Registration order is the precedence order: last wins.
            foreach (var registration in _proxies.Values.OrderByDescending(item => item.Id))
            {
                if (registration.Schemes.Contains(scheme) || registration.Schemes.Contains("*"))
                {
                    dialer = registration.Dialer;
                    break;
                }
            }
        }

        var stream = dialer is null
            ? await ConnectDirectAsync(host, port, token)
            : await dialer(host, port, token);
        if (scheme is "https" or "wss")
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host }, token);
            return ssl;
        }

        return stream;
    }

    public void SetBaseAddress(Uri uri) => _client.BaseAddress = uri;

    /// <summary>Adds a side-effect interceptor (runs before the next-chain).</summary>
    public IDisposable AddInterceptor(Func<HttpRequestMessage, ValueTask> interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        lock (_sync)
        {
            EnsureOpen();
            _interceptors.Add(interceptor);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _interceptors.Remove(interceptor);
            }
        });
    }

    /// <summary>
    /// Adds a next-chain link. Links run in registration order; a link may
    /// short-circuit by returning without invoking <paramref name="next"/>.
    /// </summary>
    public IDisposable AddInterceptorChain(
        Func<HttpRequestMessage, Func<Task<HttpResponseMessage>>, Task<HttpResponseMessage>> interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        var registration = new ChainRegistration(interceptor);
        lock (_sync)
        {
            EnsureOpen();
            _chain.Add(registration);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _chain.Remove(registration);
            }
        });
    }

    /// <summary>
    /// Registers a URL-based proxy resolver for WebSocket dials (the dialer
    /// registry above is used by HttpClient connections). Removal is identity-fenced.
    /// </summary>
    public IDisposable RegisterProxyUrlFactory(IEnumerable<string> schemes, Func<Uri?> resolve)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(resolve);
        long id;
        lock (_sync)
        {
            EnsureOpen();
            id = ++_nextProxyId;
            _proxyUrls[id] = (schemes.Select(NormalizeHttpScheme).ToHashSet(StringComparer.OrdinalIgnoreCase), resolve);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _proxyUrls.Remove(id);
            }
        });
    }

    /// <summary>Dials a WebSocket with the currently winning proxy contribution.</summary>
    public async Task<WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var socket = new ClientWebSocket();
        var proxy = ResolveWebSocketProxy(uri);
        if (proxy is not null)
        {
            socket.Options.Proxy = new WebProxy(proxy);
        }

        try
        {
            await socket.ConnectAsync(uri, token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private Uri? ResolveWebSocketProxy(Uri uri)
    {
        lock (_sync)
        {
            foreach (var (_, entry) in _proxyUrls.OrderByDescending(pair => pair.Key))
            {
                var scheme = uri.Scheme;
                if (entry.Schemes.Contains(scheme) || entry.Schemes.Contains("*"))
                {
                    var candidate = entry.Resolve();
                    if (candidate is not null)
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    public HttpState State()
    {
        lock (_sync)
        {
            return new HttpState(_closed, _active, _openStreams, _chain.Count);
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token = default)
    {
        Func<HttpRequestMessage, Task<HttpResponseMessage>> pipeline;
        Func<HttpRequestMessage, ValueTask>[] simple;
        lock (_sync)
        {
            EnsureOpen();
            _active++;
            simple = _interceptors.ToArray();
            pipeline = BuildPipeline(req => _client.SendAsync(req, token));
        }

        try
        {
            foreach (var interceptor in simple)
            {
                await interceptor(request);
            }

            return await pipeline(request);
        }
        finally
        {
            lock (_sync)
            {
                _active--;
            }
        }
    }

    /// <summary>Returns a response whose body is live; the lease is released on close.</summary>
    public async Task<StreamResponse> SendStreamAsync(HttpRequestMessage request, CancellationToken token = default)
    {
        Func<HttpRequestMessage, Task<HttpResponseMessage>> pipeline;
        Func<HttpRequestMessage, ValueTask>[] simple;
        lock (_sync)
        {
            EnsureOpen();
            _active++;
            simple = _interceptors.ToArray();
            pipeline = BuildPipeline(req => _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token));
        }

        try
        {
            foreach (var interceptor in simple)
            {
                await interceptor(request);
            }

            var response = await pipeline(request);
            try
            {
                // Buffer once: the live Body stream and Content.ReadAsStringAsync()
                // are two independent consumers of the same response bytes.
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var body = new MemoryStream(bytes, writable: false);
                var lease = new StreamLease(this, body);
                response.Content = new LeasedHttpContent(new ByteArrayContent(bytes), lease);
                lock (_sync)
                {
                    _openStreams++;
                    _leases.Add(lease);
                }

                return new StreamResponse(response, body, lease);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        finally
        {
            lock (_sync)
            {
                _active--;
            }
        }
    }

    public Task<StreamResponse> GetStreamAsync(string uri, CancellationToken token = default) =>
        SendStreamAsync(new HttpRequestMessage(HttpMethod.Get, uri), token);

    /// <summary>Transport-shaped call: the returned response body stays live and owned.</summary>
    public async Task<HttpResponseMessage> RoundTripAsync(HttpRequestMessage request, CancellationToken token = default) =>
        (await SendStreamAsync(request, token)).Detach();

    public async Task<string> GetStringAsync(string uri)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri));
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Observable lifetime for capability-withdrawal tests and diagnostics.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StreamLease[] leases;
        lock (_sync)
        {
            _closed = true;
            leases = _leases.ToArray();
            _leases.Clear();
        }

        foreach (var lease in leases)
        {
            lease.Stream.Dispose();
            lease.Release();
        }

        _client.Dispose();
        _handler.Dispose();
        await ValueTask.CompletedTask;
    }

    // Called with _sync held: registrations are captured in order, so the first
    // registered link is the outermost one.
    private Func<HttpRequestMessage, Task<HttpResponseMessage>> BuildPipeline(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> terminal)
    {
        var pipeline = terminal;
        for (var index = _chain.Count - 1; index >= 0; index--)
        {
            var current = _chain[index].Interceptor;
            var inner = pipeline;
            pipeline = request => current(request, () => inner(request));
        }

        return pipeline;
    }

    private void EnsureOpen()
    {
        if (_closed)
        {
            throw new InvalidOperationException("http service closed");
        }
    }

    private sealed class ChainRegistration
    {
        public ChainRegistration(
            Func<HttpRequestMessage, Func<Task<HttpResponseMessage>>, Task<HttpResponseMessage>> interceptor) =>
            Interceptor = interceptor;

        public Func<HttpRequestMessage, Func<Task<HttpResponseMessage>>, Task<HttpResponseMessage>> Interceptor { get; }
    }

    internal sealed class StreamLease
    {
        private readonly HttpService _owner;
        private int _released;

        public StreamLease(HttpService owner, Stream stream)
        {
            _owner = owner;
            Stream = stream;
        }

        public Stream Stream { get; }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            lock (_owner._sync)
            {
                _owner._openStreams--;
                _owner._leases.Remove(this);
            }
        }
    }

    private sealed class LeasedHttpContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly StreamLease _lease;
        private int _disposed;

        public LeasedHttpContent(HttpContent inner, StreamLease lease)
        {
            _inner = inner;
            _lease = lease;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _inner.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? -1;
            return length >= 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    _lease.Release();
                }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A live HTTP response body. Closing it releases the service-owned lease.</summary>
    public sealed class StreamResponse : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly StreamLease _lease;
        private int _released;

        internal StreamResponse(HttpResponseMessage response, Stream body, StreamLease lease)
        {
            _response = response;
            _lease = lease;
            Body = body;
        }

        public int StatusCode => (int)_response.StatusCode;
        public HttpResponseHeaders Headers => _response.Headers;
        public Stream Body { get; }

        /// <summary>Hands the owned response to a transport-shaped caller.</summary>
        internal HttpResponseMessage Detach()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                throw new InvalidOperationException("stream response already released");
            }

            return _response;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try
                {
                    _response.Dispose();
                }
                finally
                {
                    _lease.Release();
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>
/// dotenv-style configuration with four-file precedence, mode selection and
/// stack-based reversible population of the process environment.
/// </summary>
public sealed class EnvService : IAsyncDisposable, IEnvService
{
    private readonly List<IDisposable> _layers = [];

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            result[key] = Environment.ExpandEnvironmentVariables(value);
        }

        return result;
    }

    /// <summary>
    /// Explicit mode wins; otherwise NODE_ENV; otherwise "development".
    /// </summary>
    public static string ResolveMode(string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("NODE_ENV");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? "development" : fromEnvironment;
    }

    /// <summary>
    /// Merges .env, .env.local, .env.{mode} and .env.{mode}.local in that order;
    /// later files win and missing files are ignored.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadFromDirectory(string directory, string? mode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var selected = ResolveMode(mode);
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { ".env", ".env.local", $".env.{selected}", $".env.{selected}.local" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var pair in Parse(File.ReadAllText(path)))
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Pushes a layer onto the process-environment stacks. The returned handle
    /// withdraws only this layer (LIFO by ownership, not by value equality).
    /// With processWins: true, variables already present are left untouched.
    /// </summary>
    public static IDisposable ApplyLayer(IReadOnlyDictionary<string, string> values, bool processWins = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        var layer = new Layer();
        foreach (var (key, value) in values)
        {
            if (processWins && Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            if (!Layer.TryPush(layer, key, value, out var error))
            {
                Layer.Unwind(layer);
                throw error!;
            }
        }

        return layer;
    }

    /// <summary>Applies a layer owned by this service and rolls it back on dispose.</summary>
    public void Apply(IReadOnlyDictionary<string, string> values, bool processWins = true) =>
        _layers.Add(ApplyLayer(values, processWins));

    public async ValueTask DisposeAsync()
    {
        for (var index = _layers.Count - 1; index >= 0; index--)
        {
            _layers[index].Dispose();
        }

        _layers.Clear();
        await ValueTask.CompletedTask;
    }

    private sealed class Layer : IDisposable
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, List<Entry>> Stacks = new(StringComparer.Ordinal);
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Unwind(this);
            }
        }

        internal static bool TryPush(Layer owner, string key, string value, out Exception? error)
        {
            error = null;
            lock (Sync)
            {
                var stacks = Stacks;
                var stack = stacks.TryGetValue(key, out var existing) ? existing : [];
                string prior;
                bool hadPrior;
                if (stack.Count > 0)
                {
                    var top = stack[^1];
                    prior = top.Value;
                    hadPrior = true;
                }
                else
                {
                    prior = Environment.GetEnvironmentVariable(key) ?? string.Empty;
                    hadPrior = Environment.GetEnvironmentVariable(key) is not null;
                }

                try
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
                catch (Exception ex)
                {
                    error = ex;
                    return false;
                }

                stack.Add(new Entry(owner, value, prior, hadPrior));
                stacks[key] = stack;
            }

            return true;
        }

        internal static void Unwind(Layer owner)
        {
            List<Exception>? errors = null;
            lock (Sync)
            {
                foreach (var (key, stack) in Stacks.ToArray())
                {
                    var index = -1;
                    for (var cursor = stack.Count - 1; cursor >= 0; cursor--)
                    {
                        if (ReferenceEquals(stack[cursor].Owner, owner))
                        {
                            index = cursor;
                            break;
                        }
                    }

                    if (index < 0)
                    {
                        continue;
                    }

                    var entry = stack[index];
                    if (index == stack.Count - 1)
                    {
                        try
                        {
                            // Never clobber a value changed externally after installation.
                            if (Environment.GetEnvironmentVariable(key) == entry.Value)
                            {
                                Environment.SetEnvironmentVariable(key, entry.HadPrior ? entry.Prior : null);
                            }
                        }
                        catch (Exception ex)
                        {
                            (errors ??= []).Add(ex);
                        }
                    }
                    else
                    {
                        // The entry above inherits this restore target.
                        stack[index + 1] = stack[index + 1] with { Prior = entry.Prior, HadPrior = entry.HadPrior };
                    }

                    stack.RemoveAt(index);
                    if (stack.Count == 0)
                    {
                        Stacks.Remove(key);
                    }
                }
            }

            if (errors is not null)
            {
                throw new AggregateException(errors);
            }
        }

        private sealed record Entry(Layer Owner, string Value, string Prior, bool HadPrior);
    }
}

/// <summary>Command dispatcher with quote-aware tokenization and aliases.</summary>
public sealed class CliDispatcher
{
    private readonly Dictionary<string, (string[] Aliases, Func<string[], string> Handler)> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in input)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    public void Register(string name, Func<string[], string> handler, params string[] aliases) =>
        _commands[name] = (aliases, handler);

    public string Dispatch(string input)
    {
        var tokens = Tokenize(input);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var command = _commands.FirstOrDefault(pair =>
            pair.Key.Equals(tokens[0], StringComparison.OrdinalIgnoreCase) ||
            pair.Value.Aliases.Contains(tokens[0], StringComparer.OrdinalIgnoreCase));
        return command.Value.Handler is null
            ? $"unknown command: {tokens[0]}"
            : command.Value.Handler(tokens.Skip(1).ToArray());
    }
}
