using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Test-only deterministic scheduler (the Go kernel's deterministic.go counterpart).
/// It never enters production Core: it only fixes the order of already-defined steps.
/// </summary>
public sealed class DeterministicDriver
{
    private readonly List<(string Label, Action Step)> _steps = [];

    public DeterministicDriver Enqueue(string label, Action step)
    {
        _steps.Add((label, step));
        return this;
    }

    public IReadOnlyList<string> Run(int seed)
    {
        var executed = new List<string>();
        foreach (var (label, step) in _steps
                     .Select((entry, index) => (entry, order: Order(entry.Label, index, seed)))
                     .OrderBy(entry => entry.order)
                     .Select(entry => entry.entry))
        {
            step();
            executed.Add(label);
        }

        return executed;
    }

    private static long Order(string label, int index, int seed)
    {
        unchecked
        {
            long hash = seed;
            foreach (var character in label)
            {
                hash = hash * 31 + character;
            }

            return hash * 397 + index;
        }
    }
}

public sealed class DeterministicDriverTests
{
    [Fact]
    public void SameSeedReplaysTheSameInterleaving()
    {
        static IReadOnlyList<string> Run(int seed)
        {
            var log = new List<string>();
            var driver = new DeterministicDriver()
                .Enqueue("apply", () => log.Add("apply"))
                .Enqueue("commit", () => log.Add("commit"))
                .Enqueue("withdraw", () => log.Add("withdraw"))
                .Enqueue("finish", () => log.Add("finish"));
            driver.Run(seed);
            return log;
        }

        Assert.Equal(Run(17), Run(17));
    }
}
