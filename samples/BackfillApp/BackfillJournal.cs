using System.Globalization;
using System.Text;

namespace BackfillApp;

/// <summary>
/// Business journal for a catch-up/backfill app (C# counterpart of Go's
/// cmd/backfill). The per-day completion log is the only persisted state;
/// expected work comes from the declaration, not from the journal. Every replay
/// pass visits exactly the declared days that are not yet marked, and a day is
/// marked only after its work completed successfully - so a crash or a
/// cancellation between days simply leaves the rest for the next pass, and a
/// repeated mark never duplicates work.
/// </summary>
public sealed class BackfillJournal
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly object _gate = new();
    private readonly string _path;
    private readonly DateOnly[] _expected;
    private readonly HashSet<DateOnly> _done;

    /// <param name="path">Append-only completion log (created on first mark).</param>
    /// <param name="expectedDates">The declared days a catch-up pass should cover.</param>
    public BackfillJournal(string path, IEnumerable<DateOnly> expectedDates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expectedDates);

        _path = path;
        _expected = expectedDates.Distinct().OrderBy(date => date).ToArray();
        _done = LoadDone(path);
    }

    /// <summary>Snapshot of the days already marked completed.</summary>
    public IReadOnlySet<DateOnly> Completed
    {
        get
        {
            lock (_gate)
            {
                return new HashSet<DateOnly>(_done);
            }
        }
    }

    /// <summary>Marks a day done once; repeated calls do not append a second mark.</summary>
    public void Record(DateOnly date)
    {
        lock (_gate)
        {
            if (_done.Contains(date))
            {
                return;
            }

            AppendMark(date);
            _done.Add(date);
        }
    }

    /// <summary>
    /// Applies <paramref name="apply"/> to each expected day that is not yet
    /// completed, marking it immediately after success. Returns the days this
    /// pass actually replayed. Cancellation is cooperative and safe: work
    /// already marked stays marked, and the remaining days are picked up by a
    /// later pass.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> ReplayAsync(
        Func<DateOnly, Task> apply,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var replayed = new List<DateOnly>();
        foreach (var date in _expected)
        {
            token.ThrowIfCancellationRequested();
            if (Completed.Contains(date))
            {
                continue;
            }

            await apply(date).ConfigureAwait(false);
            Record(date);
            replayed.Add(date);
        }

        return replayed;
    }

    private static HashSet<DateOnly> LoadDone(string path)
    {
        var done = new HashSet<DateOnly>();
        if (!File.Exists(path))
        {
            return done;
        }

        foreach (var line in File.ReadLines(path))
        {
            // Ignore torn/invalid tail lines: a half-written mark is not evidence
            // of completed work, so that day remains eligible for replay.
            if (DateOnly.TryParseExact(line.Trim(), DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                done.Add(date);
            }
        }

        return done;
    }

    private void AppendMark(DateOnly date)
    {
        var fullPath = Path.GetFullPath(_path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length > 0)
        {
            stream.Position = stream.Length - 1;
            if (stream.ReadByte() != '\n')
            {
                // Close a torn tail first, otherwise the new mark would be
                // concatenated onto the invalid line and lost on reopen.
                stream.Position = stream.Length;
                stream.WriteByte((byte)'\n');
            }
        }

        stream.Position = stream.Length;
        var mark = Encoding.UTF8.GetBytes(date.ToString(DateFormat, CultureInfo.InvariantCulture) + "\n");
        stream.Write(mark, 0, mark.Length);
        stream.Flush(flushToDisk: true);
    }
}
