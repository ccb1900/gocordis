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

public sealed record LoadedManagedPlugin(string Id, PluginLoadContext Context, Core.Component Component);

/// <summary>Managed runtime adapter: load, instantiate and reclaim plugin assemblies.</summary>
public sealed class ManagedPluginLoader
{
    private readonly Dictionary<string, LoadedManagedPlugin> _plugins = [];

    public IReadOnlyDictionary<string, LoadedManagedPlugin> Plugins => _plugins;

    public LoadedManagedPlugin Load(string id, string assemblyPath, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_plugins.ContainsKey(id))
        {
            throw new InvalidOperationException($"plugin '{id}' is already loaded");
        }

        var context = new PluginLoadContext(assemblyPath);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var type = assembly.GetType(typeName, throwOnError: true)!;
            if (!typeof(Core.Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"'{typeName}' is not a {nameof(Core.Component)}");
            }

            var component = (Core.Component)Activator.CreateInstance(type)!;
            var plugin = new LoadedManagedPlugin(id, context, component);
            _plugins[id] = plugin;
            return plugin;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    public void Unload(string id)
    {
        if (!_plugins.Remove(id, out var plugin))
        {
            return;
        }

        plugin.Context.Unload();
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
