using System.Collections.Concurrent;
using Gocordis.Core;

namespace Showcase.Contracts;

/// <summary>
/// The one vocabulary shared by every showcase plugin. Because the assembly is
/// loaded into the default ALC once, these static keys have a single identity
/// across independent .gocordis-plugin packages — the spatial (coeffect) side
/// of the composition actually works in the packaged form.
/// </summary>
public static class K
{
    public static readonly Key<IStore> Store = new("showcase.store");
    public static readonly Key<IMetrics> Metrics = new("showcase.metrics");
    public static readonly EventKey<StoreChanged> StoreChanged = new("showcase.store-changed");
}

public interface IStore
{
    void Put(string key, string value);
    string? Get(string key);
    IReadOnlyDictionary<string, string> Snapshot();
    long Revision { get; }
    long Writes { get; }
}

public sealed record StoreChanged(string Key, string Value, long Revision);

public interface IMetrics
{
    long Observed { get; }
    string LastEvent { get; }
    string Derived { get; }
    bool Subscribed { get; }
}

/// <summary>Thread-safe in-memory store — the provider's reversible resource.</summary>
public sealed class MemoryStore : IStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private long _revision;
    private long _writes;

    public void Put(string key, string value)
    {
        _values[key] = value;
        Interlocked.Increment(ref _revision);
        Interlocked.Increment(ref _writes);
    }

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_values, StringComparer.Ordinal);

    public long Revision => Interlocked.Read(ref _revision);
    public long Writes => Interlocked.Read(ref _writes);
}

/// <summary>Mutable metrics snapshot shared with the dashboard's query handler.</summary>
/// <summary>One row in the bounded event history used by the table/trend blocks.</summary>
public sealed record ShowcaseEvent(
    string Date, string SourceId, string Status, string Value, long Revision);

/// <summary>
/// A bounded, thread-safe history of recent events. This is business state
/// owned by the provider (not runtime state) and is exposed through a query
/// so declarative views can render table/trend/stats/calendar blocks.
/// </summary>
public sealed class EventHistory
{
    private readonly ConcurrentQueue<ShowcaseEvent> _events = new();
    private const int Capacity = 200;

    public void Add(ShowcaseEvent item)
    {
        _events.Enqueue(item);
        while (_events.Count > Capacity)
        {
            _events.TryDequeue(out _);
        }
    }

    public IReadOnlyList<ShowcaseEvent> Snapshot() => _events.ToArray();
}

/// <summary>Mutable plugin config touched by form/json commands.</summary>
public sealed class ShowcaseConfig
{
    private string _label = "default";
    private int _threshold = 5;

    public string Label => Volatile.Read(ref _label);
    public int Threshold => Volatile.Read(ref _threshold);

    public void Apply(string? label, int? threshold)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            Volatile.Write(ref _label, label);
        }

        if (threshold is > 0)
        {
            Volatile.Write(ref _threshold, threshold.Value);
        }
    }
}

public sealed class MetricsState : IMetrics
{
    private long _observed;
    private string _lastEvent = "(none)";
    private string _derived = "(empty)";
    private bool _subscribed;

    public long Observed => Interlocked.Read(ref _observed);
    public string LastEvent => Volatile.Read(ref _lastEvent);
    public string Derived => Volatile.Read(ref _derived);
    public bool Subscribed => Volatile.Read(ref _subscribed);

    public void Record(string key, string value, long revision)
    {
        Interlocked.Increment(ref _observed);
        Volatile.Write(ref _lastEvent, $"{key}={value}@{revision}");
        Volatile.Write(ref _derived, $"{value.ToUpperInvariant()}/{revision}");
    }

    public void SetSubscribed(bool value) => Volatile.Write(ref _subscribed, value);
}

/// <summary>
/// Out-of-band proof of effect install/undo ordering. Set GOCORDIS_E2E_LOG to
/// a file path; each plugin appends tagged lines so a disable/enable cycle can
/// be verified without relying on Console output from a managed plugin.
/// </summary>
public static class EffectLog
{
    public static void Write(string tag)
    {
        var path = Environment.GetEnvironmentVariable("GOCORDIS_E2E_LOG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, tag + "\n");
        }
        catch (IOException)
        {
            // Observability is best-effort; never fail a fiber because of a log.
        }
    }
}
