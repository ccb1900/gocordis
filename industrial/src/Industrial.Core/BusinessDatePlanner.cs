using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Industrial business-date recovery planning. This is deliberately
/// separate from cron-fire catch-up: a fired schedule is not proof that a
/// business date was collected. Known incomplete dates are always retried;
/// terminal dates are never silently re-collected; gaps between the last
/// terminal date and today are synthesized within an explicit bound.
/// </summary>
public static class BusinessDatePlanner
{
    public static IReadOnlyList<DateOnly> Plan(
        IReadOnlyList<CollectionDateState> dates,
        string bindingId,
        DateOnly target,
        int catchUpDays,
        DateTimeOffset? now = null)
    {
        if (catchUpDays < 0) throw new ArgumentOutOfRangeException(nameof(catchUpDays));
        var moment = now ?? DateTimeOffset.UtcNow;
        var relevant = dates.Where(date => string.Equals(date.BindingId, bindingId, StringComparison.Ordinal)
                                          && date.BusinessDate <= target).ToArray();
        var planned = new SortedSet<DateOnly>();
        foreach (var date in relevant.Where(date => IsIncomplete(date, moment)))
            planned.Add(date.BusinessDate);

        // Only a real collection is a gap anchor. Go treats "no data" as a
        // log-only probe: it leaves no ledger row and must be re-probed on the
        // next trigger, so it can never close the gap here.
        var terminal = relevant
            .Where(date => date.Status == CollectionDateStatus.Succeeded)
            .OrderByDescending(date => date.BusinessDate)
            .FirstOrDefault();
        var start = terminal is null ? target : terminal.BusinessDate.AddDays(1);
        if (catchUpDays > 0)
        {
            var earliest = target.AddDays(-(catchUpDays - 1));
            // With no terminal date, an explicit catch-up window is the only
            // reason to look before today; without one, only today is planned.
            if (terminal is null || start < earliest) start = earliest;
        }
        if (catchUpDays == 0 && terminal is null)
            start = target.AddDays(1); // no synthesized dates on first deployment

        for (var date = start; date <= target; date = date.AddDays(1))
            planned.Add(date);
        return planned.ToArray();
    }

    private static bool IsIncomplete(CollectionDateState date, DateTimeOffset now) => date.Status switch
    {
        CollectionDateStatus.Pending => true,
        CollectionDateStatus.NoData => true,
        CollectionDateStatus.Failed => true,
        CollectionDateStatus.Interrupted => true,
        CollectionDateStatus.Running => date.LeaseExpiresAt is null || date.LeaseExpiresAt <= now,
        _ => false,
    };
}
