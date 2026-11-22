using System.Collections.Concurrent;
using NuGet.Versioning;

namespace Gocordis.Core;

/// <summary>
/// The single lifecycle authority. External code submits declarations (Load/Dispose);
/// only reconciliation writes fiber state. All transitions are serialized.
/// </summary>
public sealed class Runtime : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Revocable lease (not an inheritable bool): background tasks inherit
    // ExecutionContext; a stale lease must not keep bypassing the gate (P0-2).
    private readonly AsyncLocal<GateLease?> _gateLease = new();
    private readonly ConcurrentDictionary<Guid, Fiber> _fibers = [];
    private readonly ConcurrentDictionary<ICapabilityKey, Fiber> _providers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Realm, Dictionary<ICapabilityKey, Fiber>> _realmProviders = [];
    private readonly List<VersionedBinding> _versionedProviders = [];

    private sealed record VersionedBinding(
        Realm Realm,
        string Name,
        Type ValueType,
        NuGetVersion Version,
        Fiber Provider,
        object Value);
    private readonly Dictionary<ICapabilityKey, List<Handler>> _handlers =
        new(ReferenceEqualityComparer.Instance);
    private bool _closed;
    private bool _bindingsChanged;
    private readonly HashSet<Guid> _withdrawing = [];

    private sealed record Handler(Fiber Owner, Func<object?, ValueTask> Invoke);

    /// <summary>Reentrant lease: Apply may create owned children without deadlocking.</summary>
    private sealed class GateLease : IDisposable
    {
        internal static readonly GateLease Reentrant = new(null, null, false);

        private readonly SemaphoreSlim? _gate;
        private readonly Action? _restore;
        private bool _active;

        internal GateLease(SemaphoreSlim? gate, Action? restore, bool active)
        {
            _gate = gate;
            _restore = restore;
            _active = active;
        }

        internal bool Active => _active;

        public void Dispose()
        {
            _active = false;
            if (_gate is not null)
            {
                _restore?.Invoke();
                _gate.Release();
            }
        }
    }

    private async Task<GateLease> EnterAsync()
    {
        var inherited = _gateLease.Value;
        if (inherited is not null && inherited.Active)
        {
            return GateLease.Reentrant; // explicit reentrant child-creation path
        }

        await _gate.WaitAsync();
        var previous = inherited;
        var lease = new GateLease(_gate, () => _gateLease.Value = previous, active: true);
        _gateLease.Value = lease;
        return lease;
    }

    public async Task<Fiber> LoadAsync(Component component, Fiber? parent = null, Realm? realm = null)
    {
        ArgumentNullException.ThrowIfNull(component);

        var lease = await EnterAsync();
        // AsyncLocal changes inside EnterAsync do not flow back to the caller;
        // publish the lease here for nested child creation on this frame.
        if (!ReferenceEquals(lease, GateLease.Reentrant))
        {
            _gateLease.Value = lease;
        }
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (parent is not null && !_fibers.ContainsKey(parent.Id))
            {
                throw new InvalidOperationException("parent fiber does not belong to this runtime");
            }

            ValidateDeclarations(component, realm ?? Realm.Default);
            var fiber = new Fiber(Guid.NewGuid(), component, parent, generation: 0)
            {
                Realm = realm ?? Realm.Default,
            };
            _fibers[fiber.Id] = fiber;
            parent?.AddChild(fiber);

            var cycle = FindCycle();
            if (cycle is not null)
            {
                _fibers.TryRemove(fiber.Id, out _);
                parent?.RemoveChild(fiber);
                throw new DependencyCycleException($"dependency cycle: {cycle}");
            }

            await ReconcileCoreAsync();
            return fiber;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>Retire a fiber. The entry disappears; declaration validation already happened.</summary>
    public async Task DisposeAsync(Fiber fiber)
    {
        ArgumentNullException.ThrowIfNull(fiber);

        // Set the target before taking the gate so an in-flight activation can divert.
        fiber.Intent = LifecycleIntent.Retired;
        CancelActivation(fiber);

        var lease = await EnterAsync();
        // AsyncLocal changes inside EnterAsync do not flow back to the caller;
        // publish the lease here for nested child creation on this frame.
        if (!ReferenceEquals(lease, GateLease.Reentrant))
        {
            _gateLease.Value = lease;
        }
        try
        {
            if (fiber.State == FiberState.Gone)
            {
                return;
            }

            await DeactivateAsync(fiber);
            fiber.State = FiberState.Gone;
            _fibers.TryRemove(fiber.Id, out _);
            fiber.Parent?.RemoveChild(fiber);
            fiber.SignalGone();
            _bindingsChanged = true;
            await ReconcileCoreAsync();
        }
        finally
        {
            lease.Dispose();
        }
    }

    public async Task CloseAsync()
    {
        var lease = await EnterAsync();
        // AsyncLocal changes inside EnterAsync do not flow back to the caller;
        // publish the lease here for nested child creation on this frame.
        if (!ReferenceEquals(lease, GateLease.Reentrant))
        {
            _gateLease.Value = lease;
        }
        try
        {
            _closed = true;
            foreach (var fiber in _fibers.Values)
            {
                fiber.Intent = LifecycleIntent.Retired;
                CancelActivation(fiber);
            }

            foreach (var fiber in _fibers.Values.OrderByDescending(f => f.Parent is null ? 0 : 1).ToArray())
            {
                await DeactivateAsync(fiber);
                fiber.State = FiberState.Gone;
                fiber.SignalGone();
            }

            _fibers.Clear();
            _providers.Clear();
            _realmProviders.Clear();
            _versionedProviders.Clear();
            _handlers.Clear();
        }
        finally
        {
            lease.Dispose();
        }
    }

    public RuntimeSnapshot Snapshot()
    {
        return new RuntimeSnapshot(_fibers.Values.Select(f => new FiberSnapshot(
            f.Id,
            f.Component.Name,
            f.State,
            f.Generation,
            f.Parent?.Id,
            f.Component.Inject().Select(k => k.Name).ToArray(),
            f.Component.Provide().Select(k => k.Name).ToArray(),
            f.Committed.Select(pair => $"{pair.Key.Name}->{pair.Value.Id}").ToArray(),
            f.Failure?.Message)).ToArray());
    }

    /// <summary>Read-only lookup of a fiber, including one still activating.</summary>
    public Fiber? GetFiber(Guid id) => _fibers.TryGetValue(id, out var fiber) ? fiber : null;

    private static Realm EffectiveRealm(Fiber fiber, ICapabilityKey key) =>
        fiber.RealmOverrides.TryGetValue(key, out var realm) ? realm : fiber.Realm;

    private Fiber? FindProvider(Realm realm, ICapabilityKey key)
    {
        if (ReferenceEquals(realm, Realm.Default))
        {
            return _providers.GetValueOrDefault(key);
        }

        return _realmProviders.TryGetValue(realm, out var table)
            ? table.GetValueOrDefault(key)
            : null;
    }

    private void SetProvider(Realm realm, ICapabilityKey key, Fiber fiber)
    {
        if (ReferenceEquals(realm, Realm.Default))
        {
            _providers[key] = fiber;
            return;
        }

        if (!_realmProviders.TryGetValue(realm, out var table))
        {
            table = new Dictionary<ICapabilityKey, Fiber>(ReferenceEqualityComparer.Instance);
            _realmProviders[realm] = table;
        }

        table[key] = fiber;
    }

    private void RemoveProvider(Realm realm, ICapabilityKey key)
    {
        if (ReferenceEquals(realm, Realm.Default))
        {
            _providers.TryRemove(key, out _);
            return;
        }

        if (_realmProviders.TryGetValue(realm, out var table))
        {
            table.Remove(key);
            if (table.Count == 0)
            {
                _realmProviders.Remove(realm);
            }
        }
    }


    internal void RegisterVersioned<T>(VersionedKey<T> key, T value, Fiber provider)
    {
        var realm = EffectiveRealm(provider, key);
        var duplicate = _versionedProviders.FirstOrDefault(binding =>
            ReferenceEquals(binding.Realm, realm) &&
            binding.Name == key.Name &&
            binding.ValueType == typeof(T) &&
            binding.Version == key.Version &&
            !ReferenceEquals(binding.Provider, provider));
        if (duplicate is not null)
        {
            throw new DuplicateProviderException(
                $"key '{key.Name}' version {key.Version} is already provided by '{duplicate.Provider.Component.Name}'");
        }

        _versionedProviders.RemoveAll(binding =>
            ReferenceEquals(binding.Provider, provider) &&
            binding.Name == key.Name &&
            binding.ValueType == typeof(T));
        _versionedProviders.Add(new VersionedBinding(realm, key.Name, typeof(T), key.Version, provider, value!));
        MarkBindingsChanged(); // a higher compatible version can change consumers' target
    }

    internal void RemoveVersioned(Fiber provider, ICapabilityKey key)
    {
        _versionedProviders.RemoveAll(binding =>
            ReferenceEquals(binding.Provider, provider) && binding.Name == key.Name);
        MarkBindingsChanged();
    }

    // Single source of truth for the target binding: highest compatible
    // version in the consumer realm (paper §6.6). Used by both Resolve and
    // Satisfied so reconciliation can detect a stale committed binding.
    private VersionedBinding? TargetVersioned(IVersionedKey key, Fiber consumer)
    {
        var realm = EffectiveRealm(consumer, (ICapabilityKey)key);
        return _versionedProviders
            .Where(binding => ReferenceEquals(binding.Realm, realm) &&
                              binding.Name == key.Name &&
                              binding.ValueType == key.ValueType &&
                              key.Accepts(binding.Version) &&
                              !ReferenceEquals(binding.Provider, consumer) &&
                              binding.Provider.State == FiberState.Active)
            .OrderByDescending(binding => binding.Version)
            .FirstOrDefault();
    }

    internal T ResolveVersioned<T>(VersionedKey<T> key, Fiber consumer)
    {
        var binding = TargetVersioned(key, consumer);
        if (binding is null)
        {
            throw new DependencyMissingException(
                $"'{consumer.Component.Name}' requires '{key.Name}' matching '{key.Constraint ?? (IFormattable)key.Version}' " +
                $"but no active provider exists");
        }

        consumer.Committed[key] = binding.Provider;
        return (T)binding.Value;
    }

    private bool HasVersionedProvider(IVersionedKey key, Fiber consumer)
    {
        var target = TargetVersioned(key, consumer);
        if (target is null)
        {
            return false;
        }

        // A consumer committed to a lower compatible provider is NOT satisfied
        // once a higher compatible version appears: it must unwind and
        // re-activate against the new target (P1-1).
        if (consumer.Committed.TryGetValue((ICapabilityKey)key, out var current) &&
            !ReferenceEquals(current, target.Provider))
        {
            return false;
        }

        return true;
    }

    internal T RequireValue<T>(CapabilityKey key, Fiber consumer)
    {
        var provider = FindProvider(EffectiveRealm(consumer, key), key);
        if (provider is null ||
            provider.State != FiberState.Active ||
            ReferenceEquals(provider, consumer) ||
            !provider.Provided.TryGetValue(key, out var value))
        {
            throw new DependencyMissingException(
                $"'{consumer.Component.Name}' requires '{key.Name}' but no active provider exists");
        }

        consumer.Committed[key] = provider;
        return (T)value!;
    }

    internal void MarkBindingsChanged() => _bindingsChanged = true;

    internal ValueTask RegisterHandler<T>(EventKey<T> key, Func<T, ValueTask> handler, Fiber owner)
    {
        if (!_handlers.TryGetValue(key, out var list))
        {
            list = [];
            _handlers[key] = list;
        }

        var entry = new Handler(owner, payload => handler((T)payload!));
        list.Add(entry);
        owner.AddInverse(() =>
        {
            list.Remove(entry);
            return ValueTask.CompletedTask;
        });
        return ValueTask.CompletedTask;
    }

    internal async ValueTask EmitAsync<T>(EventKey<T> key, T payload, Fiber emitter)
    {
        if (!_handlers.TryGetValue(key, out var handlers))
        {
            return;
        }

        Exception? failure = null;
        foreach (var handler in handlers.ToArray())
        {
            if (handler.Owner.State is FiberState.Gone or FiberState.Unloading)
            {
                continue;
            }

            try
            {
                await handler.Invoke(payload);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private async Task ReconcileCoreAsync()
    {
        do
        {
            _bindingsChanged = false;

            // A nested reconcile (child creation from Apply) must never touch a
            // fiber whose own activation frame is still live.
            foreach (var fiber in _fibers.Values
                         .Where(f => (f.State is FiberState.Active or FiberState.Loading) && !f.IsActivating)
                         .ToArray())
            {
                if (!Satisfied(fiber))
                {
                    await DeactivateAsync(fiber);
                    fiber.State = FiberState.Pending;
                    _bindingsChanged = true;
                }
            }

            foreach (var fiber in _fibers.Values
                         .Where(f => f.State == FiberState.Pending &&
                                     !f.IsActivating &&
                                     f.Failure is null &&
                                     f.Intent == LifecycleIntent.Load)
                         .ToArray())
            {
                if (Satisfied(fiber))
                {
                    await ActivateAsync(fiber);
                }
            }
        }
        while (_bindingsChanged);
    }

    private bool Satisfied(Fiber fiber)
    {
        foreach (var key in fiber.Component.Inject().Where(key => key is not IMetaKey))
        {
            if (key is IVersionedKey versioned)
            {
                if (!HasVersionedProvider(versioned, fiber))
                {
                    return false;
                }

                continue;
            }

            var provider = FindProvider(EffectiveRealm(fiber, key), key);
            if (provider is null ||
                provider.State != FiberState.Active ||
                ReferenceEquals(provider, fiber))
            {
                return false;
            }
        }

        return true;
    }

    private async Task ActivateAsync(Fiber fiber)
    {
        fiber.IsActivating = true;
        try
        {
            await ActivateCoreAsync(fiber);
        }
        finally
        {
            fiber.IsActivating = false;
        }
    }

    private async Task ActivateCoreAsync(Fiber fiber)
    {
        fiber.State = FiberState.Loading;
        fiber.Generation++;
        var generation = fiber.Generation;
        fiber.ActivationCancellation?.Dispose();
        fiber.ActivationCancellation = new CancellationTokenSource();
        var context = new Context(fiber, this, generation);

        try
        {
            Func<ValueTask>? directInverse = null;
            await foreach (var step in StepsAsync(fiber.Component, context, inverse =>
                           {
                               // Single Apply already installed its effect; record the
                               // inverse immediately so a later divert cannot drop it.
                               directInverse = inverse;
                               if (inverse is not null)
                               {
                                   fiber.AddInverse(inverse);
                               }
                           }))
            {
                context.ThrowIfDiverted();
                var inverse = await step.Install();
                if (context.IsDiverted)
                {
                    if (inverse is not null && !ReferenceEquals(inverse, directInverse))
                    {
                        await inverse();
                    }

                    throw new DivertedException();
                }

                if (inverse is not null && !ReferenceEquals(inverse, directInverse))
                {
                    fiber.AddInverse(inverse);
                }
            }

            foreach (var key in fiber.Component.Provide())
            {
                if (!fiber.Provided.ContainsKey(key))
                {
                    continue;
                }

                var realm = EffectiveRealm(fiber, key);
                var existing = FindProvider(realm, key);
                if (existing is not null && !ReferenceEquals(existing, fiber))
                {
                    throw new DuplicateProviderException(
                        $"key '{key.Name}' is already provided by '{existing.Component.Name}' in realm '{realm.Name}'");
                }

                SetProvider(realm, key, fiber);
            }

            fiber.State = FiberState.Active;
            _bindingsChanged = true;
        }
        catch (Exception) when (context.IsDiverted)
        {
            await UnwindAsync(fiber);
            fiber.State = FiberState.Pending;
        }
        catch (Exception ex)
        {
            fiber.Failure = ex;
            await UnwindAsync(fiber);
            fiber.State = FiberState.Failed;
        }
    }

    private static async IAsyncEnumerable<EffectRegistration> StepsAsync(
        Component component,
        Context context,
        Action<Func<ValueTask>?> registerDirectInverse)
    {
        if (component is IIterativeComponent iterative)
        {
            await foreach (var step in iterative.IterateAsync(context))
            {
                yield return step;
            }

            yield break;
        }

        var cleanup = await component.ApplyAsync(context);
        Func<ValueTask>? inverse = cleanup is null ? null : () => cleanup();
        registerDirectInverse(inverse);
        yield return new EffectRegistration(() => new ValueTask<Func<ValueTask>?>(inverse));
    }

    private async Task DeactivateAsync(Fiber fiber)
    {
        if (!_withdrawing.Add(fiber.Id))
        {
            return;
        }

        try
        {
            CancelActivation(fiber);

            // Relied guard (paper Def 54): consumers that committed to this
            // provider must finish their teardown while it is still Active.
            foreach (var consumer in _fibers.Values
                         .Where(f => !ReferenceEquals(f, fiber) &&
                                     f.State is FiberState.Active or FiberState.Loading &&
                                     f.Committed.Values.Any(provider => ReferenceEquals(provider, fiber)))
                         .ToArray())
            {
                await DeactivateAsync(consumer);
                consumer.State = FiberState.Pending;
                _bindingsChanged = true;
            }

            fiber.State = FiberState.Unloading;
            await UnwindAsync(fiber);
        }
        finally
        {
            _withdrawing.Remove(fiber.Id);
        }
    }

    private static void CancelActivation(Fiber fiber)
    {
        try
        {
            fiber.ActivationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // An earlier activation already replaced this token source.
        }
    }

    private async Task UnwindAsync(Fiber fiber)
    {
        for (var i = fiber.ChildList.Count - 1; i >= 0; i--)
        {
            var child = fiber.ChildList[i];
            if (child.State == FiberState.Gone)
            {
                continue;
            }

            await DeactivateAsync(child);
            child.State = FiberState.Gone;
            _fibers.TryRemove(child.Id, out _);
            child.SignalGone();
        }

        // LIFO: the inverse of the last installed effect runs first.
        for (var i = fiber.Inverses.Count - 1; i >= 0; i--)
        {
            fiber.EffectStates[i] = EffectState.Undoing;
            try
            {
                await fiber.Inverses[i]();
            }
            catch
            {
                // A failing inverse must not prevent the remaining inverses.
            }

            fiber.EffectStates[i] = EffectState.Undone;
        }

        var providedRealms = fiber.Component.Provide()
            .ToDictionary(key => key, key => EffectiveRealm(fiber, key), ReferenceEqualityComparer.Instance);
        fiber.Inverses.Clear();
        fiber.EffectStates.Clear();
        fiber.Provided.Clear();
        // P0-3: clear committed provider references AFTER inverses ran so a
        // Pending consumer cannot keep a disposed provider's ALC alive.
        fiber.Committed.Clear();
        _versionedProviders.RemoveAll(binding => ReferenceEquals(binding.Provider, fiber));
        fiber.Metadata.Clear();
        foreach (var key in fiber.Component.Provide())
        {
            var realm = providedRealms[key];
            var provider = FindProvider(realm, key);
            if (ReferenceEquals(provider, fiber))
            {
                RemoveProvider(realm, key);
            }
        }
    }

    private void ValidateDeclarations(Component component, Realm realm)
    {
        EnsureUnique(component.Inject(), "inject");
        EnsureUnique(component.Provide(), "provide");

        foreach (var key in component.Provide())
        {
            foreach (var fiber in _fibers.Values.Where(f => f.State != FiberState.Gone))
            {
                if (fiber.Component.Provide().Any(k =>
                        ReferenceEquals(k, key) && ReferenceEquals(EffectiveRealm(fiber, k), realm)))
                {
                    throw new DuplicateProviderException(
                        $"key '{key.Name}' is already provided by '{fiber.Component.Name}' in realm '{realm.Name}'");
                }
            }
        }
    }

    private static void EnsureUnique(IReadOnlyList<ICapabilityKey> keys, string side)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = i + 1; j < keys.Count; j++)
            {
                if (ReferenceEquals(keys[i], keys[j]))
                {
                    throw new UndeclaredCapabilityException($"duplicate {side} declaration '{keys[i].Name}'");
                }
            }
        }
    }

    private string? FindCycle()
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var chain = new List<string>();

        // Cycle detection reads declarations, not current bindings: a cycle can
        // never be satisfied, so it must be rejected at the mount boundary.
        var versionedDeclarations = _fibers.Values
            .SelectMany(fiber => fiber.Component.Provide().OfType<IVersionedKey>()
                .Select(key => (Fiber: fiber, Key: key)))
            .ToArray();
        var declaredProviders = new Dictionary<(Realm, ICapabilityKey), Fiber>();
        foreach (var fiber in _fibers.Values)
        {
            foreach (var key in fiber.Component.Provide())
            {
                declaredProviders.TryAdd((EffectiveRealm(fiber, key), key), fiber);
            }
        }

        bool Visit(Fiber fiber)
        {
            if (visiting.Contains(fiber.Id))
            {
                chain.Add(fiber.Component.Name);
                return true;
            }

            if (!visited.Add(fiber.Id))
            {
                return false;
            }

            visiting.Add(fiber.Id);
            foreach (var key in fiber.Component.Inject().Where(key => key is not IMetaKey))
            {
                if (key is IVersionedKey requiredVersion)
                {
                    var best = versionedDeclarations
                        .Where(candidate => candidate.Key.Name == requiredVersion.Name &&
                                            candidate.Key.ValueType == requiredVersion.ValueType &&
                                            requiredVersion.Accepts(candidate.Key.Version))
                        .OrderByDescending(candidate => candidate.Key.Version)
                        .Select(candidate => candidate.Fiber)
                        .FirstOrDefault();
                    if (best is not null && Visit(best))
                    {
                        chain.Add(fiber.Component.Name);
                        return true;
                    }

                    continue;
                }

                if (declaredProviders.TryGetValue((EffectiveRealm(fiber, key), key), out var provider) &&
                    Visit(provider))
                {
                    chain.Add(fiber.Component.Name);
                    return true;
                }
            }

            visiting.Remove(fiber.Id);
            return false;
        }

        foreach (var fiber in _fibers.Values)
        {
            chain.Clear();
            if (Visit(fiber))
            {
                chain.Reverse();
                return string.Join(" -> ", chain);
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
