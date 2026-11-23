using System.Text;
using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;
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

    private readonly Engine _engine;
    private readonly Module _module;
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Linker? _linker;

    /// <summary>Exported linear memory, used by the capability bridge when present.</summary>
    internal Memory? Memory { get; }

    private WasmPluginInstance(
        Engine engine, Module module, Store store, Instance instance, Linker? linker, Memory? memory)
    {
        _engine = engine;
        _module = module;
        _store = store;
        _instance = instance;
        _linker = linker;
        Memory = memory;
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

        var engine = new Engine();
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

        var engine = new Engine();
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
                if (bridge is null && capabilityBridge is null)
                {
                    var plain = new Instance(store, module, []);
                    return new WasmPluginInstance(
                        engine, module, store, plain, null, plain.GetMemory("memory"));
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
                var result = new WasmPluginInstance(engine, module, store, instance, linker, memory);
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

    public object? Invoke(string export, params object[] arguments)
    {
        var function = _instance.GetFunction(export) ??
                       throw new MissingMethodException($"WASM module has no export '{export}'");
        var boxes = arguments.Select(ToBox).ToArray();
        return function.Invoke(boxes);
    }

    /// <summary>
    /// Load-time ABI check. Verified before any capability is published, so a
    /// module missing an export fails activation instead of failing at unwind.
    /// </summary>
    public void ValidateRuntimeExports()
    {
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
        _linker?.Dispose();
        _store.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
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
