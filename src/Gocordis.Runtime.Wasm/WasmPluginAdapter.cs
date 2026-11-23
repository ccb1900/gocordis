using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;
using Wasmtime;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// WASM backend built on the mature Wasmtime runtime. It keeps the Go ABI:
/// runtime_component_create / runtime_component_destroy, with generation-scoped
/// instances created per activation and discarded by the inverse effect.
/// </summary>
public sealed class WasmPluginInstance : IDisposable
{
    private readonly Engine _engine;
    private readonly Module _module;
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Linker? _linker;

    private WasmPluginInstance(Engine engine, Module module, Store store, Instance instance, Linker? linker)
    {
        _engine = engine;
        _module = module;
        _store = store;
        _instance = instance;
        _linker = linker;
    }

    public static WasmPluginInstance FromWat(string wat, string name = "plugin", WasmHostBridge? bridge = null)
    {
        var engine = new Engine();
        try
        {
            var module = Module.FromText(engine, name, wat);
            try
            {
                var store = new Store(engine);
                try
                {
                    if (bridge is null)
                    {
                        return new WasmPluginInstance(engine, module, store, new Instance(store, module, []), null);
                    }

                    var linker = new Linker(engine);
                    linker.DefineFunction("gocordis", "report", new Action<int>(bridge.Report));
                    return new WasmPluginInstance(engine, module, store, linker.Instantiate(store, module), linker);
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
        catch
        {
            engine.Dispose();
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
    private readonly string _wat;
    private readonly Key<int> _key;
    private readonly WasmHostBridge? _bridge;
    private readonly GenerationTracker? _tracker;

    public WasmComponent(
        string wat,
        Key<int> key,
        WasmHostBridge? bridge = null,
        GenerationTracker? tracker = null)
    {
        _wat = wat;
        _key = key;
        _bridge = bridge;
        _tracker = tracker;
    }

    public override string Name => "wasm-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        BackendProtocol.RunAsync(new WasmBackend(_wat, _key, _bridge, _tracker), context);
}
