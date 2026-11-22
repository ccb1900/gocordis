using System.Runtime.CompilerServices;

namespace Gocordis.Extensions;

/// <summary>
/// The requested anchor predates the retained ring: the gap cannot be replayed.
/// Re-snapshot and re-anchor at <see cref="ObservationLog{T}.LatestSequence"/>.
/// Mirrors Go's events.ErrSequenceTooOld.
/// </summary>
public sealed class SequenceTooOldException : InvalidOperationException
{
    public SequenceTooOldException(long requested, long retainedFrom)
        : base($"events: requested sequence {requested} predates the retained ring (oldest {retainedFrom})")
    {
        Requested = requested;
        RetainedFrom = retainedFrom;
    }

    public long Requested { get; }

    public long RetainedFrom { get; }
}

/// <summary>
/// A subscriber fell behind its bounded delivery queue and was terminated.
/// Mirrors Go's events.ErrSubscriptionOverflow; the emitter never blocks.
/// </summary>
public sealed class SubscriptionOverflowException : InvalidOperationException
{
    public SubscriptionOverflowException()
        : base("events: subscription overflowed") { }
}

/// <summary>
/// Sequence-anchored observation log: retains a bounded ring of published
/// events, replays (from..latest] into a new subscription before live fan-out,
/// rejects stale anchors and terminates slow subscribers instead of blocking
/// the emitter. This is the .NET counterpart of Go's observe.Observer.
/// </summary>
public sealed class ObservationLog<T> : IDisposable
{
    private readonly int _ringCapacity;
    private readonly int _queueLimit;
    private readonly object _sync = new();
    private readonly Queue<(long Sequence, T Item)> _ring = new();
    private readonly List<ObservationSubscription<T>> _subscribers = [];
    private long _sequence;
    private bool _disposed;

    public ObservationLog(int ringCapacity = 1024, int queueLimit = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ringCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueLimit);
        _ringCapacity = ringCapacity;
        _queueLimit = queueLimit;
    }

    public long LatestSequence
    {
        get
        {
            lock (_sync)
            {
                return _sequence;
            }
        }
    }

    public long OldestSequence
    {
        get
        {
            lock (_sync)
            {
                return _ring.Count == 0 ? 0 : _ring.Peek().Sequence;
            }
        }
    }

    public long Publish(T item)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var sequence = ++_sequence;
            var ev = (sequence, item);

            _ring.Enqueue(ev);
            while (_ring.Count > _ringCapacity)
            {
                _ring.Dequeue();
            }

            foreach (var subscriber in _subscribers)
            {
                subscriber.Enqueue(ev);
            }
            return sequence;
        }
    }

    /// <summary>
    /// Replays retained events with Sequence >= from, then streams live events.
    /// from = 0 starts from the oldest retained event. A from older than the
    /// retained ring is rejected with <see cref="SequenceTooOldException"/>.
    /// </summary>
    public ObservationSubscription<T> Subscribe(long from)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ring.Count > 0 && from < _ring.Peek().Sequence)
            {
                throw new SequenceTooOldException(from, _ring.Peek().Sequence);
            }

            var subscription = new ObservationSubscription<T>(this, _queueLimit);
            foreach (var ev in _ring)
            {
                if (ev.Sequence >= from)
                {
                    subscription.Enqueue(ev, replay: true);
                }
            }
            _subscribers.Add(subscription);
            return subscription;
        }
    }

    internal void Remove(ObservationSubscription<T> subscriber)
    {
        lock (_sync)
        {
            _subscribers.Remove(subscriber);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// One live observation episode. Queued replay and live events drain in
/// Sequence order; once the bounded queue is exceeded the subscription records
/// <see cref="SubscriptionOverflowException"/> as its terminal cause.
/// </summary>
public sealed class ObservationSubscription<T> : IDisposable
{
    private readonly ObservationLog<T> _log;
    private readonly int _queueLimit;
    private readonly object _sync = new();
    private readonly Queue<(long Sequence, T Item)> _queue = new();
    private bool _closed;
    private Exception? _error;

    internal ObservationSubscription(ObservationLog<T> log, int queueLimit)
    {
        _log = log;
        _queueLimit = queueLimit;
    }

    /// <summary>Terminal cause: overflow error, or null for a healthy/closed stream.</summary>
    public Exception? Error
    {
        get
        {
            lock (_sync)
            {
                return _error;
            }
        }
    }

    internal void Enqueue((long Sequence, T Item) ev, bool replay = false)
    {
        lock (_sync)
        {
            if (_closed || _error is not null)
            {
                return;
            }
            // Replay is bounded by the ring capacity and must never overflow:
            // a reconnect gap is spooled in full before live fan-out begins.
            if (!replay && _queue.Count >= _queueLimit)
            {
                _error = new SubscriptionOverflowException();
                return;
            }
            _queue.Enqueue(ev);
        }
    }

    public bool TryRead(out (long Sequence, T Item) item)
    {
        lock (_sync)
        {
            if (_queue.Count > 0)
            {
                item = _queue.Dequeue();
                return true;
            }
        }
        item = default;
        return false;
    }

    public async IAsyncEnumerable<(long Sequence, T Item)> EventsAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (TryRead(out var item))
            {
                yield return item;
                continue;
            }

            var error = Error;
            if (error is not null)
            {
                throw error;
            }

            bool closed;
            lock (_sync)
            {
                closed = _closed;
            }
            if (closed)
            {
                yield break;
            }

            await Task.Delay(1, token);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _closed = true;
        }
        _log.Remove(this);
    }
}
