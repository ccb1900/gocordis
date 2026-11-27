using System.Reflection;
using Cronos;
using Industrial.Elsa;

namespace Industrial.Tests;

/// <summary>Long-outage catch-up must be independent of downtime length: the
/// reverse scan always lands on the three occurrences closest to now.</summary>
public sealed class CronReverseCatchUpTests
{
    [Fact]
    public void MinuteCronFindsTheLatestThreeOccurrencesAfterAnyOutage()
    {
        var method = typeof(IndustrialElsaComponent).GetMethod("LastOccurrenceBefore",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var expression = CronExpression.Parse("*/1 * * * *");
        var now = DateTimeOffset.UtcNow;
        foreach (var outage in new[] { TimeSpan.FromDays(30), TimeSpan.FromDays(365) })
        {
            var checkpoint = now - outage;
            var upper = now.AddTicks(1);
            var found = new List<DateTimeOffset>();
            for (var i = 0; i < 3; i++)
            {
                var occurrence = (DateTimeOffset?)method.Invoke(null,
                    [expression, TimeZoneInfo.Utc, checkpoint, upper]);
                Assert.NotNull(occurrence);
                found.Add(occurrence!.Value);
                upper = occurrence.Value;
            }
            // Three consecutive minutes immediately before now.
            Assert.Equal(TimeSpan.FromMinutes(1), found[0] - found[1]);
            Assert.Equal(TimeSpan.FromMinutes(1), found[1] - found[2]);
            Assert.True(now - found[0] < TimeSpan.FromMinutes(2));
        }
    }
}
