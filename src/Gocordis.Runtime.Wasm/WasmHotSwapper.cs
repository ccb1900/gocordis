using Gocordis.Core;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// WASM HMR: warm-load the replacement module, then withdraw the old fiber
/// before committing the new one. Stale attempts are fenced by generation.
/// </summary>
public sealed class WasmHotSwapper
{
    private readonly Gocordis.Core.Runtime _runtime;
    private readonly Key<int> _key;
    private long _generation;
    private Fiber? _current;

    public WasmHotSwapper(Gocordis.Core.Runtime runtime, Key<int> key)
    {
        _runtime = runtime;
        _key = key;
    }

    public Fiber? Current => _current;

    public async Task<Fiber> ReplaceAsync(string wat)
    {
        var generation = Interlocked.Increment(ref _generation);

        // Warm-load: a broken replacement never disturbs the running fiber.
        var candidate = WasmPluginInstance.FromWat(wat);
        candidate.Dispose();

        if (_current is not null)
        {
            await _runtime.DisposeAsync(_current);
        }

        if (generation != Interlocked.Read(ref _generation))
        {
            throw new DivertedException();
        }

        _current = await _runtime.LoadAsync(new WasmComponent(wat, _key));
        return _current;
    }
}
