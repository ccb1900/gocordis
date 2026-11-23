using BackfillApp;

var root = Path.Combine(Path.GetTempPath(), "gocordis-backfill-demo");
Directory.CreateDirectory(root);

var journalPath = Path.Combine(root, "done.log");
var expectedDates = Enumerable.Range(1, 3)
    .Select(day => new DateOnly(2026, 9, day))
    .ToArray();
var journal = new BackfillJournal(journalPath, expectedDates);

// A catch-up pass: apply the day's business work (read source, write output)
// first; only then does the journal mark it done. If the process dies between
// days, the next pass replays exactly the unmarked ones.
var replayed = await journal.ReplayAsync(async date =>
{
    var output = Path.Combine(root, $"{date:yyyy-MM-dd}.out");
    await File.WriteAllTextAsync(output, $"collected {date:yyyy-MM-dd}");
    Console.WriteLine($"[backfill] collected {date:yyyy-MM-dd} -> {output}");
});

Console.WriteLine($"[backfill] replayed {replayed.Count} day(s); completed {journal.Completed.Count}");

// A second pass has nothing to do: the completion log is the source of truth.
var secondPass = await journal.ReplayAsync(_ => Task.CompletedTask);
Console.WriteLine($"[backfill] second pass replayed {secondPass.Count} day(s) (idempotent)");
