using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Gocordis.Extensions;

/// <summary>
/// The requested anchor predates the retained ring by more than one event: the
/// gap cannot be replayed. Re-snapshot and re-anchor at
/// <see cref="ObservationLog{T}.LatestSequence"/>.
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
    /// Starts a subscription anchored at <paramref name="from"/>: the LAST
    /// sequence the consumer has already consumed (the value paired with a
    /// runtime snapshot's EventSequence). Retained events with Sequence &gt;
    /// from are replayed in order, then live events stream; the anchor itself
    /// is never replayed. That makes Subscribe(0) the "oldest retained"
    /// sentinel, Subscribe(1) after consuming event 1 a replay of 2,3, and
    /// Subscribe(LatestSequence) a live-only stream.
    ///
    /// An anchor older than the event immediately preceding the retained ring
    /// (from &lt; OldestSequence - 1) is rejected with
    /// <see cref="SequenceTooOldException"/>: the gap cannot be replayed, so
    /// the consumer must re-snapshot and re-anchor at LatestSequence.
    /// </summary>
    public ObservationSubscription<T> Subscribe(long from)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(from);
            var oldest = _ring.Count == 0 ? 0 : _ring.Peek().Sequence;
            // from is the last CONSUMED sequence, not the first one to receive.
            // A one-behind anchor is valid (replay starts at oldest); anything
            // older left an unreplayable gap in the dropped ring prefix.
            if (from != 0 && oldest != 0 && from < oldest - 1)
            {
                throw new SequenceTooOldException(from, oldest);
            }

            var subscription = new ObservationSubscription<T>(this, _queueLimit, _ringCapacity);
            foreach (var ev in _ring)
            {
                if (ev.Sequence > from)
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
        ObservationSubscription<T>[] subscribers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscribers = [.. _subscribers];
            _subscribers.Clear();
        }

        // Close subscribers outside the log lock: a subscription may be
        // disposing concurrently and will remove itself from this log.
        foreach (var subscriber in subscribers)
        {
            subscriber.CloseFromLog();
        }
    }
}

/// <summary>
/// One live observation episode. Replay and live events are held in a bounded
/// <see cref="Channel{T}"/>; TryRead drains them in Sequence order, an
/// asynchronous WaitToReadAsync replaces polling, and a slow subscriber that
/// exceeds the live queue limit records
/// <see cref="SubscriptionOverflowException"/> as its terminal cause.
/// </summary>
public sealed class ObservationSubscription<T> : IDisposable
{
    private readonly ObservationLog<T> _log;
    private readonly int _queueLimit;
    private readonly object _sync = new();
    // Sized for the whole ring so replay can always be spooled without
    // overflowing; live fan-out is bounded separately by _queueLimit.
    private readonly Channel<(long Sequence, T Item)> _events;
    private bool _closed;
    private Exception? _error;

    internal ObservationSubscription(ObservationLog<T> log, int queueLimit, int replayCapacity)
    {
        _log = log;
        _queueLimit = queueLimit;
        _events = Channel.CreateBounded<(long Sequence, T Item)>(
            new BoundedChannelOptions(Math.Max(queueLimit, replayCapacity))
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
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

    internal void CloseFromLog()
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _events.Writer.TryComplete();
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
            // Replay is bounded by the ring capacity (the channel is sized for
            // it) and must never overflow: a reconnect gap is spooled in full
            // before live fan-out begins.
            if (!replay && _events.Reader.Count >= _queueLimit)
            {
                TerminateWithOverflow();
                return;
            }

            // TryWrite never blocks the emitter; a full bounded channel means
            // the subscriber fell behind and must be terminated.
            if (!_events.Writer.TryWrite(ev))
            {
                TerminateWithOverflow();
            }
        }
    }

    private void TerminateWithOverflow()
    {
        _error = new SubscriptionOverflowException();
        // Complete successfully (the terminal cause lives in _error): waiters
        // wake up, buffered events drain in order, then EventsAsync throws the
        // overflow error from the state check. No polling, no emitter blocking.
        _events.Writer.TryComplete();
    }

    public bool TryRead(out (long Sequence, T Item) item)
    {
        lock (_sync)
        {
            return _events.Reader.TryRead(out item);
        }
    }

    private (Exception? Error, bool Closed) State
    {
        get
        {
            lock (_sync)
            {
                return (_error, _closed);
            }
        }
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

            var (error, closed) = State;
            if (error is not null)
            {
                throw error;
            }

            if (closed)
            {
                yield break;
            }

            // A bounded channel supplies the wake-up: Publish, overflow and
            // close all write to or complete it, so no polling delay is needed.
            try
            {
                await _events.Reader.WaitToReadAsync(token);
            }
            catch (ChannelClosedException)
            {
                // Completed by overflow/close; the loop re-checks _error and
                // _closed above and either throws or completes the stream.
            }
        }
    }

    public void Dispose()
    {
        CloseFromLog();
        _log.Remove(this);
    }
}
