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
        var consumer = new ProbeConsumer<TimerService>(ExtensionKeys.Timer);
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

        var replacement = await runtime.LoadAsync(new TimerExtensionComponent());
        Assert.Equal(FiberState.Active, replacement.State);
        await WaitForStateAsync(runtime, consumerFiber.Id, FiberState.Active);
        Assert.Null(consumerFiber.Failure);
    }

    [Fact]
    public async Task EnvDependsOnFsAndSocksDependsOnHttp()
    {
        await using var runtime = new Runtime();
        using var temp = new TempDir();

        // Consumers and intermediate providers are declared BEFORE their
        // upstream providers: every one of them must wait as Pending.
        var envConsumer = await runtime.LoadAsync(new ProbeConsumer<EnvService>(ExtensionKeys.Env));
        var socksConsumer = await runtime.LoadAsync(new ProbeConsumer<SocksRegistry>(ExtensionKeys.Socks));
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
    }

    [Fact]
    public async Task ProviderWithdrawalWithdrawsCapabilityBeforeDisposingTheService()
    {
        await using var runtime = new Runtime();
        var key = new Key<Probe>("ext-probe-order");
        bool capabilityGoneAtDispose = false;
        var service = new Probe(() =>
            capabilityGoneAtDispose = ThrowsDependencyMissing(() => runtime.RequireCapability(key)));
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

    private static bool ThrowsDependencyMissing(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (DependencyMissingException)
        {
            return true;
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

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "gocordis-ext-composition-" + Guid.NewGuid().ToString("N"));

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
