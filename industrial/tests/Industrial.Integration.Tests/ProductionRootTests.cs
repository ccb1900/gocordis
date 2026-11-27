using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Production persistence must not silently fall back to a temp root.
/// Development/test hosts opt in explicitly with allowDevelopmentKey.</summary>
public sealed class ProductionRootTests
{
    [Fact]
    public void TemporaryRootIsRejectedUnlessExplicitlyAllowed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"gocordis-root-test-{Guid.NewGuid():N}");
        Assert.Throws<InvalidOperationException>(
            () => IndustrialCapabilityComponent.EnsurePersistentRoot(tempRoot, allowDevelopmentKey: false));
        Assert.Equal(Path.GetFullPath(tempRoot),
            IndustrialCapabilityComponent.EnsurePersistentRoot(tempRoot, allowDevelopmentKey: true));
    }

    [Fact]
    public void PersistentRootIsAcceptedInProduction()
    {
        var persistent = Path.Combine(Directory.GetCurrentDirectory(), "gocordis-production-data");
        Assert.Equal(Path.GetFullPath(persistent),
            IndustrialCapabilityComponent.EnsurePersistentRoot(persistent, allowDevelopmentKey: false));
    }
}
