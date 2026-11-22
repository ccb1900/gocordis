namespace Gocordis.Core;

/// <summary>
/// Per-activation context. All interaction with other components goes through it:
/// capability resolution, provision, reversible effects, events and child ownership.
/// </summary>
public sealed class Context
{
    private readonly Fiber _fiber;
    private readonly Runtime _runtime;
    private readonly int _generation;

    internal Context(Fiber fiber, Runtime runtime, int generation)
    {
        _fiber = fiber;
        _runtime = runtime;
        _generation = generation;
    }

    public CancellationToken CancellationToken =>
        _fiber.ActivationCancellation?.Token ?? CancellationToken.None;

    /// <summary>The fiber's default realm; per-key overrides are applied by <see cref="Isolate{T}"/>.</summary>
    public Realm Realm => _fiber.Realm;

    /// <summary>True when the activation target changed and installation must stop.</summary>
    public bool IsDiverted =>
        _fiber.Generation != _generation ||
        _fiber.Intent == LifecycleIntent.Retired ||
        CancellationToken.IsCancellationRequested;

    public void ThrowIfDiverted()
    {
        if (IsDiverted)
        {
            throw new DivertedException();
        }
    }

    /// <summary>Resolve a declared dependency. Missing providers are never silently substituted.</summary>
    public T Require<T>(Key<T> key)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject());
        return _runtime.RequireValue<T>(key, _fiber);
    }

    /// <summary>Resolve a declared versioned dependency by peer range (paper §6.6).</summary>
    public T Require<T>(VersionedKey<T> key)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject());
        return _runtime.ResolveVersioned(key, _fiber);
    }

    /// <summary>Publish a versioned capability; the inverse removes the binding.</summary>
    public void Provide<T>(VersionedKey<T> key, T value)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Provide());
        _runtime.RegisterVersioned(key, value, _fiber);
        _fiber.AddInverse(() =>
        {
            _runtime.RemoveVersioned(_fiber, key);
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Resolve a declared event key as a capability (e.g. a channel).</summary>
    public T Require<T>(EventKey<T> key)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject());
        return _runtime.RequireValue<T>(key, _fiber);
    }

    /// <summary>Publish a declared capability. Removal is a reversible effect.</summary>
    public void Provide<T>(Key<T> key, T value) => ProvideCore(key, value);

    public void Provide<T>(EventKey<T> key, T value) => ProvideCore(key, value);

    private void ProvideCore(CapabilityKey key, object? value)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Provide());
        if (_fiber.Provided.ContainsKey(key))
        {
            throw new DuplicateProviderException($"duplicate provision of '{key.Name}' by '{_fiber.Component.Name}'");
        }

        _fiber.Provided[key] = value;
        _fiber.AddInverse(() =>
        {
            _fiber.Provided.Remove(key);
            return ValueTask.CompletedTask;
        });
        _runtime.MarkBindingsChanged();
    }

    /// <summary>
    /// Reassign a declared key to a realm (paper Def 25). The reassignment is
    /// recorded as a reversible effect; already-isolated keys can be reassigned.
    /// </summary>
    public void Isolate<T>(Key<T> key, Realm realm)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject().Concat(_fiber.Component.Provide()).ToArray());
        ArgumentNullException.ThrowIfNull(realm);
        // Realm reassignment is a persistent interface change (paper C34), not
        // an effect that is unwound on deactivation; otherwise a fiber that
        // isolates its own dependency would oscillate Pending -> Active forever.
        _fiber.RealmOverrides[key] = realm;
        _runtime.MarkBindingsChanged();
    }

    /// <summary>Component's declared metadata contribution (left side of the monoid).</summary>
    public void DeclareMeta<T, M>(MetaKey<T, M> key, M value)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject());
        var state = GetOrCreateMeta(key);
        var hadPrevious = state.Declared is not null;
        var previous = state.Declared;
        state.Declared = value;
        _fiber.AddInverse(() =>
        {
            if (hadPrevious)
            {
                state.Declared = previous;
            }

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Context override, always applied last: σ(k)(μ ⊕ ι(k)).</summary>
    public void OverrideMeta<T, M>(MetaKey<T, M> key, M value)
    {
        EnsureGeneration();
        var state = GetOrCreateMeta(key);
        state.Override = value;
        _fiber.AddInverse(() =>
        {
            state.Override = null;
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>Read the intercepted value without changing resolution or reloading.</summary>
    public M Meta<T, M>(MetaKey<T, M> key)
    {
        EnsureGeneration();
        var declared = _fiber.Metadata.TryGetValue(key, out var state) && state.Declared is M d
            ? d
            : key.Identity;
        var overridden = state?.Override is M o ? o : key.Identity;
        return key.Combine(declared, overridden);
    }

    /// <summary>
    /// A context belongs to one activation generation. Stale contexts must not
    /// mutate the fiber after a divert or a new activation has begun.
    /// </summary>
    private void EnsureGeneration()
    {
        if (_fiber.Generation != _generation)
        {
            throw new DivertedException();
        }
    }

    private Fiber.MetaState GetOrCreateMeta(ICapabilityKey key)
    {
        if (!_fiber.Metadata.TryGetValue(key, out var state))
        {
            state = new Fiber.MetaState();
            _fiber.Metadata[key] = state;
        }

        return state;
    }

    /// <summary>Install a reversible effect. The inverse is recorded for LIFO unwind.</summary>
    public async ValueTask EffectAsync(Func<ValueTask> install, Func<ValueTask> inverse)
    {
        EnsureGeneration();
        ThrowIfDiverted();
        try
        {
            await install();
        }
        catch
        {
            // The caller supplied the inverse up front: install may have produced
            // partial side effects before throwing, so compensate best-effort.
            try
            {
                await inverse();
            }
            catch
            {
                // A failing compensation must not mask the install failure.
            }

            throw;
        }

        if (IsDiverted)
        {
            await inverse();
            throw new DivertedException();
        }

        _fiber.AddInverse(inverse);
    }

    /// <summary>Register a reversible event handler owned by this fiber.</summary>
    public ValueTask OnAsync<T>(EventKey<T> key, Func<T, ValueTask> handler)
    {
        EnsureGeneration();
        RequireDeclared(key, _fiber.Component.Inject());
        return _runtime.RegisterHandler(key, handler, _fiber);
    }

    /// <summary>Emit to a snapshot of current handlers; handlers cannot mutate the dispatch set mid-flight.</summary>
    public ValueTask EmitAsync<T>(EventKey<T> key, T payload)
    {
        EnsureGeneration();
        return _runtime.EmitAsync(key, payload, _fiber);
    }

    /// <summary>Create an owned child fiber; parent teardown cascades to it.</summary>
    public Task<Fiber> LoadChildAsync(Component child)
    {
        EnsureGeneration();
        return _runtime.LoadAsync(child, _fiber);
    }

    private static void RequireDeclared(ICapabilityKey key, IReadOnlyList<ICapabilityKey> declarations)
    {
        foreach (var declared in declarations)
        {
            if (ReferenceEquals(declared, key))
            {
                return;
            }
        }

        var side = declarations == Array.Empty<ICapabilityKey>() ? "none" : "declared";
        throw new UndeclaredCapabilityException(
            $"key '{key.Name}' is not declared on this component ({side})");
    }
}
