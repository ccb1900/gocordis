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

/// <summary>A claimed execution or cleanup lease. Execution callers should own a
/// <see cref="MaintenanceHeartbeat"/> for the entire operation; cleanup callers
/// must check <see cref="EnsureValidAsync"/> on every deletion boundary.</summary>
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

    /// <summary>Start background renewal immediately after the lease is claimed.
    /// The returned token must be used for every subsequent execution step so
    /// lease loss cancels preparation as well as the main run.</summary>
    public MaintenanceHeartbeat StartHeartbeat(
        string owner, CancellationToken callerCt, TimeSpan? interval = null) =>
        new(this, owner, interval, callerCt);
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

    /// <summary>Runs a workflow/recovery operation under an explicit heartbeat
    /// scope. Prefer <see cref="BeginExecutionAsync"/> + <see cref="MaintenanceLease.StartHeartbeat"/>
    /// so preparation is covered too; this overload remains for narrow critical sections.</summary>
    public static async ValueTask<T> RunWithHeartbeatAsync<T>(
        MaintenanceLease lease,
        string owner,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default,
        TimeSpan? heartbeatInterval = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Lost)
            throw new InvalidOperationException(
                $"execution lease for '{owner}' was already lost; operation not started");
        await using var heartbeat = lease.StartHeartbeat(owner, ct, heartbeatInterval);
        return await operation(heartbeat.Token);
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

/// <summary>Background renewal owned for the full execution lifetime. Its token
/// is canceled when the caller finishes or the lease is lost; disposal awaits
/// the loop and surfaces lease loss after the operation has stopped.</summary>
public sealed class MaintenanceHeartbeat : IAsyncDisposable
{
    private readonly MaintenanceLease _lease;
    private readonly CancellationTokenSource _linked;
    private readonly Task _loop;
    private readonly TaskCompletionSource<Exception?> _failure =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _owner;

    internal MaintenanceHeartbeat(
        MaintenanceLease lease, string owner, TimeSpan? interval, CancellationToken callerCt)
    {
        _lease = lease;
        _owner = owner;
        _linked = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        _loop = LoopAsync(interval ?? MaintenanceCoordination.CheckInterval);
    }

    public CancellationToken Token => _linked.Token;
    public bool Lost => _lease.Lost;

    private async Task LoopAsync(TimeSpan interval)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, _linked.Token);
                // Renew without the stop token so a concurrent Dispose cannot
                // turn an actual lease loss into an indistinguishable cancel.
                if (!await _lease.RenewAsync(CancellationToken.None))
                    throw new InvalidOperationException(
                        $"execution lease for '{_owner}' was lost; operation aborted");
            }
        }
        catch (OperationCanceledException) when (_linked.IsCancellationRequested)
        {
            // Caller cancellation and Dispose can race a renewal. Make one
            // final fenced renewal so cancellation cannot hide a lease loss.
            if (_lease.Lost || !await _lease.RenewAsync(CancellationToken.None))
                throw new InvalidOperationException(
                    $"execution lease for '{_owner}' was lost; operation aborted");
        }
        catch (Exception ex)
        {
            // Record first: Dispose may cancel concurrently, but a lease loss
            // observed before stopping must survive and be surfaced.
            _failure.TrySetResult(ex);
            await _linked.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_linked.IsCancellationRequested) await _linked.CancelAsync();
        await _loop;
        _failure.TrySetResult(null);
        var failure = await _failure.Task;
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
