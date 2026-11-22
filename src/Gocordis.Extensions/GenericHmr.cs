namespace Gocordis.Extensions;

/// <summary>
/// Backend-neutral HMR identity. A target is neither a Fiber nor a module: the
/// host decides what ComponentId means for its loader.
/// </summary>
public sealed record HmrTarget(string Id, string ComponentId, string? Type = null);

/// <summary>Immutable snapshot of one target's current implementation.</summary>
public sealed record HmrBinding(
    string TargetId,
    string ComponentId,
    object Value,
    long Generation,
    int UsageCount);

/// <summary>Verdict of the injected pre-replacement validator.</summary>
public sealed record HmrValidation(bool Valid, string? Error = null)
{
    public static HmrValidation Accept => new(true, null);

    public static HmrValidation Reject(string error) => new(false, error);
}

/// <summary>Outcome of one replacement attempt.</summary>
public sealed record ReplaceResult(bool Succeeded, long Generation, string? Error = null);

public delegate Task<HmrValidation> HmrValidateAsync(HmrTarget target, CancellationToken token);

public delegate ValueTask<object> HmrActivateAsync(HmrTarget target, CancellationToken token);

public delegate ValueTask HmrDeactivateAsync(object value, CancellationToken token);

/// <summary>
/// Usage of one specific generation. Release is idempotent and fenced by the
/// generation captured at Acquire, so retiring an old generation cannot
/// disturb the binding that replaced it.
/// </summary>
public sealed class UsageHandle : IDisposable
{
    private readonly GenericHmr _owner;
    private int _released;

    internal UsageHandle(GenericHmr owner, string targetId, long generation)
    {
        _owner = owner;
        TargetId = targetId;
        Generation = generation;
    }

    public string TargetId { get; }

    public long Generation { get; }

    public bool Release() =>
        Interlocked.Exchange(ref _released, 1) == 0 && _owner.Release(this);

    public void Dispose() => Release();
}

/// <summary>
/// Minimal target/binding/usage protocol for hot replacement. All state is
/// guarded by one lock; validation, activation and retirement (user code) run
/// outside it. A failed validation or activation leaves the old binding intact.
/// WasmHotSwapper is deliberately untouched: this is the generic protocol only.
/// </summary>
public sealed class GenericHmr : IAsyncDisposable
{
    private sealed record State(HmrTarget Target, object Value, long Generation, int UsageCount);

    private readonly object _sync = new();
    private readonly HmrValidateAsync? _validate;
    private readonly HmrActivateAsync? _activate;
    private readonly HmrDeactivateAsync? _deactivate;
    private readonly Dictionary<string, State> _bindings = new(StringComparer.Ordinal);
    private long _nextGeneration;
    private bool _disposed;

    public GenericHmr(
        HmrValidateAsync? validate = null,
        HmrActivateAsync? activate = null,
        HmrDeactivateAsync? deactivate = null)
    {
        _validate = validate;
        _activate = activate;
        _deactivate = deactivate;
    }

    public void RegisterTarget(HmrTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            throw new ArgumentException("HMR target id is required", nameof(target));
        }

        if (string.IsNullOrWhiteSpace(target.ComponentId))
        {
            throw new ArgumentException("HMR target component id is required", nameof(target));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_bindings.ContainsKey(target.Id))
            {
                throw new InvalidOperationException($"HMR target '{target.Id}' already exists");
            }

            _bindings[target.Id] = new State(target, target, ++_nextGeneration, 0);
        }
    }

    /// <summary>Binds a value to an existing target, advancing its generation.</summary>
    public void Bind(string targetId, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            var state = Require(targetId);
            _bindings[targetId] = state with
            {
                Value = value,
                Generation = ++_nextGeneration,
                UsageCount = 0,
            };
        }
    }

    public HmrBinding? CurrentBinding(string targetId)
    {
        lock (_sync)
        {
            return _bindings.TryGetValue(targetId, out var state)
                ? new HmrBinding(state.Target.Id, state.Target.ComponentId, state.Value,
                    state.Generation, state.UsageCount)
                : null;
        }
    }

    public IReadOnlyList<HmrTarget> Targets()
    {
        lock (_sync)
        {
            return [.. _bindings.Values.Select(state => state.Target)];
        }
    }

    /// <summary>Records one usage of the generation currently bound.</summary>
    public UsageHandle Acquire(string targetId)
    {
        lock (_sync)
        {
            var state = Require(targetId);
            _bindings[targetId] = state with { UsageCount = state.UsageCount + 1 };
            return new UsageHandle(this, targetId, state.Generation);
        }
    }

    internal bool Release(UsageHandle handle)
    {
        lock (_sync)
        {
            if (_disposed ||
                !_bindings.TryGetValue(handle.TargetId, out var state) ||
                state.Generation != handle.Generation ||
                state.UsageCount <= 0)
            {
                return false;
            }

            _bindings[handle.TargetId] = state with { UsageCount = state.UsageCount - 1 };
            return true;
        }
    }

    /// <summary>
    /// Validates the candidate first, then activates it, then commits. If any
    /// step fails or another writer changed the binding, the old target stays.
    /// </summary>
    public async Task<ReplaceResult> ReplaceAsync(
        string targetId,
        HmrTarget candidate,
        HmrValidateAsync? validate = null,
        HmrActivateAsync? activate = null,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(candidate);

        State before;
        lock (_sync)
        {
            before = Require(targetId);
        }

        var validator = validate ?? _validate;
        if (validator is not null)
        {
            var verdict = await validator(candidate, token).ConfigureAwait(false);
            if (!verdict.Valid)
            {
                return new ReplaceResult(false, before.Generation,
                    verdict.Error ?? "replacement rejected");
            }
        }

        object replacement;
        var activator = activate ?? _activate;
        try
        {
            replacement = activator is null
                ? candidate
                : await activator(candidate, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new ReplaceResult(false, before.Generation, error.Message);
        }

        long generation;
        bool discard;
        lock (_sync)
        {
            if (_disposed || !_bindings.TryGetValue(targetId, out var current))
            {
                discard = true;
                generation = before.Generation;
            }
            else if (current.Generation != before.Generation ||
                     !ReferenceEquals(current.Target, before.Target))
            {
                discard = true;
                generation = before.Generation;
            }
            else
            {
                discard = false;
                generation = ++_nextGeneration;
                _bindings[targetId] = current with
                {
                    Target = candidate,
                    Value = replacement,
                    Generation = generation,
                    UsageCount = 0,
                };
            }
        }

        if (discard)
        {
            await RetireAsync(replacement, token).ConfigureAwait(false);
            return new ReplaceResult(false, generation, "target changed during replacement");
        }

        await RetireAsync(before.Value, token).ConfigureAwait(false);
        return new ReplaceResult(true, generation, null);
    }

    public async ValueTask DisposeAsync()
    {
        State[] retired;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            retired = [.. _bindings.Values];
            _bindings.Clear();
        }

        foreach (var state in retired)
        {
            await RetireAsync(state.Value, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private State Require(string targetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_bindings.TryGetValue(targetId, out var state))
        {
            throw new KeyNotFoundException($"HMR target '{targetId}' is not registered");
        }

        return state;
    }

    private async ValueTask RetireAsync(object value, CancellationToken token)
    {
        if (_deactivate is null)
        {
            return;
        }

        try
        {
            await _deactivate(value, token).ConfigureAwait(false);
        }
        catch
        {
            // Retirement failures must not undo a committed replacement or
            // block disposal.
        }
    }
}
