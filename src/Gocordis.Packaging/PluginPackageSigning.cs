using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gocordis.Packaging;

/// <summary>
/// A package has no signature, an unsupported signature, or one that does not
/// verify against the supplied public key. Provenance failures are package
/// failures, so callers can handle them through the existing package exception
/// hierarchy.
/// </summary>
public sealed class PackageSignatureException : InvalidPluginPackageException
{
    public PackageSignatureException(string message) : base(message) { }
    public PackageSignatureException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Detached-style signing for plugin packages. The signature is stored inside
/// the zip as signature.json, but the signed digest covers manifest.json plus
/// every payload entry (sorted by full name). No manifest field, checksum or
/// payload byte is rewritten, so unsigned packages and the existing reader
/// continue to work unchanged.
/// </summary>
public static class PluginPackageSigner
{
    public const string SignatureEntryName = "signature.json";
    public const string Algorithm = "RS256";
    private const int MaxSignatureBytes = 8 * 1024;
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates an RSA-2048 key pair as PKCS#8 private / SPKI public PEM.</summary>
    public static (string PrivatePem, string PublicPem) CreateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>
    /// Signs an existing package into a new file. The default output name is
    /// &lt;name&gt;.signed.gocordis-plugin next to the input; a temp file plus an
    /// atomic move keeps a failed attempt from clobbering the destination.
    /// </summary>
    public static async Task<string> SignAsync(
        string packagePath,
        string privateKeyPem,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        if (!File.Exists(packagePath))
        {
            throw new InvalidPluginPackageException($"package not found: {packagePath}");
        }

        var destination = string.IsNullOrWhiteSpace(outputPath)
            ? DefaultSignedPath(packagePath)
            : Path.GetFullPath(outputPath);

        byte[] digest;
        await using (var source = File.OpenRead(packagePath))
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            digest = await ComputeDigestAsync(archive, ct);
        }

        byte[] signature;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            signature = rsa.SignData(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException ex)
        {
            throw new PackageSignatureException("invalid RSA private key PEM", ex);
        }

        var signatureJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["algorithm"] = Algorithm,
            ["signature"] = Convert.ToBase64String(signature),
        });
        var signatureBytes = Encoding.UTF8.GetBytes(signatureJson);

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temp = destination + ".tmp";
        try
        {
            await using (var input = File.OpenRead(packagePath))
            {
                await using var staged = File.Create(temp);
                using var sourceArchive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                using var targetArchive = new ZipArchive(staged, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var entry in sourceArchive.Entries)
                {
                    // Re-signing a signed package replaces the old signature.
                    if (string.Equals(entry.FullName, SignatureEntryName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var copy = targetArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    copy.LastWriteTime = entry.LastWriteTime;
                    await using var destinationStream = copy.Open();
                    await using var content = entry.Open();
                    await content.CopyToAsync(destinationStream, ct);
                }

                var signatureEntry = targetArchive.CreateEntry(SignatureEntryName, CompressionLevel.Optimal);
                signatureEntry.LastWriteTime = FixedTimestamp;
                await using var signatureStream = signatureEntry.Open();
                await signatureStream.WriteAsync(signatureBytes, ct);
            }

            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            TryDeleteQuietly(temp);
            throw;
        }

        return destination;
    }

    /// <summary>
    /// Verifies signature.json against the canonical digest. Missing or
    /// unsupported signatures, malformed documents, bad keys and any package
    /// tampering all surface as <see cref="PackageSignatureException"/>.
    /// </summary>
    public static async Task VerifyAsync(
        string packagePath, string publicKeyPem, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        if (!File.Exists(packagePath))
        {
            throw new PackageSignatureException($"package not found: {packagePath}");
        }

        string? algorithm;
        string? signatureBase64;
        byte[] digest;
        await using (var source = File.OpenRead(packagePath))
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var signatureEntry = archive.GetEntry(SignatureEntryName)
                ?? throw new PackageSignatureException("package is missing signature.json");
            if (signatureEntry.Length > MaxSignatureBytes)
            {
                throw new PackageSignatureException("signature.json exceeds the size limit");
            }

            byte[] signatureDocument;
            await using (var signatureStream = signatureEntry.Open())
            {
                using var buffer = new MemoryStream();
                await signatureStream.CopyToAsync(buffer, ct);
                signatureDocument = buffer.ToArray();
            }

            try
            {
                using var document = JsonDocument.Parse(signatureDocument);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("algorithm", out var algorithmNode) ||
                    algorithmNode.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("signature", out var signatureNode) ||
                    signatureNode.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("expected object with algorithm and signature strings");
                }

                algorithm = algorithmNode.GetString();
                signatureBase64 = signatureNode.GetString();
            }
            catch (JsonException ex)
            {
                throw new PackageSignatureException("signature.json is not valid JSON", ex);
            }
            catch (FormatException ex)
            {
                throw new PackageSignatureException("signature.json is malformed", ex);
            }

            digest = await ComputeDigestAsync(archive, ct);
        }

        if (!string.Equals(algorithm, Algorithm, StringComparison.Ordinal))
        {
            throw new PackageSignatureException($"unsupported signature algorithm '{algorithm}'");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64!);
        }
        catch (FormatException ex)
        {
            throw new PackageSignatureException("signature.json contains an invalid base64 signature", ex);
        }

        bool valid;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            valid = rsa.VerifyData(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException ex)
        {
            throw new PackageSignatureException("invalid RSA public key PEM", ex);
        }

        if (!valid)
        {
            throw new PackageSignatureException("package signature verification failed");
        }
    }

    /// <summary>
    /// SHA256 over a canonical stream of length-prefixed frames: the UTF-8 entry
    /// name, its decimal length, and the SHA256 of its bytes, in FullName order.
    /// The length prefix makes frame boundaries unambiguous, so signatures are
    /// stable across zip metadata (timestamps, compression level, entry order).
    /// </summary>
    private static async Task<byte[]> ComputeDigestAsync(ZipArchive archive, CancellationToken ct)
    {
        var entries = archive.Entries
            .Where(entry => !string.Equals(entry.FullName, SignatureEntryName, StringComparison.Ordinal))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        {
            AppendFrame(digest, Encoding.UTF8.GetBytes(entry.FullName));
            AppendFrame(digest, Encoding.UTF8.GetBytes(
                entry.Length.ToString(CultureInfo.InvariantCulture)));
            await using var content = entry.Open();
            AppendFrame(digest, await SHA256.HashDataAsync(content, ct));
        }

        return digest.GetHashAndReset();
    }

    private static void AppendFrame(IncrementalHash digest, byte[] field)
    {
        Span<byte> prefix = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, field.Length);
        digest.AppendData(prefix);
        digest.AppendData(field);
    }

    private static string DefaultSignedPath(string packagePath)
    {
        var full = Path.GetFullPath(packagePath);
        var name = Path.GetFileNameWithoutExtension(full);
        if (string.IsNullOrEmpty(name))
        {
            name = "package";
        }

        return Path.Combine(Path.GetDirectoryName(full)!, name + ".signed.gocordis-plugin");
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
