using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Down-time backfill planner. It does not rely on LastSuccessDate;
/// the ledger decides which files are already delivered.</summary>
public sealed class HistoricalRangePlanner : IHistoricalRangePlanner
{
    /// <summary>Offline backfill scans the last <paramref name="exclusiveLookback"/>
    /// window, not "everything before the cutoff". The ledger (never an
    /// in-memory last-success date) decides what is already delivered.</summary>
    public SourceDateRange Plan(DateTimeOffset now, TimeSpan exclusiveLookback) =>
        new(now - exclusiveLookback, now);
}
