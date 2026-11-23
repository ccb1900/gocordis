using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// WASM implementation of the unified backend protocol. The instance is staged
/// at Prepare (with required exports validated), committed through Core at
/// Activate, and released by the Core effect inverse. One instance per
/// activation, so a stale unwind can only release its own instance.
/// </summary>
public sealed class WasmBackend : IPluginBackend
{
    private readonly Func<WasmPluginInstance> _createInstance;
    private readonly Key<int>? _key;
    private readonly GenerationTracker _tracker;
    private GenerationHandle? _generation;
    private WasmPluginInstance? _instance;
    private int _value;
    private bool _released;
    private bool _disposed;

    public WasmBackend(
        string wat,
        Key<int> key,
        WasmHostBridge? bridge = null,
        GenerationTracker? tracker = null)
        : this(() => WasmPluginInstance.FromWat(wat, bridge: bridge), key, tracker)
    {
    }

    /// <summary>Binary WASM payload; WAT payloads use the string constructor.</summary>
    public WasmBackend(
        byte[] wasm,
        Key<int> key,
        string name = "plugin",
        WasmHostBridge? bridge = null,
        GenerationTracker? tracker = null)
        : this(() => WasmPluginInstance.FromBytes(wasm, name, bridge), key, tracker)
    {
    }

    internal WasmBackend(
        Func<WasmPluginInstance> createInstance,
        Key<int>? key,
        GenerationTracker? tracker)
    {
        _createInstance = createInstance;
        _key = key;
        _tracker = tracker ?? new GenerationTracker();
    }

    public string BackendId => "wasm";

    public BackendDegradationPolicy DegradationPolicy => BackendDegradationPolicy.FailFiber;

    public BackendHandle? LastHandle { get; private set; }

    public ValueTask<BackendHandle> PrepareAsync(IBackendContext context)
    {
        context.ThrowIfDiverted();
        _generation = _tracker.Begin();
        _instance = _createInstance();
        try
        {
            _instance.ValidateRuntimeExports();
        }
        catch
        {
            _instance.Dispose();
            _instance = null;
            _generation.Abandon();
            throw;
        }

        LastHandle = new BackendHandle(
            BackendId, _generation.Generation, BackendPhase.Prepared, "module");
        return ValueTask.FromResult(LastHandle);
    }

    public ValueTask<BackendHandle> ActivateAsync(IBackendContext context, BackendHandle prepared)
    {
        context.ThrowIfDiverted();
        var instance = _instance ?? throw new InvalidOperationException("WASM backend is not prepared");
        _value = Convert.ToInt32(instance.Invoke("runtime_component_create"));
        if (_key is not null)
        {
            context.Provide(_key, _value);
        }

        LastHandle = prepared.WithPhase(BackendPhase.Activated, _value.ToString());
        return ValueTask.FromResult(LastHandle);
    }

    public ValueTask<BackendHandle> RollbackAsync(
        IBackendContext context, BackendHandle? prepared, Exception reason)
    {
        _instance?.Dispose();
        _instance = null;
        _generation?.Abandon();

        var handle = prepared ?? new BackendHandle(BackendId, 0, BackendPhase.Prepared, "module");
        LastHandle = handle.WithPhase(BackendPhase.RolledBack, reason.Message);
        return ValueTask.FromResult(LastHandle);
    }

    public ValueTask<BackendHandle> ReleaseAsync(IBackendContext context, BackendHandle active)
    {
        if (_released)
        {
            LastHandle = active.WithPhase(BackendPhase.Released);
            return ValueTask.FromResult(LastHandle);
        }

        _released = true;
        try
        {
            if (_instance is not null)
            {
                try
                {
                    _instance.Invoke("runtime_component_destroy", _value);
                }
                finally
                {
                    _instance.Dispose();
                    _instance = null;
                }
            }
        }
        finally
        {
            // Settle the generation fence; a superseded generation is a no-op
            // and can never tear down a newer activation's resource.
            _generation?.Release();
        }

        LastHandle = active.WithPhase(BackendPhase.Released);
        return ValueTask.FromResult(LastHandle);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
