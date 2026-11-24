using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Paper §6.5: package dependency, assembly/contract reference and typed
/// capability dependency are three graphs. A cycle in one must not be fed into
/// the cycle detector of another. Assembly references are a compile/ALC concern
/// and are deliberately not part of the runtime cycle detector.
/// </summary>
public sealed class DependencyGraphSeparationTests
{
    [Fact]
    public async Task MutuallyDeclaringPackageDependenciesDoesNotCreateACapabilityCycle()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var key = new Key<string>("separation/capability");
        registry.RegisterComponentFactory("provider", _ => new Provider(key));
        registry.RegisterComponentFactory("consumer", _ => new Consumer(key));

        // Package graph: A ⇄ B (rejected by PluginDependencyPlanner at install,
        // but here it is descriptive manifest metadata and must not poison Core).
        var a = new PluginManifest("a", "provider", true, new Dictionary<string, object?>(),
            ["b"], ["separation/capability"]);
        var b = new PluginManifest("b", "consumer", true, new Dictionary<string, object?>(),
            ["a"], []);

        await registry.ApplyAsync([a, b]);

        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
        Assert.Equal(FiberState.Active, registry.Entries["b"].Fiber!.State);
        Assert.DoesNotContain(runtime.Snapshot().Fibers, fiber => fiber.Failure is not null);
    }

    [Fact]
    public async Task TypedCapabilityCycleIsStillRejectedIndependentlyOfPackageMetadata()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var aKey = new Key<string>("separation/a");
        var bKey = new Key<string>("separation/b");
        registry.RegisterComponentFactory("a", _ => new Mutual(aKey, bKey));
        registry.RegisterComponentFactory("b", _ => new Mutual(bKey, aKey));

        var a = new PluginManifest("a", "a", true, new Dictionary<string, object?>(), [], []);
        var b = new PluginManifest("b", "b", true, new Dictionary<string, object?>(), [], []);

        await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([a, b]));
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    private sealed class Provider : Component
    {
        private readonly Key<string> _key;
        public Provider(Key<string> key) => _key = key;
        public override string Name => "separation-provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, "value");
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Consumer : Component
    {
        private readonly Key<string> _key;
        public Consumer(Key<string> key) => _key = key;
        public override string Name => "separation-consumer";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Assert.Equal("value", context.Require(_key));
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Mutual : Component
    {
        private readonly Key<string> _inject;
        private readonly Key<string> _provide;
        public Mutual(Key<string> inject, Key<string> provide) => (_inject, _provide) = (inject, provide);
        public override string Name => "separation-mutual";
        public override IReadOnlyList<ICapabilityKey> Inject() => [_inject];
        public override IReadOnlyList<ICapabilityKey> Provide() => [_provide];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_provide, "value");
            context.Require(_inject);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
