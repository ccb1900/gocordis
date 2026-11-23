using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Package-level contract for UI resource declarations: entries name a kind,
/// an id and a payload path; the writer/reader validate those paths and the
/// integrity checksum covers the declarations as well as the bytes.
/// </summary>
public sealed class PluginPackageUiTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task WriterAndReaderRoundTripInlineUiDeclarations()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(Path.Combine(source, "ui"));
        await File.WriteAllTextAsync(Path.Combine(source, "ui", "page.json"),
            """{"id":"demo-page","title":"Demo","route":"/demo","renderer":"generic"}""");
        await File.WriteAllTextAsync(Path.Combine(source, "ui.js"), "export default function register() {}");

        var manifest = new PluginPackageManifest(
            "ui-inline", "1.0.0", "demo", "[1.0,)",
            Dependencies: [], Provides: [],
            Ui:
            [
                new PackageUiResource("page", "demo-page", "ui/page.json", new JsonObject
                {
                    ["id"] = "demo-page",
                    ["title"] = "Demo",
                    ["route"] = "/demo",
                    ["renderer"] = "generic",
                }),
                new PackageUiResource("client-module", "demo-ui", "ui.js"),
            ]);
        var package = temp.File("inline.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source, manifest, package);

        var content = await PluginPackageReader.ReadAsync(package, HostVersion);
        Assert.Equal(written.Checksum, content.Manifest.Checksum);
        Assert.Equal(2, content.Manifest.UiOrEmpty.Count);
        Assert.Contains("ui/page.json", content.Files.Keys);
        Assert.Contains("ui.js", content.Files.Keys);

        var store = new PluginStore(temp.Path("store"), HostVersion);
        await store.InstallAsync(package);
        var installed = Assert.Single(await store.ListAsync());
        var declaration = Path.Combine(installed.InstallPath, ".gocordis-ui.json");
        Assert.True(File.Exists(declaration));
        var resources = JsonNode.Parse(await File.ReadAllTextAsync(declaration))!["resources"]!.AsArray();
        Assert.Equal(2, resources.Count);
    }

    [Fact]
    public async Task UiDeclarationParticipatesInIntegrityChecksum()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(Path.Combine(source, "ui"));
        await File.WriteAllTextAsync(Path.Combine(source, "ui.js"), "export default function register() {}");
        await File.WriteAllTextAsync(Path.Combine(source, "ui", "page.json"),
            """{"id":"p","title":"P","route":"/p","renderer":"generic"}""");

        var baseManifest = new PluginPackageManifest("ui-checksum", "1.0.0", "demo", "[1.0,)",
            Dependencies: [], Provides: []);
        var withoutUi = await PluginPackageWriter.CreateAsync(source, baseManifest, temp.File("plain.gocordis-plugin"));
        var withUi = await PluginPackageWriter.CreateAsync(source, baseManifest with
        {
            Ui = [new PackageUiResource("page", "p", "ui/page.json")],
        }, temp.File("with-ui.gocordis-plugin"));
        Assert.NotEqual(withoutUi.Checksum, withUi.Checksum);

        // Tamper only the declaration in a valid package: payload bytes are
        // untouched, so only a checksum that covers UI entries can reject it.
        var tampered = withUi with
        {
            Ui = [new PackageUiResource("page", "p", "ui/page.json", new JsonObject { ["title"] = "evil" })],
        };
        var tamperedPath = temp.File("tampered.gocordis-plugin");
        await WriteRawPackageAsync(tamperedPath, tampered,
            [("ui.js", "export default function register() {}"),
             ("ui/page.json", "{\"id\":\"p\",\"title\":\"P\",\"route\":\"/p\",\"renderer\":\"generic\"}")]);
        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            PluginPackageReader.ReadAsync(tamperedPath, HostVersion));
    }

    [Fact]
    public async Task WriterRejectsMissingUnsafeAndMalformedUiEntries()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "ui.js"), "export default function register() {}");
        await File.WriteAllTextAsync(Path.Combine(source, "ui.txt"), "not a module");
        var package = temp.File("bad.gocordis-plugin");
        var manifest = new PluginPackageManifest("ui-bad", "1.0.0", "demo", "[1.0,)",
            Dependencies: [], Provides: []);

        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest with
            {
                Ui = [new PackageUiResource("page", "missing", "ui/missing.json")],
            }, package));
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest with
            {
                Ui = [new PackageUiResource("client-module", "escape", "../ui.js")],
            }, package));
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest with
            {
                Ui = [new PackageUiResource("widget", "other", "ui.js")],
            }, package));
        await Assert.ThrowsAsync<InvalidPluginPackageException>(() =>
            PluginPackageWriter.CreateAsync(source, manifest with
            {
                Ui = [new PackageUiResource("client-module", "module", "ui.txt")],
            }, package));
    }

    [Fact]
    public async Task BundledDeclarationFileIsValidatedAndChecksumCovered()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "ui.js"), "export default function register() {}");
        await File.WriteAllTextAsync(Path.Combine(source, "gocordis-ui.json"),
            """
            {"resources":[{"kind":"client-module","id":"demo-ui","path":"ui.js"}]}
            """);
        var manifest = new PluginPackageManifest("ui-file", "1.0.0", "demo", "[1.0,)",
            Dependencies: [], Provides: []);
        var package = temp.File("file.gocordis-plugin");
        var written = await PluginPackageWriter.CreateAsync(source, manifest, package);
        var content = await PluginPackageReader.ReadAsync(package, HostVersion);
        Assert.Equal(written.Checksum, content.Manifest.Checksum);

        var tampered = await RewriteEntryAsync(temp.File("tampered.gocordis-plugin"), package,
            "payload/gocordis-ui.json",
            Encoding.UTF8.GetBytes("""{"resources":[{"kind":"client-module","id":"evil","path":"ui.js"}]}"""));
        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            PluginPackageReader.ReadAsync(tampered, HostVersion));
    }

    private static async Task WriteRawPackageAsync(
        string path, PluginPackageManifest manifest, IEnumerable<(string Path, string Content)> files)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("manifest.json");
        await using (var writer = new StreamWriter(entry.Open()))
        {
            await writer.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        foreach (var (relative, content) in files)
        {
            var payload = archive.CreateEntry("payload/" + relative);
            await using var writer = new StreamWriter(payload.Open());
            await writer.WriteAsync(content);
        }
    }

    private static async Task<string> RewriteEntryAsync(string target, string source, string entryName, byte[] bytes)
    {
        using var input = File.OpenRead(source);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = File.Create(target);
        using var result = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var entry in archive.Entries)
        {
            var copy = result.CreateEntry(entry.FullName);
            await using var stream = copy.Open();
            var content = entry.FullName == entryName ? bytes : await ReadEntryAsync(entry);
            await stream.WriteAsync(content);
        }

        return target;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(), $"gocordis-pkg-ui-{Guid.NewGuid():N}");
        public string Root { get; }
        public string Path(string name) => global::System.IO.Path.Combine(Root, name);
        public string File(string name) => global::System.IO.Path.Combine(Root, name);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
