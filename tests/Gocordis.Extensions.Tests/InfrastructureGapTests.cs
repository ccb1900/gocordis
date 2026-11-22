using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Gocordis.Extensions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Batch-3 infrastructure gap acceptance tests. Mirrors the (new) acceptance
/// rows in docs/migration/feature-matrix.md for fs/env/http/http-socks/server.
/// </summary>
public sealed class InfrastructureGapTests
{
    [Fact]
    public async Task FsFullConformance()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gocordis-fs-gap-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var outside = Path.Combine(temp, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            await using var fs = new FileSystemService();
            fs.Mount("data", root);

            // Mkdir / WriteAllText / ReadAllBytes / List.
            await fs.MkdirAsync("data/nested/deeper", recursive: true);
            await fs.WriteAllTextAsync("data/nested/deeper/file.txt", "0123456789");
            Assert.Equal("0123456789", await fs.ReadAllTextAsync("data/nested/deeper/file.txt"));
            Assert.Equal("0123456789"u8.ToArray(), await fs.ReadAllBytesAsync("data/nested/deeper/file.txt"));
            Assert.Contains("file.txt", await fs.ListAsync("data/nested/deeper"));

            // Range: offset/limit are honoured, negative limit reads to end.
            Assert.Equal("2345"u8.ToArray(), await fs.ReadRangeAsync("data/nested/deeper/file.txt", 2, 4));
            Assert.Equal("56789"u8.ToArray(), await fs.ReadRangeAsync("data/nested/deeper/file.txt", 5, -1));
            Assert.Empty(await fs.ReadRangeAsync("data/nested/deeper/file.txt", 999, 4));

            // Copy / Move / Remove.
            await fs.CopyAsync("data/nested/deeper/file.txt", "data/copied.txt");
            Assert.Equal("0123456789", await fs.ReadAllTextAsync("data/copied.txt"));
            await fs.MoveAsync("data/copied.txt", "data/moved.txt");
            Assert.False(await fs.ExistsAsync("data/copied.txt"));
            Assert.True(await fs.ExistsAsync("data/moved.txt"));
            await fs.RemoveAsync("data/moved.txt");
            Assert.False(await fs.ExistsAsync("data/moved.txt"));

            // Every new operation still goes through the mount boundary.
            await Assert.ThrowsAsync<InvalidOperationException>(() => fs.RemoveAsync("data/../outside/victim.txt"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fs.MkdirAsync("data/../outside/newdir"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fs.CopyAsync("data/nested/deeper/file.txt", "data/../outside/copied.txt"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fs.ReadRangeAsync("data/../outside/file.txt", 0, 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fs.ReadAllBytesAsync("data/../outside/file.txt"));
            Assert.False(Directory.Exists(Path.Combine(outside, "newdir")));

            // Symlinks that escape the mount are rejected for the new paths too.
            var link = Path.Combine(root, "escape");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return; // platform cannot create directory symlinks
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fs.CopyAsync("data/escape/secret.txt", "data/dest.txt"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task EnvFourFilePriorityAndRevert()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gocordis-env-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string shared = "GOCORDIS_ENV_GAP_SHARED";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".env"), $"ONLY_BASE=base\n{shared}=base\n");
            await File.WriteAllTextAsync(Path.Combine(directory, ".env.local"), $"ONLY_LOCAL=local\n{shared}=local\n");
            await File.WriteAllTextAsync(Path.Combine(directory, ".env.prod"), $"ONLY_MODE=prod\n{shared}=prod\n");
            await File.WriteAllTextAsync(Path.Combine(directory, ".env.prod.local"),
                $"ONLY_MODE_LOCAL=prod-local\n{shared}=prod-local\n");

            var loaded = EnvService.LoadFromDirectory(directory, mode: "prod");
            Assert.Equal("base", loaded["ONLY_BASE"]);
            Assert.Equal("local", loaded["ONLY_LOCAL"]);
            Assert.Equal("prod", loaded["ONLY_MODE"]);
            Assert.Equal("prod-local", loaded["ONLY_MODE_LOCAL"]);
            Assert.Equal("prod-local", loaded[shared]); // later file wins

            Assert.Equal("stage", EnvService.ResolveMode("stage"));
            Assert.Throws<ArgumentNullException>(() => EnvService.LoadFromDirectory(null!));

            var layer = EnvService.ApplyLayer(loaded, processWins: false);
            Assert.Equal("prod-local", Environment.GetEnvironmentVariable(shared));
            layer.Dispose();
            Assert.Null(Environment.GetEnvironmentVariable(shared));
            layer.Dispose(); // idempotent
        }
        finally
        {
            Environment.SetEnvironmentVariable(shared, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnvApplyLayersUnwindOnlyTheirOwnValues()
    {
        const string key = "GOCORDIS_ENV_GAP_STACK";
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "original");
            var first = EnvService.ApplyLayer(
                new Dictionary<string, string> { [key] = "first" }, processWins: false);
            var second = EnvService.ApplyLayer(
                new Dictionary<string, string> { [key] = "second" }, processWins: false);

            Assert.Equal("second", Environment.GetEnvironmentVariable(key));
            first.Dispose(); // not on top: retire only this contribution
            Assert.Equal("second", Environment.GetEnvironmentVariable(key));
            second.Dispose();
            Assert.Equal("original", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public async Task HttpStateStreamCloseConformance()
    {
        await using var server = new ServerHost();
        server.MapGet("/payload", () => new System.Text.Json.Nodes.JsonObject { ["value"] = "abc" });
        await server.StartAsync();
        var baseUri = new Uri(server.Address!);

        await using (var http = new HttpService())
        {
            http.SetBaseAddress(baseUri);
            Assert.False(http.State().Closed);
            Assert.Equal(0, http.State().Active);
            Assert.Equal(0, http.State().OpenStreams);

            // Stream: request completion and stream ownership are distinct.
            var stream = await http.GetStreamAsync("/payload");
            Assert.Equal(1, http.State().OpenStreams);
            Assert.Equal(0, http.State().Active);
            Assert.Contains("abc", await new StreamReader(stream.Body).ReadToEndAsync());
            await stream.DisposeAsync();
            Assert.Equal(0, http.State().OpenStreams);
            await stream.DisposeAsync(); // idempotent
            Assert.Equal(0, http.State().OpenStreams);

            // RoundTrip: disposing the returned response releases the lease.
            var response = await http.RoundTripAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/payload")));
            Assert.Equal(1, http.State().OpenStreams);
            Assert.Contains("abc", await response.Content.ReadAsStringAsync());
            response.Dispose();
            Assert.Equal(0, http.State().OpenStreams);
        }

        // Ordered next-chain, short-circuit, and observation after the service closes.
        await using var chained = new HttpService();
        chained.SetBaseAddress(baseUri);
        var order = new List<string>();
        using var outer = chained.AddInterceptorChain(async (_, next) =>
        {
            order.Add("outer-in");
            var response = await next();
            order.Add("outer-out");
            return response;
        });
        using var inner = chained.AddInterceptorChain(async (_, next) =>
        {
            order.Add("inner-in");
            var response = await next();
            order.Add("inner-out");
            return response;
        });

        Assert.Contains("abc", await chained.GetStringAsync("/payload"));
        Assert.Equal(new[] { "outer-in", "inner-in", "inner-out", "outer-out" }, order);

        using var shortCircuit = chained.AddInterceptorChain((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("blocked") }));
        Assert.Equal("blocked", await chained.GetStringAsync("/payload"));
        Assert.Equal(0, chained.State().Active);
        Assert.Equal(0, chained.State().OpenStreams);
        await chained.DisposeAsync();
        Assert.True(chained.State().Closed);
    }

    [Fact]
    public async Task SocksProxyRegistrationAndUndo()
    {
        Assert.Equal("socks5", SocksRegistry.NormalizeScheme("socks"));
        Assert.Equal("socks5", SocksRegistry.NormalizeScheme("SOCKS5H"));
        Assert.Equal("socks5://127.0.0.1:1080", SocksRegistry.ProxyUrl("socks", "127.0.0.1:1080"));
        Assert.Throws<NotSupportedException>(() => SocksRegistry.NormalizeScheme("socks4"));
        Assert.Throws<ArgumentException>(() => SocksRegistry.ProxyUrl("socks5", ""));

        await using var proxy = new FakeSocks5();
        await using var registry = new SocksRegistry();

        Assert.Null(registry.ResolveAddress("socks5"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ConnectAsync("socks5", "target.example", 8080));

        var registration = registry.Register("socks5", $"127.0.0.1:{proxy.Port}");
        Assert.Equal($"socks5://127.0.0.1:{proxy.Port}", registry.ResolveAddress("socks5"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var tunnel = await registry.ConnectAsync("socks5", "target.example", 8080, cts.Token))
        {
            var seen = await proxy.ConnectSeen.Task.WaitAsync(cts.Token);
            Assert.Equal(("target.example", 8080), seen);
        }

        // Registration identity, not value equality, decides what is withdrawn.
        var other = registry.Register("socks5", "127.0.0.1:1099");
        registration.Dispose();
        Assert.Equal("socks5://127.0.0.1:1099", registry.ResolveAddress("socks5"));
        other.Dispose();
        Assert.Null(registry.ResolveAddress("socks5"));
        registration.Dispose(); // idempotent
    }

    [Fact]
    public async Task SocksConnectSendsIpv6AtypAndPunycode()
    {
        await using var proxy = new FakeSocks5();
        await using var registry = new SocksRegistry();
        using var registration = registry.Register("socks5", $"127.0.0.1:{proxy.Port}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // An IPv6 literal must be encoded as ATYP=4 with 16 address bytes,
        // not forwarded as a domain name (ATYP=3).
        await using (var tunnel = await registry.ConnectAsync("socks5", "::1", 443, cts.Token))
        {
            Assert.NotNull(tunnel);
        }

        var ipv6 = Assert.Single(proxy.Requests);
        Assert.Equal(4, ipv6.Atyp);
        Assert.Equal(IPAddress.IPv6Loopback.GetAddressBytes(), ipv6.Address);
        Assert.Equal("::1", ipv6.Host);
        Assert.Equal(443, ipv6.Port);

        // Non-ASCII names must be IDNA-encoded before the length check and the
        // DOMAINNAME request, so the proxy sees the punycode form.
        await using (var tunnel = await registry.ConnectAsync("socks5", "bücher.example", 8080, cts.Token))
        {
            Assert.NotNull(tunnel);
        }

        var requests = proxy.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        var idn = requests[1];
        Assert.Equal(3, idn.Atyp);
        Assert.Equal("xn--bcher-kva.example", Encoding.ASCII.GetString(idn.Address));
        Assert.Equal("xn--bcher-kva.example", idn.Host);
        Assert.Equal(8080, idn.Port);
    }

    [Fact]
    public async Task SocksRegistryParsesBracketedIpv6Proxy()
    {
        // Brackets belong to URL syntax only; the dialer must see the bare
        // address or TcpClient tries to resolve "[::1]" as a host name.
        Assert.Equal("socks5://::1:1080", SocksRegistry.ProxyUrl("socks5", "[::1]:1080"));
        Assert.Equal("socks5://2001:db8::1:1080", SocksRegistry.ProxyUrl("socks5h", "[2001:db8::1]:1080"));

        await using var registry = new SocksRegistry();
        using var handle = registry.Register("socks5", "[::1]:1080");
        Assert.Equal("socks5://::1:1080", registry.ResolveAddress("socks5"));
        Assert.NotNull(registry.Resolve("socks5"));

        handle.Dispose();
        Assert.Null(registry.ResolveAddress("socks5"));
    }

    [Fact]
    public async Task ServerAclProxyWsConformance()
    {
        Assert.Equal("api.example.com", HostAcl.StripPort("API.Example.com:443"));
        Assert.True(HostAcl.IsAllowed("localhost:5173", ["example.com"], [])); // loopback exemption
        Assert.True(HostAcl.IsAllowed("api.example.com", [".example.com"], [])); // apex + subdomains
        Assert.False(HostAcl.IsAllowed("evil.example", [], [])); // empty allow = loopback only
        Assert.False(HostAcl.IsAllowed("api.example.com", [".example.com"], [".example.com"])); // deny wins

        await using var guard = new ServerRouteGuard();

        // Static route guard is reversible and scoped to its route key.
        var staticKey = ServerRouteGuard.StaticRoute("/assets");
        var check = guard.Add(_ => false, staticKey);
        Assert.False(guard.IsAllowed("GET", "/assets/app.js", "example.com"));
        Assert.True(guard.IsAllowed("GET", "/api/data", "example.com"));
        check.Dispose();
        Assert.True(guard.IsAllowed("GET", "/assets/app.js", "example.com"));

        // WS route guard: a method-less key overrides every method on that path.
        using (guard.Add(_ => false, ServerRouteGuard.WebSocketRoute("/ws")))
        {
            Assert.False(guard.IsAllowed("GET", "/ws", "example.com"));
            Assert.True(guard.IsAllowed("GET", "/other", "example.com"));
        }

        // ACL policy as a reversible guard; denial is a 404 through the middleware.
        var policy = HostAcl.Policy([".example.com"], ["blocked.example.com"]);
        using (guard.Add(policy))
        {
            Assert.False(guard.IsAllowed("GET", "/anything", "blocked.example.com"));
            Assert.True(guard.IsAllowed("GET", "/anything", "api.example.com"));
        }

        using (guard.Add(policy))
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/assets/app.js";
            context.Request.Host = new HostString("blocked.example.com:443");
            var reached = false;
            var middleware = guard.Middleware(_ =>
            {
                reached = true;
                return Task.CompletedTask;
            });
            await middleware(context);
            Assert.Equal(404, context.Response.StatusCode);
            Assert.False(reached);
        }
    }

    private sealed class FakeSocks5 : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _loop;

        public int Port { get; }
        public TaskCompletionSource<(string Host, int Port)> ConnectSeen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<CapturedConnect> Requests { get; } = new();

        public sealed record CapturedConnect(byte Atyp, byte[] Address, string Host, int Port);

        public FakeSocks5()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    try
                    {
                        await HandleAsync(client);
                    }
                    catch (Exception ex)
                    {
                        ConnectSeen.TrySetException(ex);
                    }
                }
            }
            catch
            {
                // listener shutdown interrupts the pending accept
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var greeting = await ReadExactlyAsync(stream, 3);
            if (greeting[0] != 5)
            {
                return;
            }

            await stream.WriteAsync(new byte[] { 5, 0 });
            var header = await ReadExactlyAsync(stream, 4);
            var atyp = header[3];
            byte[] address;
            string host;
            switch (atyp)
            {
                case 1:
                    address = await ReadExactlyAsync(stream, 4);
                    host = new IPAddress(address).ToString();
                    break;
                case 4:
                    address = await ReadExactlyAsync(stream, 16);
                    host = new IPAddress(address).ToString();
                    break;
                case 3:
                    var length = (await ReadExactlyAsync(stream, 1))[0];
                    address = await ReadExactlyAsync(stream, length);
                    host = Encoding.ASCII.GetString(address);
                    break;
                default:
                    throw new InvalidOperationException($"unexpected SOCKS5 address type {atyp}");
            }

            var portBytes = await ReadExactlyAsync(stream, 2);
            var port = (portBytes[0] << 8) | portBytes[1];
            Requests.Enqueue(new CapturedConnect(atyp, address, host, port));
            ConnectSeen.TrySetResult((host, port));
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 });
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
                if (read == 0)
                {
                    throw new EndOfStreamException("fake SOCKS5 peer closed early");
                }

                offset += read;
            }

            return buffer;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch
            {
                // listener shutdown is expected to interrupt an accept
            }
        }
    }
}
