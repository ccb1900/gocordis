namespace Gocordis.Extensions;

/// <summary>JS-style timers owned by a service; disposal cancels everything.</summary>
public sealed class TimerService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<CancellationTokenSource> _timers = [];
    private CancellationTokenSource? _debounce;
    private DateTimeOffset _lastThrottle = DateTimeOffset.MinValue;

    public async Task TimeoutAsync(TimeSpan delay, Func<ValueTask> action, CancellationToken token = default)
    {
        using var linked = Link(token, out var registration);
        try
        {
            await Task.Delay(delay, linked.Token);
            await action();
        }
        finally
        {
            lock (_sync)
            {
                _timers.Remove(linked);
            }

            registration.Dispose();
            linked.Dispose();
        }
    }

    public async Task IntervalAsync(TimeSpan period, Func<ValueTask> action, CancellationToken token = default)
    {
        using var linked = Link(token, out var registration);
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                await Task.Delay(period, linked.Token);
                await action();
            }
        }
        catch (OperationCanceledException)
        {
            // cooperative stop
        }
        finally
        {
            lock (_sync)
            {
                _timers.Remove(linked);
            }

            registration.Dispose();
            linked.Dispose();
        }
    }

    public async Task DebounceAsync(TimeSpan window, Func<ValueTask> action, CancellationToken token = default)
    {
        CancellationTokenSource source;
        lock (_sync)
        {
            _debounce?.Cancel();
            source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _debounce = source;
        }

        try
        {
            await Task.Delay(window, source.Token);
            await action();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer call
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_debounce, source))
                {
                    _debounce = null;
                }
            }

            source.Dispose();
        }
    }

    public async Task<bool> ThrottleAsync(TimeSpan window, Func<ValueTask> action)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastThrottle < window)
            {
                return false;
            }

            _lastThrottle = now;
        }

        await action();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource[] sources;
        lock (_sync)
        {
            sources = _timers.Concat([_debounce!]).Where(source => source is not null).ToArray();
            _timers.Clear();
            _debounce = null;
        }

        foreach (var source in sources)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already completed and disposed by its own using scope
            }

            source.Dispose();
        }

        await ValueTask.CompletedTask;
    }

    private CancellationTokenSource Link(CancellationToken token, out CancellationTokenRegistration registration)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_sync)
        {
            _timers.Add(source);
        }

        registration = default;
        return source;
    }
}

/// <summary>Once/interval scheduler with a deterministic pump (no wall-clock race in tests).</summary>
public sealed class Scheduler : IAsyncDisposable
{
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly HashSet<Guid> _running = [];
    private readonly HashSet<Guid> _cancelled = [];
    private bool _disposed;

    public IReadOnlyCollection<Job> Jobs
    {
        get
        {
            lock (_jobs)
            {
                return _jobs.Values.ToArray();
            }
        }
    }

    public Guid ScheduleOnce(DateTimeOffset at, Func<ValueTask> action)
    {
        var id = Guid.NewGuid();
        lock (_jobs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs[id] = new Job(id, at, null, action);
        }

        return id;
    }

    public Guid ScheduleInterval(TimeSpan period, Func<ValueTask> action)
    {
        var id = Guid.NewGuid();
        lock (_jobs)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs[id] = new Job(id, DateTimeOffset.UtcNow + period, period, action);
        }

        return id;
    }

    public bool Cancel(Guid id)
    {
        lock (_jobs)
        {
            // A running interval job must stay cancelled after its action
            // completes: the completion path consults _cancelled.
            _cancelled.Add(id);
            return _jobs.Remove(id);
        }
    }

    public async Task<Guid?> RunNextAsync(DateTimeOffset now)
    {
        Job? due;
        lock (_jobs)
        {
            due = _jobs.Values
                .Where(job => job.Next <= now && !_running.Contains(job.Id))
                .OrderBy(job => job.Next)
                .FirstOrDefault();
            if (due is not null)
            {
                _running.Add(due.Id);
            }
        }

        if (due is null)
        {
            return null;
        }

        try
        {
            await due.Action();
        }
        finally
        {
            lock (_jobs)
            {
                _running.Remove(due.Id);
            }
        }
        lock (_jobs)
        {
            if (_disposed || _cancelled.Contains(due.Id))
            {
                return due.Id; // cancelled / disposed while running: no resurrection
            }

            if (due.Period is { } period)
            {
                _jobs[due.Id] = due with { Next = now + period };
            }
            else
            {
                _jobs.Remove(due.Id);
            }
        }

        return due.Id;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_jobs)
        {
            _disposed = true;
            _jobs.Clear();
        }

        // No job may re-enter _jobs after disposal; wait for in-flight actions
        // to leave so a later RunNextAsync cannot observe a half-disposed state.
        while (true)
        {
            lock (_jobs)
            {
                if (_running.Count == 0)
                {
                    break;
                }
            }

            await Task.Delay(1);
        }
    }

    public sealed record Job(Guid Id, DateTimeOffset Next, TimeSpan? Period, Func<ValueTask> Action);
}
