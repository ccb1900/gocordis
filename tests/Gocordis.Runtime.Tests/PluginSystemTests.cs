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
    public async Task DependencyOrderIsResolvedBeforeLoading()
    {
        var order = new List<string>();
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config =>
        {
            var name = (string)config["value"]!;
            order.Add(name);
            return new EchoComponent(name);
        });

        var baseManifest = Manifest("base", "echo", true, "base") with
        {
            Provides = ["base-out"],
        };
        var dependent = Manifest("dep", "echo", true, "dep") with
        {
            Requires = ["base-out"],
        };

        await registry.ApplyAsync([dependent, baseManifest]);

        Assert.Equal(new[] { "base", "dep" }, order);
    }

    [Fact]
    public async Task UnknownFactoryAndCyclesAreRejected()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new EchoComponent((string)config["value"]!));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.ApplyAsync([Manifest("x", "missing", true, "x")]));

        var a = Manifest("a", "echo", true, "a") with { Provides = ["ka"], Requires = ["kb"] };
        var b = Manifest("b", "echo", true, "b") with { Provides = ["kb"], Requires = ["ka"] };
        await Assert.ThrowsAsync<DependencyCycleException>(() => registry.ApplyAsync([a, b]));
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
