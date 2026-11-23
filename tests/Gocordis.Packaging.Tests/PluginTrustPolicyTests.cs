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
            store.InstallAsync(package, default, PluginTrustPolicy.TrustedOnly()));
        Assert.Empty(await store.ListAsync());
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
