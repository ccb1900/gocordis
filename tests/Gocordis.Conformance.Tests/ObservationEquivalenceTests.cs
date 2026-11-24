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
