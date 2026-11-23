using BenchmarkDotNet.Attributes;
using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Benchmarks;

/// <summary>Benchmark host owns BOTH the Runtime and the Registry. Registry
/// does not own the Runtime's decision loop; leaking it would pollute repeated
/// benchmark iterations.</summary>
internal sealed class BenchmarkHost : IAsyncDisposable
{
    public Runtime Runtime { get; } = new();
    public PluginRegistry Registry { get; }

    public BenchmarkHost(Key<int>[] keys)
    {
        Registry = new PluginRegistry(Runtime);
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            Registry.RegisterFactory($"bench-{index}", _ => new BenchComponent(
                $"p-{index}",
                inject: index == 0 ? [] : [keys[index - 1]],
                provide: [keys[index]]));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Registry.DisposeAsync();
        await Runtime.DisposeAsync();
    }
}

/// <summary>
/// Measures declaration-reconciliation COST only (the control plane), never
/// capability invocation throughput. A chain of plugins with typed keys gives
/// the sorter a real topological dependency graph.
/// </summary>
[MemoryDiagnoser]
public class PluginRegistryApplyBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int PluginCount { get; set; }

    public PluginManifest[] Declarations { get; private set; } = [];
    public Key<int>[] Keys { get; private set; } = [];

    [GlobalSetup]
    public void Setup()
    {
        Keys = Enumerable.Range(0, PluginCount).Select(i => new Key<int>($"bench-{i}")).ToArray();
        Declarations = Enumerable.Range(0, PluginCount)
            .Select(i => new PluginManifest(
                $"p-{i}", $"bench-{i}", true, new Dictionary<string, object?>(),
                i == 0 ? [] : [$"p-{i - 1}"], [], "1.0.0"))
            .ToArray();
    }

    /// <summary>Cold apply: new registry, full factory + sort + fiber activation.</summary>
    [Benchmark]
    public async Task ColdApply()
    {
        await using var host = new BenchmarkHost(Keys);
        await host.Registry.ApplyAsync(Declarations);
    }


}

/// <summary>
/// Steady-state apply: the registry already owns every fiber; only the second,
/// unchanged ApplyAsync is measured (fingerprint reuse + sorter path).
/// </summary>
[MemoryDiagnoser]
public class PluginRegistrySteadyApplyBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int PluginCount { get; set; }

    private PluginRegistryApplyBenchmarks _inner = null!;
    private BenchmarkHost _host = null!;
    private PluginManifest[] _declarations = [];

    [GlobalSetup]
    public async Task Setup()
    {
        _inner = new PluginRegistryApplyBenchmarks { PluginCount = PluginCount };
        _inner.Setup();
        _host = new BenchmarkHost(_inner.Keys);
        await _host.Registry.ApplyAsync(_inner.Declarations);
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _host.DisposeAsync();

    [Benchmark]
    public async Task SteadyApply() =>
        await _host.Registry.ApplyAsync(
            _inner.Declarations.Select(manifest => manifest with { }).ToArray());
}

internal sealed class BenchComponent : Component
{
    private readonly IReadOnlyList<ICapabilityKey> _inject;
    private readonly IReadOnlyList<ICapabilityKey> _provide;

    public BenchComponent(
        string name, IReadOnlyList<ICapabilityKey> inject, IReadOnlyList<ICapabilityKey> provide)
    {
        Name = name;
        _inject = inject;
        _provide = provide;
    }

    public override string Name { get; }
    public override IReadOnlyList<ICapabilityKey> Inject() => _inject;
    public override IReadOnlyList<ICapabilityKey> Provide() => _provide;

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        ValueTask.FromResult<Cleanup?>(null);
}
