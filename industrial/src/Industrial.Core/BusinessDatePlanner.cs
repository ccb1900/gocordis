using System.Globalization;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Industrial business-date recovery planning. This is deliberately
/// separate from cron-fire catch-up: a fired schedule is not proof that a
/// business date was collected. Known incomplete dates are always retried;
/// terminal dates are never silently re-collected; gaps up to the last
/// completed business date are synthesized within an explicit bound.
/// </summary>
public static class BusinessDatePlanner
{
    /// <summary>Normal catch-up window used by unattended (automatic) runs.</summary>
    public const int DefaultCatchUpDays = 30;

    /// <summary>Unattended runs never widen the normal window.</summary>
    public const int MaxAutomaticCatchUpDays = 30;

    /// <summary>Hard ceiling for an explicit manual history backfill.</summary>
    public const int MaxExplicitHistoryDays = 366;

    /// <summary>Resolves the business timezone of a binding. Missing configuration
    /// falls back to UTC; an explicitly configured but unknown id is rejected so
    /// a typo cannot silently move the business-date boundary.</summary>
    public static TimeZoneInfo ResolveBusinessZone(
        IReadOnlyDictionary<string, object?>? triggerConfig,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        object? configured = null;
        if (triggerConfig?.TryGetValue("timezone", out var triggerZone) == true)
        {
            configured = triggerZone;
        }

        if ((configured is null || string.IsNullOrWhiteSpace(configured.ToString())) &&
            parameters?.TryGetValue("timezone", out var parameterZone) == true)
        {
            configured = parameterZone;
        }

        var id = configured?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id!, out var zone))
            throw new InvalidOperationException($"unknown business timezone '{id}'");
        return zone;
    }

    /// <summary>The last business date whose production window is closed. A
    /// binding never targets its local "today": production is still writing it.
    /// The default policy is yesterday in the binding's own business timezone.
    /// </summary>
    public static DateOnly LastCompletedBusinessDate(
        TimeZoneInfo zone, DateTimeOffset? now = null)
    {
        var local = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, zone);
        return DateOnly.FromDateTime(local.DateTime).AddDays(-1);
    }

    /// <summary>Parses the manual-only businessDate/date override. Returns false
    /// for absent values and rejects non-empty values that are not a calendar
    /// date, so a malformed override cannot be mistaken for "no override".</summary>
    public static bool TryParseOverride(object? raw, out DateOnly date)
    {
        date = default;
        var text = raw switch
        {
            null => null,
            string value => value,
            System.Text.Json.Nodes.JsonValue value
                when value.TryGetValue<string>(out var jsonText) => jsonText,
            _ => raw.ToString(),
        };
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!DateOnly.TryParseExact(text!.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            throw new InvalidOperationException(
                $"businessDate must use yyyy-MM-dd; got '{text}'");
        }
        return true;
    }

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
