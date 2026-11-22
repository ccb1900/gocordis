using Gocordis.Core;
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

    private WasmPluginInstance(Engine engine, Module module, Store store, Instance instance)
    {
        _engine = engine;
        _module = module;
        _store = store;
        _instance = instance;
    }

    public static WasmPluginInstance FromWat(string wat, string name = "plugin")
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
                    var instance = new Instance(store, module, []);
                    return new WasmPluginInstance(engine, module, store, instance);
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

    private static ValueBox ToBox(object value) => value switch
    {
        int number => (ValueBox)number,
        long number => (ValueBox)number,
        string text => (ValueBox)text,
        _ => (ValueBox)Convert.ToInt32(value),
    };

    public void Dispose()
    {
        _store.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
}

public sealed class WasmComponent : Component
{
    private readonly string _wat;
    private readonly Key<int> _key;

    public WasmComponent(string wat, Key<int> key)
    {
        _wat = wat;
        _key = key;
    }

    public override string Name => "wasm-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var instance = WasmPluginInstance.FromWat(_wat);
        var value = 0;
        try
        {
            value = Convert.ToInt32(instance.Invoke("runtime_component_create"));
            context.Provide(_key, value);
        }
        catch
        {
            instance.Dispose();
            throw;
        }

        return () =>
        {
            try
            {
                instance.Invoke("runtime_component_destroy", value);
            }
            finally
            {
                instance.Dispose();
            }

            return ValueTask.CompletedTask;
        };
    }
}
