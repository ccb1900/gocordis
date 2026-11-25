using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// P3: bounded state-space exploration of LEGAL command sequences, not just
/// load-order permutations. Ops include provider withdrawal, consumer teardown,
/// child fibers, Rehome, asynchronous effect install and a failing component.
/// Everything here is test-only; Core remains the single lifecycle authority.
/// </summary>
public enum SmallOp
{
    LoadProvider,
    LoadConsumer,
    LoadFailing,
    WithdrawProvider,
    TeardownConsumer,
    RehomeProvider,
    AsyncInstall,
}

public sealed record SmallTrace(IReadOnlyList<SmallOp> Steps)
{
    public string Describe() => string.Join(" -> ", Steps);

    public string Diagnostic(int seed) =>
        $"seed={seed}; trace=[{Describe()}]; replay SmallStateWorld.ExecuteAsync in this order";

    /// <summary>Greedy delta-debug: remove any op while the failure still
    /// reproduces. Used to shrink a counterexample before reporting it.</summary>
    public static SmallTrace Minimize(
        IReadOnlyList<SmallOp> steps, Func<IReadOnlyList<SmallOp>, bool> stillFails)
    {
        var current = steps.ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = current.Count - 1; i >= 0; i--)
            {
                var candidate = current.Where((_, index) => index != i).ToArray();
                if (stillFails(candidate))
                {
                    current = [.. candidate];
                    changed = true;
                }
            }
        }

        return new SmallTrace(current);
    }
}

public sealed class SmallStateWorld : IAsyncDisposable
{
    private readonly List<string> _journal = [];

    public Runtime Runtime { get; } = new();
    public Key<string> Value { get; } = new("explore/value");
    public IReadOnlyList<string> Journal => _journal;
    public Fiber? Provider { get; private set; }
    public Fiber? Consumer { get; private set; }
    public Fiber? Failing { get; private set; }
    public bool ProviderWithdrawn { get; private set; }
    public bool ConsumerTornDown { get; private set; }

    public static bool IsLegal(IReadOnlyList<SmallOp> sequence)
    {
        var provider = false;
        var providerGone = false;
        var consumer = false;
        var consumerGone = false;
        var failing = false;
        foreach (var op in sequence)
        {
            switch (op)
            {
                case SmallOp.LoadProvider when !provider:
                    provider = true;
                    break;
                case SmallOp.LoadConsumer when provider && !consumer:
                    consumer = true;
                    break;
                case SmallOp.LoadFailing when !failing:
                    failing = true;
                    break;
                case SmallOp.WithdrawProvider when provider && !providerGone:
                    providerGone = true;
                    break;
                case SmallOp.TeardownConsumer when consumer && !consumerGone:
                    consumerGone = true;
                    break;
                case SmallOp.RehomeProvider when provider && !providerGone:
                    break;
                case SmallOp.AsyncInstall when provider && !providerGone:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    public async Task ExecuteAsync(SmallOp op)
    {
        switch (op)
        {
            case SmallOp.LoadProvider:
                Provider = await Runtime.LoadAsync(new ExploreProvider(Value, _journal));
                break;
            case SmallOp.LoadConsumer:
                Consumer = await Runtime.LoadAsync(new ExploreConsumer(Value, _journal));
                break;
            case SmallOp.LoadFailing:
                Failing = await Runtime.LoadAsync(new ExploreFailure());
                break;
            case SmallOp.WithdrawProvider:
                ProviderWithdrawn = true;
                await Runtime.DisposeAsync(Provider!);
                break;
            case SmallOp.TeardownConsumer:
                ConsumerTornDown = true;
                await Runtime.DisposeAsync(Consumer!);
                break;
            case SmallOp.RehomeProvider:
                Provider = await Runtime.RehomeAsync(
                    Provider!, new Realm("explore-" + Guid.NewGuid()),
                    new ExploreProvider(Value, _journal));
                break;
            case SmallOp.AsyncInstall:
                await Runtime.LoadAsync(new AsyncInstall(_journal));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }

    public async Task AssertInvariantsAsync(SmallTrace trace)
    {
        var snapshot = Runtime.Snapshot();
        foreach (var fiber in snapshot.Fibers)
        {
            Assert.True(Enum.IsDefined(fiber.State), trace.Describe());
            if (fiber.Failure is not null)
            {
                Assert.Equal("explore-failing", fiber.Name);
            }

            if (fiber.Name == "explore-consumer")
            {
                Assert.NotEqual(FiberState.Failed, fiber.State);
            }

            var activeProviders = snapshot.Fibers.Count(candidate =>
                candidate.State == FiberState.Active &&
                candidate.Provides.Contains(Value.Name, StringComparer.Ordinal));
            Assert.True(activeProviders <= 1, $"duplicate active provider: {trace.Describe()}");
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Runtime.PendingCommands != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, Runtime.PendingCommands);
    }

    public ValueTask DisposeAsync() => Runtime.DisposeAsync();

    public sealed class ExploreProvider : Component
    {
        private readonly Key<string> _key;
        private readonly List<string> _journal;

        public ExploreProvider(Key<string> key, List<string> journal) => (_key, _journal) = (key, journal);

        public override string Name => "explore-provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, "value");
            await context.EffectAsync(
                () => { _journal.Add("install:provider"); return ValueTask.CompletedTask; },
                () => { _journal.Add("undo:provider"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    private sealed class ExploreConsumer : Component
    {
        private readonly Key<string> _key;
        private readonly List<string> _journal;

        public ExploreConsumer(Key<string> key, List<string> journal) => (_key, _journal) = (key, journal);

        public override string Name => "explore-consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Assert.Equal("value", context.Require(_key));
            await context.LoadChildAsync(new ExploreChild(_journal));
            await context.EffectAsync(
                () => { _journal.Add("install:consumer"); return ValueTask.CompletedTask; },
                () => { _journal.Add("undo:consumer"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    private sealed class ExploreChild : Component
    {
        private readonly List<string> _journal;
        public ExploreChild(List<string> journal) => _journal = journal;
        public override string Name => "explore-child";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(
                () => { _journal.Add("install:child"); return ValueTask.CompletedTask; },
                () => { _journal.Add("undo:child"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    private sealed class AsyncInstall : Component
    {
        private readonly List<string> _journal;
        public AsyncInstall(List<string> journal) => _journal = journal;
        public override string Name => "explore-async";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(
                async () => { await Task.Yield(); _journal.Add("install:async"); },
                () => { _journal.Add("undo:async"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    private sealed class ExploreFailure : Component
    {
        public override string Name => "explore-failing";
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new InvalidOperationException("explore-failure");
    }
}

public sealed class SmallStateExplorationTests
{
    private static readonly SmallOp[] AllOps = Enum.GetValues<SmallOp>();

    [Fact]
    public async Task EveryLegalCommandSequenceUpToDepthFourKeepsTheKernelWellFormed()
    {
        var explored = 0;
        foreach (var sequence in Enumerate(4))
        {
            if (!SmallStateWorld.IsLegal(sequence))
            {
                continue;
            }

            explored++;
            await using var world = new SmallStateWorld();
            var trace = new SmallTrace(sequence);
            Exception? failure = null;
            try
            {
                foreach (var op in sequence)
                {
                    await world.ExecuteAsync(op);
                }

                await world.AssertInvariantsAsync(trace);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Assert.True(failure is null,
                $"invariant broken: {trace.Diagnostic(explored)}\n{failure}");
        }

        Assert.True(explored >= 50, $"explorer only covered {explored} legal sequences");
    }

    [Fact]
    public void ExplorerIncludesWithdrawalTeardownChildRehomeAsyncInstallAndFailureInBothOrders()
    {
        var sequences = Enumerate(4).Where(SmallStateWorld.IsLegal).ToArray();
        Assert.Contains(sequences, sequence =>
            sequence.Contains(SmallOp.WithdrawProvider) && sequence.Contains(SmallOp.LoadConsumer));
        Assert.Contains(sequences, sequence =>
            sequence.Contains(SmallOp.LoadConsumer) &&
            sequence.Contains(SmallOp.WithdrawProvider) &&
            sequence.ToList().IndexOf(SmallOp.LoadConsumer) <
            sequence.ToList().IndexOf(SmallOp.WithdrawProvider));
        Assert.Contains(sequences, sequence =>
            sequence.Contains(SmallOp.WithdrawProvider) &&
            sequence.Contains(SmallOp.LoadConsumer) &&
            sequence.ToList().IndexOf(SmallOp.WithdrawProvider) <
            sequence.ToList().IndexOf(SmallOp.LoadConsumer));
        Assert.Contains(sequences, sequence => sequence.Contains(SmallOp.TeardownConsumer));
        Assert.Contains(sequences, sequence => sequence.Contains(SmallOp.RehomeProvider));
        Assert.Contains(sequences, sequence => sequence.Contains(SmallOp.AsyncInstall));
        Assert.Contains(sequences, sequence => sequence.Contains(SmallOp.LoadFailing));
    }

    [Fact]
    public async Task CounterexamplesCarryAReplayableTraceAndAreMinimized()
    {
        var noisy = new[]
        {
            SmallOp.LoadFailing,
            SmallOp.LoadProvider,
            SmallOp.AsyncInstall,
            SmallOp.LoadConsumer,
            SmallOp.WithdrawProvider,
            SmallOp.TeardownConsumer,
        };
        var trace = new SmallTrace(noisy);
        var diagnostic = trace.Diagnostic(seed: 17);
        Assert.Contains("seed=17", diagnostic);
        Assert.Contains(nameof(SmallOp.LoadProvider), diagnostic);
        Assert.Contains(nameof(SmallOp.WithdrawProvider), diagnostic);

        var minimal = SmallTrace.Minimize(
            noisy,
            steps => steps.Contains(SmallOp.LoadProvider) &&
                     steps.ToList().IndexOf(SmallOp.WithdrawProvider) >
                     steps.ToList().IndexOf(SmallOp.LoadProvider));
        Assert.Equal([SmallOp.LoadProvider, SmallOp.WithdrawProvider], minimal.Steps);

        // The shrunken counterexample is not just text: it replays on a fresh
        // world and preserves the failure condition it describes.
        Assert.True(SmallStateWorld.IsLegal(minimal.Steps));
        await using var world = new SmallStateWorld();
        foreach (var op in minimal.Steps)
        {
            await world.ExecuteAsync(op);
        }

        await world.AssertInvariantsAsync(minimal);
        Assert.True(world.ProviderWithdrawn);
    }

    [Fact]
    public async Task Thm73RulesFireAndBoundedStepCountsAreRecorded()
    {
        // Thm73 premises: finite names, finite Effect iterator, acyclic provider
        // precedence. Rule availability and observed step counts are executable
        // evidence only — upper bound not proved.
        await using var runtime = new Runtime();
        var start = runtime.Events.Sequence;
        var key = new Key<string>("thm73-explore");

        var provider = await runtime.LoadAsync(new SmallStateWorld.ExploreProvider(key, []));
        await runtime.DisposeAsync(provider);
        var events = runtime.Events.Snapshot().Events.Where(e => e.Sequence > start).ToArray();
        var counts = Enum.GetValues<RuntimeEventType>()
            .ToDictionary(type => type, type => events.Count(e => e.Type == type.ToString()));

        Assert.True(counts[RuntimeEventType.ActivationLoading] >= 1, "StartActivation never fired");
        Assert.True(counts[RuntimeEventType.ActivationUnloading] >= 1, "StartUnwind never fired");
        Assert.True(counts[RuntimeEventType.FiberCreated] >= 1, "fiber creation rule never fired");
        Assert.True(counts[RuntimeEventType.ActivationActive] >= 1, "activation commit rule never fired");
        Assert.True(counts[RuntimeEventType.ActivationEnded] >= 1, "activation close rule never fired");
        Assert.InRange(events.Length, 2, 32);
        Assert.True(runtime.Events.Sequence > start);
    }

    [Fact]
    public async Task ConcurrentApplyAndDisposeInterleavingsAreRepeatableAndResidueFree()
    {
        for (var round = 0; round < 3; round++)
        {
            var (forward, forwardJournal) = await RunInterleavingAsync(releaseFirst: true);
            var (reverse, reverseJournal) = await RunInterleavingAsync(releaseFirst: false);
            var (repeat, repeatJournal) = await RunInterleavingAsync(releaseFirst: true);

            Assert.True(ObservationEquivalence.Equivalent(forward, reverse),
                "the two deterministic release orders reached different observations");
            Assert.True(ObservationEquivalence.Equivalent(forward, repeat),
                "the same schedule was not repeatable");
            // Independent fibers unwind in LIFO within each activation, but the
            // cross-fiber order is a scheduler detail; compare journal content.
            Assert.Equal(forwardJournal.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                repeatJournal.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal(4, forwardJournal.Length);
            Assert.Equal(4, reverseJournal.Length);
        }
    }

    /// <summary>
    /// Two Apply tasks are held at a deterministic barrier; two Dispose tasks
    /// are submitted while both are in flight, then the gates are released in
    /// a chosen order. No sleep and no scheduler-order assumption: the only
    /// synchronization is TaskCompletionSource.
    /// </summary>
    private static async Task<(RuntimeSnapshot Snapshot, string[] Journal)> RunInterleavingAsync(
        bool releaseFirst)
    {
        var gate = new ApplyGate();
        var journal = new List<string>();
        var journalGate = new object();
        await using var runtime = new Runtime();

        var firstLoad = runtime.LoadAsync(new GatedApply(
            "interleave-a", gate, gate.First, journal, journalGate));
        var secondLoad = runtime.LoadAsync(new GatedApply(
            "interleave-b", gate, gate.Second, journal, journalGate));
        await gate.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var fibers = runtime.Snapshot().Fibers.ToArray();
        var first = runtime.GetFiber(fibers.Single(f => f.Name == "interleave-a").Id);
        var second = runtime.GetFiber(fibers.Single(f => f.Name == "interleave-b").Id);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var firstDispose = runtime.DisposeAsync(first);
        var secondDispose = runtime.DisposeAsync(second);
        if (releaseFirst)
        {
            gate.First.SetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Second.SetResult();
        }
        else
        {
            gate.Second.SetResult();
            await secondDispose.WaitAsync(TimeSpan.FromSeconds(5));
            gate.First.SetResult();
        }

        await Task.WhenAll(firstLoad, secondLoad, firstDispose, secondDispose)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.QuiesceAsync();

        var snapshot = runtime.Snapshot();
        Assert.Empty(snapshot.Fibers);
        Assert.Empty(snapshot.CleanupFailures);
        Assert.Empty(snapshot.EffectFailures);

        string[] log;
        lock (journalGate)
        {
            log = journal.ToArray();
        }

        foreach (var name in new[] { "interleave-a", "interleave-b" })
        {
            var install = Array.IndexOf(log, $"install:{name}");
            var undo = Array.IndexOf(log, $"undo:{name}");
            Assert.True(install >= 0, $"{name} install never ran");
            Assert.True(undo > install, $"{name} inverse did not follow its install");
        }

        return (snapshot, log);
    }

    private sealed class ApplyGate
    {
        private int _entered;

        public TaskCompletionSource BothEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource First { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Second { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Enter()
        {
            if (Interlocked.Increment(ref _entered) == 2)
            {
                BothEntered.TrySetResult();
            }
        }
    }

    private sealed class GatedApply : Component
    {
        private readonly ApplyGate _gate;
        private readonly TaskCompletionSource _release;
        private readonly List<string> _journal;
        private readonly object _journalGate;

        public GatedApply(
            string name, ApplyGate gate, TaskCompletionSource release,
            List<string> journal, object journalGate)
        {
            Name = name;
            _gate = gate;
            _release = release;
            _journal = journal;
            _journalGate = journalGate;
        }

        public override string Name { get; }

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            // Register the inverse BEFORE publishing gate entry: a Dispose
            // submitted after BothEntered must see a fully landed effect. The
            // previous order announced entry between the journal write and
            // EffectAsync, letting unwind race ahead of inverse registration.
            await context.EffectAsync(
                () =>
                {
                    lock (_journalGate)
                    {
                        _journal.Add($"install:{Name}");
                    }

                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    lock (_journalGate)
                    {
                        _journal.Add($"undo:{Name}");
                    }

                    return ValueTask.CompletedTask;
                });
            _gate.Enter();
            await _release.Task;
            return null;
        }
    }

    private static IEnumerable<SmallOp[]> Enumerate(int depth)
    {
        var buffer = new SmallOp[depth];
        foreach (var sequence in Walk(0))
        {
            yield return sequence;
        }

        IEnumerable<SmallOp[]> Walk(int index)
        {
            if (index == depth)
            {
                yield return (SmallOp[])buffer.Clone();
                yield break;
            }

            foreach (var op in AllOps)
            {
                buffer[index] = op;
                foreach (var result in Walk(index + 1))
                {
                    yield return result;
                }
            }
        }
    }
}
