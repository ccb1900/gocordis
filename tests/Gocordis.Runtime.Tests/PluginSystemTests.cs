using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class PluginSystemTests
{
    [Fact]
    public void TomlManifestInheritsTheGoDeclarationShape()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            type = "echo"
            enabled = true
            requires = ["base"]
            provides = ["alpha-out"]
            [components.config]
            value = "hello"
            n = 3
            """;

        var manifest = Assert.Single(PluginManifest.ParseToml(toml));

        Assert.Equal("alpha", manifest.Id);
        Assert.Equal("echo", manifest.Type);
        Assert.True(manifest.Enabled);
        Assert.Equal("hello", manifest.Config["value"]);
        Assert.Equal(3L, manifest.Config["n"]);
        Assert.Equal(new[] { "base" }, manifest.Requires);
        Assert.Equal(new[] { "alpha-out" }, manifest.Provides);
    }

    [Fact]
    public async Task EnabledDeclarationsLoadAndDisabledOnesStayDeclarations()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        await registry.ApplyAsync([
            Manifest("on", "echo", true, "A"),
            Manifest("off", "echo", false, "B"),
        ]);

        Assert.Equal(2, registry.Entries.Count);
        Assert.Equal(FiberState.Active, registry.Entries["on"].Fiber!.State);
        Assert.Null(registry.Entries["off"].Fiber);
    }

    [Fact]
    public async Task EnabledSwitchLoadsAndDisposesThroughCoreOnly()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        await registry.ApplyAsync([Manifest("p", "echo", false, "A")]);
        Assert.Null(registry.Entries["p"].Fiber);

        await registry.ApplyAsync([Manifest("p", "echo", true, "A")]);
        var loaded = registry.Entries["p"].Fiber!;
        Assert.Equal(FiberState.Active, loaded.State);

        await registry.ApplyAsync([Manifest("p", "echo", false, "A")]);
        Assert.Equal(FiberState.Gone, loaded.State);
        Assert.Null(registry.Entries["p"].Fiber);
    }

    [Fact]
    public async Task ConfigChangeReplacesTheFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        await registry.ApplyAsync([Manifest("p", "echo", true, "A")]);
        var oldFiber = registry.Entries["p"].Fiber!;

        await registry.ApplyAsync([Manifest("p", "echo", true, "B")]);
        var newFiber = registry.Entries["p"].Fiber!;

        Assert.NotSame(oldFiber, newFiber);
        Assert.Equal(FiberState.Gone, oldFiber.State);
        Assert.Equal(FiberState.Active, newFiber.State);
    }

    [Fact]
    public async Task DependencyOrderComesFromTypedKeysNotManifestStrings()
    {
        var shared = new Key<int>("typed-base");
        var order = new List<string>();
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("base", _ =>
        {
            order.Add("base");
            return new KeyedComponent("base", provide: [shared]);
        });
        registry.RegisterFactory("dep", _ =>
        {
            order.Add("dep");
            return new KeyedComponent("dep", inject: [shared]);
        });

        // Manifest strings deliberately disagree with the real typed graph.
        var baseManifest = Manifest("base", "base", true, "base") with { Provides = ["unrelated"] };
        var dependent = Manifest("dep", "dep", true, "dep") with { Requires = ["also-unrelated"] };

        await registry.ApplyAsync([dependent, baseManifest]);

        var dep = runtime.Snapshot().Fibers.Single(f => f.Name == "dep");
        var baseFiber = runtime.Snapshot().Fibers.Single(f => f.Name == "base");
        Assert.Equal(FiberState.Active, dep.State);
        Assert.Contains($"typed-base->{baseFiber.Id}", dep.BoundProviders);
    }

    [Fact]
    public async Task CapabilityDeclarationChangeReplacesTheFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        var first = Manifest("p", "echo", true, "A") with
        {
            Capabilities = ["provide|org.example/one|endpoint|1.0.0||"],
        };
        await registry.ApplyAsync([first]);
        var oldFiber = registry.Entries["p"].Fiber!;

        var second = first with
        {
            Capabilities = ["provide|org.example/two|endpoint|1.0.0||"],
        };
        await registry.ApplyAsync([second]);
        var newFiber = registry.Entries["p"].Fiber!;

        Assert.NotSame(oldFiber, newFiber);
        Assert.Equal(FiberState.Gone, oldFiber.State);
        Assert.Equal(FiberState.Active, newFiber.State);
    }

    [Fact]
    public async Task UnresolvedTypedCapabilityIsRejectedEvenWhenManifestClaimsItExists()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("dep", _ => new KeyedComponent("dep", inject: [new Key<int>("missing")]));

        var manifest = Manifest("dep", "dep", true, "dep") with { Requires = ["fake-provider"] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyAsync([manifest]));
    }

    [Fact]
    public async Task UnknownFactoryAndTypedDependencyCyclesAreRejected()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.ApplyAsync([Manifest("x", "missing", true, "x")]));

        var aKey = new Key<int>("a");
        var bKey = new Key<int>("b");
        registry.RegisterFactory("a", _ => new KeyedComponent("a", inject: [bKey], provide: [aKey]));
        registry.RegisterFactory("b", _ => new KeyedComponent("b", inject: [aKey], provide: [bKey]));
        await Assert.ThrowsAsync<DependencyCycleException>(() => registry.ApplyAsync([
            Manifest("a", "a", true, "a"),
            Manifest("b", "b", true, "b"),
        ]));
    }

    [Fact]
    public async Task VersionedDependencyWithoutProviderNeedsExternalDeclaration()
    {
        var key = new VersionedKey<string>("storage", "1.0.0", "[1.0,2.0)");
        var manifest = new PluginManifest(
            "consumer", "versioned", true,
            new Dictionary<string, object?>(), [], []);

        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("versioned", _ => new VersionedConsumer(key));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ApplyAsync([manifest]));
        Assert.Contains("versioned capability 'storage'", error.Message);

        // Declared external: same versioned dependency is now legal.
        registry.ExternalCapabilities.Add(key);
        await registry.ApplyAsync([manifest]);
        // External declaration is a validation exemption, not a fake provider:
        // the fiber remains Pending until a real compatible provider appears.
        Assert.Equal(FiberState.Pending, registry.Entries["consumer"].Fiber!.State);
    }

    private sealed class VersionedConsumer : Component
    {
        private readonly IVersionedKey _key;
        public VersionedConsumer(IVersionedKey key) => _key = key;
        public override string Name => "versioned-consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class KeyedComponent : Component
    {
        private readonly IReadOnlyList<ICapabilityKey> _inject;
        private readonly IReadOnlyList<ICapabilityKey> _provide;

        public KeyedComponent(string name, IReadOnlyList<ICapabilityKey>? inject = null, IReadOnlyList<ICapabilityKey>? provide = null)
        {
            Name = name;
            _inject = inject ?? [];
            _provide = provide ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => _inject;

        public override IReadOnlyList<ICapabilityKey> Provide() => _provide;

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            foreach (var key in _inject)
            {
                if (key is Key<int> typed)
                {
                    context.Require(typed);
                }
            }

            foreach (var key in _provide)
            {
                if (key is Key<int> typed)
                {
                    context.Provide(typed, 1);
                }
            }

            return null;
        }
    }

    private static PluginManifest Manifest(string id, string type, bool enabled, string value) =>
        new(id, type, enabled, new Dictionary<string, object?> { ["value"] = value }, [], []);

    private sealed class EchoComponent : Component
    {
        private readonly Key<string> _echo;
        private readonly string _value;

        public EchoComponent(string value)
        {
            _value = value;
            _echo = new Key<string>($"echo-{value}");
        }

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_echo];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_echo, _value);
            return null;
        }
    }
}
