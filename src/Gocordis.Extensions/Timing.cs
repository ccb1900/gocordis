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
            registration.Dispose();
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
            registration.Dispose();
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
    }

    public async Task<bool> ThrottleAsync(TimeSpan window, Func<ValueTask> action)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastThrottle < window)
        {
            return false;
        }

        _lastThrottle = now;
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

    public IReadOnlyCollection<Job> Jobs => _jobs.Values.ToArray();

    public Guid ScheduleOnce(DateTimeOffset at, Func<ValueTask> action)
    {
        var id = Guid.NewGuid();
        _jobs[id] = new Job(id, at, null, action);
        return id;
    }

    public Guid ScheduleInterval(TimeSpan period, Func<ValueTask> action)
    {
        var id = Guid.NewGuid();
        _jobs[id] = new Job(id, DateTimeOffset.UtcNow + period, period, action);
        return id;
    }

    public bool Cancel(Guid id) => _jobs.Remove(id);

    public async Task<Guid?> RunNextAsync(DateTimeOffset now)
    {
        var due = _jobs.Values.Where(job => job.Next <= now).OrderBy(job => job.Next).FirstOrDefault();
        if (due is null)
        {
            return null;
        }

        await due.Action();
        if (due.Period is { } period)
        {
            _jobs[due.Id] = due with { Next = now + period };
        }
        else
        {
            _jobs.Remove(due.Id);
        }

        return due.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _jobs.Clear();
        await ValueTask.CompletedTask;
    }

    public sealed record Job(Guid Id, DateTimeOffset Next, TimeSpan? Period, Func<ValueTask> Action);
}
