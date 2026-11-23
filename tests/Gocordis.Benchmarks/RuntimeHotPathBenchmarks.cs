using BenchmarkDotNet.Attributes;
using Gocordis.Core;

namespace Gocordis.Benchmarks;

/// <summary>Call overhead of a stable capability endpoint (no Core lock on the
/// hot path once the consumer cached the endpoint).</summary>
[MemoryDiagnoser]
public class CapabilityInvokeBenchmarks
{
    private static readonly CapabilityId Id = new("bench", "invoke");
    private DelegatingCapabilityEndpoint _endpoint = null!;
    private CapabilityEnvelope _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        _request = CapabilityEnvelope.Text("x");
        _endpoint = new DelegatingCapabilityEndpoint(
            Id,
            new CapabilityContract(Id, CapabilityValueKind.Endpoint, null),
            (_, _) => ValueTask.FromResult(CapabilityEnvelope.Text("ok")));
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _endpoint.DisposeAsync();

    [Benchmark]
    public async Task InvokeAsync() => await _endpoint.InvokeAsync(_request, default);
}

/// <summary>Cost of provider withdrawal with a fixed number of dependent
/// consumers (uses Runtime's reverse consumer index).</summary>
[MemoryDiagnoser]
public class ProviderWithdrawalBenchmarks
{
    [Params(10, 100)]
    public int Consumers { get; set; }

    private Runtime _runtime = null!;
    private Fiber _provider = null!;

    [IterationSetup]
    public async Task Setup()
    {
        _runtime = new Runtime();
        var key = new Key<int>("withdraw");
        _provider = await _runtime.LoadAsync(new BenchProvider(key));
        for (var i = 0; i < Consumers; i++)
        {
            var consumer = await _runtime.LoadAsync(new BenchConsumer(key));
            if (consumer.State != FiberState.Active)
            {
                throw new InvalidOperationException("consumer did not activate");
            }
        }
    }

    [IterationCleanup]
    public async Task Cleanup() => await _runtime.DisposeAsync();

    [Benchmark]
    public async Task Withdraw() => await _runtime.DisposeAsync(_provider);

    private sealed class BenchProvider : Component
    {
        private readonly Key<int> _key;
        public BenchProvider(Key<int> key) => _key = key;
        public override string Name => "provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, 1);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class BenchConsumer : Component
    {
        private readonly Key<int> _key;
        public BenchConsumer(Key<int> key) => _key = key;
        public override string Name => "consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _ = context.Require(_key);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
