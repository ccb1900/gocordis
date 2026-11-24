using Gocordis.Core;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Paper §3.3.2 / Def 31–37 and Thm 80: the observational projection used by
/// confluence tests must be total, must ignore generative episode ids, and must
/// NOT erase failure or failed-inverse evidence.
/// </summary>
public sealed class ObservationEquivalenceTests
{
    [Fact]
    public void EquivalentIsTotalReflexiveSymmetricAndTransitiveOverAllFiberStates()
    {
        var snapshots = Enum.GetValues<FiberState>()
            .Select(state => Snap(state))
            .Append(Snap(FiberState.Active, requires: ["other"]))
            .ToArray();

        foreach (var left in snapshots)
        {
            Assert.True(ObservationEquivalence.Equivalent(left, left));
            foreach (var right in snapshots)
            {
                var forward = ObservationEquivalence.Equivalent(left, right);
                Assert.Equal(forward, ObservationEquivalence.Equivalent(right, left));
                Assert.Equal(left.Fibers[0].State == right.Fibers[0].State &&
                             left.Fibers[0].Requires.SequenceEqual(right.Fibers[0].Requires),
                    forward);
            }
        }

        var a = Snap(FiberState.Active);
        var b = Snap(FiberState.Active);
        var c = Snap(FiberState.Active);
        Assert.True(ObservationEquivalence.Equivalent(a, b));
        Assert.True(ObservationEquivalence.Equivalent(b, c));
        Assert.True(ObservationEquivalence.Equivalent(a, c));
    }

    [Fact]
    public void GenerativeIdentityAndRevisionAreNotObservable()
    {
        var left = Snap(FiberState.Active);
        var original = Snap(FiberState.Active);
        var changed = original.Fibers[0] with
        {
            Generation = original.Fibers[0].Generation + 7,
            Revision = original.Fibers[0].Revision + 3,
            ParentId = Guid.NewGuid(),
        };
        var right = new RuntimeSnapshot([changed]);

        Assert.True(ObservationEquivalence.Equivalent(left, right));
    }

    [Fact]
    public void FailedFiberIsNotEquivalentToCleanFiberEvenWithTheSameMessage()
    {
        var failed = Snap(FiberState.Failed, "boom", "InvalidOperationException");
        var anotherType = Snap(FiberState.Failed, "boom", "ApplicationException");
        var clean = Snap(FiberState.Active);

        Assert.False(ObservationEquivalence.Equivalent(failed, clean));
        Assert.False(ObservationEquivalence.Equivalent(failed, anotherType));
    }

    [Fact]
    public void FailedUnwindIsNeverEquivalentToCleanRecovery()
    {
        var clean = Snap(FiberState.Gone);
        var incomplete = Snap(FiberState.Gone, cleanupFailures: ["inverse failed"]);

        Assert.True(incomplete.Fibers[0].CleanupIncomplete);
        Assert.False(ObservationEquivalence.Equivalent(clean, incomplete));
    }

    [Fact]
    public void EffectFailureAttributesArePartOfTheProjection()
    {
        var first = Snap(FiberState.Failed, "boom", "InvalidOperationException",
            effectLabel: "open-resource");
        var second = Snap(FiberState.Failed, "boom", "InvalidOperationException",
            effectLabel: "close-resource");

        Assert.False(ObservationEquivalence.Equivalent(first, second));
    }

    [Fact]
    public void Thm80NormalFormComparisonUsesDef76ProjectionAndExcludesFailedFibers()
    {
        var left = Snap(FiberState.Active);
        var equivalentEndpoint = left.Fibers[0] with
        {
            Id = Guid.NewGuid(),
            Generation = left.Fibers[0].Generation + 5,
            Revision = left.Fibers[0].Revision + 2,
        };
        Assert.True(ObservationEquivalence.Equivalent(left, new RuntimeSnapshot([equivalentEndpoint])));

        // Failed fibers are excluded from Thm80 normal forms; even if retained
        // for diagnostics they must not compare equal to a healthy endpoint.
        var failedTypeA = Snap(FiberState.Failed, "boom", "InvalidOperationException");
        var failedTypeB = Snap(FiberState.Failed, "boom", "ApplicationException");
        var clean = Snap(FiberState.Active);
        Assert.False(ObservationEquivalence.Equivalent(failedTypeA, clean));
        Assert.False(ObservationEquivalence.Equivalent(failedTypeA, failedTypeB));
    }

    [Fact]
    public async Task GlobalFailuresSurviveFiberRemovalAndBreakEquivalence()
    {
        static async Task<RuntimeSnapshot> RunWithFailedInverseAsync()
        {
            await using var runtime = new Runtime();
            var fiber = await runtime.LoadAsync(new FailingInverseProbe());
            await runtime.DisposeAsync(fiber);
            return runtime.Snapshot();
        }

        var withFailure = await RunWithFailedInverseAsync();
        var clean = new RuntimeSnapshot([]);

        Assert.Empty(withFailure.Fibers);
        Assert.NotEmpty(withFailure.CleanupFailures);
        Assert.NotEmpty(withFailure.EffectFailures);
        Assert.False(ObservationEquivalence.Equivalent(clean, withFailure));
        Assert.False(ObservationEquivalence.Equivalent(withFailure, clean));
    }

    [Fact]
    public void SameNameDifferentKeyInstanceIsNotEquivalent()
    {
        var leftKey = new Key<string>("same-name");
        var rightKey = new Key<string>("same-name");
        var left = Snapshot(Fiber("consumer", requiredKeys: [leftKey]));
        var sameInstance = Snapshot(Fiber("consumer", requiredKeys: [leftKey]));
        var otherInstance = Snapshot(Fiber("consumer", requiredKeys: [rightKey]));

        Assert.True(ObservationEquivalence.Equivalent(left, sameInstance));
        Assert.False(ObservationEquivalence.Equivalent(left, otherInstance));
    }

    [Fact]
    public void SameNameDifferentValueTypeIsNotEquivalent()
    {
        var text = new Key<string>("same-name");
        var number = new Key<int>("same-name");
        var left = Snapshot(Fiber("consumer", requiredKeys: [text]));
        var right = Snapshot(Fiber("consumer", requiredKeys: [number]));

        Assert.False(ObservationEquivalence.Equivalent(left, right));
    }

    [Fact]
    public void DuplicateComponentNamesAreOrderIndependentButKeepTheirSemanticProjection()
    {
        var first = new Key<string>("first");
        var second = new Key<string>("second");
        var left = Snapshot(
            Fiber("duplicate", FiberState.Active, requiredKeys: [first]),
            Fiber("duplicate", FiberState.Pending, requiredKeys: [second]));
        var reversed = Snapshot(
            Fiber("duplicate", FiberState.Pending, requiredKeys: [second]),
            Fiber("duplicate", FiberState.Active, requiredKeys: [first]));

        Assert.True(ObservationEquivalence.Equivalent(left, reversed));

        var swappedKeys = Snapshot(
            Fiber("duplicate", FiberState.Active, requiredKeys: [second]),
            Fiber("duplicate", FiberState.Pending, requiredKeys: [first]));
        Assert.False(ObservationEquivalence.Equivalent(left, swappedKeys));
    }

    [Fact]
    public void DifferentRealmAndPerKeyVisibilityAreNotEquivalent()
    {
        var key = new Key<string>("routed");
        var other = new Realm("other");
        var baseline = Snapshot(Fiber("consumer", requiredKeys: [key]));
        var otherHome = Snapshot(Fiber("consumer", requiredKeys: [key], homeRealm: other));
        var routed = Snapshot(Fiber("consumer", requiredKeys: [key],
            routes: [new KeyRoute(key, other)]));

        Assert.False(ObservationEquivalence.Equivalent(baseline, otherHome));
        Assert.False(ObservationEquivalence.Equivalent(baseline, routed));
        Assert.False(ObservationEquivalence.Equivalent(otherHome, routed));
    }

    [Fact]
    public void BoundProvidersCompareFullProviderSemanticIdentityNotJustName()
    {
        var key = new Key<string>("bound");
        var otherKey = new Key<string>("bound");
        var providerIdentity = new ProviderIdentitySnapshot(
            "provider", FiberState.Active, Realm.Default, [key], []);

        var baseline = Snapshot(Fiber("consumer", bindings: [
            new CapabilityBindingSnapshot(key, providerIdentity)]));
        var same = Snapshot(Fiber("consumer", bindings: [
            new CapabilityBindingSnapshot(key, new ProviderIdentitySnapshot(
                "provider", FiberState.Active, Realm.Default, [key], []))]));
        var differentProviderKey = Snapshot(Fiber("consumer", bindings: [
            new CapabilityBindingSnapshot(key, new ProviderIdentitySnapshot(
                "provider", FiberState.Active, Realm.Default, [otherKey], []))]));
        var differentProviderState = Snapshot(Fiber("consumer", bindings: [
            new CapabilityBindingSnapshot(key, new ProviderIdentitySnapshot(
                "provider", FiberState.Unloading, Realm.Default, [key], []))]));

        Assert.True(ObservationEquivalence.Equivalent(baseline, same));
        Assert.False(ObservationEquivalence.Equivalent(baseline, differentProviderKey));
        Assert.False(ObservationEquivalence.Equivalent(baseline, differentProviderState));
    }

    private static RuntimeSnapshot Snapshot(params FiberSnapshot[] fibers) => new(fibers);

    private static FiberSnapshot Fiber(
        string name,
        FiberState state = FiberState.Active,
        ICapabilityKey[]? requiredKeys = null,
        ICapabilityKey[]? providedKeys = null,
        Realm? homeRealm = null,
        KeyRoute[]? routes = null,
        CapabilityBindingSnapshot[]? bindings = null) =>
        new(Guid.NewGuid(), name, state, 0, null,
            (requiredKeys ?? []).Select(key => key.Name).ToArray(),
            (providedKeys ?? []).Select(key => key.Name).ToArray(),
            [], null)
        {
            RequiredKeys = requiredKeys ?? [],
            ProvidedKeys = providedKeys ?? [],
            HomeRealm = homeRealm ?? Realm.Default,
            Routes = routes ?? [],
            SemanticBindings = bindings ?? [],
        };

    private sealed class FailingInverseProbe : Component
    {
        public override string Name => "observation-failing-inverse";

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () => ValueTask.FromException(new InvalidOperationException("observation inverse failed")));
            return null;
        }
    }

    private static RuntimeSnapshot Snap(
        FiberState state,
        string? failure = null,
        string? failureType = null,
        bool cleanupIncomplete = false,
        IReadOnlyList<string>? cleanupFailures = null,
        string? effectLabel = null,
        IReadOnlyList<string>? requires = null) =>
        new([
            new FiberSnapshot(
                Guid.NewGuid(), "component", state, 0, null,
                requires ?? ["dependency"], ["capability"], ["dependency->provider"], failure)
            {
                FailureType = failureType,
                CleanupIncomplete = cleanupIncomplete || (cleanupFailures?.Count ?? 0) > 0,
                CleanupFailures = cleanupFailures ?? [],
                EffectFailures = effectLabel is null
                    ? []
                    : [new EffectFailureRecord(
                        Guid.NewGuid(), 1, 0, effectLabel, true,
                        "InvalidOperationException", "boom", new InvalidOperationException("boom"), null)],
            }
        ]);
}
