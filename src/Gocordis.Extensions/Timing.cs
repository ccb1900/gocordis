using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>JS-style timers owned by a service; disposal cancels everything.</summary>
public sealed class TimerService : IAsyncDisposable, ITimerService
{
    private readonly object _sync = new();
    private readonly List<CancellationTokenSource> _timers = [];
    private CancellationTokenSource? _debounce;
    private DateTimeOffset _lastThrottle = DateTimeOffset.MinValue;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<TimerScope, byte> _scopes = new();

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

    // ---- owner-bound (effect-owned) timers ---------------------------------

    /// <summary>
    /// One-shot timer bound to <paramref name="owner"/>: unwinding that
    /// activation cancels the entry and joins callbacks admitted before the
    /// unwind. <see cref="TimerHandle.Stop"/> is deliberately non-waiting so a
    /// callback may stop its own entry without deadlocking.
    /// </summary>
    public TimerHandle TimeoutOwned(Context owner, TimeSpan delay, Func<ValueTask> action)
    {
        var scope = NewScope();
        var runner = RunTimeoutAsync(scope, delay, action);
        scope.Track(runner);
        Bind(owner, scope);
        return new TimerHandle(scope);
    }

    /// <summary>Repeating timer bound to the owner's activation.</summary>
    public TimerHandle IntervalOwned(Context owner, TimeSpan period, Func<ValueTask> action)
    {
        var scope = NewScope();
        var runner = RunIntervalAsync(scope, period, action);
        scope.Track(runner);
        Bind(owner, scope);
        return new TimerHandle(scope);
    }

    /// <summary>
    /// Debounced trigger bound to the owner's activation. A newer trigger
    /// supersedes the pending fire; the last call in a burst wins.
    /// </summary>
    public Func<Task> DebounceOwned(Context owner, TimeSpan window, Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = NewScope();
        var sync = new object();
        CancellationTokenSource? pending = null;

        async Task RunPendingAsync(CancellationTokenSource candidate)
        {
            using (candidate)
            {
                try
                {
                    await Task.Delay(Positive(window), candidate.Token);
                    await scope.InvokeAsync(action);
                }
                catch (OperationCanceledException) when (candidate.IsCancellationRequested)
                {
                    // superseded by a newer trigger, or the owner withdrew
                }
                finally
                {
                    lock (sync)
                    {
                        if (ReferenceEquals(pending, candidate))
                        {
                            pending = null;
                        }
                    }
                }
            }
        }

        Task Trigger()
        {
            lock (sync)
            {
                if (scope.IsStopped)
                {
                    return Task.CompletedTask;
                }

                pending?.Cancel();
                var candidate = CancellationTokenSource.CreateLinkedTokenSource(scope.Token);
                pending = candidate;
                var task = RunPendingAsync(candidate);
                scope.Track(task);
                return task;
            }
        }

        Bind(owner, scope);
        return Trigger;
    }

    /// <summary>Leading/trailing throttle bound to the owner's activation.</summary>
    public Func<Task> ThrottleOwned(Context owner, TimeSpan window, Func<ValueTask> action) =>
        ThrottleCore(owner, window, action, trailing: true);

    /// <summary>Leading-only throttle bound to the owner's activation.</summary>
    public Func<Task> ThrottleNoTrailingOwned(Context owner, TimeSpan window, Func<ValueTask> action) =>
        ThrottleCore(owner, window, action, trailing: false);

    private Func<Task> ThrottleCore(Context owner, TimeSpan window, Func<ValueTask> action, bool trailing)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = NewScope();
        var sync = new object();
        var last = DateTimeOffset.MinValue;
        CancellationTokenSource? pending = null;
        var scheduled = false;

        async Task RunTrailingAsync(CancellationTokenSource candidate, TimeSpan remaining)
        {
            using (candidate)
            {
                try
                {
                    await Task.Delay(Positive(remaining), candidate.Token);
                    bool fire;
                    lock (sync)
                    {
                        fire = scheduled;
                        scheduled = false;
                        if (fire)
                        {
                            last = DateTimeOffset.UtcNow;
                        }
                    }

                    if (fire)
                    {
                        await scope.InvokeAsync(action);
                    }
                }
                catch (OperationCanceledException) when (candidate.IsCancellationRequested)
                {
                    // a newer leading fire won the window
                }
                finally
                {
                    lock (sync)
                    {
                        if (ReferenceEquals(pending, candidate))
                        {
                            pending = null;
                        }
                    }
                }
            }
        }

        Task Trigger()
        {
            lock (sync)
            {
                if (scope.IsStopped)
                {
                    return Task.CompletedTask;
                }

                var now = DateTimeOffset.UtcNow;
                if (last == DateTimeOffset.MinValue || now - last >= window)
                {
                    last = now;
                    return scope.InvokeAsync(action);
                }

                if (!trailing)
                {
                    return Task.CompletedTask;
                }

                scheduled = true;
                if (pending is not null)
                {
                    return Task.CompletedTask;
                }

                var candidate = CancellationTokenSource.CreateLinkedTokenSource(scope.Token);
                pending = candidate;
                var task = RunTrailingAsync(candidate, window - (now - last));
                scope.Track(task);
                return task;
            }
        }

        Bind(owner, scope);
        return Trigger;
    }

    /// <summary>
    /// Completes when the timeout fires; throws <see cref="OperationCanceledException"/>
    /// if the owner's activation withdraws first. No callback outlives the owner.
    /// </summary>
    public Task TimeoutWait(Context owner, TimeSpan delay)
    {
        var scope = NewScope();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = RunTimeoutAsync(scope, delay, () =>
        {
            fired.TrySetResult();
            return ValueTask.CompletedTask;
        });
        scope.Track(runner);
        Bind(owner, scope);
        return AwaitWithScopeAsync(scope, fired.Task);
    }

    private static async Task AwaitWithScopeAsync(TimerScope scope, Task fired)
    {
        var token = scope.Token;
        try
        {
            await fired.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("timer owner withdrew before the timeout fired", token);
        }
    }

    /// <summary>
    /// Tick sequence bound to the owner. Breaking out of enumeration stops the
    /// interval; an owner unwind ends the sequence without another tick.
    /// </summary>
    public IAsyncEnumerable<DateTimeOffset> Ticks(Context owner, TimeSpan period)
    {
        var scope = NewScope();
        var token = scope.Token; // captured before the owner can unwind and dispose the scope
        var channel = Channel.CreateUnbounded<DateTimeOffset>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var runner = RunTicksAsync(scope, period, channel.Writer);
        scope.Track(runner);
        Bind(owner, scope);
        return DrainTicksAsync(token, channel.Reader);
    }

    private static async Task RunTicksAsync(TimerScope scope, TimeSpan period, ChannelWriter<DateTimeOffset> writer)
    {
        var actual = period > TimeSpan.Zero ? period : TimeSpan.FromMilliseconds(1);
        try
        {
            while (!scope.IsStopped)
            {
                await Task.Delay(actual, scope.Token);
                await scope.InvokeAsync(() =>
                {
                    writer.TryWrite(DateTimeOffset.UtcNow);
                    return ValueTask.CompletedTask;
                });
            }
        }
        catch (OperationCanceledException) when (scope.IsStopped)
        {
            // owner unwind or service disposal
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async IAsyncEnumerable<DateTimeOffset> DrainTicksAsync(
        [EnumeratorCancellation] CancellationToken token, ChannelReader<DateTimeOffset> reader)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(token))
                {
                    yield break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                yield break;
            }

            while (reader.TryRead(out var tick))
            {
                yield return tick;
            }
        }
    }

    /// <summary>
    /// Cancellation token that fires on timeout, on manual cancellation or when
    /// the owner withdraws. The timer and its effect are owned by the activation.
    /// </summary>
    public CancellationTokenSource TimeoutContext(Context owner, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var scope = NewScope();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(owner.CancellationToken, scope.Token);
        var runner = RunTimeoutAsync(scope, delay, () =>
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }

            return ValueTask.CompletedTask;
        });
        scope.Track(runner);
        try
        {
            Bind(owner, scope, cts.Dispose);
        }
        catch
        {
            cts.Dispose();
            throw;
        }

        return cts;
    }

    private TimerScope NewScope()
    {
        var scope = new TimerScope(_lifetime.Token);
        _scopes[scope] = 0;
        return scope;
    }

    private static void Bind(Context owner, TimerScope scope, Action? afterStop = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        try
        {
            owner.EffectAsync(
                static () => ValueTask.CompletedTask,
                async () =>
                {
                    await scope.StopAndWaitAsync();
                    afterStop?.Invoke();
                }).GetAwaiter().GetResult();
        }
        catch
        {
            // A refused effect must compensate now: nothing may outlive the owner.
            scope.StopAndWaitAsync().GetAwaiter().GetResult();
            afterStop?.Invoke();
            throw;
        }
    }

    private static async Task RunTimeoutAsync(TimerScope scope, TimeSpan delay, Func<ValueTask> action)
    {
        try
        {
            await Task.Delay(Positive(delay), scope.Token);
            await scope.InvokeAsync(action);
        }
        catch (OperationCanceledException) when (scope.IsStopped)
        {
            // cooperative stop
        }
    }

    private static async Task RunIntervalAsync(TimerScope scope, TimeSpan period, Func<ValueTask> action)
    {
        var actual = period > TimeSpan.Zero ? period : TimeSpan.FromMilliseconds(1);
        try
        {
            while (!scope.IsStopped)
            {
                await Task.Delay(actual, scope.Token);
                await scope.InvokeAsync(action);
            }
        }
        catch (OperationCanceledException) when (scope.IsStopped)
        {
            // cooperative stop
        }
    }

    private static TimeSpan Positive(TimeSpan value) =>
        value > TimeSpan.Zero ? value : TimeSpan.Zero;

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await Task.WhenAll(_scopes.Keys.Select(scope => scope.StopAndWaitAsync().AsTask()));
        _scopes.Clear();
        _lifetime.Dispose();

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

    // The public contract exposes disposable handles; concrete callers keep the
    // richer TimerHandle API without widening the shared contract assembly.
    IDisposable ITimerService.TimeoutOwned(Context owner, TimeSpan delay, Func<ValueTask> action) =>
        TimeoutOwned(owner, delay, action);

    IDisposable ITimerService.IntervalOwned(Context owner, TimeSpan period, Func<ValueTask> action) =>
        IntervalOwned(owner, period, action);
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

/// <summary>
/// Non-waiting control over one owner-bound timer. Joining admitted callbacks
/// is reserved for the effect inverse; exposing it here would let a callback
/// stop (and self-join) its own entry.
/// </summary>
public sealed class TimerHandle : IDisposable
{
    private readonly TimerScope _scope;

    internal TimerHandle(TimerScope scope) => _scope = scope;

    public bool IsStopped => _scope.IsStopped;

    /// <summary>Cancels the entry without waiting; safe inside its own callback.</summary>
    public bool Stop() => _scope.Stop();

    public void Dispose() => Stop();
}

/// <summary>
/// One cancellable timer episode. Stop gates new callback admissions;
/// StopAndWaitAsync additionally joins the runner and admitted callbacks.
/// All mutable state is guarded by one lock, so cancellation, admission and
/// disposal cannot interleave.
/// </summary>
internal sealed class TimerScope
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenRegistration _parentRegistration;
    private readonly List<Task> _runners = [];
    private int _callbacks;
    private TaskCompletionSource? _idle;
    private bool _stopped;

    internal TimerScope(CancellationToken parent)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        _parentRegistration = parent.CanBeCanceled ? parent.Register(() => Stop()) : default;
    }

    internal CancellationToken Token => _cts.Token;

    internal bool IsStopped
    {
        get
        {
            lock (_sync)
            {
                return _stopped;
            }
        }
    }

    internal bool Stop()
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return false;
            }

            _stopped = true;
            _cts.Cancel();
            return true;
        }
    }

    internal void Track(Task runner)
    {
        lock (_sync)
        {
            _runners.Add(runner);
        }

        _ = runner.ContinueWith(_ =>
        {
            lock (_sync)
            {
                _runners.Remove(runner);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal async Task InvokeAsync(Func<ValueTask> action)
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            _callbacks++;
            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (--_callbacks == 0)
                {
                    _idle?.TrySetResult();
                    _idle = null;
                }
            }
        }
    }

    internal async ValueTask StopAndWaitAsync()
    {
        Stop();
        while (true)
        {
            Task[] runners;
            Task? idle;
            lock (_sync)
            {
                runners = [.. _runners];
                idle = _idle?.Task;
            }

            var pending = idle is null ? runners : [.. runners, idle];
            if (pending.Length == 0)
            {
                break;
            }

            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch
            {
                // A cooperative stop must never mask the owner's unwind.
            }
        }

        _parentRegistration.Dispose();
        _cts.Dispose();
    }
}
