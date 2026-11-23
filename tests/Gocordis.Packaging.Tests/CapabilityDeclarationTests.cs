using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Capability declarations travel with the package and are covered by its
/// integrity checksum. A malformed declaration is rejected before packing,
/// exactly like an unsafe id or version.
/// </summary>
public sealed class CapabilityDeclarationTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task WriterAndReaderRoundTripCapabilityDeclarations()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "not-a-real-dll");

        var declarations = new[]
        {
            "provide|org.example.calc/add|endpoint|1.2.0|application/json|calc",
            "require|org.example/store|endpoint|[1.0,2.0)|application/json|store",
        };
        var manifest = new PluginPackageManifest(
            "cap-plugin", "1.0.0", "cap", "[1.0,)",
            Dependencies: [], Provides: [], Capabilities: declarations);
        var package = temp.File("cap.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source, manifest, package);

        var content = await PluginPackageReader.ReadAsync(package, HostVersion);

        Assert.Equal(written.Checksum, content.Manifest.Checksum);
        Assert.Equal(declarations, content.Manifest.CapabilitiesOrEmpty);
        var parsed = PluginManifest.ParseCapabilities(content.Manifest.CapabilitiesOrEmpty);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(CapabilityDirection.Provide, parsed[0].Direction);
        Assert.Equal("[1.0,2.0)", parsed[1].VersionRange);
    }

    [Fact]
    public async Task CapabilityDeclarationParticipatesInIntegrityChecksum()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var baseline = new PluginPackageManifest(
            "cap-checksum", "1.0.0", "cap", "[1.0,)", Dependencies: [], Provides: [],
            Capabilities: ["provide|org.example/a|endpoint|1.0.0||"]);
        var withDeclaration = baseline with
        {
            Capabilities = ["provide|org.example/b|endpoint|1.0.0||"],
        };

        var first = await PluginPackageWriter.CreateAsync(
            source, baseline, temp.File("first.gocordis-plugin"));
        var second = await PluginPackageWriter.CreateAsync(
            source, withDeclaration, temp.File("second.gocordis-plugin"));

        Assert.NotEqual(first.Checksum, second.Checksum);
    }

    [Fact]
    public async Task MalformedCapabilityDeclarationIsRejectedBeforePacking()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var manifest = new PluginPackageManifest(
            "cap-invalid", "1.0.0", "cap", "[1.0,)", Dependencies: [], Provides: [],
            Capabilities: ["provide|not a valid id|endpoint|1.0.0||"]);

        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest, temp.File("bad.gocordis-plugin")));
    }

    [Fact]
    public void TomlManifestParsesCapabilityDeclarations()
    {
        var manifests = PluginManifest.ParseToml("""
            [[components]]
            id = "demo"
            capabilities = ["provide|org.example/a|endpoint|1.0.0||"]
            """);

        var manifest = Assert.Single(manifests);
        var declaration = Assert.Single(PluginManifest.ParseCapabilities(manifest.Capabilities));
        Assert.Equal("org.example/a", declaration.Id.ToString());
        Assert.Equal(CapabilityValueKind.Endpoint, declaration.ValueKind);
    }

    [Fact]
    public void IncompatibleProviderVersionIsRejectedBeforeCoreBinding()
    {
        var catalog = new CapabilityCatalog();
        catalog.Declare([
            CapabilityDeclaration.Parse("provide|org.example/versioned|endpoint|1.0.0||"),
            CapabilityDeclaration.Parse("require|org.example/versioned|endpoint|[2.0.0,3.0.0)||"),
        ]);

        var error = Assert.Throws<CapabilityVersionException>(() => catalog.ValidateCompatibility());
        Assert.Contains("org.example/versioned", error.Message);
    }

    [Fact]
    public void CompatibleRangePassesAndMissingProviderIsNotARejectedDeclaration()
    {
        var catalog = new CapabilityCatalog();
        catalog.Declare([
            CapabilityDeclaration.Parse("provide|org.example/versioned|endpoint|2.1.0||"),
            CapabilityDeclaration.Parse("require|org.example/versioned|endpoint|[2.0.0,3.0.0)||"),
        ]);
        catalog.ValidateCompatibility();

        var pending = new CapabilityCatalog();
        pending.Declare([
            CapabilityDeclaration.Parse("require|org.example/missing|endpoint|[1.0.0,)||"),
        ]);
        pending.ValidateCompatibility(); // no provider → Pending, not Failed
    }

    [Fact]
    public void TwoProviderVersionsForOneIdAreRejected()
    {
        var catalog = new CapabilityCatalog();
        catalog.Declare([
            CapabilityDeclaration.Parse("provide|org.example/duplicate|endpoint|1.0.0||"),
            CapabilityDeclaration.Parse("provide|org.example/duplicate|endpoint|2.0.0||"),
        ]);

        Assert.Throws<CapabilityVersionException>(() => catalog.ValidateCompatibility());
    }

    [Fact]
    public async Task NonEndpointValueKindIsRejectedInPackages()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var manifest = new PluginPackageManifest(
            "kind-gate", "1.0.0", "kind", "[1.0,)",
            Capabilities: ["provide|org.example/a|int64|1.0.0||"]);

        var error = await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest, temp.At("bad.gocordis-plugin")));
        Assert.Contains("endpoint", error.Message);
    }

    [Fact]
    public void ReplaceRejectsTwoProvidersAtTheSameVersionWithoutMutatingTheCatalog()
    {
        var catalog = new CapabilityCatalog();
        catalog.Replace([
            CapabilityDeclaration.Parse("provide|org.example/dup|endpoint|1.0.0||"),
            CapabilityDeclaration.Parse("require|org.example/dup|endpoint|[1.0.0,2.0.0)||"),
        ]);
        var stableKey = catalog.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example", "dup"), CapabilityValueKind.Endpoint);

        Assert.Throws<CapabilityVersionException>(() => catalog.Replace([
            CapabilityDeclaration.Parse("provide|org.example/dup|endpoint|1.0.0||"),
            CapabilityDeclaration.Parse("provide|org.example/dup|endpoint|1.0.0||"),
        ]));

        // The rejected plan left the old declarations AND key identity untouched.
        Assert.Equal(2, catalog.Declarations.Count);
        Assert.Same(stableKey, catalog.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example", "dup"), CapabilityValueKind.Endpoint));
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-cap-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => System.IO.Path.Combine(_root, name);

        public string File(string name) => At(name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
