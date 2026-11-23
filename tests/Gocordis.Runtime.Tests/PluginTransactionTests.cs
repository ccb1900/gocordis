using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch B: declaration batches are transactions. A failed commit must not
/// leave a half-applied catalog or record a failed fiber as a live plugin.
/// </summary>
public sealed class PluginTransactionTests
{
    [Fact]
    public async Task ApplyAsync_RestoresPreviousDeclarationsWhenCommitFailsMidBatch()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));
        registry.RegisterFactory("boom", _ => new Boom());

        await registry.ApplyAsync([
            Manifest("a", "echo", true, "A"),
            Manifest("b", "echo", true, "B"),
        ]);

        var aAfterFirst = registry.Entries["a"].Fiber;
        var bAfterFirst = registry.Entries["b"].Fiber;

        await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([
            Manifest("a", "echo", true, "C"),
            Manifest("b", "boom", true, "ignored"),
        ]));

        Assert.Equal(2, registry.Entries.Count);
        Assert.Equal("A", registry.Entries["a"].Manifest.Config["value"]);
        Assert.Equal("B", registry.Entries["b"].Manifest.Config["value"]);
        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
        Assert.Equal(FiberState.Active, registry.Entries["b"].Fiber!.State);
        Assert.NotSame(aAfterFirst, registry.Entries["a"].Fiber);
        Assert.NotSame(bAfterFirst, registry.Entries["b"].Fiber);
        Assert.DoesNotContain(runtime.Snapshot().Fibers, f => f.Name == "boom");
    }

    [Fact]
    public async Task ApplyAsync_ActivationFailureIsNotRecordedAsAnEntry()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("boom", _ => new Boom());

        await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([
            Manifest("bad", "boom", true, "ignored"),
        ]));

        Assert.Empty(registry.Entries);
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task ChecksumChangeReplacesTheFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));

        var first = Manifest("p", "echo", true, "A") with { Checksum = "v1" };
        var second = first with { Checksum = "v2" };

        await registry.ApplyAsync([first]);
        var before = registry.Entries["p"].Fiber;

        await registry.ApplyAsync([second]);
        var after = registry.Entries["p"].Fiber;

        Assert.NotSame(before, after);
        Assert.Equal("v2", registry.Entries["p"].Manifest.Checksum);
    }

    [Fact]
    public async Task ProtectedIdCannotBeDisabledByADeclaration()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));
        registry.ProtectedIds.Add("host");

        await registry.ApplyAsync([Manifest("host", "echo", true, "A")]);
        var fiber = registry.Entries["host"].Fiber;

        await Assert.ThrowsAsync<ProtectedPluginException>(() => registry.ApplyAsync([
            Manifest("host", "echo", false, "A"),
        ]));

        Assert.Same(fiber, registry.Entries["host"].Fiber);
        Assert.Equal(FiberState.Active, fiber!.State);
    }

    private static PluginManifest Manifest(string id, string type, bool enabled, string value) =>
        new(id, type, enabled, new Dictionary<string, object?> { ["value"] = value }, [], []);

    private sealed class Echo : Component
    {
        private readonly Key<string> _key;
        private readonly string _value;

        public Echo(string value)
        {
            _value = value;
            _key = new Key<string>($"transaction-echo-{value}");
        }

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _value);
            return null;
        }
    }

    private sealed class Boom : Component
    {
        public override string Name => "boom";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new InvalidOperationException("commit boom");
    }
}
