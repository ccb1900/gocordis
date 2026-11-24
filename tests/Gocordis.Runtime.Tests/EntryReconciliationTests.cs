using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Paper §5.2.1 field-level dispatch: an Entry change selects the least
/// disruptive Core operation and never bypasses the single lifecycle authority.
/// </summary>
public sealed class EntryReconciliationTests
{
    [Fact]
    public async Task InterceptOnlyChangeUpdatesMetaWithoutReplacingTheFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applies = 0;
        registry.RegisterComponentFactory("probe", _ => new CountingComponent("probe", () => applies++));
        var before = Manifest("a", "probe", intercept: "before");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["a"].Fiber!;
        var generation = fiber.Generation;

        var after = before with { Intercept = "after" };
        await registry.ApplyAsync([after]);

        Assert.Same(fiber, registry.Entries["a"].Fiber);
        Assert.Equal(generation, fiber.Generation);
        Assert.Equal(1, applies);
        Assert.Equal("after", runtime.GetMeta(fiber, EntryMetaKeys.Intercept));
        Assert.Equal(EntryChangeKind.Intercept, EntryDiff.Classify(before, after));
    }

    [Fact]
    public async Task IsolateOnlyChangeRehomesProviderAndKeepsConsumerBound()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var key = new Key<string>("entry-reconcile/value");
        registry.ExternalCapabilities.Add(key);
        registry.RegisterFactory("probe", _ => new Provider("provider", key, "same-value"));
        var consumerComponent = new Consumer("consumer", key);
        registry.RegisterComponentFactory("consumer", _ => consumerComponent);
        var realms = new Dictionary<string, Realm>(StringComparer.Ordinal);
        registry.RealmFor = (isolate, id) =>
        {
            if (isolate is null)
            {
                return Realm.Default;
            }

            return realms.TryGetValue(isolate, out var realm)
                ? realm
                : realms[isolate] = new Realm(isolate);
        };

        await registry.ApplyAsync([
            Manifest("provider", "probe", provides: [key.Name]),
            Manifest("consumer", "consumer", requires: [key.Name]),
        ]);
        var oldFiber = registry.Entries["provider"].Fiber!;
        var consumerFiber = registry.Entries["consumer"].Fiber!;
        Assert.Equal(FiberState.Active, consumerFiber.State);

        var rehomedProvider = registry.Entries["provider"].Manifest with { Isolate = "shared-a" };
        var rehomedConsumer = registry.Entries["consumer"].Manifest with { Isolate = "shared-a" };
        await registry.ApplyAsync([rehomedConsumer, rehomedProvider]);

        var nextFiber = registry.Entries["provider"].Fiber!;
        Assert.NotSame(oldFiber, nextFiber);
        Assert.Equal(FiberState.Active, registry.Entries["consumer"].Fiber!.State);
        Assert.Equal("same-value", consumerComponent.LastValue);
        Assert.Equal(EntryChangeKind.Rehome, EntryDiff.Classify(
            registry.Entries["provider"].Manifest with { Isolate = null }, rehomedProvider));
    }

    [Fact]
    public async Task ConfigChangeAcceptedByComponentKeepsFiberAndDoesNotTouchUnrelatedEntry()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applied = "";
        registry.RegisterFactory("configurable", _ => new Configurable("configurable", value => applied = value));
        registry.RegisterFactory("other", _ => new CountingComponent("other", static () => { }));

        await registry.ApplyAsync([
            Manifest("configurable", "configurable", configValue: "one"),
            Manifest("other", "other"),
        ]);
        var fiber = registry.Entries["configurable"].Fiber!;
        var unrelated = registry.Entries["other"].Fiber!;

        await registry.ApplyAsync([
            registry.Entries["configurable"].Manifest with
            {
                Config = new Dictionary<string, object?> { ["value"] = "two" },
            },
            registry.Entries["other"].Manifest,
        ]);

        Assert.Same(fiber, registry.Entries["configurable"].Fiber);
        Assert.Same(unrelated, registry.Entries["other"].Fiber);
        Assert.Equal("two", applied);
    }

    [Fact]
    public async Task InterceptIsolateAndUrlChangesLeaveUnrelatedEntriesAlone()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applies = new Dictionary<string, int>(StringComparer.Ordinal);
        registry.RegisterComponentFactory("probe", manifest =>
            new CountingComponent(manifest.Id, () => applies[manifest.Id] = applies.GetValueOrDefault(manifest.Id) + 1));

        var a = Manifest("a", "probe", intercept: "before");
        var b = Manifest("b", "probe");
        await registry.ApplyAsync([a, b]);
        var aFiber = registry.Entries["a"].Fiber!;
        var bFiber = registry.Entries["b"].Fiber!;

        // intercept → in-place Meta update; neither entry is reloaded.
        await registry.ApplyAsync([a with { Intercept = "after" }, b]);
        Assert.Same(aFiber, registry.Entries["a"].Fiber);
        Assert.Same(bFiber, registry.Entries["b"].Fiber);

        // isolate → Rehome only the annotated entry.
        var rehomed = a with { Isolate = "shared-a" };
        await registry.ApplyAsync([rehomed, b]);
        var rehomedFiber = registry.Entries["a"].Fiber!;
        Assert.NotSame(aFiber, rehomedFiber);
        Assert.Same(bFiber, registry.Entries["b"].Fiber);
        Assert.Equal(2, applies["a"]);

        // url/source → rebuild only the annotated entry.
        await registry.ApplyAsync([rehomed with { Source = "payload-v2" }, b]);
        Assert.NotSame(rehomedFiber, registry.Entries["a"].Fiber);
        Assert.Same(bFiber, registry.Entries["b"].Fiber);
        Assert.Equal(3, applies["a"]);
    }

    [Fact]
    public async Task IncrementalReconciliationReachesTheSameEndpointAsAFreshLoad()
    {
        await using var incrementalRuntime = new Gocordis.Core.Runtime();
        await using var incremental = new PluginRegistry(incrementalRuntime);
        await using var freshRuntime = new Gocordis.Core.Runtime();
        await using var fresh = new PluginRegistry(freshRuntime);
        foreach (var registry in new[] { incremental, fresh })
        {
            registry.RegisterComponentFactory("probe", manifest => new CountingComponent(manifest.Id, static () => { }));
        }

        var first = Manifest("a", "probe");
        var second = Manifest("b", "probe");
        var final = new[] { first with { Intercept = "final" }, second };

        await incremental.ApplyAsync([first]);
        await incremental.ApplyAsync([first, second]);
        await incremental.ApplyAsync(final);
        await fresh.ApplyAsync(final);

        Assert.Equal(
            fresh.Entries.Values.Select(entry => (entry.Manifest.Id, entry.Fiber?.State)).OrderBy(item => item.Id),
            incremental.Entries.Values.Select(entry => (entry.Manifest.Id, entry.Fiber?.State)).OrderBy(item => item.Id));
        Assert.Equal(
            freshRuntime.Snapshot().Fibers.Select(fiber => (fiber.Name, fiber.State)).OrderBy(item => item.Name),
            incrementalRuntime.Snapshot().Fibers.Select(fiber => (fiber.Name, fiber.State)).OrderBy(item => item.Name));
    }

    [Fact]
    public async Task DisabledProviderDoesNotBindADifferentKeyInstanceWithTheSameName()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var providerKey = new Key<string>("shadow/value");
        var consumerKey = new Key<string>("shadow/value");
        var consumer = new Consumer("consumer", consumerKey);
        // No ExternalCapabilities registration: the ONLY thing that lets this
        // cold-disabled declaration through Prepare is the disabled provider's
        // package-level "provides" name. Core still gates by key identity.
        registry.RegisterComponentFactory("provider", _ => new Provider("provider", providerKey, "provider"));
        registry.RegisterComponentFactory("consumer", _ => consumer);

        var provider = Manifest("provider", "provider", provides: [providerKey.Name]) with
        {
            Enabled = false,
        };
        var consumerManifest = Manifest("consumer", "consumer", requires: [consumerKey.Name]);
        await registry.ApplyAsync([provider, consumerManifest]);

        Assert.Equal(FiberState.Pending, registry.Entries["consumer"].Fiber!.State);
        Assert.Null(consumer.LastValue);
    }

    private static PluginManifest Manifest(
        string id,
        string type,
        string? intercept = null,
        string? isolate = null,
        IReadOnlyList<string>? requires = null,
        IReadOnlyList<string>? provides = null,
        string? configValue = null) =>
        new(id, type, true,
            configValue is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["value"] = configValue },
            requires ?? [], provides ?? [],
            Url: $"{type}@1.0.0", Isolate: isolate, Intercept: intercept);

    private sealed class CountingComponent : Component
    {
        private readonly Action _onApply;

        public CountingComponent(string name, Action onApply)
        {
            Name = name;
            _onApply = onApply;
        }

        public override string Name { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _onApply();
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Provider : Component
    {
        private readonly Key<string> _key;
        private readonly string _value;

        public Provider(string name, Key<string> key, string value)
        {
            Name = name;
            _key = key;
            _value = value;
        }

        public override string Name { get; }
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _value);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Consumer : Component
    {
        private readonly Key<string> _key;

        public Consumer(string name, Key<string> key)
        {
            Name = name;
            _key = key;
        }

        public override string Name { get; }
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public string? LastValue { get; private set; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            LastValue = context.Require(_key);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Configurable : Component, IConfigurableComponent
    {
        private readonly Action<string> _record;

        public Configurable(string name, Action<string> record)
        {
            Name = name;
            _record = record;
        }

        public override string Name { get; }

        public ValueTask<bool> TryApplyConfigAsync(
            IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default)
        {
            _record(config["value"]?.ToString() ?? string.Empty);
            return ValueTask.FromResult(true);
        }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }
}
