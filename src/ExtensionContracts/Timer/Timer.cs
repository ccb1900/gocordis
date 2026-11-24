using Gocordis.Core;

namespace Gocordis.Extensions;

public static class TimerKeys
{
    public static readonly Key<ITimerService> Service = new("gocordis.extension.timer");
}

public interface ITimerService : IAsyncDisposable
{
    Task TimeoutAsync(TimeSpan delay, Func<ValueTask> action, CancellationToken token = default);
    Task IntervalAsync(TimeSpan period, Func<ValueTask> action, CancellationToken token = default);
    Task DebounceAsync(TimeSpan window, Func<ValueTask> action, CancellationToken token = default);
    Task<bool> ThrottleAsync(TimeSpan window, Func<ValueTask> action);
    IDisposable TimeoutOwned(Context owner, TimeSpan delay, Func<ValueTask> action);
    IDisposable IntervalOwned(Context owner, TimeSpan period, Func<ValueTask> action);
    Task TimeoutWait(Context owner, TimeSpan delay);
    IAsyncEnumerable<DateTimeOffset> Ticks(Context owner, TimeSpan period);
}
