namespace Gocordis.Runtime.Managed;

/// <summary>
/// Managed counterpart of the Go Commit/Abandon/Release protocol: a stale
/// activation can never release resources belonging to a newer generation.
/// </summary>
public sealed class GenerationTracker
{
    private readonly object _sync = new();
    private int _current;

    public int Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public GenerationHandle Begin()
    {
        lock (_sync)
        {
            _current++;
            return new GenerationHandle(this, _current);
        }
    }

    internal bool TryRelease(int generation)
    {
        lock (_sync)
        {
            if (generation != _current)
            {
                return false;
            }

            _current++;
            return true;
        }
    }
}

public sealed class GenerationHandle : IDisposable
{
    private readonly GenerationTracker _tracker;
    private bool _settled;

    internal GenerationHandle(GenerationTracker tracker, int generation)
    {
        _tracker = tracker;
        Generation = generation;
    }

    public int Generation { get; }

    public bool Release()
    {
        if (_settled)
        {
            return false;
        }

        _settled = true;
        return _tracker.TryRelease(Generation);
    }

    public void Abandon() => _settled = true;

    public void Dispose() => Abandon();
}
