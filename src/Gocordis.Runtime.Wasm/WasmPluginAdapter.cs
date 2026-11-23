using System.Text;
using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Wasmtime;

namespace Gocordis.Runtime.Wasm;

/// <summary>Go WASM backend parity: modules larger than 64 MiB are refused at load time.</summary>
public sealed class WasmModuleTooLargeException : Exception
{
    public WasmModuleTooLargeException(long size)
        : base($"WASM module is {size} bytes; limit is {WasmPluginInstance.MaxModuleBytes} bytes")
    {
        Size = size;
    }

    public long Size { get; }
}

/// <summary>
/// WASM backend built on the mature Wasmtime runtime. It keeps the Go ABI:
/// runtime_component_create / runtime_component_destroy, with generation-scoped
/// instances created per activation and discarded by the inverse effect.
/// </summary>
public sealed class WasmPluginInstance : IDisposable
{
    /// <summary>Go loader/wasm maximum module size (64 MiB).</summary>
    public const long MaxModuleBytes = 64L * 1024 * 1024;

    /// <summary>Store-level linear memory ceiling; a runaway guest cannot grow past it.</summary>
    public const long MaxStoreMemoryBytes = 64L * 1024 * 1024;

    /// <summary>Epoch tick period; deadline ticks are budget.TotalMilliseconds / this.</summary>
    public const int EpochTickMilliseconds = 5;

    /// <summary>Default guest execution budget per export call, mirroring the Go
    /// backend's CloseOnContextDone guarantee with an epoch deadline.</summary>
    public static readonly TimeSpan DefaultExecutionBudget = TimeSpan.FromSeconds(10);

    private readonly Engine _engine;
    private readonly Module _module;
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Linker? _linker;
    private readonly EpochTicker? _ticker;
    private CancellationTokenRegistration _cancellation;
    private volatile bool _interrupted;
    private volatile bool _destroying;

    /// <summary>Exported linear memory, used by the capability bridge when present.</summary>
    internal Memory? Memory { get; }

    private WasmPluginInstance(
        Engine engine, Module module, Store store, Instance instance,
        Linker? linker, Memory? memory, EpochTicker? ticker)
    {
        _engine = engine;
        _module = module;
        _store = store;
        _instance = instance;
        _linker = linker;
        Memory = memory;
        _ticker = ticker;
    }

    public static void EnsureModuleSize(long size)
    {
        if (size > MaxModuleBytes)
        {
            throw new WasmModuleTooLargeException(size);
        }
    }

    public static WasmPluginInstance FromWat(
        string wat,
        string name = "plugin",
        WasmHostBridge? bridge = null,
        WasmCapabilityBridge? capabilityBridge = null)
    {
        ArgumentNullException.ThrowIfNull(wat);
        EnsureModuleSize(Encoding.UTF8.GetByteCount(wat));

        var engine = new Engine(new Config()
            .WithEpochInterruption(true)
            .WithMaximumStackSize(8 * 1024 * 1024)
            .WithStaticMemoryMaximumSize(MaxStoreMemoryBytes));
        try
        {
            var module = Module.FromText(engine, name, wat);
            return Create(engine, module, bridge, capabilityBridge);
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    public static WasmPluginInstance FromBytes(
        byte[] wasm,
        string name = "plugin",
        WasmHostBridge? bridge = null,
        WasmCapabilityBridge? capabilityBridge = null)
    {
        ArgumentNullException.ThrowIfNull(wasm);
        EnsureModuleSize(wasm.LongLength);

        var engine = new Engine(new Config()
            .WithEpochInterruption(true)
            .WithMaximumStackSize(8 * 1024 * 1024)
            .WithStaticMemoryMaximumSize(MaxStoreMemoryBytes));
        try
        {
            var module = Module.FromBytes(engine, name, wasm);
            return Create(engine, module, bridge, capabilityBridge);
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private static WasmPluginInstance Create(
        Engine engine, Module module, WasmHostBridge? bridge, WasmCapabilityBridge? capabilityBridge)
    {
        try
        {
            var store = new Store(engine);
            try
            {
                // Store-level resource ceiling plus an epoch ticker so a guest
                // stuck in an infinite loop is interrupted instead of pinning
                // the activation (the Go backend's CloseOnContextDone analog).
                store.SetLimits(MaxStoreMemoryBytes, 10_000, 1_000, 1_000, 1_000);
                if (bridge is null && capabilityBridge is null)
                {
                    // EVERY module gets the same execution budget, not just
                    // bridged/capability modules: a plain plugin can spin too.
                    var plain = new Instance(store, module, []);
                    var plainTicker = new EpochTicker(engine);
                    return new WasmPluginInstance(
                        engine, module, store, plain, null, plain.GetMemory("memory"), plainTicker);
                }

                var linker = new Linker(engine);
                if (bridge is not null)
                {
                    linker.DefineFunction("gocordis", "report", new Action<int>(bridge.Report));
                }

                if (capabilityBridge is not null)
                {
                    linker.DefineFunction("gocordis", "capability_buffer",
                        new Func<int>(capabilityBridge.Buffer));
                    linker.DefineFunction("gocordis", "capability_require",
                        new Func<int, int, int>(capabilityBridge.Require));
                    linker.DefineFunction("gocordis", "capability_invoke",
                        new Func<int, int, int, int>(capabilityBridge.Invoke));
                }

                var instance = linker.Instantiate(store, module);
                var memory = instance.GetMemory("memory");
                var ticker = new EpochTicker(engine);
                var result = new WasmPluginInstance(engine, module, store, instance, linker, memory, ticker);
                capabilityBridge?.Attach(result);
                return result;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }
        catch
        {
            module.Dispose();
            throw;
        }
    }

    public object? Invoke(string export, params object[] arguments) =>
        Invoke(export, DefaultExecutionBudget, arguments);

    /// <summary>
    /// Invokes a guest export under the epoch execution budget. A deadline is
    /// armed per call so an interrupted guest cannot leave the store reusable:
    /// the caller must treat the instance as dead after an interruption trap.
    /// </summary>
    /// <summary>
    /// Converts the activation token into a guest interrupt: cancellation arms
    /// an expired epoch deadline, so the next Wasmtime step traps. After an
    /// interrupt the instance is marked unusable (never silently reused).
    /// </summary>
    internal void AttachCancellation(CancellationToken token)
    {
        _cancellation = token.Register(() =>
        {
            // Teardown must still be able to call runtime_component_destroy;
            // cancelling must not turn a clean unwind into a failed cleanup.
            if (_destroying)
            {
                return;
            }

            _interrupted = true;
            try
            {
                _store.SetEpochDeadline(1);
                _engine.IncrementEpoch();
            }
            catch (ObjectDisposedException)
            {
                // The instance was already torn down; nothing to interrupt.
            }
        });
    }

    public object? Invoke(string export, TimeSpan budget, params object[] arguments)
    {
        var isDestroy = string.Equals(export, "runtime_component_destroy", StringComparison.Ordinal);
        if (isDestroy)
        {
            // The inverse runs while the activation token is already cancelled.
            // Give teardown an effectively infinite deadline so the module can
            // release its resources and the LIFO effect still completes.
            _destroying = true;
            _store.SetEpochDeadline(ulong.MaxValue);
        }
        else if (_interrupted)
        {
            throw new CapabilityInvocationException(
                "provider-abort", "WASM instance was cancelled or interrupted");
        }

        var function = _instance.GetFunction(export) ??
                       throw new MissingMethodException($"WASM module has no export '{export}'");
        var ticks = Math.Max(1, (long)(budget.TotalMilliseconds / EpochTickMilliseconds));
        _store.SetEpochDeadline((ulong)ticks);
        var boxes = arguments.Select(ToBox).ToArray();
        try
        {
            return function.Invoke(boxes);
        }
        catch (WasmtimeException ex) when (
            ex.Message.Contains("epoch", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("interrupt", StringComparison.OrdinalIgnoreCase))
        {
            throw new CapabilityInvocationException(
                "execution-budget-exceeded", $"WASM export '{export}' exceeded its execution budget: {ex.Message}");
        }
    }

    /// <summary>
    /// Load-time ABI check. Verified before any capability is published, so a
    /// module missing an export fails activation instead of failing at unwind.
    /// </summary>
    public void ValidateRuntimeExports(IReadOnlyList<string>? requiredExports = null)
    {
        foreach (var export in requiredExports ?? [])
        {
            if (_instance.GetFunction(export) is null)
            {
                throw new MissingMethodException(
                    $"WASM module has no export '{export}' required by its capability declaration");
            }
        }

        if (_instance.GetFunction("runtime_component_create") is null)
        {
            throw new MissingMethodException("WASM module has no export 'runtime_component_create'");
        }

        if (_instance.GetFunction("runtime_component_destroy") is null)
        {
            throw new MissingMethodException("WASM module has no export 'runtime_component_destroy'");
        }
    }

    private static ValueBox ToBox(object value) => value switch
    {
        int number => (ValueBox)number,
        long number => (ValueBox)number,
        string text => (ValueBox)text,
        _ => (ValueBox)Convert.ToInt32(value),
    };

    public void Dispose()
    {
        _cancellation.Dispose();
        _ticker?.Dispose();
        _linker?.Dispose();
        _store.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
}

/// <summary>
/// Advances the engine epoch on a timer so per-call deadlines can fire. Test
/// and production hosts get the same interruption semantics without a custom
/// scheduler; the Go parity target is wazero's CloseOnContextDone.
/// </summary>
internal sealed class EpochTicker : IDisposable
{
    private readonly System.Threading.Timer _timer;

    public EpochTicker(Engine engine, int intervalMilliseconds = WasmPluginInstance.EpochTickMilliseconds)
    {
        _timer = new System.Threading.Timer(
            _ =>
            {
                // A disposed engine races the last timer callback after Dispose;
                // the tick is harmless then and must not crash the process.
                try
                {
                    engine.IncrementEpoch();
                }
                catch (ObjectDisposedException)
                {
                }
            }, null, intervalMilliseconds, intervalMilliseconds);
    }

    public void Dispose() => _timer.Dispose();
}

public sealed class WasmComponent : Component
{
    private readonly Key<int> _key;
    private readonly GenerationTracker? _tracker;

    private readonly Func<WasmPluginInstance> _createInstance;

    public WasmComponent(
        string wat,
        Key<int> key,
        WasmHostBridge? bridge = null,
        GenerationTracker? tracker = null)
        : this(() => WasmPluginInstance.FromWat(wat, bridge: bridge), key, tracker)
    {
    }

    /// <summary>Binary WASM payload as installed by a platform package.</summary>
    public WasmComponent(
        byte[] wasm,
        Key<int> key,
        string name = "plugin",
        WasmHostBridge? bridge = null,
        GenerationTracker? tracker = null)
        : this(() => WasmPluginInstance.FromBytes(wasm, name, bridge), key, tracker)
    {
    }

    private WasmComponent(Func<WasmPluginInstance> createInstance, Key<int> key, GenerationTracker? tracker)
    {
        _createInstance = createInstance;
        _key = key;
        _tracker = tracker;
    }

    public override string Name => "wasm-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        BackendProtocol.RunAsync(new WasmBackend(_createInstance, _key, _tracker), context);
}
