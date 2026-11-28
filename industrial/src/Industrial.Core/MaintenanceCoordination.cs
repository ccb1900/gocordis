using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Ledger-owned names for the one execution/maintenance protocol.
/// Cleaners own different data, but they share this coordination so a workflow
/// can never start while either cleaner is deleting recoverable data.</summary>
public static class MaintenanceNames
{
    public const string Gate = "maintenance-gate";
    public const string Cleanup = "cleanup";
    public const string ExecutionPrefix = "execution:";
}

/// <summary>A claimed cleanup lease. Callers must check <see cref="EnsureValidAsync"/>
/// during long operations and stop deleting immediately when it reports lost.</summary>
public sealed class MaintenanceLease : IAsyncDisposable
{
    private readonly ICollectionLedger _ledger;
    private readonly TimeSpan _lease;
    private readonly TimeSpan _checkInterval;
    private DateTimeOffset _nextCheck;

    public string Name { get; }
    public string Token { get; }
    public bool Lost { get; private set; }

    internal MaintenanceLease(ICollectionLedger ledger, string name, string token,
        TimeSpan lease, TimeSpan checkInterval)
    {
        _ledger = ledger;
        Name = name;
        Token = token;
        _lease = lease;
        _checkInterval = checkInterval;
        _nextCheck = DateTimeOffset.UtcNow + checkInterval;
    }

    public async ValueTask<bool> EnsureValidAsync(CancellationToken ct = default)
    {
        if (Lost) return false;
        // Check consumers on every deletion boundary, not only when the lease
        // is due: a workflow started after this cleanup began must stop it.
        if (await _ledger.HasActiveMaintenanceAsync(MaintenanceNames.ExecutionPrefix, ct))
        {
            Lost = true;
            return false;
        }
        if (DateTimeOffset.UtcNow >= _nextCheck)
        {
            Lost = !await RenewAsync(ct);
            _nextCheck = DateTimeOffset.UtcNow + _checkInterval;
        }
        return !Lost;
    }

    /// <summary>Fenced renewal for an execution lease. Unlike cleanup checks,
    /// this does not inspect the execution prefix: the lease is a consumer.</summary>
    public async ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        if (Lost) return false;
        Lost = !await _ledger.RenewMaintenanceAsync(Name, Token, _lease, ct);
        return !Lost;
    }

    public async ValueTask ReleaseAsync()
    {
        await _ledger.ReleaseMaintenanceAsync(Name, Token, CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ReleaseAsync();
}

/// <summary>Execution and cleanup claims are serialized through a short-lived
/// ledger gate. A workflow claims its execution lease while still holding the
/// gate; cleanup later sees that lease and refuses to delete. Cleanup claims
/// its lease while still holding the gate; workflow later sees the cleanup
/// lease and waits. This removes the check/start window without introducing a
/// second coordination system.</summary>
public static class MaintenanceCoordination
{
    private static readonly TimeSpan GateLease = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExecutionWait = TimeSpan.FromMinutes(5);

    public static async ValueTask<MaintenanceLease> BeginExecutionAsync(
        ICollectionLedger ledger, string owner, CancellationToken ct = default)
    {
        var name = $"{MaintenanceNames.ExecutionPrefix}{owner}";
        var deadline = DateTimeOffset.UtcNow + ExecutionWait;
        while (true)
        {
            var gate = $"gate-{Guid.NewGuid():N}";
            if (await ledger.TryClaimMaintenanceAsync(
                    MaintenanceNames.Gate, gate, GateLease, ct))
            {
                try
                {
                    var token = $"exec-{Guid.NewGuid():N}";
                    if (!await ledger.IsMaintenanceActiveAsync(MaintenanceNames.Cleanup, ct) &&
                        await ledger.TryClaimMaintenanceAsync(name, token, ClaimLease, ct))
                    {
                        return new MaintenanceLease(ledger, name, token, ClaimLease, CheckInterval);
                    }
                }
                finally
                {
                    await ledger.ReleaseMaintenanceAsync(
                        MaintenanceNames.Gate, gate, CancellationToken.None);
                }
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException(
                    "collection execution could not start because cleanup is active");
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Runs a workflow/recovery operation while its execution lease
    /// is renewed by a background heartbeat. A failed renewal cancels the
    /// operation and surfaces the loss; the caller still releases the lease.</summary>
    public static async ValueTask<T> RunWithHeartbeatAsync<T>(
        MaintenanceLease lease,
        string owner,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default,
        TimeSpan? heartbeatInterval = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(operation);
        var interval = heartbeatInterval ?? CheckInterval;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatFailure = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task HeartbeatAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(interval, linked.Token);
                    if (!await lease.RenewAsync(linked.Token))
                        throw new InvalidOperationException(
                            $"execution lease for '{owner}' was lost; operation aborted");
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                // Normal completion or caller cancellation.
            }
            catch (Exception ex)
            {
                heartbeatFailure.TrySetResult(ex);
                await linked.CancelAsync();
            }
        }

        var heartbeat = HeartbeatAsync();
        Exception? operationError = null;
        T result = default!;
        try
        {
            result = await operation(linked.Token);
        }
        catch (Exception ex)
        {
            operationError = ex;
        }

        if (!linked.IsCancellationRequested) await linked.CancelAsync();
        heartbeatFailure.TrySetResult(null);
        await heartbeat;
        var failure = await heartbeatFailure.Task;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (operationError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operationError).Throw();
        return result;
    }

    public static async ValueTask<MaintenanceLease?> BeginCleanupAsync(
        ICollectionLedger ledger, CancellationToken ct = default)
    {
        while (true)
        {
            var gate = $"gate-{Guid.NewGuid():N}";
            if (await ledger.TryClaimMaintenanceAsync(
                    MaintenanceNames.Gate, gate, GateLease, ct))
            {
                try
                {
                    if (await ledger.HasActiveMaintenanceAsync(
                            MaintenanceNames.ExecutionPrefix, ct))
                        return null;
                    var token = $"cleanup-{Guid.NewGuid():N}";
                    if (await ledger.TryClaimMaintenanceAsync(
                            MaintenanceNames.Cleanup, token, ClaimLease, ct))
                        return new MaintenanceLease(
                            ledger, MaintenanceNames.Cleanup, token, ClaimLease, CheckInterval);
                }
                finally
                {
                    await ledger.ReleaseMaintenanceAsync(
                        MaintenanceNames.Gate, gate, CancellationToken.None);
                }
            }
            // Another cleaner holds the exclusive cleanup lease; the timer
            // retries on its next tick instead of queueing behind it.
            return null;
        }
    }
}
