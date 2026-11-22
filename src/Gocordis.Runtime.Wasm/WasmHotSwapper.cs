using Gocordis.Core;
using Gocordis.Runtime.Managed;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// WASM HMR: validate the warm candidate, withdraw the old fiber, then commit
/// the new one. Every replacement advances a generation fence, so a late
/// release from an older activation can never dispose the current fiber.
/// </summary>
public sealed class WasmHotSwapper : IAsyncDisposable
{
    private readonly Gocordis.Core.Runtime _runtime;
    private readonly Key<int> _key;
    private readonly GenerationTracker _tracker = new();
    private Fiber? _current;
    private readonly SemaphoreSlim _replaceGate = new(1, 1);
    private readonly WasmHostBridge _bridge;
    private bool _disposed;

    public WasmHotSwapper(Gocordis.Core.Runtime runtime, Key<int> key, WasmHostBridge? bridge = null)
    {
        _runtime = runtime;
        _key = key;
        _bridge = bridge ?? new WasmHostBridge();
    }

    public Fiber? Current => _current;

    public int Generation => _tracker.Current;

    /// <summary>Start a new fence generation (used by callers retiring a fiber later).</summary>
    public GenerationHandle BeginGeneration() => _tracker.Begin();

    /// <summary>
    /// Release the current fiber only when the supplied generation is still
    /// current. A stale handle returns false and changes nothing.
    /// </summary>
    public async Task<bool> ReleaseGenerationAsync(GenerationHandle handle)
    {
        if (!handle.Release())
        {
            return false;
        }

        var fiber = _current;
        _current = null;
        if (fiber is not null)
        {
            await _runtime.DisposeAsync(fiber);
            await fiber.GoneAsync();
        }

        return true;
    }

    public async Task<Fiber> ReplaceAsync(string wat)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _replaceGate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Advancing the fence invalidates every previously issued handle
            // before this replacement mutates _current.
            _tracker.Begin();

            // Candidate stays alive until the old fiber reaches Gone: this is a
            // serialized warm handoff (not zero-downtime atomic replacement).
            var candidate = WasmPluginInstance.FromWat(wat, bridge: _bridge);
            try
            {
                candidate.ValidateRuntimeExports();
            }
            catch
            {
                candidate.Dispose();
                throw;
            }

            try
            {
                if (_current is not null)
                {
                    await _runtime.DisposeAsync(_current);
                    await _current.GoneAsync();
                }

                _current = await _runtime.LoadAsync(
                    new WasmComponent(wat, _key, _bridge, _tracker));
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _replaceGate.WaitAsync();
        try
        {
            await ReleaseGenerationAsync(_tracker.Begin());
        }
        finally
        {
            _replaceGate.Release();
        }
    }
}
