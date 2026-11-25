using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Gocordis.Runtime.Contracts;

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
        // The two framework contract assemblies are ALWAYS shared: custom
        // shared names extend this set, never replace it. Replacing would make
        // Key<T>/Component identities diverge from the host and break every
        // plugin that did not explicitly re-declare them.
        _sharedAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(Core.Component).Assembly.GetName().Name!,
            typeof(Plugins.PluginManifest).Assembly.GetName().Name!,
        };
        if (sharedAssemblies is not null)
        {
            foreach (var name in sharedAssemblies)
            {
                _sharedAssemblies.Add(name);
            }
        }
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
    // Concurrency contract: load/unload/dispose are serialized internally so a
    // direct concurrent caller cannot interleave generation bookkeeping. The
    // product path additionally serializes via PluginRegistry's gate.
    private readonly object _pluginsGate = new();
    private readonly Dictionary<string, LoadedManagedPlugin> _plugins = [];
    private readonly ConcurrentDictionary<string, GenerationTracker> _generations =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, LoadedManagedPlugin> Plugins => _plugins;

    /// <summary>The fence for one plugin id. Reused across retire/reload cycles.</summary>
    public GenerationTracker GenerationsFor(string id) =>
        _generations.GetOrAdd(id, _ => new GenerationTracker());

    public LoadedManagedPlugin Load(
        string id, string assemblyPath, string typeName,
        IReadOnlyList<string>? sharedAssemblies = null,
        Core.ICapabilityCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_pluginsGate)
        {
        if (_plugins.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"plugin '{id}' is already loaded (live: {string.Join(", ", _plugins.Keys)})");
        }

        GenerationHandle? generation = null;
        PluginLoadContext? context = null;
        try
        {
            // Promote declared contracts into the default ALC before creating
            // the plugin ALC. PluginLoadContext returns null for shared names,
            // so the single default-context copy becomes the shared identity.
            var shared = SharedAssemblyRegistry.EnsureLoaded(
                sharedAssemblies, Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);
            generation = GenerationsFor(id).Begin();
            context = new PluginLoadContext(assemblyPath, shared);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var type = assembly.GetType(typeName, throwOnError: true)!;
            if (!typeof(Core.Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"'{typeName}' is not a {nameof(Core.Component)}");
            }

            var component = Instantiate(type, catalog);
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
    }

    /// <summary>
    /// Creates a plugin that opts into cross-backend capabilities by declaring a
    /// constructor taking <see cref="Core.ICapabilityCatalog"/>. Plugins that do
    /// not opt in keep the parameterless path and their own static Key<T> values.
    /// </summary>
    private static Core.Component Instantiate(Type type, Core.ICapabilityCatalog? catalog)
    {
        if (catalog is not null)
        {
            var constructor = type.GetConstructor([typeof(Core.ICapabilityCatalog)]);
            if (constructor is not null)
            {
                return (Core.Component)constructor.Invoke([catalog]);
            }
        }

        return (Core.Component)Activator.CreateInstance(type)!;
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

        lock (_pluginsGate)
        {
            if (!_plugins.Remove(id, out var plugin))
            {
                return false;
            }

            plugin.Context.Unload();
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        string[] ids;
        lock (_pluginsGate)
        {
            ids = [.. _plugins.Keys];
        }

        foreach (var id in ids)
        {
            Unload(id);
        }

        await ValueTask.CompletedTask;
    }
}
