using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Paper §1.3: the five contributions are checked in ONE end-to-end behavior
/// loop — revertible effects, reactive coeffects, unified Context, single Fiber
/// calculus, and the Cordis declaration/registry implementation. This is a
/// behavioral anchor, not a claim that C01–C40 alone prove §1.3.
/// </summary>
public sealed class PaperContributionsTests
{
    [Fact]
    public async Task FiveContributionsCloseOneRuntimeLoop()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var journal = new List<string>();
        var value = new Key<string>("contributions/value");
        var meta = new MetaKey<string, string>(
            "contributions.meta", string.Empty, static (current, incoming) =>
                string.IsNullOrEmpty(incoming) ? current : incoming);
        var message = new EventKey<string>("contributions/message");

        registry.RegisterComponentFactory("provider", _ => new Provider(value, message, meta, journal));
        registry.RegisterComponentFactory("listener", _ => new Listener(message, journal));
        registry.RegisterComponentFactory("consumer", _ => new Consumer(value, journal));
        var provider = Manifest("provider", "provider");
        var listener = Manifest("listener", "listener");
        var consumer = Manifest("consumer", "consumer");

        // Cordis implementation: one declaration set, one registry apply, one Runtime.
        await registry.ApplyAsync([provider, listener, consumer]);
        Assert.All(registry.Entries.Values, entry => Assert.Equal(FiberState.Active, entry.Fiber!.State));

        // Reactive coeffects: consumer bound to the provider before Apply returned.
        Assert.Contains("require:payload", journal);
        // Unified Context: meta override + event registration + child fiber + effect install.
        Assert.Contains("meta:override", journal);
        Assert.Contains("event:ping", journal);
        Assert.Contains("install:provider", journal);
        Assert.Contains("child:active", journal);
        // Revertible effects + calculus: withdrawing the provider drains the
        // consumer first, then unwinds the provider's effect in LIFO order.
        var oldProviderFiber = registry.Entries["provider"].Fiber!;
        var consumerFiber = registry.Entries["consumer"].Fiber!;
        await registry.ApplyAsync([provider with { Enabled = false }, listener, consumer]);
        // Awaiting the retired fiber's terminal state is the deterministic
        // quiescence point — no sleep or state polling.
        await oldProviderFiber.GoneAsync();
        // Wait for the consumer's observable Pending state directly, published
        // with the snapshot after the reactive withdrawal completes.
        try
        {
            await runtime.WaitForFiberStateAsync(consumerFiber.Id, FiberState.Pending)
                .WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            var snapshot = runtime.Snapshot();
            var state = snapshot.Fibers.FirstOrDefault(f => f.Id == consumerFiber.Id);
            throw new TimeoutException(
                $"consumer did not reach Pending: state={state?.State.ToString() ?? "missing"}, " +
                $"failure={state?.Failure ?? "none"}, fibers={snapshot.Fibers.Count}, " +
                $"cleanup={snapshot.CleanupFailures.Count}, effects={snapshot.EffectFailures.Count}");
        }
        Assert.Equal(FiberState.Pending, registry.Entries["consumer"].Fiber!.State);
        Assert.Null(registry.Entries["consumer"].Fiber!.Failure);
        Assert.Contains("undo:provider", journal);
        Assert.Contains("child:undo", journal);
        Assert.True(journal.FindIndex(item => item == "child:undo") <
                    journal.FindIndex(item => item == "undo:provider"));
    }

    private static PluginManifest Manifest(string id, string type) =>
        new(id, type, true, new Dictionary<string, object?>(), [], []);

    private sealed class Provider : Component
    {
        private readonly Key<string> _value;
        private readonly EventKey<string> _message;
        private readonly MetaKey<string, string> _meta;
        private readonly List<string> _journal;

        public Provider(
            Key<string> value, EventKey<string> message, MetaKey<string, string> meta, List<string> journal)
        {
            _value = value;
            _message = message;
            _meta = meta;
            _journal = journal;
        }

        public override string Name => "contributions-provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_value, _message];
        public override IReadOnlyList<ICapabilityKey> Inject() => [_meta];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_value, "payload");
            context.Provide(_message, "ready");
            context.DeclareMeta(_meta, "declared");
            context.OverrideMeta(_meta, "override");
            _journal.Add($"meta:{context.Meta(_meta)}");
            await context.EffectAsync(
                () => { _journal.Add("install:provider"); return ValueTask.CompletedTask; },
                () => { _journal.Add("undo:provider"); return ValueTask.CompletedTask; });
            await context.LoadChildAsync(new Child(_journal));
            return null;
        }
    }

    private sealed class Listener : Component
    {
        private readonly EventKey<string> _message;
        private readonly List<string> _journal;
        public Listener(EventKey<string> message, List<string> journal) => (_message, _journal) = (message, journal);
        public override string Name => "contributions-listener";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_message];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.OnAsync(_message, payload =>
            {
                _journal.Add($"event:{payload}");
                return ValueTask.CompletedTask;
            });
            await context.EmitAsync(_message, "ping");
            return null;
        }
    }

    private sealed class Child : Component
    {
        private readonly List<string> _journal;
        public Child(List<string> journal) => _journal = journal;
        public override string Name => "contributions-child";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(
                () => { _journal.Add("child:active"); return ValueTask.CompletedTask; },
                () => { _journal.Add("child:undo"); return ValueTask.CompletedTask; });
            return null;
        }
    }

    private sealed class Consumer : Component
    {
        private readonly Key<string> _value;
        private readonly List<string> _journal;
        public Consumer(Key<string> value, List<string> journal) => (_value, _journal) = (value, journal);
        public override string Name => "contributions-consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_value];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _journal.Add($"require:{context.Require(_value)}");
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
