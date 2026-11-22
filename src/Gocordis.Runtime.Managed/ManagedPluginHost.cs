using Gocordis.Core;

namespace Gocordis.Runtime.Managed;

/// <summary>
/// Single lifecycle owner for managed plugins. Unload order is fixed:
/// Dispose Fiber → await Gone → drop references → ALC.Unload → GC reclaim.
/// Callers must use this host; raw ManagedPluginLoader.Unload does not know
/// whether a component is still installed in the runtime.
/// </summary>
public sealed class ManagedPluginHost : IAsyncDisposable
{
    private readonly Gocordis.Core.Runtime _runtime;
    private readonly Dictionary<string, Fiber> _fibers = new(StringComparer.Ordinal);

    public ManagedPluginHost(Gocordis.Core.Runtime runtime)
    {
        _runtime = runtime;
    }

    public ManagedPluginLoader Loader { get; } = new();

    public IReadOnlyDictionary<string, Fiber> Fibers => _fibers;

    public async Task<Fiber> LoadAsync(string id, string assemblyPath, string typeName)
    {
        var plugin = Loader.Load(id, assemblyPath, typeName);
        try
        {
            var fiber = await _runtime.LoadAsync(plugin.Component);
            _fibers[id] = fiber;
            return fiber;
        }
        catch
        {
            Loader.Unload(id);
            throw;
        }
    }

    public async Task UnloadAsync(string id)
    {
        if (!_fibers.Remove(id, out var fiber))
        {
            Loader.Unload(id);
            return;
        }

        await _runtime.DisposeAsync(fiber);
        await fiber.GoneAsync();
        Loader.Unload(id);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _fibers.Keys.ToArray())
        {
            await UnloadAsync(id);
        }

        await Loader.DisposeAsync();
    }
}
