using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gocordis.Core;

namespace Gocordis.Plugins;

public sealed record PluginEntry(
    PluginManifest Manifest,
    Fiber? Fiber,
    string ConfigurationKey,
    Component? Component = null);

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
    // All factories are normalized to the manifest-aware shape; the legacy
    // config-only registration is a thin adapter over it.
    private readonly Dictionary<string, Func<PluginManifest, Component>> _factories = [];
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

    /// <summary>Legacy config-only registration, kept source-compatible.</summary>
    public void RegisterFactory(string type, Func<IReadOnlyDictionary<string, object?>, Component> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterComponentFactory(type, manifest => factory(manifest.Config));
    }

    /// <summary>
    /// Factory that sees the whole declaration (source path, backend, entry and
    /// config). Backend selection belongs to the composition root, not Core.
    /// </summary>
    public void RegisterComponentFactory(string type, Func<PluginManifest, Component> factory)
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
            PreparedBatch? batch = null;
            try
            {
                batch = Prepare(declarations.ToArray(), rejectProtectedDisable: true);
                await CommitAsync(batch);
            }
            catch (Exception commitError)
            {
                if (batch is not null)
                {
                    // Staged-but-never-activated components may own an ALC. A
                    // generation-fenced release is safe here (a live fiber is
                    // never in this set) and prevents a silent leak on failure.
                    await ReleaseUnusedStagedAsync(batch);
                    await RollbackAsync(batch, previous, commitError);
                }

                throw;
            }

            await ReleaseUnusedStagedAsync(batch!);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases only the components constructed by this batch that never reached
    /// a fiber (disabled or skipped declarations). Applied components stay under
    /// Core's effect authority and must not be released here.
    /// </summary>
    private static async ValueTask ReleaseUnusedStagedAsync(PreparedBatch batch)
    {
        foreach (var component in batch.NewlyStaged)
        {
            if (batch.Applied.Contains(component))
            {
                continue;
            }

            if (component is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                ReleaseStaged(component);
            }
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
        var batchNewlyStaged = new HashSet<Component>(ReferenceEqualityComparer.Instance);

        try
        {
            foreach (var manifest in declarations)
            {
                if (rejectProtectedDisable && ProtectedIds.Contains(manifest.Id) && !manifest.Enabled)
                {
                    throw new ProtectedPluginException(manifest.Id);
                }

                var fingerprint = _entries.TryGetValue(manifest.Id, out var previous) &&
                                 ManifestsEquivalent(previous.Manifest, manifest)
                    ? previous.ConfigurationKey
                    : Fingerprint(manifest);
                if (_entries.TryGetValue(manifest.Id, out var current) &&
                    current.ConfigurationKey == fingerprint &&
                    current.Component is { } reused)
                {
                    // An unchanged declaration keeps its existing component: no
                    // new factory call and therefore no redundant assembly load.
                    desired[manifest.Id] = manifest;
                    staged[manifest.Id] = reused;
                    fingerprints[manifest.Id] = fingerprint;
                    continue;
                }

                if (!_factories.TryGetValue(manifest.Type, out var factory))
                {
                    throw new InvalidOperationException(
                        $"unknown plugin type '{manifest.Type}' for '{manifest.Id}'");
                }

                Component component;
                try
                {
                    component = factory(manifest);
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
                fingerprints[manifest.Id] = fingerprint;
                batchNewlyStaged.Add(component);
            }

            var ordered = SortByTypedDependencies(staged);
            return new PreparedBatch(desired, staged, ordered, fingerprints, batchNewlyStaged);
        }
        catch
        {
            // Prepare can fail after a factory has already opened a collectible
            // ALC (duplicate provider, cycle, unknown later type). Release every
            // component staged in this failed pass; none of them owns a fiber.
            foreach (var component in batchNewlyStaged)
            {
                ReleaseStaged(component);
            }

            throw;
        }
    }

    private static void ReleaseStaged(Component component)
    {
        if (component is IDisposable disposable)
        {
            disposable.Dispose();
        }
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

        var pending = new List<(string Id, PluginManifest Manifest, string Key, Component Component)>();
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
                _entries[id] = new PluginEntry(manifest, null, configurationKey, component);
                continue;
            }

            pending.Add((id, manifest, configurationKey, component));
        }

        // One decision-domain batch: lifecycle order is unchanged (components
        // are submitted in topological order), but snapshot publication and
        // public replies are coalesced by Core's FlushAwaitingLoads.
        if (pending.Count == 0)
        {
            return;
        }

        var fibers = await _runtime.LoadBatchAsync(pending.Select(item => item.Component));
        for (var index = 0; index < pending.Count; index++)
        {
            var (id, manifest, configurationKey, component) = pending[index];
            var fiber = fibers[index];
            batch.LoadedFibers.Add(fiber);
            batch.Applied.Add(component);
            if (fiber.State == FiberState.Failed)
            {
                throw new InvalidOperationException(
                    $"plugin '{id}' failed to activate", fiber.Failure);
            }

            _entries[id] = new PluginEntry(manifest, fiber, configurationKey, component);
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

        PreparedBatch? restore = null;
        try
        {
            restore = Prepare(previous.Values.Select(entry => entry.Manifest).ToArray(),
                rejectProtectedDisable: false);
            await CommitAsync(restore);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
            _entries.Clear();
        }
        finally
        {
            if (restore is not null)
            {
                await ReleaseUnusedStagedAsync(restore);
            }
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
        var versionedOwners = new Dictionary<
            (string Name, Type ValueType), List<(string Id, IVersionedKey Key)>>();
        foreach (var (id, component) in staged)
        {
            foreach (var key in component.Provide())
            {
                keyOwner[key] = id;
                if (key is IVersionedKey versioned)
                {
                    var index = (versioned.Name, versioned.ValueType);
                    if (!versionedOwners.TryGetValue(index, out var owners))
                    {
                        owners = [];
                        versionedOwners[index] = owners;
                    }

                    owners.Add((id, versioned));
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
                    // Indexed by (name, value type): no linear scan over all
                    // versioned providers for every consumer capability.
                    var best = versionedOwners
                        .TryGetValue((requiredVersion.Name, requiredVersion.ValueType), out var owners)
                        ? owners.Where(candidate => requiredVersion.Accepts(candidate.Key.Version))
                            .OrderByDescending(candidate => candidate.Key.Version)
                            .FirstOrDefault()
                        : default;
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

    /// <summary>
    /// Cheap semantic equivalence for fingerprint reuse: compares the same
    /// fields the fingerprint serializes, without allocating a JSON document
    /// for an unchanged declaration. Config dictionaries are compared by key
    /// and value, not by reference (ApplicationLoader creates new records).
    /// </summary>
    private static bool ManifestsEquivalent(PluginManifest left, PluginManifest right) =>
        left.Id == right.Id &&
        left.Type == right.Type &&
        left.Enabled == right.Enabled &&
        left.Version == right.Version &&
        left.Source == right.Source &&
        left.Checksum == right.Checksum &&
        left.Backend == right.Backend &&
        left.EntryAssembly == right.EntryAssembly &&
        left.EntryType == right.EntryType &&
        left.Requires.SequenceEqual(right.Requires, StringComparer.Ordinal) &&
        left.Provides.SequenceEqual(right.Provides, StringComparer.Ordinal) &&
        left.CapabilitiesOrEmpty.SequenceEqual(right.CapabilitiesOrEmpty, StringComparer.Ordinal) &&
        (left.SharedAssemblies ?? []).SequenceEqual(right.SharedAssemblies ?? [], StringComparer.Ordinal) &&
        left.Config.Count == right.Config.Count &&
        left.Config.All(pair => right.Config.TryGetValue(pair.Key, out var value) &&
                                Equals(pair.Value, value));

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
            manifest.Backend,
            manifest.EntryAssembly,
            manifest.EntryType,
            manifest.Requires,
            manifest.Provides,
            Config = manifest.Config.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            // Both fields change how a component is staged and which capabilities
            // it declares. Without them, a declaration-only change reuses the
            // previous ALC/component and silently keeps the old capability set.
            Capabilities = manifest.CapabilitiesOrEmpty
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SharedAssemblies = (manifest.SharedAssemblies ?? [])
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record PreparedBatch(
        IReadOnlyDictionary<string, PluginManifest> Desired,
        IReadOnlyDictionary<string, Component> Staged,
        IReadOnlyList<(string Id, Component Component)> Ordered,
        IReadOnlyDictionary<string, string> Fingerprints,
        IReadOnlyCollection<Component> NewlyStaged)
    {
        internal List<Fiber> LoadedFibers { get; } = [];

        internal HashSet<Component> Applied { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
