using System.Collections.Concurrent;

namespace Gocordis.Core;

/// <summary>
/// Canonical runtime event vocabulary. Events are immutable observations of one
/// Runtime state transition, not a logging system. The monotonic Sequence is
/// the ordering authority; Timestamp is informational.
///
/// Migrated from Go <c>runtime/events.go</c>. The kernel assigns the sequence;
/// retention is a bounded ring so a console can render a timeline without the
/// Runtime knowing about subscribers.
/// </summary>
public enum RuntimeEventType
{
    FiberCreated,
    ActivationLoading,
    ActivationActive,
    ActivationUnloading,
    ActivationEnded,
    DependencyCaptured,
    ProviderPublished,
    ProviderWithdrawn,
    EffectCommitted,
    EffectUndoing,
    EffectUndone,
    Failure,
}

public sealed record RuntimeEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Type,
    Guid FiberId,
    long ActivationId,
    object? Data);

/// <summary>{events, sequence} wire shape consumed by the console timeline.</summary>
public sealed record RuntimeEventsSnapshot(
    IReadOnlyList<RuntimeEvent> Events, long Sequence);

/// <summary>
/// Bounded, thread-safe event ring with a single monotonic sequence authority.
/// Sequence assignment and append are one atomic step so concurrent emitters
/// observe strictly increasing sequences.
/// </summary>
public sealed class RuntimeEventRing
{
    private readonly object _sync = new();
    private readonly Queue<RuntimeEvent> _events;
    private long _sequence;

    public RuntimeEventRing(int capacity = 256)
    {
        Capacity = Math.Max(1, capacity);
        _events = new Queue<RuntimeEvent>(Capacity);
    }

    public int Capacity { get; }

    /// <summary>Append one event and return its assigned sequence.</summary>
    public RuntimeEvent Append(string type, Guid fiberId, long activationId, object? data = null)
    {
        lock (_sync)
        {
            var sequence = ++_sequence;
            var item = new RuntimeEvent(
                sequence, DateTimeOffset.UtcNow, type, fiberId, activationId, data);
            _events.Enqueue(item);
            while (_events.Count > Capacity)
            {
                _events.Dequeue();
            }

            return item;
        }
    }

    /// <summary>Coherent {events, sequence} read for a query/stream response.</summary>
    public RuntimeEventsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new RuntimeEventsSnapshot(_events.ToArray(), _sequence);
        }
    }

    public long Sequence
    {
        get { lock (_sync) { return _sequence; } }
    }
}
