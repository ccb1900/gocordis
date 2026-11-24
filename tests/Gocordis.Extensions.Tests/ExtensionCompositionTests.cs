using System.Net;
using System.Net.Sockets;
using System.Text;
using Gocordis.Core;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>
/// Behavior tests for the pluginized extension layer. These drive the real
/// Runtime decision loop — constructing a service directly is deliberately NOT
/// accepted as evidence of capability composition.
/// </summary>
public sealed class ExtensionCompositionTests
{
    [Fact]
    public async Task ConsumerStaysPendingUntilTimerProviderArrivesThenWithdrawsAndReactivates()
    {
        await using var runtime = new Runtime();
        var consumer = new ProbeConsumer<ITimerService>(ExtensionKeys.Timer);
        var consumerFiber = await runtime.LoadAsync(consumer);
        Assert.Equal(FiberState.Pending, consumerFiber.State);
        Assert.False(consumer.Applied);

        var providerFiber = await runtime.LoadAsync(new TimerExtensionComponent());
        Assert.Equal(FiberState.Active, providerFiber.State);
        await WaitForStateAsync(runtime, consumerFiber.Id, FiberState.Active);
        Assert.True(consumer.Applied);
        Assert.NotNull(runtime.RequireCapability(ExtensionKeys.Timer));

        await runtime.DisposeAsync(providerFiber);
        await WaitForStateAsync(runtime, consumerFiber.Id, FiberState.Pending);
        Assert.Null(consumerFiber.Failure);
        Assert.Throws<DependencyMissingException>(() => runtime.RequireCapability(ExtensionKeys.Timer));

        consumer.Reset();
        var replacement = await runtime.LoadAsync(new TimerExtensionComponent());
        Assert.Equal(FiberState.Active, replacement.State);
        await WaitForStateAsync(runtime, consumerFiber.Id, FiberState.Active);
        Assert.True(consumer.Applied, "reactivation must run ApplyAsync again");
        Assert.Null(consumerFiber.Failure);
    }

    [Fact]
    public async Task EnvDependsOnFsAndSocksDependsOnHttp()
    {
        await using var runtime = new Runtime();
        using var temp = new TempDir();
        var variable = "GOCORDIS_EXT_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        await File.WriteAllTextAsync(Path.Combine(temp.Root, ".env"), $"{variable}=applied\n");
        Assert.Null(Environment.GetEnvironmentVariable(variable));

        // Consumers and intermediate providers are declared BEFORE their
        // upstream providers: every one of them must wait as Pending.
        var envConsumer = await runtime.LoadAsync(new ProbeConsumer<IEnvService>(ExtensionKeys.Env));
        var socksConsumer = await runtime.LoadAsync(new ProbeConsumer<ISocksRegistry>(ExtensionKeys.Socks));
        var envFiber = await runtime.LoadAsync(new EnvExtensionComponent(temp.Root));
        var socksFiber = await runtime.LoadAsync(new SocksExtensionComponent());
        Assert.Equal(FiberState.Pending, envConsumer.State);
        Assert.Equal(FiberState.Pending, socksConsumer.State);
        Assert.Equal(FiberState.Pending, envFiber.State);
        Assert.Equal(FiberState.Pending, socksFiber.State);

        var fsFiber = await runtime.LoadAsync(new FileSystemExtensionComponent());
        var httpFiber = await runtime.LoadAsync(new HttpExtensionComponent());
        Assert.Equal(FiberState.Active, fsFiber.State);
        Assert.Equal(FiberState.Active, httpFiber.State);
        await WaitForStateAsync(runtime, envFiber.Id, FiberState.Active);
        await WaitForStateAsync(runtime, socksFiber.Id, FiberState.Active);
        await WaitForStateAsync(runtime, envConsumer.Id, FiberState.Active);
        await WaitForStateAsync(runtime, socksConsumer.Id, FiberState.Active);
        Assert.Equal("applied", Environment.GetEnvironmentVariable(variable));

        var http = runtime.RequireCapability(ExtensionKeys.Http);
        Assert.Same(http, runtime.RequireCapability(ExtensionKeys.Socks).Http);

        await runtime.DisposeAsync(httpFiber);
        await WaitForStateAsync(runtime, socksFiber.Id, FiberState.Pending);
        await WaitForStateAsync(runtime, socksConsumer.Id, FiberState.Pending);
        Assert.Null(socksConsumer.Failure);
        await runtime.DisposeAsync(fsFiber);
        await WaitForStateAsync(runtime, envFiber.Id, FiberState.Pending);
        await WaitForStateAsync(runtime, envConsumer.Id, FiberState.Pending);
        Assert.Null(envConsumer.Failure);
        Assert.True(Environment.GetEnvironmentVariable(variable) is null,
            "withdrawn env layer must be unwound");
    }

    [Fact]
    public async Task SocksContributionRoutesAnHttpRequestAndUnwindsOnProviderWithdrawal()
    {
        await using var runtime = new Runtime();
        await using var fake = new FakeSocks5();
        var httpFiber = await runtime.LoadAsync(new HttpExtensionComponent());
        var socksFiber = await runtime.LoadAsync(new SocksExtensionComponent());
        var http = runtime.RequireCapability(ExtensionKeys.Http);
        var socks = runtime.RequireCapability(ExtensionKeys.Socks);
        // The SOCKS fiber registers its dialer when it becomes Active.
        Assert.Equal(1, http.ProxyCount);

        socks.Register("socks5", $"127.0.0.1:{fake.Port}");
        Assert.Equal(1, http.ProxyCount);

        var request = http.GetStringAsync("http://example.invalid/probe");
        _ = request.ContinueWith(static _ => { }, TaskContinuationOptions.OnlyOnFaulted);
        var winner = await Task.WhenAny(fake.ConnectSeen.Task, request)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fake.ConnectSeen.Task.IsCompleted,
            $"request did not traverse proxy; request state={request.Status}, error={request.Exception?.InnerException?.Message}");
        var seen = await fake.ConnectSeen.Task;
        // The fake SOCKS server only records a CONNECT when an HTTP request was
        // actually routed through the registered proxy dialer.
        Assert.False(string.IsNullOrWhiteSpace(seen.Host));
        Assert.Equal(80, seen.Port);

        // The proxy registration is owned by the SOCKS fiber, not by HTTP.
        await runtime.DisposeAsync(socksFiber);
        Assert.Equal(0, http.ProxyCount);
        Assert.Equal(FiberState.Active, httpFiber.State);
    }

    [Fact]
    public async Task MemoryProviderSupportsReadWriteWatchAndUnmount()
    {
        await using var fs = new FileSystemService();
        fs.MountMemory("/mem");
        await fs.WriteAllTextAsync("/mem/a.txt", "hello");
        Assert.Equal("hello", await fs.ReadAllTextAsync("/mem/a.txt"));

        var observed = 0;
        using var watch = fs.Watch("/mem/a.txt", _ => observed++);
        await fs.WriteAllTextAsync("/mem/a.txt", "world");
        await WaitForAsync(() => Volatile.Read(ref observed) > 0);
        Assert.True(observed > 0);

        Assert.True(fs.Unmount("/mem"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fs.ReadAllTextAsync("/mem/a.txt"));
    }

    [Fact]
    public async Task MountScopedHandlesAreIdentitySafeOnRemount()
    {
        using var temp = new TempDir();
        var firstRoot = Path.Combine(temp.Root, "first");
        var secondRoot = Path.Combine(temp.Root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        await using var fs = new FileSystemService();
        using var first = fs.MountScoped("/data", firstRoot);
        using var second = fs.MountScoped("/data", secondRoot);
        await fs.WriteAllTextAsync("/data/answer.txt", "second");

        first.Dispose(); // must not unmount the newer registration
        Assert.Equal("second", await fs.ReadAllTextAsync("/data/answer.txt"));
        Assert.True(fs.Unmount("/data"));
    }

    [Fact]
    public async Task WatchOwnedAsyncUnwindsWithItsOwnerAndStopsCallbacks()
    {
        await using var runtime = new Runtime();
        using var temp = new TempDir();
        var path = Path.Combine(temp.Root, "watched.txt");
        await File.WriteAllTextAsync(path, "one");
        var provider = await runtime.LoadAsync(new WatchExtensionComponent());
        var watch = runtime.RequireCapability(ExtensionKeys.Watch);
        var observed = 0;
        var owner = await runtime.LoadAsync(new WatchOwner(watch, path, _ => observed++));
        Assert.Equal(FiberState.Active, owner.State);

        await File.WriteAllTextAsync(path, "two");
        await WaitForAsync(() => Volatile.Read(ref observed) > 0);

        await runtime.DisposeAsync(owner);
        var before = Volatile.Read(ref observed);
        await File.WriteAllTextAsync(path, "three");
        await Task.Delay(100);
        Assert.True(Volatile.Read(ref observed) == before,
            "withdrawn watcher must not deliver changes");
        Assert.Equal(FiberState.Active, provider.State);
    }

    [Fact]
    public async Task ProxyRegistrationsAreIdentityFencedAndLastWins()
    {
        await using var service = new HttpService();
        await using var first = new FakeSocks5();
        await using var second = new FakeSocks5();
        var firstHandle = service.RegisterProxyDialer(["http"],
            (h, p, t) => first.ConnectAsync("socks5", h, p, t));
        var secondHandle = service.RegisterProxyDialer(["http"],
            (h, p, t) => second.ConnectAsync("socks5", h, p, t));
        Assert.Equal(2, service.ProxyCount);

        var request = service.GetStringAsync("http://example.invalid/winner");
        _ = request.ContinueWith(static _ => { }, TaskContinuationOptions.OnlyOnFaulted);
        await second.ConnectSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.ConnectSeen.Task.IsCompleted);

        // Removing the older handle must not remove the newer winner.
        firstHandle.Dispose();
        Assert.Equal(1, service.ProxyCount);
        secondHandle.Dispose();
        Assert.Equal(0, service.ProxyCount);
    }

    [Fact]
    public async Task ProviderWithdrawalWithdrawsCapabilityBeforeDisposingTheService()
    {
        await using var runtime = new Runtime();
        var key = new Key<Probe>("ext-probe-order");
        bool capabilityGoneAtDispose = false;
        // HasProvided inspects the binding table without looking at fiber state,
        // so this fails if disposal is installed/undone before withdrawal.
        var service = new Probe(() => capabilityGoneAtDispose = !runtime.HasProvided(key));
        var provider = new ProbeProvider(key, service);

        var fiber = await runtime.LoadAsync(provider);
        Assert.Same(service, runtime.RequireCapability(key));
        await runtime.DisposeAsync(fiber);

        Assert.True(capabilityGoneAtDispose, "capability must be withdrawn before the service is disposed");
        Assert.Throws<DependencyMissingException>(() => runtime.RequireCapability(key));
    }

    [Fact]
    public async Task OfficialHttpProviderDisposesItsServiceOnWithdrawal()
    {
        await using var runtime = new Runtime();
        var provider = await runtime.LoadAsync(new HttpExtensionComponent());
        var service = runtime.RequireCapability(ExtensionKeys.Http);
        Assert.False(service.IsDisposed);

        await runtime.DisposeAsync(provider);
        Assert.True(service.IsDisposed);
        Assert.Throws<DependencyMissingException>(() => runtime.RequireCapability(ExtensionKeys.Http));
    }

    [Fact]
    public async Task PublishDisposesTheServiceOnceWhenBindingFails()
    {
        await using var runtime = new Runtime();
        var key = new Key<CountingService>("ext-one-shot");
        var service = new CountingService();
        var fiber = await runtime.LoadAsync(new DuplicateBindProvider(key, service));

        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.Equal(1, service.DisposeCount);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static async Task WaitForStateAsync(Runtime runtime, Guid fiberId, FiberState expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var observed = runtime.Snapshot().Fibers.FirstOrDefault(fiber => fiber.Id == fiberId);
            if (observed?.State == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"fiber {fiberId} did not reach {expected} within 5s");
    }

    private sealed class ProbeConsumer<T> : Component where T : notnull
    {
        private readonly Key<T> _key;

        public ProbeConsumer(Key<T> key) => _key = key;

        public bool Applied { get; private set; }

        public void Reset() => Applied = false;

        public override string Name => "probe-timer-consumer";

        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Assert.NotNull(context.Require(_key));
            Applied = true;
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class ProbeProvider : Component
    {
        private readonly Key<Probe> _key;
        private readonly Probe _service;

        public ProbeProvider(Key<Probe> key, Probe service)
        {
            _key = key;
            _service = service;
        }

        public override string Name => "probe-provider";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ExtensionPublish.PublishAsync(context, _key, _service);
    }

    private sealed class WatchOwner : Component
    {
        private readonly IWatchService _watch;
        private readonly string _path;
        private readonly Action<WatchEvent> _onEvent;

        public WatchOwner(IWatchService watch, string path, Action<WatchEvent> onEvent)
        {
            _watch = watch;
            _path = path;
            _onEvent = onEvent;
        }

        public override string Name => "watch-owner";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _watch.WatchOwnedAsync(context, _path, _onEvent);
            return null;
        }
    }

    private sealed class DuplicateBindProvider : Component
    {
        private readonly Key<CountingService> _key;
        private readonly CountingService _service;

        public DuplicateBindProvider(Key<CountingService> key, CountingService service)
        {
            _key = key;
            _service = service;
        }

        public override string Name => "duplicate-bind-provider";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _service);
            return await ExtensionPublish.PublishAsync(context, _key, () => _service);
        }
    }

    private sealed class CountingService : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Probe : IAsyncDisposable
    {
        private readonly Action _onDispose;

        public Probe(Action onDispose) => _onDispose = onDispose;

        public ValueTask DisposeAsync()
        {
            _onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSocks5 : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _loop;

        public FakeSocks5()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public TaskCompletionSource<(string Host, int Port)> ConnectSeen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Stream> ConnectAsync(string scheme, string host, int port, CancellationToken token)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, Port, token);
                var stream = client.GetStream();
                await stream.WriteAsync(new byte[] { 5, 1, 0 }, token);
                var choice = await ReadExactlyAsync(stream, 2, token);
                if (choice[0] != 5 || choice[1] != 0)
                {
                    throw new InvalidOperationException("SOCKS5 method rejected");
                }

                var header = new byte[] { 5, 1, 0, 3, (byte)host.Length };
                await stream.WriteAsync(header, token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(host), token);
                await stream.WriteAsync(new byte[] { (byte)(port >> 8), (byte)port }, token);
                var reply = await ReadExactlyAsync(stream, 10, token);
                if (reply[1] != 0)
                {
                    throw new InvalidOperationException($"SOCKS5 connect rejected: {reply[1]}");
                }

                ConnectSeen.TrySetResult((host, port));
                return stream;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private async Task LoopAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stream = client.GetStream();
                            await ReadExactlyAsync(stream, 3, CancellationToken.None);
                            await stream.WriteAsync(new byte[] { 5, 0 });
                            var header = await ReadExactlyAsync(stream, 4, CancellationToken.None);
                            var atyp = header[3];
                            var length = atyp switch { 1 => 4, 4 => 16, 3 => 1, _ => 0 };
                            if (length == 0)
                            {
                                return;
                            }

                            var address = await ReadExactlyAsync(stream, length, CancellationToken.None);
                            string target;
                            if (atyp == 3)
                            {
                                var nameBytes = await ReadExactlyAsync(stream, address[0], CancellationToken.None);
                                target = Encoding.ASCII.GetString(nameBytes);
                            }
                            else
                            {
                                target = atyp == 1
                                    ? new IPAddress(address).ToString()
                                    : new IPAddress(address).ToString();
                            }

                            var portBytes = await ReadExactlyAsync(stream, 2, CancellationToken.None);
                            ConnectSeen.TrySetResult((target, (portBytes[0] << 8) | portBytes[1]));
                            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 });
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                        catch
                        {
                            // handshake failures are observed by the dialer side
                        }
                        finally
                        {
                            client.Dispose();
                        }
                    });
                }
            }
            catch
            {
                // listener shutdown is expected
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token);
                if (read == 0)
                {
                    throw new EndOfStreamException("fake SOCKS5 peer closed early");
                }

                offset += read;
            }

            return buffer;
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "gocordis-ext-composition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
