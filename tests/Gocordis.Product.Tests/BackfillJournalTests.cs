using BackfillApp;
using Xunit;

namespace Gocordis.Product.Tests;

public sealed class BackfillJournalTests
{
    [Fact]
    public async Task BackfillJournalReplaysOnlyMissingDatesAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "gocordis-backfill-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "done.log");
            var d1 = new DateOnly(2026, 9, 1);
            var d2 = new DateOnly(2026, 9, 2);
            var d3 = new DateOnly(2026, 9, 3);
            var d4 = new DateOnly(2026, 9, 4);
            var d5 = new DateOnly(2026, 9, 5);
            var d6 = new DateOnly(2026, 9, 6);

            var journal = new BackfillJournal(path, new[] { d1, d2, d3 });

            // A repeated mark is a no-op in memory and on disk.
            journal.Record(d1);
            journal.Record(d1);
            Assert.Equal(new[] { d1 }, journal.Completed);
            Assert.Single(File.ReadAllLines(path));

            // Only the two unmarked days are replayed; marks follow successful work.
            var applied = new List<DateOnly>();
            var first = await journal.ReplayAsync(date =>
            {
                applied.Add(date);
                return Task.CompletedTask;
            });
            Assert.Equal(new[] { d2, d3 }, first);
            Assert.Equal(new[] { d2, d3 }, applied);
            Assert.Equal(new[] { d1, d2, d3 }, journal.Completed.OrderBy(date => date));

            var second = await journal.ReplayAsync(date =>
            {
                applied.Add(date);
                return Task.CompletedTask;
            });
            Assert.Empty(second);
            Assert.Equal(2, applied.Count);

            // A torn tail (power loss mid-write) is ignored on reopen, so the new
            // day is replayed and its mark lands on a fresh line.
            await File.AppendAllTextAsync(path, "2026-09-0");
            var recovered = new BackfillJournal(path, new[] { d1, d2, d3, d4 });
            Assert.Equal(new[] { d1, d2, d3 }, recovered.Completed.OrderBy(date => date));
            Assert.Equal(new[] { d4 }, await recovered.ReplayAsync(_ => Task.CompletedTask));
            Assert.Equal(4, recovered.Completed.Count);
            Assert.Empty(await recovered.ReplayAsync(_ => Task.CompletedTask));
            var reopened = new BackfillJournal(path, new[] { d1, d2, d3, d4 });
            Assert.Equal(4, reopened.Completed.Count);

            // Cancellation leaves already-marked days persisted; a fresh pass
            // resumes at the first day that was never applied.
            var canceling = new BackfillJournal(Path.Combine(root, "cancel.log"), new[] { d5, d6 });
            using var cts = new CancellationTokenSource();
            var seen = new List<DateOnly>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                canceling.ReplayAsync(date =>
                {
                    seen.Add(date);
                    cts.Cancel();
                    return Task.CompletedTask;
                }, cts.Token));
            Assert.Equal(new[] { d5 }, seen);
            Assert.Equal(new[] { d5 }, canceling.Completed);
            Assert.Equal(new[] { d6 }, await canceling.ReplayAsync(date =>
            {
                seen.Add(date);
                return Task.CompletedTask;
            }));
            Assert.Equal(2, canceling.Completed.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
