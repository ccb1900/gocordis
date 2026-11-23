using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace Gocordis.Runtime.Managed;

/// <summary>
/// One collectible load context per plugin. The shared contract assembly is
/// deliberately resolved by the default context so plugin and host agree on
/// type identity; all other assemblies come from the plugin's dependency graph.
/// ALC is not a security sandbox — process isolation remains available.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly HashSet<string> _sharedAssemblies;

    public PluginLoadContext(
        string pluginAssemblyPath,
        IEnumerable<string>? sharedAssemblies = null)
        : base($"plugin:{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
        _resolver = new AssemblyDependencyResolver(Path.GetFullPath(pluginAssemblyPath));
        _sharedAssemblies = new HashSet<string>(
            sharedAssemblies ??
            [
                typeof(Core.Component).Assembly.GetName().Name!,
                typeof(Plugins.PluginManifest).Assembly.GetName().Name!,
            ],
            StringComparer.Ordinal);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Every host-shared contract assembly must keep one identity, otherwise
        // typed keys and static registries differ between host and plugin.
        if (assemblyName.Name is { } name && _sharedAssemblies.Contains(name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

public sealed record LoadedManagedPlugin(
    string Id,
    PluginLoadContext Context,
    Core.Component Component,
    GenerationHandle Generation);

/// <summary>Managed runtime adapter: load, instantiate and reclaim plugin assemblies.</summary>
public sealed class ManagedPluginLoader
{
    private readonly Dictionary<string, LoadedManagedPlugin> _plugins = [];
    private readonly ConcurrentDictionary<string, GenerationTracker> _generations =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LoadedManagedPlugin> Plugins => _plugins;

    /// <summary>The fence for one plugin id. Reused across retire/reload cycles.</summary>
    public GenerationTracker GenerationsFor(string id) =>
        _generations.GetOrAdd(id, _ => new GenerationTracker());

    public LoadedManagedPlugin Load(string id, string assemblyPath, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_plugins.ContainsKey(id))
        {
            throw new InvalidOperationException($"plugin '{id}' is already loaded");
        }

        GenerationHandle? generation = null;
        PluginLoadContext? context = null;
        try
        {
            generation = GenerationsFor(id).Begin();
            context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var type = assembly.GetType(typeName, throwOnError: true)!;
            if (!typeof(Core.Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"'{typeName}' is not a {nameof(Core.Component)}");
            }

            var component = (Core.Component)Activator.CreateInstance(type)!;
            var plugin = new LoadedManagedPlugin(id, context, component, generation);
            context = null; // ownership transferred to the plugin record
            _plugins[id] = plugin;
            return plugin;
        }
        catch
        {
            generation?.Abandon();
            context?.Unload();
            throw;
        }
    }

    /// <summary>Unfenced unload retained for explicit host-driven teardown.</summary>
    public void Unload(string id) => Unload(id, expected: null);

    /// <summary>
    /// Fenced unload. A stale generation (one superseded by a newer load) can
    /// never unload the ALC that belongs to the newer generation.
    /// </summary>
    public bool Unload(string id, GenerationHandle? expected)
    {
        if (expected is not null && !expected.Release())
        {
            return false;
        }

        if (!_plugins.Remove(id, out var plugin))
        {
            return false;
        }

        plugin.Context.Unload();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _plugins.Keys.ToArray())
        {
            Unload(id);
        }

        await ValueTask.CompletedTask;
    }
}
