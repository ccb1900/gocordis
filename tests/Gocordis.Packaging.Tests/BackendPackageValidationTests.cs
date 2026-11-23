using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Process and WASM packages are first-class artifacts: the writer validates
/// their entry metadata and payload, and the reader/installer round-trips them.
/// </summary>
public sealed class BackendPackageValidationTests
{
    private const string HostVersion = "1.5.0";

    [Theory]
    [InlineData("wasm", "plugin.wat")]
    [InlineData("wasm", "plugin.wasm")]
    [InlineData("process", "plugin.sh")]
    public async Task WriterAndReaderRoundTripBackendPackages(string backend, string entry)
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, entry), "payload");
        var manifest = new PluginPackageManifest(
            $"pkg-{backend}", "1.0.0", "demo", "[1.0,)",
            Backend: backend, EntryAssembly: entry,
            Capabilities: ["provide|org.example/a|endpoint|1.0.0||"]);

        var package = temp.File($"{backend}.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source, manifest, package);
        var content = await PluginPackageReader.ReadAsync(package, HostVersion);

        Assert.Equal(written.Checksum, content.Manifest.Checksum);
        Assert.Equal(backend, content.Manifest.Backend);
        Assert.Equal(entry, content.Manifest.EntryAssembly);
        Assert.Contains(entry, content.Files.Keys);
    }

    [Fact]
    public async Task WasmEntryMustBeAModule()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "plugin.txt"), "payload");
        var manifest = new PluginPackageManifest(
            "bad-wasm", "1.0.0", "demo", "[1.0,)",
            Backend: "wasm", EntryAssembly: "plugin.txt", Capabilities: []);

        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest, temp.File("bad.gocordis-plugin")));
    }

    [Fact]
    public async Task ProcessEntryMustBeASafeRelativePath()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        var manifest = new PluginPackageManifest(
            "bad-proc", "1.0.0", "demo", "[1.0,)",
            Backend: "process", EntryAssembly: "../escape.sh", Capabilities: []);

        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest, temp.File("bad.gocordis-plugin")));
    }

    [Fact]
    public async Task DeclaredEntryMustExistInThePayload()
    {
        using var temp = new TempDir();
        var source = temp.At("source");
        Directory.CreateDirectory(source);
        var manifest = new PluginPackageManifest(
            "missing-entry", "1.0.0", "demo", "[1.0,)",
            Backend: "wasm", EntryAssembly: "plugin.wat", Capabilities: []);

        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest, temp.File("bad.gocordis-plugin")));
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-backend-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => Path.Combine(_root, name);

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
