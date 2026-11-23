using Gocordis.Core;
using Xunit;

namespace Gocordis.Core.Tests;

/// <summary>
/// Red tests for paper C34 (§4.4 Isolation/Configuration): a realm reassignment
/// is an interface change, so it must be carried out as a revision composite
/// (retire -> deactivate -> O-Remove -> same-name reinsert) by the Runtime's
/// command domain. The replacement is a NEW fiber identity, never a disguise
/// of the old one.
/// </summary>
public sealed class RevisionSemanticsTests
{
    [Fact]
    public async Task RehomeRetiresOldFiberAndReinsertsWithNewIdentityAndRealm()
    {
        var key = new Key<string>("c34.service");
        var oldRealm = new Realm("c34-old");
        var newRealm = new Realm("c34-new");
        var cleanupLog = new List<string>();
        var applied = new List<string>();

        await using var runtime = new Runtime();
        var oldProvider = Provider("c34-provider", "v1", applied, cleanupLog, key);
        var oldFiber = await runtime.LoadAsync(oldProvider, realm: oldRealm);
        Assert.Equal(FiberState.Active, oldFiber.State);
        Assert.Equal(1, oldFiber.Generation);

        // A consumer bound in the old realm must lose its binding (O-Remove of
        // the old provider record) and never silently follow the replacement.
        var oldConsumer = await runtime.LoadAsync(Consumer("old-consumer", key), realm: oldRealm);
        Assert.Equal(FiberState.Active, oldConsumer.State);
        Assert.Contains(key.Name, runtime.Snapshot().Fibers
            .Single(fiber => fiber.Id == oldConsumer.Id).BoundProviders.Single(), StringComparison.Ordinal);

        var replacementComponent = Provider("c34-provider", "v2", applied, cleanupLog, key);
        var newFiber = await runtime.RehomeAsync(oldFiber, newRealm, replacementComponent)
            .WaitAsync(TimeSpan.FromSeconds(5));

        // Identity + revision: the endpoint is a fresh fiber, not a re-labelled
        // version of the old one.
        Assert.NotEqual(oldFiber.Id, newFiber.Id);
        Assert.True(newFiber.Generation > oldFiber.Generation);
        Assert.True(newFiber.Revision > oldFiber.Revision);
        Assert.Equal("c34-provider", newFiber.Component.Name);
        Assert.Equal(FiberState.Gone, oldFiber.State);
        Assert.Equal(FiberState.Active, newFiber.State);
        Assert.Equal(["c34-provider:undo"], cleanupLog);

        // Old binding removed; the old-realm consumer falls back to Pending.
        await oldFiber.GoneAsync();
        Assert.Equal(FiberState.Pending, oldConsumer.State);
        var after = runtime.Snapshot();
        Assert.DoesNotContain(after.Fibers, fiber => fiber.Id == oldFiber.Id);
        Assert.DoesNotContain(after.Fibers.SelectMany(fiber => fiber.BoundProviders),
            binding => binding.Contains(oldFiber.Id.ToString(), StringComparison.Ordinal));
        Assert.Equal(newFiber.Revision, after.Fibers.Single(fiber => fiber.Id == newFiber.Id).Revision);

        // New realm routing is effective: a consumer in the fresh realm sees
        // the reinserted provider, while the old realm still has no provider.
        string? observed = null;
        var newConsumer = await runtime.LoadAsync(
            Consumer("new-consumer", key, value => observed = value), realm: newRealm);
        Assert.Equal(FiberState.Active, newConsumer.State);
        Assert.Equal("v2", observed);
        Assert.Equal(new[] { "v1", "v2" }, applied);
        Assert.Contains(
            $"{key.Name}->{newFiber.Id}",
            runtime.Snapshot().Fibers.Single(f => f.Id == newConsumer.Id).BoundProviders);
    }

    [Fact]
    public async Task RehomeRetiresChildrenBeforeTheirOwnerAndReinsertsAtTheSamePosition()
    {
        var cleanupLog = new List<string>();
        await using var runtime = new Runtime();

        Fiber? firstChild = null;
        Fiber? secondChild = null;
        var parent = new DelegateComponent(
            "c34-parent",
            async context =>
            {
                firstChild = await context.LoadChildAsync(Child(cleanupLog, 1));
                secondChild = await context.LoadChildAsync(Child(cleanupLog, 2));
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        cleanupLog.Add("parent:undo");
                        return ValueTask.CompletedTask;
                    });
                return null;
            });
        var oldParent = await runtime.LoadAsync(parent);
        Assert.NotNull(firstChild);
        Assert.NotNull(secondChild);
        Assert.Equal(2, oldParent.Children.Count);

        var replacement = new DelegateComponent("c34-parent", _ => ValueTask.FromResult<Cleanup?>(null));
        var newParent = await runtime.RehomeAsync(oldParent, new Realm("c34-parent-fresh"), replacement)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(FiberState.Gone, oldParent.State);
        Assert.Equal(FiberState.Gone, firstChild!.State);
        Assert.Equal(FiberState.Gone, secondChild!.State);
        await Task.WhenAll(oldParent.GoneAsync(), firstChild.GoneAsync(), secondChild.GoneAsync());

        // Children are deactivated before the owner, and the composite never
        // reuses the old fiber or its owned children.
        // Children are retired before the owner. Sibling child fibers unwind
        // concurrently under the Runtime decision loop, so only the
        // children-before-parent ordering is contractual.
        Assert.Equal(3, cleanupLog.Count);
        Assert.Equal("parent:undo", cleanupLog[^1]);
        Assert.Contains("child-1:undo", cleanupLog.Take(2).ToArray());
        Assert.Contains("child-2:undo", cleanupLog.Take(2).ToArray());
        Assert.Empty(newParent.Children);
        Assert.Equal(oldParent.Parent, newParent.Parent);
        Assert.NotEqual(oldParent.Id, newParent.Id);
        Assert.True(newParent.Revision > oldParent.Revision);

        static DelegateComponent Child(List<string> log, int index) => new(
            $"child-{index}",
            async context =>
            {
                var name = context.ComponentName;
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        log.Add($"{name}:undo");
                        return ValueTask.CompletedTask;
                    });
                return null;
            });
    }

    [Fact]
    public async Task RehomeRejectsANonActiveFiberWithoutCreatingAReplacement()
    {
        await using var runtime = new Runtime();
        var fiber = await runtime.LoadAsync(new DelegateComponent("never", _ => ValueTask.FromResult<Cleanup?>(null)));
        await runtime.DisposeAsync(fiber);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RehomeAsync(fiber, new Realm("fresh")));
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task IsolateAsyncRoutesThroughTheRuntimeAndLegacyIsolateStillWorks()
    {
        var key = new Key<string>("c34.isolate");
        var special = new Realm("c34-special");
        var seen = new List<string>();

        await using var runtime = new Runtime();
        await runtime.LoadAsync(Provider("default", "default", [], [], key));
        await runtime.LoadAsync(Provider("special", "special", [], [], key), realm: special);

        var asyncConsumer = new DelegateComponent(
            "async-isolate",
            async context =>
            {
                seen.Add(context.Require(key));
                await context.IsolateAsync(key, special);
                context.ThrowIfDiverted();
                return null;
            },
            inject: [key]);
        var fiber = await runtime.LoadAsync(asyncConsumer);
        await runtime.DisposeAsync(fiber);

        // The legacy synchronous API remains source- and behavior-compatible:
        // the override still belongs to one activation and is reversible.
        await runtime.LoadAsync(new DelegateComponent(
            "legacy-isolate",
            async context =>
            {
                seen.Add(context.Require(key));
                context.Isolate(key, special);
                return null;
            },
            inject: [key]));

        Assert.Equal(new[] { "default", "default" }, seen);
    }

    private static DelegateComponent Provider(
        string name,
        string value,
        List<string> applied,
        List<string> cleanup,
        Key<string> key) => new(
            name,
            async context =>
            {
                applied.Add(value);
                context.Provide(key, value);
                await context.EffectAsync(
                    () => ValueTask.CompletedTask,
                    () =>
                    {
                        cleanup.Add($"{name}:undo");
                        return ValueTask.CompletedTask;
                    });
                return null;
            },
            provide: [key]);

    private static DelegateComponent Consumer(
        string name,
        Key<string> key,
        Action<string>? observe = null) => new(
            name,
            async context =>
            {
                var value = context.Require(key);
                observe?.Invoke(value);
                return null;
            },
            inject: [key]);
}
