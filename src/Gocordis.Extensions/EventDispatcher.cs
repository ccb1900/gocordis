using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>
/// A waterfall chain node called next() more than once. The violation surfaces
/// in the dispatch result even when the handler ignores next()'s exception.
/// Mirrors Go's ErrWaterfallNextTwice (runtime sentinel, ADR-0004).
/// </summary>
public sealed class WaterfallNextTwiceException : InvalidOperationException
{
    public WaterfallNextTwiceException()
        : base("waterfall: next() called more than once") { }
}

/// <summary>
/// Parallel dispatch failure that preserves EVERY handler error in snapshot
/// registration order (Go's errors.Join normalization) while remaining an
/// InvalidOperationException so legacy fail-fast callers keep working.
/// </summary>
public sealed class ParallelDispatchException : InvalidOperationException
{
    public ParallelDispatchException(IReadOnlyList<Exception> errors)
        : base(string.Join(Environment.NewLine, errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<Exception> Errors { get; }
}

/// <summary>
/// JS-Cordis-compatible event dispatch modes built on Core event keys.
/// Handler registration is reversible (IDisposable) and dispatch uses a
/// snapshot of live entries, so a handler cannot mutate the set it iterates and
/// a handler retired by an earlier handler in the same batch is skipped.
/// </summary>
public sealed class EventDispatcher
{
    private readonly Dictionary<object, List<Entry<Func<object?, ValueTask>>>> _handlers =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<object, List<Entry<Func<object?, ValueTask<object?>>>>> _folds =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<object, List<Entry<Func<object?, ValueTask<(object? Value, bool Present)>>>>> _values =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<object, List<Entry<Func<object?, Func<ValueTask>, ValueTask>>>> _chains =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Plain observer handler. Participates in Emit/Serial/Parallel/Bail.</summary>
    public IDisposable On<T>(EventKey<T> key, Func<T, ValueTask> handler) =>
        Subscribe(_handlers, key, payload => handler((T)payload!));

    /// <summary>
    /// Value handler for JS-style bail(): present=false is the explicit
    /// "undefined" spelling; BailValue keeps looking for a present result.
    /// </summary>
    public IDisposable OnValue<T, R>(EventKey<T> key, Func<T, (R Value, bool Present)> handler) =>
        Subscribe(_values, key, payload =>
        {
            var (value, present) = handler((T)payload!);
            return ValueTask.FromResult<(object? Value, bool Present)>(((object?)value, present));
        });

    /// <summary>Legacy fold-style waterfall: current = await handler(current).</summary>
    public IDisposable OnWaterfall<T>(EventKey<T> key, Func<T, ValueTask<T>> handler) =>
        Subscribe(_folds, key, async payload => (object?)await handler((T)payload!));

    /// <summary>
    /// Chain-aware waterfall middleware: run the "before" work, call next()
    /// to continue the chain, then run the "after" work. Not calling next()
    /// short-circuits; calling it twice throws WaterfallNextTwiceException.
    /// </summary>
    public IDisposable OnWaterfall<T>(EventKey<T> key, Func<T, Func<ValueTask>, ValueTask> handler) =>
        Subscribe(_chains, key, (payload, next) => handler((T)payload!, next));

    public Task EmitAsync<T>(EventKey<T> key, T payload) => EmitSerialAsync(key, payload);

    public Task EmitAsync<T>(EventKey<T> key, T payload, CancellationToken token) =>
        EmitSerialAsync(key, payload, token);

    public Task<IReadOnlyList<Exception>> EmitSerialAsync<T>(EventKey<T> key, T payload) =>
        EmitSerialAsync(key, payload, default);

    public async Task<IReadOnlyList<Exception>> EmitSerialAsync<T>(
        EventKey<T> key, T payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        foreach (var entry in Snapshot(_handlers, key))
        {
            if (!entry.Active) continue;
            token.ThrowIfCancellationRequested();
            try
            {
                await entry.Handler(payload);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        return failures;
    }

    public Task EmitParallelAsync<T>(EventKey<T> key, T payload) => EmitParallelAsync(key, payload, default);

    public async Task EmitParallelAsync<T>(EventKey<T> key, T payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = Snapshot(_handlers, key);
        var errors = new Exception?[snapshot.Length];
        var running = new List<Task>();
        var started = 0;

        for (var index = 0; index < snapshot.Length; index++)
        {
            var entry = snapshot[index];
            if (!entry.Active) continue;
            if (token.IsCancellationRequested) break;

            var slot = index;
            running.Add(Task.Run(async () =>
            {
                try
                {
                    await entry.Handler(payload);
                }
                catch (Exception error)
                {
                    errors[slot] = error;
                }
            }));
            started++;
        }

        await Task.WhenAll(running);

        var ordered = errors.Where(error => error is not null).Select(error => error!).ToArray();
        if (ordered.Length > 0)
        {
            throw new ParallelDispatchException(ordered);
        }
        if (started < snapshot.Length)
        {
            token.ThrowIfCancellationRequested();
        }
    }

    public Task<bool> EmitBailAsync<T>(EventKey<T> key, T payload) => EmitBailAsync(key, payload, default);

    public async Task<bool> EmitBailAsync<T>(EventKey<T> key, T payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var entry in Snapshot(_handlers, key))
        {
            if (!entry.Active) continue;
            token.ThrowIfCancellationRequested();
            try
            {
                await entry.Handler(payload);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    public ValueTask<(R Value, bool Present)> EmitBailValueAsync<T, R>(EventKey<T> key, T payload) =>
        EmitBailValueAsync<T, R>(key, payload, default);

    public async ValueTask<(R Value, bool Present)> EmitBailValueAsync<T, R>(
        EventKey<T> key, T payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var entry in Snapshot(_values, key))
        {
            if (!entry.Active) continue;
            token.ThrowIfCancellationRequested();

            var (value, present) = await entry.Handler(payload);
            if (!present) continue;
            if (value is null) return (default!, true);
            if (value is R typed) return (typed, true);
            throw new InvalidOperationException(
                $"BailValue result type mismatch: got {value.GetType()}, want {typeof(R)}");
        }

        return (default!, false);
    }

    public ValueTask<T> EmitWaterfallAsync<T>(EventKey<T> key, T seed) => EmitWaterfallAsync(key, seed, default);

    public async ValueTask<T> EmitWaterfallAsync<T>(EventKey<T> key, T seed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        object? current = seed;
        foreach (var entry in Snapshot(_folds, key))
        {
            if (!entry.Active) continue;
            token.ThrowIfCancellationRequested();
            current = await entry.Handler(current);
        }

        return (T)current!;
    }

    public ValueTask EmitWaterfallChainAsync<T>(EventKey<T> key, T payload) =>
        EmitWaterfallChainAsync(key, payload, default);

    public ValueTask EmitWaterfallChainAsync<T>(EventKey<T> key, T payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return RunChainAsync(Snapshot(_chains, key), 0, payload, token);
    }

    /// <summary>
    /// Drives the snapshot as an ordered synchronous chain. All state lives on
    /// this call stack so a nested/reentrant dispatch captures its own snapshot.
    /// </summary>
    private static async ValueTask RunChainAsync(
        IReadOnlyList<Entry<Func<object?, Func<ValueTask>, ValueTask>>> snapshot,
        int index,
        object? payload,
        CancellationToken token)
    {
        while (index < snapshot.Count && !snapshot[index].Active)
        {
            index++;
        }
        if (index >= snapshot.Count)
        {
            return;
        }
        token.ThrowIfCancellationRequested();

        var entry = snapshot[index];
        var calls = 0;
        Func<ValueTask> next = () =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                return ValueTask.FromException(new WaterfallNextTwiceException());
            }
            return RunChainAsync(snapshot, index + 1, payload, token);
        };

        await entry.Handler(payload, next);

        // A second next() is a contract violation even if the handler swallowed
        // the faulted ValueTask: surface it at the dispatch boundary.
        if (Volatile.Read(ref calls) > 1)
        {
            throw new WaterfallNextTwiceException();
        }
    }

    private static IDisposable Subscribe<THandler>(
        Dictionary<object, List<Entry<THandler>>> table, object key, THandler handler)
    {
        var entry = new Entry<THandler>(handler);
        lock (table)
        {
            if (!table.TryGetValue(key, out var list))
            {
                list = [];
                table[key] = list;
            }
            list.Add(entry);
        }

        return new Subscription(() =>
        {
            entry.Deactivate();
            lock (table)
            {
                if (table.TryGetValue(key, out var list))
                {
                    list.Remove(entry);
                    if (list.Count == 0)
                    {
                        table.Remove(key);
                    }
                }
            }
        });
    }

    private static Entry<THandler>[] Snapshot<THandler>(
        Dictionary<object, List<Entry<THandler>>> table, object key)
    {
        lock (table)
        {
            return table.TryGetValue(key, out var list) ? list.ToArray() : [];
        }
    }

    /// <summary>One registration; Active stays observable inside a live snapshot.</summary>
    private sealed class Entry<THandler>
    {
        private int _active = 1;

        public Entry(THandler handler) => Handler = handler;

        public THandler Handler { get; }

        public bool Active => Volatile.Read(ref _active) == 1;

        public void Deactivate() => Interlocked.Exchange(ref _active, 0);
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
