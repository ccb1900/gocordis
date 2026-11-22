using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gocordis.Core;

namespace Gocordis.Plugins;

public sealed record PluginEntry(PluginManifest Manifest, Fiber? Fiber, string ConfigurationKey);

/// <summary>
/// Plugin discovery, declaration reconciliation and lifecycle. All fiber state
/// transitions are delegated to Core; this class never writes a fiber directly.
/// </summary>
public sealed class PluginRegistry : IAsyncDisposable
{
    private readonly Runtime _runtime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>, Component>> _factories = [];
    private readonly Dictionary<string, PluginEntry> _entries = [];

    public PluginRegistry(Runtime runtime)
    {
        _runtime = runtime;
    }

    public IReadOnlyDictionary<string, PluginEntry> Entries => _entries;

    /// <summary>Platform-declared ids cannot be disabled by a catalog action.</summary>
    public HashSet<string> ProtectedIds { get; } = new(StringComparer.Ordinal);

    public void RegisterFactory(string type, Func<IReadOnlyDictionary<string, object?>, Component> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(type, factory))
        {
            throw new InvalidOperationException($"factory already registered for type '{type}'");
        }
    }

    public async Task<IReadOnlyList<PluginManifest>> DiscoverAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var manifests = new List<PluginManifest>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.toml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var text = await File.ReadAllTextAsync(path);
            manifests.AddRange(PluginManifest.ParseToml(text));
        }

        return manifests;
    }

    /// <summary>
    /// Reconcile declarations to fibers. Enabled=true loads, false disposes but
    /// keeps the validated declaration. Changes replace (dispose then load).
    /// </summary>
    public async Task ApplyAsync(IEnumerable<PluginManifest> declarations)
    {
        var declarationsArray = declarations.ToArray();
        var duplicateIds = declarationsArray.GroupBy(manifest => manifest.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"duplicate plugin ids: {string.Join(", ", duplicateIds)}");
        }

        var desired = declarationsArray.ToDictionary(manifest => manifest.Id);

        // Stage the whole batch: factory construction, declaration probing and
        // duplicate provider detection all happen before any fiber is touched.
        _ = SortByDependencies(desired.Values);
        var staged = new Dictionary<string, Component>(StringComparer.Ordinal);
        var providedKeys = new Dictionary<ICapabilityKey, string>(ReferenceEqualityComparer.Instance);
        foreach (var manifest in SortByDependencies(desired.Values))
        {
            if (!_factories.TryGetValue(manifest.Type, out var factory))
            {
                throw new InvalidOperationException(
                    $"unknown plugin type '{manifest.Type}' for '{manifest.Id}'");
            }

            Component component;
            try
            {
                component = factory(manifest.Config);
                _ = component.Inject();
                _ = component.Provide();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"factory for '{manifest.Id}' failed", ex);
            }

            foreach (var key in component.Provide())
            {
                if (providedKeys.TryGetValue(key, out var owner))
                {
                    throw new DuplicateProviderException(
                        $"key '{key.Name}' is provided by both '{owner}' and '{manifest.Id}'");
                }

                providedKeys[key] = manifest.Id;
            }

            staged[manifest.Id] = component;
        }

        await _gate.WaitAsync();
        try
        {
            foreach (var id in _entries.Keys.Except(desired.Keys).ToArray())
            {
                if (!ProtectedIds.Contains(id))
                {
                    await RetireAsync(id);
                }
            }

            foreach (var manifest in SortByDependencies(desired.Values))
            {
                var configurationKey = Fingerprint(manifest);
                if (ProtectedIds.Contains(manifest.Id) && !manifest.Enabled &&
                    _entries.TryGetValue(manifest.Id, out var protectedEntry) &&
                    protectedEntry.Fiber is not null)
                {
                    _entries[manifest.Id] = protectedEntry with { Manifest = manifest };
                    continue;
                }

                if (_entries.TryGetValue(manifest.Id, out var current) &&
                    current.ConfigurationKey == configurationKey)
                {
                    continue;
                }

                if (_entries.ContainsKey(manifest.Id))
                {
                    await RetireAsync(manifest.Id);
                }

                if (!manifest.Enabled)
                {
                    _entries[manifest.Id] = new PluginEntry(manifest, null, configurationKey);
                    continue;
                }

                var fiber = await _runtime.LoadAsync(staged[manifest.Id]);
                _entries[manifest.Id] = new PluginEntry(manifest, fiber, configurationKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var id in _entries.Keys.ToArray())
            {
                await RetireAsync(id);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RetireAsync(string id)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        if (entry.Fiber is not null)
        {
            await _runtime.DisposeAsync(entry.Fiber);
        }

        _entries.Remove(id);
    }

    private static IReadOnlyList<PluginManifest> SortByDependencies(IEnumerable<PluginManifest> manifests)
    {
        var all = manifests.ToArray();
        var provided = new HashSet<string>(all.SelectMany(manifest => manifest.Provides), StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<PluginManifest>();

        void Visit(PluginManifest manifest)
        {
            if (!visiting.Add(manifest.Id))
            {
                throw new DependencyCycleException($"plugin dependency cycle at '{manifest.Id}'");
            }

            foreach (var dependency in manifest.Requires.Where(provided.Contains))
            {
                var provider = all.First(candidate => candidate.Provides.Contains(dependency, StringComparer.Ordinal));
                if (!visited.Contains(provider.Id))
                {
                    Visit(provider);
                }
            }

            visiting.Remove(manifest.Id);
            if (visited.Add(manifest.Id))
            {
                ordered.Add(manifest);
            }
        }

        foreach (var manifest in all)
        {
            Visit(manifest);
        }

        return ordered;
    }

    private static string Fingerprint(PluginManifest manifest)
    {
        var payload = JsonSerializer.Serialize(new
        {
            manifest.Type,
            manifest.Enabled,
            manifest.Version,
            manifest.Source,
            Config = manifest.Config.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
