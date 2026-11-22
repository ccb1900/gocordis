namespace Gocordis.Core;

/// <summary>
/// Per-activation context. Install effects only while Installing; read declared
/// coeffects while Installing/Committed/Unwinding (paper §4.4: Cleanup can
/// still read its dependency); dead after Unwound.
/// </summary>
public sealed class Context
{
    private readonly Activation _activation;

    internal Context(Activation activation) => _activation = activation;

    private Fiber _fiber => _activation.Fiber;

    public CancellationToken CancellationToken => _activation.Cancellation.Token;

    public Realm Realm => _fiber.Realm;

    public bool IsDiverted =>
        _activation.Phase is ActivationPhase.Unwinding or ActivationPhase.Unwound ||
        _fiber.Intent == LifecycleIntent.Retired ||
        CancellationToken.IsCancellationRequested;

    public void ThrowIfDiverted()
    {
        if (IsDiverted)
        {
            throw new DivertedException();
        }
    }

    private void EnsureReadable()
    {
        if (_activation.Phase == ActivationPhase.Unwound)
        {
            throw new DivertedException();
        }
    }

    private void EnsureWritable()
    {
        if (_activation.Phase != ActivationPhase.Installing ||
            _fiber.Intent == LifecycleIntent.Retired ||
            CancellationToken.IsCancellationRequested)
        {
            throw new DivertedException();
        }
    }

    public T Require<T>(Key<T> key)
    {
        EnsureReadable();
        RequireDeclared(key, _fiber.Component.Inject());
        return _activation.Runtime.RequireValue<T>(key, _activation);
    }

    public T Require<T>(VersionedKey<T> key)
    {
        EnsureReadable();
        RequireDeclared(key, _fiber.Component.Inject());
        return _activation.Runtime.ResolveVersioned(key, _activation);
    }

    public T Require<T>(EventKey<T> key)
    {
        EnsureReadable();
        RequireDeclared(key, _fiber.Component.Inject());
        return _activation.Runtime.RequireValue<T>(key, _activation);
    }

    public M Meta<T, M>(MetaKey<T, M> key)
    {
        EnsureReadable();
        var declared = _fiber.Metadata.TryGetValue(key, out var state) && state.Declared is M d
            ? d : key.Identity;
        var overridden = state?.Override is M o ? o : key.Identity;
        return key.Combine(declared, overridden);
    }

    public void Provide<T>(VersionedKey<T> key, T value)
    {
        EnsureWritable();
        RequireDeclared(key, _fiber.Component.Provide());
        _activation.Runtime.RegisterVersioned(key, value, _activation);
        AddInverse(() =>
        {
            _activation.Runtime.RemoveVersioned(_activation, key);
            return ValueTask.CompletedTask;
        });
    }

    public void Provide<T>(Key<T> key, T value) => ProvideCore(key, value);

    public void Provide<T>(EventKey<T> key, T value) => ProvideCore(key, value);

    private void ProvideCore(CapabilityKey key, object? value)
    {
        EnsureWritable();
        RequireDeclared(key, _fiber.Component.Provide());
        _activation.Runtime.Mutate(() =>
        {
            if (_fiber.Provided.ContainsKey(key))
            {
                throw new DuplicateProviderException($"duplicate provision of '{key.Name}'");
            }

            _fiber.Provided[key] = value;
            _activation.Effects.Add(new EffectSlot(() =>
            {
                _fiber.Provided.Remove(key);
                return ValueTask.CompletedTask;
            }) { State = EffectState.Committed });
            _activation.Runtime.MarkBindingsChanged();
        });
    }

    public void Isolate<T>(Key<T> key, Realm realm)
    {
        EnsureWritable();
        RequireDeclared(key, [.. _fiber.Component.Inject(), .. _fiber.Component.Provide()]);
        ArgumentNullException.ThrowIfNull(realm);
        _activation.Runtime.Mutate(() =>
        {
            _fiber.RealmOverrides[key] = realm;
            _activation.Runtime.MarkBindingsChanged();
        });
    }

    public void DeclareMeta<T, M>(MetaKey<T, M> key, M value)
    {
        EnsureWritable();
        RequireDeclared(key, _fiber.Component.Inject());
        _activation.Runtime.Mutate(() =>
        {
            var state = GetOrCreateMeta(key);
            var hadPrevious = state.Declared is not null;
            var previous = state.Declared;
            state.Declared = value;
            _activation.Effects.Add(new EffectSlot(() =>
            {
                if (hadPrevious)
                {
                    state.Declared = previous;
                }

                return ValueTask.CompletedTask;
            }) { State = EffectState.Committed });
        });
    }

    public void OverrideMeta<T, M>(MetaKey<T, M> key, M value)
    {
        EnsureWritable();
        _activation.Runtime.Mutate(() =>
        {
            var state = GetOrCreateMeta(key);
            state.Override = value;
            _activation.Effects.Add(new EffectSlot(() =>
            {
                state.Override = null;
                return ValueTask.CompletedTask;
            }) { State = EffectState.Committed });
        });
    }

    public async ValueTask EffectAsync(Func<ValueTask> install, Func<ValueTask> inverse)
    {
        EnsureWritable();
        var slot = new EffectSlot(inverse);
        _activation.Runtime.Mutate(() =>
        {
            _activation.Effects.Add(slot);
            slot.State = EffectState.Installing;
        });
        try
        {
            await install();
        }
        catch
        {
            _activation.Runtime.Mutate(() => slot.State = EffectState.Undone);
            try
            {
                await inverse();
            }
            catch
            {
                // never mask install failure
            }

            throw;
        }

        if (IsDiverted)
        {
            _activation.Runtime.Mutate(() => slot.State = EffectState.Undoing);
            await inverse();
            _activation.Runtime.Mutate(() => slot.State = EffectState.Undone);
            throw new DivertedException();
        }

        _activation.Runtime.Mutate(() => slot.State = EffectState.Committed);
    }

    public ValueTask OnAsync<T>(EventKey<T> key, Func<T, ValueTask> handler)
    {
        EnsureWritable();
        RequireDeclared(key, _fiber.Component.Inject());
        return _activation.Runtime.RegisterHandler(key, handler, _activation);
    }

    public ValueTask EmitAsync<T>(EventKey<T> key, T payload)
    {
        EnsureReadable();
        return _activation.Runtime.EmitAsync(key, payload, _activation);
    }

    public Task<Fiber> LoadChildAsync(Component child)
    {
        EnsureWritable();
        return _activation.Runtime.LoadChildAsync(child, _fiber);
    }

    internal void AddInverse(Func<ValueTask> inverse) =>
        _activation.Runtime.Mutate(() =>
            _activation.Effects.Add(new EffectSlot(inverse) { State = EffectState.Committed }));

    private Fiber.MetaState GetOrCreateMeta(ICapabilityKey key)
    {
        if (!_fiber.Metadata.TryGetValue(key, out var state))
        {
            state = new Fiber.MetaState();
            _fiber.Metadata[key] = state;
        }

        return state;
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

        throw new UndeclaredCapabilityException($"key '{key.Name}' is not declared");
    }
}
