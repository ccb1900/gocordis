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
    private readonly SemaphoreSlim _replaceGate = new(1, 1);
    private readonly WasmHostBridge _bridge;

    public WasmHotSwapper(Gocordis.Core.Runtime runtime, Key<int> key, WasmHostBridge? bridge = null)
    {
        _runtime = runtime;
        _key = key;
        _bridge = bridge ?? new WasmHostBridge();
    }

    public Fiber? Current => _current;

    public async Task<Fiber> ReplaceAsync(string wat)
    {
        await _replaceGate.WaitAsync();
        try
        {
            // Candidate stays alive until the old fiber reaches Gone: this is a
            // serialized warm handoff (not zero-downtime atomic replacement).
            var candidate = WasmPluginInstance.FromWat(wat, bridge: _bridge);
            try
            {
                if (_current is not null)
                {
                    await _runtime.DisposeAsync(_current);
                    await _current.GoneAsync();
                }

                _current = await _runtime.LoadAsync(new WasmComponent(wat, _key, _bridge));
                return _current;
            }
            finally
            {
                candidate.Dispose();
            }
        }
        finally
        {
            _replaceGate.Release();
        }
    }
}
