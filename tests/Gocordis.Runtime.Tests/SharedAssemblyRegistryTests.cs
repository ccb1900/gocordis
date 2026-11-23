using System.Runtime.Loader;
using Gocordis.Runtime.Managed;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Contract tests for the shared-contract-assembly promotion gate. Names are
/// unique per test where a fresh promotion is needed, because the registry is
/// process-static and shared assemblies live in AssemblyLoadContext.Default
/// for the whole process.
/// </summary>
public sealed class SharedAssemblyRegistryTests
{
    [Fact]
    public void UnapprovedNameCannotBePromotedIntoTheDefaultContext()
    {
        var name = "Unapproved." + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Path.GetTempPath(), "gocordis-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        Assert.False(SharedAssemblyRegistry.IsAllowed(name));
        var ex = Assert.Throws<SharedAssemblyNotAllowedException>(
            () => SharedAssemblyRegistry.EnsureLoaded([name], dir));
        Assert.Contains("not host-approved", ex.Message);
    }

    [Fact]
    public void SameNameAndVersionButDifferentBytesIsAnIdentityConflict()
    {
        // Gocordis.Core is pre-approved and already loaded in the Default ALC.
        // A payload with the same name/version/MVID but different bytes must be
        // rejected on the content-hash dimension — no silent identity switch.
        var dir = Path.Combine(Path.GetTempPath(), "gocordis-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var name = "Gocordis.Core";
        var source = typeof(Gocordis.Core.Component).Assembly.Location;
        var payload = Path.Combine(dir, name + ".dll");
        File.Copy(source, payload);

        // Identical copy matches the loaded identity (no second load needed).
        Assert.Equal([name], SharedAssemblyRegistry.EnsureLoaded([name], dir));

        // Append one byte: same assembly name, version and MVID, new SHA-256.
        File.WriteAllBytes(payload, File.ReadAllBytes(payload).Concat(new byte[] { 0 }).ToArray());
        var conflict = Assert.Throws<SharedAssemblyConflictException>(
            () => SharedAssemblyRegistry.EnsureLoaded([name], dir));
        Assert.Contains("identity conflict", conflict.Message);
        Assert.Contains("host restart", conflict.Message);

        // The default context still holds the original assembly — no swap.
        var still = AssemblyLoadContext.Default.Assemblies
            .First(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));
        Assert.Equal(SharedAssemblyIdentity.FromFile(source).Mvid, still.ManifestModule.ModuleVersionId);
    }

    [Fact]
    public void DeclaredNameMustMatchPayloadAssemblyName()
    {
        var name = "Mismatch." + Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Path.GetTempPath(), "gocordis-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.Copy(typeof(Gocordis.Core.Component).Assembly.Location, Path.Combine(dir, name + ".dll"));

        SharedAssemblyRegistry.Allow(name);
        var ex = Assert.Throws<SharedAssemblyConflictException>(
            () => SharedAssemblyRegistry.EnsureLoaded([name], dir));
        Assert.Contains("does not match payload identity", ex.Message);
    }

    [Fact]
    public void IdentityIncludesMvidAndContentHash()
    {
        var core = SharedAssemblyIdentity.FromFile(typeof(Gocordis.Core.Component).Assembly.Location);
        var plugins = SharedAssemblyIdentity.FromFile(typeof(Gocordis.Plugins.PluginManifest).Assembly.Location);

        Assert.NotEqual(string.Empty, core.Sha256);
        Assert.NotEqual(Guid.Empty, core.Mvid);
        Assert.False(core.Matches(plugins), "different assemblies must not match on full identity");
        Assert.True(core.Matches(core with { Sha256 = string.Empty }),
            "an unverifiable in-memory hash is skipped, other fields still match");
    }
}
