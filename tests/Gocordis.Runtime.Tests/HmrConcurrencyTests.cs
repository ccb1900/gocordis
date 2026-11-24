using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Paper §5.2.2: declaration revisions are an optimistic-concurrency guard for
/// the HMR read→plan→commit window, and an Apply that fails mid-commit must undo
/// every fiber/effect/provider it created, not just the failed one.
/// </summary>
public sealed class HmrConcurrencyTests
{
    [Fact]
    public async Task StaleExpectedVersionIsRejectedBeforeStagingOrTouchingAFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var stages = 0;
        registry.RegisterComponentFactory("plugin", manifest =>
        {
            Interlocked.Increment(ref stages);
            return new Passive(manifest.Id);
        });

        var manifest = Manifest("a", "plugin", "v1");
        await registry.ApplyAsync([manifest]);
        var version = registry.DeclarationVersion;
        var fiber = registry.Entries["a"].Fiber!;
        var stagesAfterFirstApply = Volatile.Read(ref stages);

        var conflict = await Assert.ThrowsAsync<DeclarationVersionConflictException>(
            () => registry.ApplyAsync([manifest with { Source = "v2" }], version - 1));

        Assert.Equal(version - 1, conflict.ExpectedVersion);
        Assert.Equal(version, conflict.ActualVersion);
        Assert.Same(fiber, registry.Entries["a"].Fiber);
        Assert.Equal("v1", registry.Entries["a"].Manifest.Source);
        Assert.Equal(version, registry.DeclarationVersion);
        Assert.Equal(stagesAfterFirstApply, Volatile.Read(ref stages));
        Assert.Single(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task CommitStagePartialActivationFailureRollsBackEveryNewFiberEffectAndProvider()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var key = new Key<string>("commit-rollback-capability");
        var journal = new List<string>();
        var firstDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        registry.RegisterComponentFactory("old", manifest =>
        {
            Assert.Equal("v1", manifest.Source);
            return new ProviderComponent("old", "old", key, journal, Task.CompletedTask);
        });
        var stages = new Dictionary<string, int>(StringComparer.Ordinal);
        registry.RegisterComponentFactory("new", manifest =>
        {
            stages[manifest.Id] = stages.GetValueOrDefault(manifest.Id) + 1;
            var step = manifest.Id switch
            {
                "first" => new OrderedComponent("first", "first", journal,
                    Task.CompletedTask, firstDone, failAfterSignal: false),
                "second" => new OrderedComponent("second", "second", journal,
                    firstDone.Task, secondFailed, failAfterSignal: true),
                "third" => new OrderedComponent("third", "third", journal,
                    secondFailed.Task, null, failAfterSignal: false),
                _ => throw new InvalidOperationException($"unexpected id '{manifest.Id}'"),
            };
            return step;
        });

        var previous = Manifest("old", "old", "v1");
        await registry.ApplyAsync([previous]);
        var oldFiber = registry.Entries["old"].Fiber!;
        Assert.Equal("old", runtime.RequireCapability(key));
        Assert.Equal(["install:old"], journal);

        var desired = new[] { "first", "second", "third" }
            .Select(id => Manifest(id, "new", "v2"))
            .ToArray();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ApplyAsync(desired));

        // All three components were staged (no factory failure) and the failure
        // came from ApplyAsync inside Core's commit, after the first fiber had
        // activated and before the third was undone.
        Assert.All(new[] { "first", "second", "third" }, id => Assert.Equal(1, stages[id]));
        Assert.Equal("plugin 'second' failed to activate", error.Message);
        Assert.Equal("commit boom", error.InnerException?.Message);

        var restored = Assert.Single(registry.Entries.Values);
        Assert.Equal("old", restored.Manifest.Id);
        Assert.Equal("v1", restored.Manifest.Source);
        Assert.Equal(FiberState.Active, restored.Fiber!.State);
        // Revision is retire→reinsert, never identity-preserving.
        Assert.NotSame(oldFiber, restored.Fiber);

        // No orphan fiber: the only live fiber is the restored declaration.
        var fibers = runtime.Snapshot().Fibers;
        Assert.Single(fibers);
        Assert.Contains(restored.Fiber.Id, fibers.Select(fiber => fiber.Id));
        Assert.All(fibers, fiber => Assert.Contains(
            fiber.Id, registry.Entries.Values.Select(entry => entry.Fiber!.Id)));

        // No duplicate or leaked provider: the old capability has exactly one
        // provider, and none of the three rejected declarations still provides.
        Assert.Equal("old", runtime.RequireCapability(key));
        Assert.Single(fibers, fiber => fiber.Provides.Contains(key.Name, StringComparer.Ordinal));
        foreach (var id in new[] { "first", "second", "third" })
        {
            Assert.DoesNotContain(id, registry.Entries.Keys);
            Assert.DoesNotContain(fibers, fiber => fiber.Name == id);
            Assert.Throws<DependencyMissingException>(
                () => runtime.RequireCapability(new Key<string>($"commit-rollback-{id}")));
        }

        // Every effect installed by the batch (including the retired original)
        // was inverted; the final visible state is the reinserted original.
        Assert.Contains("undo:old", journal);
        Assert.Contains("undo:first", journal);
        Assert.Contains("undo:third", journal);
        Assert.Equal(2, journal.Count(entry => entry == "install:old"));
        Assert.Equal("install:old", journal[^1]);
    }

    private static PluginManifest Manifest(string id, string type, string source) =>
        new(id, type, true, new Dictionary<string, object?>(), [], [], Source: source);

    private sealed class Passive : Component
    {
        public Passive(string name) => Name = name;
        public override string Name { get; }
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class ProviderComponent : Component
    {
        private readonly string _value;
        private readonly Key<string> _key;
        private readonly List<string> _journal;
        private readonly Task _waitFor;

        public ProviderComponent(
            string name, string value, Key<string> key, List<string> journal, Task waitFor)
        {
            Name = name;
            _value = value;
            _key = key;
            _journal = journal;
            _waitFor = waitFor;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _waitFor;
            context.Provide(_key, _value);
            _journal.Add($"install:{_value}");
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () => { _journal.Add($"undo:{_value}"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    /// <summary>Orders commit-time ApplyAsync calls without sleeping: the second
    /// entry cannot fail until the first has installed its effect, and the third
    /// cannot activate until the second has failed.</summary>
    private sealed class OrderedComponent : Component
    {
        private readonly string _value;
        private readonly Key<string> _key;
        private readonly List<string> _journal;
        private readonly Task _waitFor;
        private readonly TaskCompletionSource? _signal;
        private readonly bool _failAfterSignal;

        public OrderedComponent(
            string name, string value, List<string> journal,
            Task waitFor, TaskCompletionSource? signal, bool failAfterSignal)
        {
            Name = name;
            _value = value;
            // Distinct keys: Core and Prepare must never see a duplicate provider
            // while the three replacement fibers are being activated.
            _key = new Key<string>($"commit-rollback-{value}");
            _journal = journal;
            _waitFor = waitFor;
            _signal = signal;
            _failAfterSignal = failAfterSignal;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _waitFor;
            if (_failAfterSignal)
            {
                _signal!.TrySetResult();
                throw new InvalidOperationException("commit boom");
            }

            context.Provide(_key, _value);
            _journal.Add($"install:{_value}");
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () => { _journal.Add($"undo:{_value}"); return ValueTask.CompletedTask; });
            _signal?.TrySetResult();
            return null;
        }
    }
}
