using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>
/// JS-Cordis-compatible event dispatch modes built on Core event keys.
/// Handler registration is reversible (IDisposable) and dispatch uses a
/// snapshot, so a handler cannot mutate the set it is iterating.
/// </summary>
public sealed class EventDispatcher
{
    private readonly Dictionary<object, List<Func<object?, ValueTask>>> _handlers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<Func<object?, ValueTask<object?>>>> _waterfalls =
        new(ReferenceEqualityComparer.Instance);

    public IDisposable On<T>(EventKey<T> key, Func<T, ValueTask> handler)
    {
        return Subscribe(_handlers, key, payload => handler((T)payload!));
    }

    public IDisposable OnWaterfall<T>(EventKey<T> key, Func<T, ValueTask<T>> handler)
    {
        return Subscribe(_waterfalls, key, async payload => (object?)await handler((T)payload!));
    }

    public Task EmitAsync<T>(EventKey<T> key, T payload) => EmitSerialAsync(key, payload);

    public async Task<IReadOnlyList<Exception>> EmitSerialAsync<T>(EventKey<T> key, T payload)
    {
        var failures = new List<Exception>();
        foreach (var handler in Snapshot(_handlers, key))
        {
            try
            {
                await handler(payload);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return failures;
    }

    public async Task EmitParallelAsync<T>(EventKey<T> key, T payload)
    {
        var tasks = Snapshot(_handlers, key).Select(handler => Task.Run(async () => await handler(payload)));
        await Task.WhenAll(tasks);
    }

    public async Task<bool> EmitBailAsync<T>(EventKey<T> key, T payload)
    {
        foreach (var handler in Snapshot(_handlers, key))
        {
            try
            {
                await handler(payload);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    public async ValueTask<T> EmitWaterfallAsync<T>(EventKey<T> key, T seed)
    {
        object current = seed!;
        foreach (var handler in Snapshot(_waterfalls, key))
        {
            current = await handler(current);
        }

        return (T)current;
    }

    private static IDisposable Subscribe<TKey, THandler>(
        Dictionary<object, List<THandler>> table, TKey key, THandler handler)
    {
        if (!table.TryGetValue(key!, out var list))
        {
            list = [];
            table[key!] = list;
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    private static IReadOnlyList<T> Snapshot<T>(Dictionary<object, List<T>> table, object key) =>
        table.TryGetValue(key, out var list) ? list.ToArray() : [];

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
