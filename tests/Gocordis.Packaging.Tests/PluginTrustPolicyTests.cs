using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

public sealed class PluginTrustPolicyTests
{
    [Fact]
    public async Task TrustedOnlyPolicyRejectsUnsignedPackages()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("unsigned.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "trust-demo", "1.0.0", "trust-demo", "[1.0,)",
            Backend: "builtin", EntryAssembly: "plugin.dll"), package);

        var store = new PluginStore(temp.At("store"), "1.0.0");
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            store.InstallAsync(package, default, new PluginTrustPolicy(true, ["pinned-key"])));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public void TrustedOnlyRejectsAnEmptyKeySet()
    {
        Assert.Throws<ArgumentException>(() => PluginTrustPolicy.TrustedOnly());
        Assert.Throws<ArgumentException>(() => PluginTrustPolicy.TrustedOnly(" "));
    }

    [Fact]
    public async Task FromPemFilesParsesAndValidatesTrustConfiguration()
    {
        using var temp = new TempDir();
        var pem = temp.At("publisher.pem");
        await File.WriteAllTextAsync(pem, "pem-public-key");

        Assert.Same(PluginTrustPolicy.Development,
            PluginTrustPolicy.FromPemFiles(null, null));
        Assert.Same(PluginTrustPolicy.Development,
            PluginTrustPolicy.FromPemFiles("development", [pem]));

        var trusted = PluginTrustPolicy.FromPemFiles("trusted-only", [pem]);
        Assert.True(trusted.RequireSignature);
        Assert.Equal(["pem-public-key"], trusted.TrustedPublicKeys);

        Assert.Throws<ArgumentException>(() =>
            PluginTrustPolicy.FromPemFiles("trusted-only", null));
        Assert.Throws<ArgumentException>(() =>
            PluginTrustPolicy.FromPemFiles("unrestricted", [pem]));

        var ex = Assert.Throws<ArgumentException>(() =>
            PluginTrustPolicy.FromPemFiles("trusted-only", [temp.At("missing.pem")]));
        Assert.Contains("--public-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignatureFromAnUntrustedKeyIsRejected()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("signed.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "trust-other", "1.0.0", "trust-other", "[1.0,)",
            Backend: "builtin", EntryAssembly: "plugin.dll"), package);

        var (privateKey, _) = PluginPackageSigner.CreateKeyPair();
        var (_, otherPublicKey) = PluginPackageSigner.CreateKeyPair();
        var signed = await PluginPackageSigner.SignAsync(package, privateKey);
        var store = new PluginStore(temp.At("store"), "1.0.0");
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            store.InstallAsync(signed, default, PluginTrustPolicy.TrustedOnly(otherPublicKey)));
    }

    [Fact]
    public async Task RawRequireSignaturePolicyWithoutPinnedKeyIsRejected()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("signed.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "trust-raw", "1.0.0", "trust-raw", "[1.0,)",
            Backend: "builtin", EntryAssembly: "plugin.dll"), package);
        var (privateKey, _) = PluginPackageSigner.CreateKeyPair();
        var signed = await PluginPackageSigner.SignAsync(package, privateKey);

        var store = new PluginStore(temp.At("store"), "1.0.0");
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            store.InstallAsync(signed, default, new PluginTrustPolicy(true, null)));
    }

    [Fact]
    public async Task PinnedPublisherSignatureIsAccepted()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.dll"), "payload");
        var package = temp.At("signed.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            "trust-signed", "1.0.0", "trust-signed", "[1.0,)",
            Backend: "builtin", EntryAssembly: "plugin.dll"), package);

        var (privateKey, publicKey) = PluginPackageSigner.CreateKeyPair();
        var signed = await PluginPackageSigner.SignAsync(package, privateKey);
        var store = new PluginStore(temp.At("store"), "1.0.0");
        var result = await store.InstallAsync(signed, default, PluginTrustPolicy.TrustedOnly(publicKey));
        Assert.Contains(result.Changes, change => change.Id == "trust-signed");
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-trust-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
