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
        var delta = EntryDiff.Classify(before, after);
        Assert.True(delta.InterceptChanged);
        Assert.False(delta.Rebuild || delta.Rehome || delta.ConfigChanged || delta.Disabled);
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
        var delta = EntryDiff.Classify(
            registry.Entries["provider"].Manifest with { Isolate = null }, rehomedProvider);
        Assert.True(delta.Rehome);
        Assert.False(delta.Rebuild || delta.Disabled);
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
        // url/source → rebuild only the annotated entry. Apply counts are not
        // asserted: Core may reapply the same component during a rehome, which
        // is a lifecycle detail, not the "unrelated entry untouched" contract.
        await registry.ApplyAsync([rehomed with { Source = "payload-v2" }, b]);
        Assert.NotSame(rehomedFiber, registry.Entries["a"].Fiber);
        Assert.Same(bFiber, registry.Entries["b"].Fiber);
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

    // ---- P1 regression: every changed Entry field must be honoured ---------

    [Fact]
    public void EntryDeltaReportsEveryChangedFieldInsteadOfOnePriorityLabel()
    {
        var current = Manifest("p", "probe", intercept: "before", configValue: "one");

        var configAndDisabled = EntryDiff.Classify(current, current with
        {
            Enabled = false,
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        });
        Assert.True(configAndDisabled.ConfigChanged);
        Assert.True(configAndDisabled.EnabledChanged);
        Assert.True(configAndDisabled.Disabled);
        Assert.False(configAndDisabled.Rebuild);
        Assert.False(configAndDisabled.HasMetaChange);

        var rehomeAndConfig = EntryDiff.Classify(current, current with
        {
            Isolate = "realm-a",
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        });
        Assert.True(rehomeAndConfig.Rehome);
        Assert.True(rehomeAndConfig.ConfigChanged);

        var interceptAndConfig = EntryDiff.Classify(current, current with
        {
            Intercept = "after",
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        });
        Assert.True(interceptAndConfig.InterceptChanged);
        Assert.True(interceptAndConfig.ConfigChanged);

        var interceptAndDisabled = EntryDiff.Classify(current, current with
        {
            Intercept = "after",
            Enabled = false,
        });
        Assert.True(interceptAndDisabled.InterceptChanged);
        Assert.True(interceptAndDisabled.Disabled);

        var rehomeAndDisabled = EntryDiff.Classify(current, current with
        {
            Isolate = "realm-a",
            Enabled = false,
        });
        Assert.True(rehomeAndDisabled.Rehome);
        Assert.True(rehomeAndDisabled.Disabled);
    }

    [Fact]
    public void JsonElementConfigIsComparedByValueAcrossManifestReloads()
    {
        // ApplicationManifest deserializes config values as JsonElement. Two
        // reads of the same declaration produce different JsonDocument owners,
        // so reference-identity equality forced a needless managed ALC rebuild.
        using var first = System.Text.Json.JsonDocument.Parse(
            """{"intervalMs":1500,"nested":{"b":2,"a":1}}""");
        using var second = System.Text.Json.JsonDocument.Parse(
            """{"intervalMs":1500,"nested":{"a":1,"b":2}}""");
        var before = Manifest("p", "probe") with
        {
            Config = new Dictionary<string, object?> { ["cfg"] = first.RootElement },
        };
        var after = Manifest("p", "probe") with
        {
            Config = new Dictionary<string, object?> { ["cfg"] = second.RootElement },
        };

        var delta = EntryDiff.Classify(before, after);
        Assert.False(delta.ConfigChanged);
        Assert.True(delta.IsEmpty);
    }

    [Fact]
    public async Task ConfigAndDisabledRetiresTheFiberInsteadOfApplyingConfigInPlace()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applied = new List<string>();
        registry.RegisterFactory("configurable", _ => new Configurable("configurable", value => applied.Add(value)));
        var before = Manifest("c", "configurable", configValue: "one");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["c"].Fiber!;

        await registry.ApplyAsync([before with
        {
            Enabled = false,
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        }]);

        Assert.Empty(applied);
        Assert.Null(registry.Entries["c"].Fiber);
        await fiber.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Gone, fiber.State);
        Assert.DoesNotContain(runtime.Snapshot().Fibers, item => item.Name == "configurable");
    }

    [Fact]
    public async Task RehomeAndConfigReinsertsOnceWithTheNewlyDeclaredConfig()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var realms = new Dictionary<string, Realm>(StringComparer.Ordinal);
        registry.RealmFor = (isolate, _) => isolate is null
            ? Realm.Default
            : realms.TryGetValue(isolate, out var realm) ? realm : realms[isolate] = new Realm(isolate);
        var constructed = new List<string>();
        registry.RegisterComponentFactory("rehomed", manifest =>
        {
            constructed.Add((string)manifest.Config["value"]!);
            return new CountingComponent("rehomed", static () => { });
        });
        var before = Manifest("p", "rehomed", configValue: "one");

        await registry.ApplyAsync([before]);
        var oldFiber = registry.Entries["p"].Fiber!;

        await registry.ApplyAsync([before with
        {
            Isolate = "realm-a",
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        }]);

        var nextFiber = registry.Entries["p"].Fiber!;
        Assert.NotSame(oldFiber, nextFiber);
        await oldFiber.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["one", "two"], constructed);
        Assert.Equal(FiberState.Active, nextFiber.State);
    }

    [Fact]
    public async Task InterceptAndConfigAreAppliedTogetherWithoutReplacingTheFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applied = new List<string>();
        registry.RegisterFactory("configurable", _ => new Configurable("configurable", value => applied.Add(value)));
        var before = Manifest("c", "configurable", intercept: "before", configValue: "one");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["c"].Fiber!;

        await registry.ApplyAsync([before with
        {
            Intercept = "after",
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        }]);

        Assert.Same(fiber, registry.Entries["c"].Fiber);
        Assert.Equal("after", runtime.GetMeta(fiber, EntryMetaKeys.Intercept));
        Assert.Equal(["two"], applied);
    }

    [Fact]
    public async Task RefusedInPlaceConfigFallsBackToOneRebuildAndKeepsTheMetaEdit()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var constructed = new List<string>();
        registry.RegisterComponentFactory("refusing", manifest =>
        {
            var value = (string)manifest.Config["value"]!;
            constructed.Add(value);
            return new RefusingConfigurable("refusing", value);
        });
        var before = Manifest("c", "refusing", intercept: "before", configValue: "one");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["c"].Fiber!;

        await registry.ApplyAsync([before with
        {
            Intercept = "after",
            Config = new Dictionary<string, object?> { ["value"] = "two" },
        }]);

        var replacement = registry.Entries["c"].Fiber!;
        Assert.NotSame(fiber, replacement);
        Assert.Equal(["one", "two"], constructed);
        Assert.Equal("after", runtime.GetMeta(replacement, EntryMetaKeys.Intercept));
        Assert.Equal(FiberState.Active, replacement.State);
    }

    [Fact]
    public async Task InterceptAndDisabledRetiresTheFiberWithoutInstallingStaleMeta()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("probe", _ => new CountingComponent("probe", static () => { }));
        var before = Manifest("c", "probe", intercept: "before");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["c"].Fiber!;

        await registry.ApplyAsync([before with { Intercept = "after", Enabled = false }]);

        Assert.Null(registry.Entries["c"].Fiber);
        Assert.Equal("after", registry.Entries["c"].Manifest.Intercept);
        await fiber.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Gone, fiber.State);
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task RehomeAndDisabledRetiresTheFiberWithoutRehomingIt()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var rehomes = 0;
        registry.RealmFor = (isolate, _) => isolate is null ? Realm.Default : new Realm(isolate);
        registry.RegisterComponentFactory("probe", _ =>
        {
            rehomes++;
            return new CountingComponent("probe", static () => { });
        });
        var before = Manifest("c", "probe");

        await registry.ApplyAsync([before]);
        var fiber = registry.Entries["c"].Fiber!;

        await registry.ApplyAsync([before with { Isolate = "realm-a", Enabled = false }]);

        Assert.Null(registry.Entries["c"].Fiber);
        Assert.Equal("realm-a", registry.Entries["c"].Manifest.Isolate);
        await fiber.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(runtime.Snapshot().Fibers);
        Assert.Equal(1, rehomes);
    }

    // ---- P1 regression: transaction boundary of in-place edits -------------

    [Fact]
    public async Task FailureDuringInPlaceConfigRollsBackAlreadyAppliedEdits()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("flaky", _ => new FlakyConfigurable("one"));
        registry.RegisterComponentFactory("probe", manifest => new CountingComponent(manifest.Id, static () => { }));
        var a = Manifest("a", "flaky", configValue: "one");
        var b = Manifest("b", "probe", intercept: "before");

        await registry.ApplyAsync([a, b]);
        // Install a meta override first so rollback has a non-empty value to
        // restore; the failing batch then edits config and meta mid-flight.
        await registry.ApplyAsync([a, b with { Intercept = "kept" }]);

        await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([
            b with { Intercept = "after" },
            a with { Config = new Dictionary<string, object?> { ["value"] = "two" } },
        ]));

        Assert.Equal("one", registry.Entries["a"].Manifest.Config["value"]);
        Assert.Equal("one", ((FlakyConfigurable)registry.Entries["a"].Component!).Current);
        Assert.Equal("kept", registry.Entries["b"].Manifest.Intercept);
        Assert.Equal("kept", runtime.GetMeta(registry.Entries["b"].Fiber!, EntryMetaKeys.Intercept));
        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
        Assert.Equal(FiberState.Active, registry.Entries["b"].Fiber!.State);
        Assert.Single(runtime.Snapshot().Fibers, fiber => fiber.Name == "flaky");
    }

    [Fact]
    public async Task CommitFailureAfterInPlaceProviderRestoresCapabilityWithoutDuplicateOrOrphan()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var key = new Key<string>("registry-transaction/shared");
        registry.RegisterComponentFactory("provider", manifest =>
            new JournalProvider(key, (string)manifest.Config["value"]!));
        registry.RegisterFactory("boom", _ => new Boom());
        registry.RegisterComponentFactory("consumer", _ => new Consumer("consumer", key));
        var provider = Manifest("p", "provider", configValue: "one");
        var consumer = Manifest("c", "consumer");

        await registry.ApplyAsync([provider, consumer]);
        var consumerFiber = registry.Entries["c"].Fiber!;

        await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([
            provider with { Config = new Dictionary<string, object?> { ["value"] = "two" } },
            Manifest("bad", "boom"),
            consumer,
        ]));

        Assert.Equal("one", runtime.RequireCapability(key));
        Assert.Equal("one", registry.Entries["p"].Manifest.Config["value"]);
        Assert.Equal(FiberState.Active, registry.Entries["p"].Fiber!.State);
        Assert.Same(consumerFiber, registry.Entries["c"].Fiber);
        Assert.Single(runtime.Snapshot().Fibers, fiber => fiber.Name == "provider");
        Assert.DoesNotContain(runtime.Snapshot().Fibers, fiber => fiber.Name == "boom");
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

    /// <summary>Refuses in-place config: the coordinator must fall back to a
    /// single rebuild of that entry, keeping its other field-level edits.</summary>
    private sealed class RefusingConfigurable : Component, IConfigurableComponent
    {
        public RefusingConfigurable(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public override string Name { get; }
        public string Value { get; }
        public override IReadOnlyList<ICapabilityKey> Inject() => [];

        public ValueTask<bool> TryApplyConfigAsync(
            IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    /// <summary>Mutates its own state, then fails: a half-applied in-place edit
    /// must be rolled back by the registry transaction.</summary>
    private sealed class FlakyConfigurable : Component, IConfigurableComponent
    {
        public FlakyConfigurable(string current) => Current = current;

        public override string Name => "flaky";
        public string Current { get; private set; }

        public ValueTask<bool> TryApplyConfigAsync(
            IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default)
        {
            Current = config["value"]?.ToString() ?? string.Empty;
            if (Current == "two")
            {
                throw new InvalidOperationException("in-place config boom");
            }

            return ValueTask.FromResult(true);
        }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class JournalProvider : Component, IConfigurableComponent
    {
        private readonly Key<string> _key;

        public JournalProvider(Key<string> key, string value)
        {
            _key = key;
            Current = value;
        }

        public override string Name => "provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];
        public string Current { get; private set; }

        public ValueTask<bool> TryApplyConfigAsync(
            IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default)
        {
            Current = config["value"]?.ToString() ?? string.Empty;
            return ValueTask.FromResult(true);
        }

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, Current);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Boom : Component
    {
        public override string Name => "boom";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new InvalidOperationException("commit boom");
    }
}
