using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Final contract freeze: the manifest is the single declaration shape. Fields
/// that exist must be validated with one error semantics before a Component is
/// ever constructed; unsupported fields must not be silently ignored.
/// </summary>
public sealed class ManifestContractTests
{
    [Fact]
    public void UnknownComponentFieldIsRejected()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            type = "echo"
            enabled = true
            unexpected = "value"
            """;

        var error = Assert.Throws<ManifestParseException>(() => PluginManifest.ParseToml(toml));
        Assert.Contains("alpha", error.Message);
        Assert.Contains("unexpected", error.Message);
    }

    [Fact]
    public void InvalidRequiresTypeIsRejected()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            requires = 42
            """;

        Assert.Throws<ManifestParseException>(() => PluginManifest.ParseToml(toml));
    }

    [Fact]
    public void NonStringSourceAndChecksumAreRejected()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            source = 7
            """;

        Assert.Throws<ManifestParseException>(() => PluginManifest.ParseToml(toml));
    }

    [Fact]
    public void InvalidVersionIsRejected()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            version = "not-a-version"
            """;

        Assert.Throws<ManifestParseException>(() => PluginManifest.ParseToml(toml));
    }

    [Fact]
    public void VersionIsNormalized()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            version = "1.2"
            """;

        var manifest = Assert.Single(PluginManifest.ParseToml(toml));
        Assert.Equal("1.2.0", manifest.Version);
    }

    [Fact]
    public void SupportedFieldsParseWithoutUnknownFieldNoise()
    {
        const string toml = """
            [[components]]
            id = "alpha"
            type = "echo"
            enabled = true
            requires = ["db"]
            provides = ["adapter"]
            version = "2.0.1"
            source = "local"
            checksum = "sha256:abc"
            [components.config]
            value = "hello"
            """;

        var manifest = Assert.Single(PluginManifest.ParseToml(toml));
        Assert.Equal("alpha", manifest.Id);
        Assert.Equal("echo", manifest.Type);
        Assert.True(manifest.Enabled);
        Assert.Equal(new[] { "db" }, manifest.Requires);
        Assert.Equal(new[] { "adapter" }, manifest.Provides);
        Assert.Equal("2.0.1", manifest.Version);
        Assert.Equal("local", manifest.Source);
        Assert.Equal("sha256:abc", manifest.Checksum);
        Assert.Equal("hello", manifest.Config["value"]);
    }
}
