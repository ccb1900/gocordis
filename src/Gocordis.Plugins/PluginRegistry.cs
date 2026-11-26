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

/// <summary>
/// Declarations and the revision they were read at, taken under one gate hold.
/// A reader that plans a later Apply must carry the revision into the commit so
/// a concurrent declaration change cannot be silently overwritten.
/// </summary>
public sealed record DeclarationSnapshot(
    IReadOnlyDictionary<string, PluginEntry> Entries,
    long DeclarationVersion);

/// <summary>A declaration file that could not be discovered, with its error.</summary>
public sealed record ManifestDiagnostic(string Path, Exception Error);

/// <summary>
/// Optimistic-concurrency failure for a declaration batch whose expected
/// revision is no longer current. The batch is rejected BEFORE staging or
/// touching a fiber; no rollback is needed because nothing was mutated.
/// </summary>
public sealed class DeclarationVersionConflictException : InvalidOperationException
{
    public DeclarationVersionConflictException(long expectedVersion, long actualVersion)
        : base($"declaration version conflict: expected {expectedVersion}, current {actualVersion}")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}

/// <summary>
/// Plugin discovery, declaration reconciliation and lifecycle. All fiber state
/// transitions are delegated to Core; this class never writes a fiber directly.
/// A declaration batch is a transaction: prepare validates the whole batch,
/// commit applies it, and rollback restores the previous declarations if any
/// commit step fails.
/// </summary>
public sealed class PluginRegistry : IAsyncDisposable
{
    /// <summary>Framework capability key. Consumers read declarations through
    /// this instead of receiving a host-owned concrete registry.</summary>
    public static readonly Key<PluginRegistry> Key = new("plugin-registry");

    private readonly Runtime _runtime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // All factories are normalized to the manifest-aware shape; the legacy
    // config-only registration is a thin adapter over it.
    private readonly Dictionary<string, Func<PluginManifest, Component>> _factories = [];
    private readonly Dictionary<string, PluginEntry> _entries = [];
    private IReadOnlyList<ManifestDiagnostic> _diagnostics = [];
    private long _declarationVersion;

    /// <summary>Resolves an Entry's isolate annotation to a Core Realm. The host
    /// owns this map; the registry only submits it to RehomeAsync.</summary>
    public Func<string?, string, Realm>? RealmFor { get; set; }

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

    /// <summary>
    /// Monotonic declaration revision. It advances only after a fully committed
    /// Apply; failed or rolled-back batches leave it untouched. Reading it
    /// without the gate is an approximation — use <see cref="ReadDeclarations"/>
    /// when the revision must describe an exact declaration snapshot.
    /// </summary>
    public long DeclarationVersion => Volatile.Read(ref _declarationVersion);

    /// <summary>
    /// Atomically reads declarations together with their revision under the
    /// same gate as Apply/Dispose. This is the read side of the HMR
    /// read→plan→commit optimistic-concurrency guard.
    /// </summary>
    public DeclarationSnapshot ReadDeclarations()
    {
        _gate.Wait();
        try
        {
            return new DeclarationSnapshot(
                new Dictionary<string, PluginEntry>(_entries, StringComparer.Ordinal),
                _declarationVersion);
        }
        finally
        {
            _gate.Release();
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
    /// Prepare → Commit → Rollback. Prepare validates and stages the whole
    /// batch BEFORE a single existing fiber is touched; commit records every
    /// fiber it retires/creates and every in-place edit; rollback reconciles
    /// exactly those fibers back to the previous declarations. The gate covers
    /// all three phases.
    /// </summary>
    public Task ApplyAsync(IEnumerable<PluginManifest> declarations) =>
        ApplyAsync(declarations, expectedVersion: null);

    /// <summary>
    /// Applies a declaration batch only if the revision observed when the batch
    /// was planned is still current. The check and the whole Prepare→Commit→
    /// Rollback sequence share ONE gate hold: a stale caller is rejected before
    /// a single component is staged or a single fiber is touched.
    /// </summary>
    public async Task ApplyAsync(IEnumerable<PluginManifest> declarations, long? expectedVersion)
    {
        await _gate.WaitAsync();
        try
        {
            if (expectedVersion.HasValue && _declarationVersion != expectedVersion.Value)
            {
                throw new DeclarationVersionConflictException(expectedVersion.Value, _declarationVersion);
            }

            var previous = new Dictionary<string, PluginEntry>(_entries, StringComparer.Ordinal);
            var batch = Prepare(previous, declarations.ToArray(), rejectProtectedDisable: true);
            var undo = new RegistryUndo(this, previous);
            try
            {
                await CommitAsync(batch, undo);
            }
            catch (Exception commitError)
            {
                await undo.RollbackAsync(commitError);
                throw;
            }
            finally
            {
                await ReleaseUnusedStagedAsync(batch);
            }

            // Only a committed batch advances the revision. A version check
            // failure, a Prepare failure and a rolled-back Commit all leave the
            // previous revision (and the previous declarations) in place.
            _declarationVersion++;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Applies read-time metadata and an accepted in-place config
    /// update to a live fiber. The undo record is captured before the first
    /// Core command, so a throw halfway through is fully compensated.</summary>
    private async Task ApplyInPlaceAsync(EntryPlan plan, RegistryUndo undo)
    {
        var entry = _entries[plan.Id];
        var fiber = entry.Fiber!;
        if (plan.ApplyConfig)
        {
            var accepted = await ((IConfigurableComponent)plan.Component).TryApplyConfigAsync(plan.Manifest.Config);
            if (!accepted)
            {
                // The component refuses an in-place edit: fall back to a
                // retire/reinsert of THIS entry only, using the replacement
                // component Prepare already staged and validated. Metadata is
                // applied to the replacement by the batch-load step below.
                plan.Component = plan.Fallback ??
                    throw new InvalidOperationException($"missing config fallback for '{plan.Id}'");
                plan.Action = EntryAction.Rebuild;
                plan.Load = true;
                plan.ApplyConfig = false;
                await RetireAsync(plan.Id);
                return;
            }
        }

        if (plan.SetIntercept)
        {
            await _runtime.SetMetaAsync(fiber, EntryMetaKeys.Intercept, plan.Manifest.Intercept ?? string.Empty);
        }

        if (plan.SetParent)
        {
            await _runtime.SetMetaAsync(fiber, EntryMetaKeys.Parent, plan.Manifest.ParentId ?? string.Empty);
        }

        _entries[plan.Id] = entry with { Manifest = plan.Manifest, ConfigurationKey = plan.Fingerprint };
    }

    private async Task ApplyRehomeAsync(EntryPlan plan, RegistryUndo undo, PreparedBatch batch)
    {
        var entry = _entries[plan.Id];
        var fiber = entry.Fiber!;
        undo.CaptureMeta(plan.Id, fiber);
        var replacement = await _runtime.RehomeAsync(fiber, plan.Realm ?? Realm.Default, plan.Component);
        undo.Created(replacement);
        batch.Applied.Add(plan.Component);
        if (plan.SetIntercept)
        {
            await _runtime.SetMetaAsync(replacement, EntryMetaKeys.Intercept, plan.Manifest.Intercept ?? string.Empty);
        }

        if (plan.SetParent)
        {
            await _runtime.SetMetaAsync(replacement, EntryMetaKeys.Parent, plan.Manifest.ParentId ?? string.Empty);
        }

        _entries[plan.Id] = entry with
        {
            Manifest = plan.Manifest,
            Fiber = replacement,
            ConfigurationKey = plan.Fingerprint,
            Component = plan.Component,
        };
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

    /// <summary>
    /// Validates and stages the complete batch before commit mutates a fiber.
    /// The plan is derived from the whole <see cref="EntryDelta"/>: Disabled
    /// retires the fiber regardless of any other field, Rebuild outranks
    /// Rehome, and meta/config edits keep the fiber only when they can really
    /// be applied in place.
    /// </summary>
    private PreparedBatch Prepare(
        IReadOnlyDictionary<string, PluginEntry> previous,
        IReadOnlyList<PluginManifest> declarations,
        bool rejectProtectedDisable)
    {
        var duplicateIds = declarations.GroupBy(manifest => manifest.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"duplicate plugin ids: {string.Join(", ", duplicateIds)}");
        }

        var plans = new List<EntryPlan>();
        var staged = new Dictionary<string, Component>(StringComparer.Ordinal);
        var newlyStaged = new HashSet<Component>(ReferenceEqualityComparer.Instance);
        // ProtectedIds is mutated by hosts outside this transaction gate; read
        // one immutable snapshot so a concurrent registration cannot corrupt
        // HashSet enumeration here.
        var protectedIds = new HashSet<string>(ProtectedIds, StringComparer.Ordinal);

        try
        {
            foreach (var manifest in declarations)
            {
                if (rejectProtectedDisable && protectedIds.Contains(manifest.Id) && !manifest.Enabled)
                {
                    throw new ProtectedPluginException(manifest.Id);
                }

                previous.TryGetValue(manifest.Id, out var prior);
                var delta = prior is null ? default : EntryDiff.Classify(prior.Manifest, manifest);
                var configInPlace = prior?.Fiber is { State: FiberState.Active } &&
                                    delta.ConfigChanged && prior.Component is IConfigurableComponent;
                var action = DecideAction(prior, manifest, delta, configInPlace);
                var fingerprint = Fingerprint(manifest);

                // A disabled declaration has no fiber to rehome/rebuild: reuse
                // its existing component when there is one. Staging a fresh ALC
                // here would be churn, not reconciliation.
                var needsNewComponent = !manifest.Enabled
                    ? prior?.Component is null
                    : action == EntryAction.Rebuild ||
                      (action == EntryAction.Rehome && (delta.ConfigChanged || prior?.Component is null));
                Component component;
                if (!needsNewComponent && prior?.Component is { } reusable)
                {
                    // Unchanged/meta-only declarations keep their component:
                    // no redundant factory call and no redundant ALC load.
                    component = reusable;
                }
                else
                {
                    component = Stage(manifest.Id, manifest, newlyStaged);
                    if (action == EntryAction.Rehome && prior?.Fiber is { } oldFiber &&
                        !string.Equals(component.Name, oldFiber.Component.Name, StringComparison.Ordinal))
                    {
                        // Core's C34 reinsert requires the same component name.
                        // Fall back to an ordinary revision for this entry.
                        action = EntryAction.Rebuild;
                    }
                }

                // Keep a validated replacement ready: a component may refuse an
                // in-place config edit, and rollback rebuilds touched entries
                // from their previous declarations. Both need a ready factory.
                Component? fallback = null;
                if (configInPlace && action == EntryAction.InPlace)
                {
                    fallback = Stage(manifest.Id, manifest, newlyStaged);
                }

                plans.Add(new EntryPlan
                {
                    Id = manifest.Id,
                    Manifest = manifest,
                    Delta = delta,
                    Fingerprint = fingerprint,
                    Action = action,
                    Component = component,
                    Fallback = fallback,
                    SetIntercept = delta.InterceptChanged &&
                                   action is EntryAction.InPlace or EntryAction.Rehome or EntryAction.Rebuild,
                    SetParent = delta.ParentChanged &&
                                action is EntryAction.InPlace or EntryAction.Rehome or EntryAction.Rebuild,
                    ApplyConfig = configInPlace && action == EntryAction.InPlace,
                    Realm = action == EntryAction.Rehome
                        ? RealmFor?.Invoke(manifest.Isolate, manifest.Id) ?? Realm.Default
                        : null,
                    Load = action == EntryAction.Rebuild && manifest.Enabled,
                });
                staged[manifest.Id] = component;
            }

            // Only the effective component of each entry participates in the
            // duplicate-provider check; an in-place config fallback provides
            // the same keys as its owner and is not a second provider.
            var providedKeys = new Dictionary<ICapabilityKey, string>(ReferenceEqualityComparer.Instance);
            foreach (var plan in plans)
            {
                foreach (var key in plan.Component.Provide())
                {
                    if (providedKeys.TryGetValue(key, out var owner))
                    {
                        throw new DuplicateProviderException(
                            $"key '{key.Name}' is provided by both '{owner}' and '{plan.Id}'");
                    }

                    providedKeys[key] = plan.Id;
                }
            }

            // A disabled declaration still owns its capability names. Consumers
            // of such a key must stay Pending when the provider is withdrawn,
            // not turn a valid declaration batch into a Prepare error.
            var inactiveCapabilities = DeclaredCapabilitiesOfDisabled(declarations);
            var ordered = SortByTypedDependencies(staged, inactiveCapabilities)
                .Select(item => plans.Single(plan => plan.Id == item.Id))
                .ToArray();
            return new PreparedBatch(staged, ordered, newlyStaged.ToArray());
        }
        catch
        {
            // Prepare can fail after a factory has already opened a collectible
            // ALC (duplicate provider, cycle, unknown later type). Release every
            // component staged in this failed pass; none of them owns a fiber.
            foreach (var component in newlyStaged)
            {
                ReleaseStaged(component);
            }

            throw;
        }
    }

    private static EntryAction DecideAction(
        PluginEntry? prior, PluginManifest manifest, EntryDelta delta, bool configInPlace)
    {
        if (prior is null)
        {
            return EntryAction.Rebuild;
        }

        // Administrative state is absolute: disable retires the fiber even if
        // config, metadata, realm or declaration fields changed at the same time.
        if (!manifest.Enabled)
        {
            return EntryAction.Rebuild;
        }

        if (delta.Rebuild)
        {
            return EntryAction.Rebuild;
        }

        if (delta.Rehome)
        {
            return EntryAction.Rehome;
        }

        if (prior.Fiber is not { State: FiberState.Active })
        {
            return EntryAction.Rebuild;
        }

        if (delta.ConfigChanged && !configInPlace)
        {
            return EntryAction.Rebuild;
        }

        return delta.IsEmpty ? EntryAction.Keep : EntryAction.InPlace;
    }

    private Component Stage(string id, PluginManifest manifest, HashSet<Component> newlyStaged)
    {
        if (!_factories.TryGetValue(manifest.Type, out var factory))
        {
            throw new InvalidOperationException($"unknown plugin type '{manifest.Type}' for '{id}'");
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
            throw new InvalidOperationException($"factory for '{id}' failed: {ex.Message}", ex);
        }

        newlyStaged.Add(component);
        return component;
    }

    private static void ReleaseStaged(Component component)
    {
        if (component is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task CommitAsync(PreparedBatch batch, RegistryUndo undo)
    {
        var protectedIds = new HashSet<string>(ProtectedIds, StringComparer.Ordinal);
        // Platform-owned ids survive a declaration that omits them entirely.
        foreach (var id in _entries.Keys.Except(batch.ById.Keys).ToArray())
        {
            if (protectedIds.Contains(id))
            {
                continue;
            }

            undo.Touch(id);
            await RetireAsync(id);
        }

        // Only entries whose runtime state really changes are recorded for
        // rollback. An unchanged entry must keep its live fiber when a later
        // step of the batch fails.
        foreach (var plan in batch.Ordered)
        {
            switch (plan.Action)
            {
                case EntryAction.Keep:
                    if (_entries.TryGetValue(plan.Id, out var kept))
                    {
                        _entries[plan.Id] = kept with
                        {
                            Manifest = plan.Manifest,
                            ConfigurationKey = plan.Fingerprint,
                        };
                    }

                    break;
                case EntryAction.InPlace:
                    undo.Touch(plan.Id);
                    await ApplyInPlaceAsync(plan, undo);
                    break;
                case EntryAction.Rebuild:
                    undo.Touch(plan.Id);
                    await RetireAsync(plan.Id);
            if (!plan.Load)
            {
                // Never retain the retired component here: a managed component
                // roots its collectible ALC, and a disabled entry must not keep
                // that ALC alive after Core has unwound the fiber.
                _entries[plan.Id] = new PluginEntry(plan.Manifest, null, plan.Fingerprint, null);
            }


                    break;
            }
        }

        // Rehome consumers before their providers: a consumer that follows a
        // provider into a new realm must still be Active when Core retires the
        // provider's old fiber (C34 rehome requires an Active fiber).
        foreach (var plan in batch.Ordered.Reverse())
        {
            if (plan.Action != EntryAction.Rehome)
            {
                continue;
            }

            undo.Touch(plan.Id);
            await ApplyRehomeAsync(plan, undo, batch);
        }

        // One decision-domain batch: lifecycle order is unchanged (components
        // are submitted in topological order), but snapshot publication and
        // public replies are coalesced by Core's FlushAwaitingLoads.
        var pending = batch.Ordered
            .Where(plan => plan.Action == EntryAction.Rebuild && plan.Load)
            .ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var fibers = await _runtime.LoadBatchAsync(pending.Select(plan => plan.Component));
        for (var index = 0; index < pending.Length; index++)
        {
            var plan = pending[index];
            var fiber = fibers[index];
            undo.Created(fiber);
            batch.Applied.Add(plan.Component);
            if (fiber.State == FiberState.Failed)
            {
                throw new InvalidOperationException(
                    $"plugin '{plan.Id}' failed to activate", fiber.Failure);
            }

            _entries[plan.Id] = new PluginEntry(plan.Manifest, fiber, plan.Fingerprint, plan.Component);
            if (plan.SetIntercept)
            {
                await _runtime.SetMetaAsync(fiber, EntryMetaKeys.Intercept, plan.Manifest.Intercept ?? string.Empty);
            }

            if (plan.SetParent)
            {
                await _runtime.SetMetaAsync(fiber, EntryMetaKeys.Parent, plan.Manifest.ParentId ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Explicit undo record for one Apply. It covers every entry whose
    /// declaration or fiber the batch touched AND every fiber the batch
    /// created, including in-place-modified originals (which are NOT part of
    /// PreparedBatch's loaded fibers). Restoring a touched entry always means
    /// disposing any fiber it has now and loading a fresh component from its
    /// previous manifest, so an accepted in-place config edit cannot survive a
    /// later commit failure, and untouched entries keep their live fibers.
    /// </summary>
    private sealed class RegistryUndo
    {
        private readonly PluginRegistry _owner;
        private readonly IReadOnlyDictionary<string, PluginEntry> _previous;
        private readonly HashSet<string> _touched = new(StringComparer.Ordinal);
        private readonly List<string> _restoreOrder = [];
        private readonly HashSet<Fiber> _created = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Guid> _createdIds = [];
        private readonly HashSet<Guid> _fiberIdsBefore;
        private readonly Dictionary<string, Dictionary<IMetaKey, object?>> _meta = new(StringComparer.Ordinal);
        private readonly List<Exception> _errors = [];

        public RegistryUndo(PluginRegistry owner, IReadOnlyDictionary<string, PluginEntry> previous)
        {
            _owner = owner;
            _previous = previous;
            _fiberIdsBefore = owner._runtime.Snapshot().Fibers
                .Select(fiber => fiber.Id).ToHashSet();
        }

        public void Touch(string id)
        {
            if (_touched.Add(id))
            {
                // Commit visits plans in dependency order; restore in that same
                // order so a provider is back before its consumer is reloaded.
                _restoreOrder.Add(id);
                if (_owner._entries.TryGetValue(id, out var entry) && entry.Fiber is { } fiber)
                {
                    CaptureMeta(id, fiber);
                }
            }
        }

        public void Created(Fiber fiber)
        {
            _created.Add(fiber);
            _createdIds.Add(fiber.Id);
        }

        public void CaptureMeta(string id, Fiber fiber)
        {
            if (_meta.ContainsKey(id))
            {
                return;
            }

            var values = new Dictionary<IMetaKey, object?>(ReferenceEqualityComparer.Instance);
            foreach (var key in new IMetaKey[] { EntryMetaKeys.Intercept, EntryMetaKeys.Parent })
            {
                values[key] = _owner._runtime.GetMeta(fiber, key);
            }

            _meta[id] = values;
        }

        public async Task RollbackAsync(Exception original)
        {
            _errors.Add(original);

            // A failed LoadBatchAsync can leave a fiber behind before it
            // returns, so combine the explicit record with a snapshot diff.
            // Both sets only contain fibers that did not exist before Apply.
            var batchFibers = _owner._runtime.Snapshot().Fibers
                .Where(fiber => !_fiberIdsBefore.Contains(fiber.Id))
                .Select(fiber => _owner._runtime.GetFiber(fiber.Id))
                .OfType<Fiber>()
                .ToArray();
            foreach (var fiber in batchFibers)
            {
                Created(fiber);
            }

            foreach (var fiber in _created)
            {
                await SafeAsync(() => _owner._runtime.DisposeAsync(fiber));
            }

            foreach (var id in _restoreOrder)
            {
                _owner._entries.TryGetValue(id, out var current);
                _previous.TryGetValue(id, out var before);

                // An in-place-edited original fiber is not in _created: dispose
                // it explicitly so no stale Active fiber or duplicate provider
                // survives next to the restored one.
                if (current?.Fiber is { } live && !_createdIds.Contains(live.Id))
                {
                    await SafeAsync(() => _owner._runtime.DisposeAsync(live));
                }

                if (before is null)
                {
                    _owner._entries.Remove(id);
                    continue;
                }

                if (before.Fiber is null || !before.Manifest.Enabled)
                {
                    _owner._entries[id] = before;
                    continue;
                }

                var scratch = new HashSet<Component>(ReferenceEqualityComparer.Instance);
                try
                {
                    var component = _owner.Stage(id, before.Manifest, scratch);
                    var realm = _owner.RealmFor?.Invoke(before.Manifest.Isolate, id);
                    var restored = await _owner._runtime.LoadAsync(component, realm: realm);
                    if (restored.State == FiberState.Failed)
                    {
                        throw new InvalidOperationException(
                            $"rollback could not reactivate '{id}'", restored.Failure);
                    }

                    if (_meta.TryGetValue(id, out var values))
                    {
                        foreach (var (key, value) in values)
                        {
                            await SafeAsync(() => _owner._runtime.SetMetaAsync(restored, key, value));
                        }
                    }

                    _owner._entries[id] = before with { Component = component, Fiber = restored };
                }
                catch (Exception ex)
                {
                    _errors.Add(ex);
                    foreach (var staged in scratch)
                    {
                        ReleaseStaged(staged);
                    }

                    // Keep the declaration; the runtime has no fiber for it now.
                    _owner._entries[id] = before with { Fiber = null };
                }
            }

            if (_errors.Count > 1)
            {
                throw new AggregateException("registry rollback encountered additional errors", _errors);
            }
        }

        private async Task SafeAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _errors.Add(ex);
            }
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

    private sealed record InactiveCapabilities(
        HashSet<ICapabilityKey> Typed,
        HashSet<string> DeclaredNames);

    /// <summary>Keys of providers that are declared but currently disabled. Core
    /// keeps consumers of those keys Pending; Prepare must not reject the batch.
    /// A previously loaded component gives the exact Key&lt;T&gt; identity. On a
    /// cold disable there is no component yet, so package-declared names are
    /// kept separately as declaration-level evidence only — never as a binding
    /// (Core still matches by reference identity).</summary>
    private InactiveCapabilities DeclaredCapabilitiesOfDisabled(
        IReadOnlyList<PluginManifest> declarations)
    {
        var typed = new HashSet<ICapabilityKey>(ReferenceEqualityComparer.Instance);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var disabledIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in declarations)
        {
            if (manifest.Enabled)
            {
                continue;
            }

            disabledIds.Add(manifest.Id);
            foreach (var name in manifest.Provides)
            {
                names.Add(name);
            }

            foreach (var wire in manifest.CapabilitiesOrEmpty)
            {
                var declaration = PluginManifest.ParseCapabilities([wire])[0];
                if (declaration.Direction == CapabilityDirection.Provide)
                {
                    names.Add(declaration.Id.ToString());
                }
            }
        }

        foreach (var id in disabledIds)
        {
            if (_entries.TryGetValue(id, out var previous) && previous.Component is { } component)
            {
                foreach (var key in component.Provide())
                {
                    typed.Add(key);
                }
            }
        }

        return new InactiveCapabilities(typed, names);
    }

    private static bool IsDeclaredInactiveCapability(InactiveCapabilities inactive, ICapabilityKey key)
    {
        if (inactive.Typed.Contains(key) || inactive.DeclaredNames.Contains(key.Name))
        {
            return true;
        }

        // Package wires use namespace/name; typed keys use a dotted flat name.
        var lastDot = key.Name.LastIndexOf('.');
        return lastDot > 0 &&
               inactive.DeclaredNames.Contains(key.Name[..lastDot] + "/" + key.Name[(lastDot + 1)..]);
    }

    private IReadOnlyList<(string Id, Component Component)> SortByTypedDependencies(
        IReadOnlyDictionary<string, Component> staged, InactiveCapabilities inactiveCapabilities)
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

                    if (!ExternalCapabilities.Contains(requiredVersion) &&
                        !IsDeclaredInactiveCapability(inactiveCapabilities, requiredVersion))
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

                if (!ExternalCapabilities.Contains(key) &&
                    !IsDeclaredInactiveCapability(inactiveCapabilities, key))
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

    private enum EntryAction
    {
        Keep,
        InPlace,
        Rehome,
        Rebuild,
    }

    /// <summary>One entry's part of the batch plan. Fields whose combination
    /// would retire the fiber are already resolved into <see cref="Action"/>
    /// before commit runs.</summary>
    private sealed class EntryPlan
    {
        public required string Id { get; init; }
        public required PluginManifest Manifest { get; init; }
        public required EntryDelta Delta { get; init; }
        public required string Fingerprint { get; init; }
        public EntryAction Action { get; set; }
        public required Component Component { get; set; }
        public Component? Fallback { get; init; }
        public bool SetIntercept { get; init; }
        public bool SetParent { get; init; }
        public bool ApplyConfig { get; set; }
        public Realm? Realm { get; init; }
        public bool Load { get; set; }
    }

    private sealed record PreparedBatch(
        IReadOnlyDictionary<string, Component> ById,
        IReadOnlyList<EntryPlan> Ordered,
        IReadOnlyCollection<Component> NewlyStaged)
    {
        internal HashSet<Component> Applied { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
