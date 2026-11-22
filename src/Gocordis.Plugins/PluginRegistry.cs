using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gocordis.Core;

namespace Gocordis.Plugins;

public sealed record PluginEntry(PluginManifest Manifest, Fiber? Fiber, string ConfigurationKey);

/// <summary>A declaration file that could not be discovered, with its error.</summary>
public sealed record ManifestDiagnostic(string Path, Exception Error);

/// <summary>
/// Plugin discovery, declaration reconciliation and lifecycle. All fiber state
/// transitions are delegated to Core; this class never writes a fiber directly.
/// A declaration batch is a transaction: prepare validates the whole batch,
/// commit applies it, and rollback restores the previous declarations if any
/// commit step fails.
/// </summary>
public sealed class PluginRegistry : IAsyncDisposable
{
    private readonly Runtime _runtime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>, Component>> _factories = [];
    private readonly Dictionary<string, PluginEntry> _entries = [];
    private IReadOnlyList<ManifestDiagnostic> _diagnostics = [];

    public PluginRegistry(Runtime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// Stable read-only view of the current declarations. The copy is taken
    /// under the same gate as Apply/Dispose, so readers can enumerate it while
    /// a reconciliation mutates the live table without ever observing a
    /// half-applied batch or a collection-modified fault.
    /// </summary>
    public IReadOnlyDictionary<string, PluginEntry> Entries
    {
        get
        {
            _gate.Wait();
            try
            {
                return new Dictionary<string, PluginEntry>(_entries, StringComparer.Ordinal);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Diagnostics from the most recent discovery pass.</summary>
    public IReadOnlyList<ManifestDiagnostic> Diagnostics => Volatile.Read(ref _diagnostics);

    /// <summary>Platform-declared ids cannot be disabled by a catalog action.</summary>
    public HashSet<string> ProtectedIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Capabilities supplied by the host rather than by declared plugins.</summary>
    public HashSet<ICapabilityKey> ExternalCapabilities { get; } =
        new(ReferenceEqualityComparer.Instance);

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
            Volatile.Write(ref _diagnostics, Array.Empty<ManifestDiagnostic>());
            return [];
        }

        var manifests = new List<PluginManifest>();
        var diagnostics = new List<ManifestDiagnostic>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.toml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                var text = await File.ReadAllTextAsync(path);
                manifests.AddRange(PluginManifest.ParseToml(text));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ManifestDiagnostic(path, ex));
            }
        }

        Volatile.Write(ref _diagnostics, diagnostics.ToArray());
        return manifests;
    }

    /// <summary>
    /// Prepare → Commit → Rollback. The gate covers the whole transaction so
    /// staging, validation and commit never interleave with another call.
    /// </summary>
    public async Task ApplyAsync(IEnumerable<PluginManifest> declarations)
    {
        await _gate.WaitAsync();
        try
        {
            var previous = new Dictionary<string, PluginEntry>(_entries, StringComparer.Ordinal);
            var batch = Prepare(declarations.ToArray(), rejectProtectedDisable: true);
            try
            {
                await CommitAsync(batch);
            }
            catch (Exception commitError)
            {
                await RollbackAsync(batch, previous, commitError);
                throw;
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

    // ---- transaction phases -------------------------------------------------

    private PreparedBatch Prepare(IReadOnlyList<PluginManifest> declarations, bool rejectProtectedDisable)
    {
        var duplicateIds = declarations.GroupBy(manifest => manifest.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"duplicate plugin ids: {string.Join(", ", duplicateIds)}");
        }

        var desired = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
        var staged = new Dictionary<string, Component>(StringComparer.Ordinal);
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        var providedKeys = new Dictionary<ICapabilityKey, string>(ReferenceEqualityComparer.Instance);

        foreach (var manifest in declarations)
        {
            if (rejectProtectedDisable && ProtectedIds.Contains(manifest.Id) && !manifest.Enabled)
            {
                throw new ProtectedPluginException(manifest.Id);
            }

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

            desired[manifest.Id] = manifest;
            staged[manifest.Id] = component;
            fingerprints[manifest.Id] = Fingerprint(manifest);
        }

        var ordered = SortByTypedDependencies(staged);
        return new PreparedBatch(desired, staged, ordered, fingerprints);
    }

    private async Task CommitAsync(PreparedBatch batch)
    {
        foreach (var id in _entries.Keys.Except(batch.Desired.Keys).ToArray())
        {
            if (!ProtectedIds.Contains(id))
            {
                await RetireAsync(id);
            }
        }

        foreach (var (id, component) in batch.Ordered)
        {
            var manifest = batch.Desired[id];
            var configurationKey = batch.Fingerprints[id];
            if (ProtectedIds.Contains(id) && !manifest.Enabled &&
                _entries.TryGetValue(id, out var protectedEntry) && protectedEntry.Fiber is not null)
            {
                _entries[id] = protectedEntry with { Manifest = manifest };
                continue;
            }

            if (_entries.TryGetValue(id, out var current) && current.ConfigurationKey == configurationKey)
            {
                continue;
            }

            if (_entries.ContainsKey(id))
            {
                await RetireAsync(id);
            }

            if (!manifest.Enabled)
            {
                _entries[id] = new PluginEntry(manifest, null, configurationKey);
                continue;
            }

            var fiber = await _runtime.LoadAsync(component);
            batch.LoadedFibers.Add(fiber);
            if (fiber.State == FiberState.Failed)
            {
                throw new InvalidOperationException(
                    $"plugin '{id}' failed to activate", fiber.Failure);
            }

            _entries[id] = new PluginEntry(manifest, fiber, configurationKey);
        }
    }

    private async Task RollbackAsync(
        PreparedBatch batch,
        IReadOnlyDictionary<string, PluginEntry> previous,
        Exception commitError)
    {
        var errors = new List<Exception> { commitError };
        foreach (var fiber in batch.LoadedFibers)
        {
            try
            {
                await _runtime.DisposeAsync(fiber);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        _entries.Clear();
        if (previous.Count == 0)
        {
            if (errors.Count > 1)
            {
                throw new AggregateException("rollback encountered additional errors", errors);
            }

            return;
        }

        try
        {
            var restore = Prepare(previous.Values.Select(entry => entry.Manifest).ToArray(),
                rejectProtectedDisable: false);
            await CommitAsync(restore);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
            _entries.Clear();
        }

        if (errors.Count > 1)
        {
            throw new AggregateException("rollback could not fully restore previous declarations", errors);
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

    private IReadOnlyList<(string Id, Component Component)> SortByTypedDependencies(
        IReadOnlyDictionary<string, Component> staged)
    {
        var keyOwner = new Dictionary<ICapabilityKey, string>(ReferenceEqualityComparer.Instance);
        var versionedOwners = new List<(string Id, IVersionedKey Key)>();
        foreach (var (id, component) in staged)
        {
            foreach (var key in component.Provide())
            {
                keyOwner[key] = id;
                if (key is IVersionedKey versioned)
                {
                    versionedOwners.Add((id, versioned));
                }
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<(string, Component)>();

        void Visit(string id)
        {
            if (!visiting.Add(id))
            {
                throw new DependencyCycleException($"plugin dependency cycle at '{id}'");
            }

            foreach (var key in staged[id].Inject())
            {
                if (key is IMetaKey)
                {
                    continue;
                }

                if (key is IVersionedKey requiredVersion)
                {
                    var best = versionedOwners
                        .Where(candidate => candidate.Key.Name == requiredVersion.Name &&
                                            candidate.Key.ValueType == requiredVersion.ValueType &&
                                            requiredVersion.Accepts(candidate.Key.Version))
                        .OrderByDescending(candidate => candidate.Key.Version)
                        .FirstOrDefault();
                    if (best.Id is not null && ReferenceEquals(best.Id, id))
                    {
                        throw new DependencyCycleException($"plugin '{id}' depends on its own versioned capability");
                    }

                    if (best.Id is not null)
                    {
                        if (!visited.Contains(best.Id))
                        {
                            Visit(best.Id);
                        }

                        continue;
                    }

                    if (!ExternalCapabilities.Contains(requiredVersion))
                    {
                        throw new InvalidOperationException(
                            $"plugin '{id}' requires versioned capability '{requiredVersion.Name}' " +
                            $"matching '{requiredVersion.Constraint ?? (IFormattable)requiredVersion.Version}' " +
                            "with no provider");
                    }

                    continue;
                }

                if (keyOwner.TryGetValue(key, out var providerId))
                {
                    if (!visited.Contains(providerId))
                    {
                        Visit(providerId);
                    }

                    continue;
                }

                if (!ExternalCapabilities.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"plugin '{id}' requires capability '{key.Name}' with no provider");
                }
            }

            visiting.Remove(id);
            if (visited.Add(id))
            {
                ordered.Add((id, staged[id]));
            }
        }

        foreach (var id in staged.Keys)
        {
            Visit(id);
        }

        return ordered;
    }

    private static string Fingerprint(PluginManifest manifest)
    {
        var payload = JsonSerializer.Serialize(new
        {
            manifest.Id,
            manifest.Type,
            manifest.Enabled,
            manifest.Version,
            manifest.Source,
            manifest.Checksum,
            manifest.Requires,
            manifest.Provides,
            Config = manifest.Config.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record PreparedBatch(
        IReadOnlyDictionary<string, PluginManifest> Desired,
        IReadOnlyDictionary<string, Component> Staged,
        IReadOnlyList<(string Id, Component Component)> Ordered,
        IReadOnlyDictionary<string, string> Fingerprints)
    {
        internal List<Fiber> LoadedFibers { get; } = [];
    }
}
