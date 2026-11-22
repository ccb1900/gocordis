using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Security and transactional invariants for plugin packages. These tests are
/// adversarial: malicious ids, decompression limits, manifest tampering and
/// real filesystem commit failures.
/// </summary>
public sealed class PackageSecurityTests
{
    private const string HostVersion = "1.5.0";

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../../escape")]
    [InlineData("a\\..\\escape")]
    [InlineData("/absolute")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task MaliciousPluginIdIsRejectedBeforeAnyStoreWrite(string id)
    {
        using var temp = new TempDir();
        var storeRoot = temp.Path("store");
        var outside = temp.File("escape-marker.txt");
        await File.WriteAllTextAsync(outside, "untouched");

        var manifest = new PluginPackageManifest(id, "1.0.0", "demo", "[1.0,)");
        var package = temp.File("malicious.gocordis-plugin");

        var writerError = await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(temp.Path("source"), manifest, package));
        Assert.Contains(id, writerError.Message, StringComparison.Ordinal);

        // Bypass the writer to prove the reader/store boundary also refuses it.
        await WriteRawPackageAsync(package, manifest, [("entry.txt", Encoding.UTF8.GetBytes("x"))]);
        var store = new PluginStore(storeRoot, HostVersion);
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() => store.InstallAsync(package));

        Assert.True(File.Exists(outside)); // marker still present
        Assert.Equal("untouched", await File.ReadAllTextAsync(outside));
        Assert.Empty(Directory.Exists(storeRoot) ? Directory.GetDirectories(storeRoot) : []);
    }

    [Fact]
    public async Task ReaderEnforcesManifestPayloadAndCountLimits()
    {
        using var temp = new TempDir();
        var manifest = new PluginPackageManifest("bounded", "1.0.0", "demo", "[1.0,)");

        var oversizedFile = temp.File("oversized.gocordis-plugin");
        await WriteRawPackageAsync(oversizedFile, manifest,
            [("big.bin", new byte[PluginPackageReader.MaxPayloadFileBytes + 1])]);
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageReader.ReadAsync(oversizedFile, HostVersion));

        var oversizedManifest = temp.File("manifest.gocordis-plugin");
        await WriteRawPackageAsync(oversizedManifest, manifest,
            [("entry.txt", Encoding.UTF8.GetBytes("x"))],
            new string('x', (int)PluginPackageReader.MaxManifestBytes + 1));
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageReader.ReadAsync(oversizedManifest, HostVersion));

        var tooManyFiles = temp.File("many.gocordis-plugin");
        var entries = Enumerable.Range(0, (int)PluginPackageReader.MaxPayloadFiles + 1)
            .Select(i => ($"f{i}.txt", Array.Empty<byte>())).ToArray();
        await WriteRawPackageAsync(tooManyFiles, manifest, entries);
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageReader.ReadAsync(tooManyFiles, HostVersion));

        var duplicate = temp.File("duplicate.gocordis-plugin");
        await WriteRawPackageAsync(duplicate, manifest,
            [("entry.txt", Encoding.UTF8.GetBytes("a")), ("entry.txt", Encoding.UTF8.GetBytes("b"))]);
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageReader.ReadAsync(duplicate, HostVersion));
    }

    [Fact]
    public async Task ManifestMetadataTamperingFailsIntegrityCheck()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "payload");
        var package = temp.File("valid.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("integrity", "1.0.0", "demo", "[1.0,)"), package);

        var tampered = written with { Type = "evil" };
        var tamperedPackage = temp.File("tampered.gocordis-plugin");
        await WriteRawPackageAsync(tamperedPackage, tampered,
            [("entry.txt", Encoding.UTF8.GetBytes("payload"))]);

        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            PluginPackageReader.ReadAsync(tamperedPackage, HostVersion));
    }

    [Fact]
    public async Task FailedInstallLeavesNoFinalDirectoryAndNoLockChange()
    {
        using var temp = new TempDir();
        var storeRoot = temp.Path("store");
        Directory.CreateDirectory(storeRoot);
        Directory.CreateDirectory(Path.Combine(storeRoot, "installed.json")); // poisoned lock target
        var package = await BuildPackageAsync(temp, "install-fail", "1.0.0");
        var store = new PluginStore(storeRoot, HostVersion);

        await Assert.ThrowsAnyAsync<Exception>(() => store.InstallAsync(package));

        Assert.False(Directory.Exists(Path.Combine(storeRoot, "install-fail", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(storeRoot, "installed.json")));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task FailedUpgradeRestoresOldVersionAndLock()
    {
        using var temp = new TempDir();
        var storeRoot = temp.Path("store");
        var store = new PluginStore(storeRoot, HostVersion);
        var v1 = await BuildPackageAsync(temp, "upgrade-fail", "1.0.0");
        await store.InstallAsync(v1);

        Directory.CreateDirectory(Path.Combine(storeRoot, "installed.json.tmp")); // fail at commit
        var v2 = await BuildPackageAsync(temp, "upgrade-fail", "2.0.0");
        await Assert.ThrowsAnyAsync<Exception>(() => store.UpgradeAsync(v2));

        Assert.True(File.Exists(Path.Combine(storeRoot, "upgrade-fail", "1.0.0", "entry.txt")));
        Assert.False(Directory.Exists(Path.Combine(storeRoot, "upgrade-fail", "2.0.0")));
        var resolved = await store.ResolveAsync("upgrade-fail", "[1.0,3.0)");
        Assert.NotNull(resolved);
        Assert.Equal("1.0.0", resolved.Version);
    }

    [Fact]
    public async Task FailedUninstallRestoresDirectoryAndLock()
    {
        using var temp = new TempDir();
        var storeRoot = temp.Path("store");
        var store = new PluginStore(storeRoot, HostVersion);
        var package = await BuildPackageAsync(temp, "uninstall-fail", "1.0.0");
        await store.InstallAsync(package);

        Directory.CreateDirectory(Path.Combine(storeRoot, "installed.json.tmp"));
        await Assert.ThrowsAnyAsync<Exception>(() => store.UninstallAsync("uninstall-fail"));

        Assert.True(File.Exists(Path.Combine(storeRoot, "uninstall-fail", "1.0.0", "entry.txt")));
        var resolved = await store.ResolveAsync("uninstall-fail", "[1.0,2.0)");
        Assert.NotNull(resolved);
    }

    private static async Task<string> BuildPackageAsync(TempDir temp, string id, string version)
    {
        var source = temp.Path($"source-{id}-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), version);
        var package = temp.File($"{id}-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(id, version, "demo", "[1.0,)"), package);
        return package;
    }

    private static async Task WriteRawPackageAsync(
        string path,
        PluginPackageManifest manifest,
        IReadOnlyList<(string Path, byte[] Bytes)> files,
        string? manifestJson = null)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("manifest.json");
        await using (var writer = new StreamWriter(entry.Open()))
        {
            await writer.WriteAsync(manifestJson ?? JsonSerializer.Serialize(manifest));
        }

        foreach (var (relative, bytes) in files)
        {
            var payload = archive.CreateEntry("payload/" + relative.Replace('\\', '/'));
            await using var target = payload.Open();
            await target.WriteAsync(bytes);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-sec-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }
        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string File(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
