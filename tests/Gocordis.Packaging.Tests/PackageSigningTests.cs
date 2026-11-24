using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;
using CliProgram = Gocordis.Cli.Program;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Red tests for the package signing / provenance gap. Signing is deliberately
/// additive: it only inserts signature.json into the zip, so unsigned packages,
/// their manifest checksums and the ordinary reader path stay unchanged.
/// </summary>
[Collection("CLI")]
public sealed class PackageSigningTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task SignVerifyRoundTripPreservesPackageIntegrity()
    {
        using var temp = new TempDir();
        var (package, written) = await CreateBasePackageAsync(temp, "signed-demo");
        var original = await PluginPackageReader.ReadAsync(package, HostVersion);

        var (privatePem, publicPem) = PluginPackageSigner.CreateKeyPair();
        Assert.Contains("PRIVATE KEY", privatePem, StringComparison.Ordinal);
        Assert.Contains("PUBLIC KEY", publicPem, StringComparison.Ordinal);

        var signed = await PluginPackageSigner.SignAsync(package, privatePem);
        Assert.Equal(temp.File("signed-demo.signed.gocordis-plugin"), signed);
        Assert.True(File.Exists(signed));

        await PluginPackageSigner.VerifyAsync(signed, publicPem);

        // The reader must skip signature.json (no "unexpected entry") while its
        // original checksum/hash checks still run on the unchanged payload.
        var content = await PluginPackageReader.ReadAsync(signed, HostVersion);
        Assert.Equal(written.Checksum, content.Manifest.Checksum);
        Assert.Equal(original.Manifest.Checksum, content.Manifest.Checksum);
        Assert.Equal(original.Files.Count, content.Files.Count);
        Assert.True(original.Files["entry.txt"].SequenceEqual(content.Files["entry.txt"]));
        Assert.NotNull(content.Signature);
        Assert.Contains("RS256", Encoding.UTF8.GetString(content.Signature!), StringComparison.Ordinal);

        // Signing produced a separate file; the original unsigned package is intact.
        Assert.Null((await PluginPackageReader.ReadAsync(package, HostVersion)).Signature);
    }

    [Fact]
    public async Task TamperedSignedPackageFailsVerification()
    {
        using var temp = new TempDir();
        var (package, _) = await CreateBasePackageAsync(temp, "tamper-demo");
        var (privatePem, publicPem) = PluginPackageSigner.CreateKeyPair();
        var signed = await PluginPackageSigner.SignAsync(package, privatePem);

        var tampered = await RewriteEntryAsync(temp.File("tampered.gocordis-plugin"), signed,
            "payload/entry.txt", Encoding.UTF8.GetBytes("evil payload"));

        await Assert.ThrowsAsync<PackageSignatureException>(() =>
            PluginPackageSigner.VerifyAsync(tampered, publicPem));

        // The reader's own manifest-integrity check must still reject the tamper.
        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            PluginPackageReader.ReadAsync(tampered, HostVersion));
    }

    [Fact]
    public async Task UnsignedPackageWithPublicKeyFailsDoctor()
    {
        using var temp = new TempDir();
        var (package, _) = await CreateBasePackageAsync(temp, "unsigned-demo");
        var (_, publicPem) = PluginPackageSigner.CreateKeyPair();
        var publicKeyPath = temp.File("unsigned-demo.public.pem");
        await File.WriteAllTextAsync(publicKeyPath, publicPem);

        // Existing doctor behaviour without a public key must not change.
        var withoutKey = await RunAsync("plugin", "doctor", package, "--host-version", HostVersion);
        Assert.Equal(0, withoutKey.Code);
        Assert.DoesNotContain(ParseDiagnostics(withoutKey.Out), IsError);

        var withKey = await RunAsync("plugin", "doctor", package, "--host-version", HostVersion,
            "--public-key", publicKeyPath);
        Assert.Equal(1, withKey.Code);
        var diagnostics = ParseDiagnostics(withKey.Out);
        Assert.Contains(diagnostics, diagnostic => IsError(diagnostic) && Code(diagnostic) == "SIGNATURE_INVALID");
    }

    [Fact]
    public async Task SignCommandAndDoctorPublicKeyAreWired()
    {
        using var temp = new TempDir();
        var (package, _) = await CreateBasePackageAsync(temp, "cli-signed-demo");
        var keys = temp.Path("keys");

        var keygen = await RunAsync("plugin", "keygen", "--out", keys, "--name", "release");
        Assert.Equal(0, keygen.Code);
        var privateKey = Path.Combine(keys, "release.private.pem");
        var publicKey = Path.Combine(keys, "release.public.pem");
        Assert.True(File.Exists(privateKey));
        Assert.True(File.Exists(publicKey));
        var generated = JsonNode.Parse(keygen.Out)!;
        Assert.Equal(privateKey, generated["privateKey"]!.GetValue<string>());
        Assert.Equal(publicKey, generated["publicKey"]!.GetValue<string>());

        // Existing key files are not overwritten unless explicitly allowed.
        var again = await RunAsync("plugin", "keygen", "--out", keys, "--name", "release");
        Assert.NotEqual(0, again.Code);
        var forced = await RunAsync("plugin", "keygen", "--out", keys, "--name", "release", "--force");
        Assert.Equal(0, forced.Code);

        var signed = temp.File("cli-signed-demo.signed.gocordis-plugin");
        var sign = await RunAsync("plugin", "sign", package, "--key", privateKey, "--output", signed);
        Assert.Equal(0, sign.Code);
        Assert.Equal(signed, JsonNode.Parse(sign.Out)!["signed"]!.GetValue<string>());
        Assert.True(File.Exists(signed));

        var doctor = await RunAsync("plugin", "doctor", signed, "--host-version", HostVersion,
            "--public-key", publicKey);
        Assert.Equal(0, doctor.Code);
        var diagnostics = ParseDiagnostics(doctor.Out);
        Assert.DoesNotContain(diagnostics, IsError);
        Assert.Contains(diagnostics, diagnostic => Code(diagnostic) == "SIGNATURE_OK");
    }

    private static async Task<(string Path, PluginPackageManifest Manifest)> CreateBasePackageAsync(
        TempDir temp, string id)
    {
        var source = temp.Path($"source-{id}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "payload for " + id);
        var package = temp.File($"{id}.gocordis-plugin");
        var manifest = await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(id, "1.0.0", "demo", "[1.0,)"), package);
        return (package, manifest);
    }

    /// <summary>Copies a zip verbatim except for one entry whose bytes are replaced.</summary>
    private static async Task<string> RewriteEntryAsync(
        string target, string source, string entryName, byte[] replacement)
    {
        using var input = File.OpenRead(source);
        using var original = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = File.Create(target);
        using var rewritten = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var entry in original.Entries)
        {
            var copy = rewritten.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = entry.LastWriteTime;
            await using var destination = copy.Open();
            if (string.Equals(entry.FullName, entryName, StringComparison.Ordinal))
            {
                await destination.WriteAsync(replacement);
            }
            else
            {
                await using var content = entry.Open();
                await content.CopyToAsync(destination);
            }
        }

        return target;
    }

    private static JsonArray ParseDiagnostics(string json) => JsonNode.Parse(json)!.AsArray();

    private static bool IsError(JsonNode? diagnostic) =>
        string.Equals(diagnostic!["severity"]!.GetValue<string>(), "error", StringComparison.Ordinal);

    private static string Code(JsonNode? diagnostic) => diagnostic!["code"]!.GetValue<string>();

    private static async Task<(int Code, string Out, string Error)> RunAsync(params string[] args)
    {
        var originalOut = System.Console.Out;
        var originalError = System.Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        System.Console.SetOut(output);
        System.Console.SetError(error);
        try
        {
            var code = await CliProgram.Main(args);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            System.Console.SetOut(originalOut);
            System.Console.SetError(originalError);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gocordis-signing-{Guid.NewGuid():N}");
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
