using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Plugins;
using NuGet.Versioning;

namespace Gocordis.Packaging;

public sealed record PluginDependency(string Id, string VersionRange);

public sealed record PackageFile(string Path, string Sha256, long Length);

/// <summary>
/// One UI resource entry of a plugin package. <paramref name="Kind"/> is
/// page / panel / client-module, <paramref name="Id"/> is the page or panel id
/// (or the client module name), and <paramref name="Path"/> is a payload file:
/// the definition JSON for pages/panels, the ESM entry for client modules.
/// An inline <paramref name="Definition"/> may replace the definition file in
/// the manifest; both forms are checksum-covered and path-validated.
/// </summary>
public sealed record PackageUiResource(
    string Kind,
    string Id,
    string Path,
    JsonNode? Definition = null);

/// <summary>
/// Self-contained plugin package declaration. The package format is a plain zip
/// with manifest.json + payload/; NuGet.Versioning is only a version library,
/// not a package manager requirement.
/// </summary>
public sealed record PluginPackageManifest(
    string Id,
    string Version,
    string Type,
    string HostVersionRange,
    string? DisplayName = null,
    IReadOnlyList<PluginDependency> Dependencies = null!,
    IReadOnlyList<string> Provides = null!,
    string? Checksum = null,
    IReadOnlyList<PackageFile> Files = null!,
    string? Backend = null,
    string? EntryAssembly = null,
    string? EntryType = null,
    IReadOnlyList<PackageUiResource>? Ui = null,
    IReadOnlyList<string>? SharedAssemblies = null,
    JsonNode? ConfigSchema = null,
    IReadOnlyList<string>? Capabilities = null)
{
    public IReadOnlyList<PluginDependency> DependenciesOrEmpty => Dependencies ?? [];
    public IReadOnlyList<string> ProvidesOrEmpty => Provides ?? [];
    public IReadOnlyList<PackageFile> FilesOrEmpty => Files ?? [];
    public IReadOnlyList<PackageUiResource> UiOrEmpty => Ui ?? [];
    public IReadOnlyList<string> SharedAssembliesOrEmpty => SharedAssemblies ?? [];
    public IReadOnlyList<string> CapabilitiesOrEmpty => Capabilities ?? [];
}

public sealed record PluginPackageContent(
    PluginPackageManifest Manifest,
    IReadOnlyDictionary<string, byte[]> Files,
    byte[]? Signature = null);

public sealed record PluginChange(PluginChangeKind Kind, string Id, string Version);

public enum PluginChangeKind { Added, Updated, Removed, Unchanged }

public sealed record PluginStoreResult(IReadOnlyList<PluginChange> Changes);

public class InvalidPluginPackageException : Exception
{
    public InvalidPluginPackageException(string message) : base(message) { }
    public InvalidPluginPackageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class PackageIntegrityException : InvalidPluginPackageException
{
    public PackageIntegrityException(string message) : base(message) { }
}

public sealed class IncompatiblePluginException : InvalidPluginPackageException
{
    public IncompatiblePluginException(string message) : base(message) { }
}

public sealed class MissingPluginDependencyException : InvalidPluginPackageException
{
    public MissingPluginDependencyException(string message) : base(message) { }
}

/// <summary>Formats limits shared by writer and reader; they are deliberately explicit.</summary>
public static class PluginPackageLimits
{
    public const int MaxManifestBytes = 1 << 20;          // 1 MiB
    public const long MaxPackageBytes = 256L << 20;       // 256 MiB
    public const long MaxPayloadFileBytes = 16L << 20;    // 16 MiB
    public const long MaxPayloadBytes = 64L << 20;        // 64 MiB total uncompressed
    public const int MaxPayloadFiles = 512;
}

public static class PluginPackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PluginPackageManifest> CreateAsync(
        string sourceDirectory,
        PluginPackageManifest manifest,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ValidateIdentity(manifest); // reject hostile identity before touching the filesystem
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        var files = new List<PackageFile>();
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => RelativePath(sourceDirectory, path), StringComparer.Ordinal))
        {
            var relative = RelativePath(sourceDirectory, file);
            var info = new FileInfo(file);
            if (info.Length > PluginPackageLimits.MaxPayloadFileBytes)
            {
                throw new InvalidPluginPackageException(
                    $"payload file '{relative}' exceeds {PluginPackageLimits.MaxPayloadFileBytes} bytes");
            }

            total += info.Length;
            if (total > PluginPackageLimits.MaxPayloadBytes)
            {
                throw new InvalidPluginPackageException(
                    $"payload exceeds total limit of {PluginPackageLimits.MaxPayloadBytes} bytes");
            }

            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            files.Add(new PackageFile(relative, Sha256(bytes), bytes.Length));
        }

        if (files.Count > PluginPackageLimits.MaxPayloadFiles)
        {
            throw new InvalidPluginPackageException(
                $"package has {files.Count} payload files; limit is {PluginPackageLimits.MaxPayloadFiles}");
        }

        // UI entries are part of the declaration: every declared path must be
        // a real payload file, and a bundled gocordis-ui.json is validated too.
        PluginPackageUi.Validate(manifest, files.Select(file => file.Path).ToArray(),
            path => File.ReadAllBytes(Path.Combine(sourceDirectory, path)));

        if (!string.IsNullOrWhiteSpace(manifest.Backend) &&
            !string.Equals(manifest.Backend, "builtin", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(manifest.EntryAssembly) &&
            !files.Any(file => string.Equals(file.Path, manifest.EntryAssembly, StringComparison.Ordinal)))
        {
            throw new InvalidPluginPackageException(
                $"{manifest.Backend} entry '{manifest.EntryAssembly}' is not in the package payload");
        }

        // A shared contract assembly must ship in the payload: the managed
        // loader promotes it into the default ALC so every plugin that declares
        // the same name resolves one Key<T> identity across separate ALCs.
        foreach (var shared in manifest.SharedAssembliesOrEmpty)
        {
            var payloadName = shared + ".dll";
            if (!files.Any(file => string.Equals(file.Path, payloadName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidPluginPackageException(
                    $"shared assembly '{shared}' requires '{payloadName}' in the package payload");
            }
        }

        var completed = manifest with { Checksum = ComputeIntegrityChecksum(manifest with { Files = files }), Files = files };
        var manifestJson = JsonSerializer.Serialize(completed, JsonOptions);
        if (Encoding.UTF8.GetByteCount(manifestJson) > PluginPackageLimits.MaxManifestBytes)
        {
            throw new InvalidPluginPackageException("manifest exceeds the size limit");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var temp = outputPath + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTimestamp;
                await using (var writer = new StreamWriter(entry.Open()))
                {
                    await writer.WriteAsync(manifestJson);
                }

                foreach (var file in files)
                {
                    var payload = archive.CreateEntry("payload/" + file.Path.Replace('\\', '/'), CompressionLevel.Optimal);
                    payload.LastWriteTime = FixedTimestamp;
                    await using var target = payload.Open();
                    await using var source = File.OpenRead(Path.Combine(sourceDirectory, file.Path));
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            // Check the staged file before it can replace a valid package, so
            // an oversized build never clobbers the published output.
            if (new FileInfo(temp).Length > PluginPackageLimits.MaxPackageBytes)
            {
                throw new InvalidPluginPackageException("package exceeds the size limit");
            }

            File.Move(temp, outputPath, overwrite: true);
        }
        catch
        {
            TryDeleteQuietly(temp); // cancellation, I/O fault or size overflow: never strand .tmp
            throw;
        }

        return completed;
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best-effort cleanup only: never mask the original failure
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup only: never mask the original failure
        }
    }

    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void ValidateIdentity(PluginPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!IsValidPluginId(manifest.Id))
        {
            throw new InvalidPluginPackageException($"invalid plugin id '{manifest.Id}'");
        }

        if (!NuGetVersion.TryParse(manifest.Version, out var version))
        {
            throw new InvalidPluginPackageException($"invalid plugin version '{manifest.Version}'");
        }

        EnsureSafeSegment(version.ToNormalizedString(), "version");
        _ = VersionRange.Parse(manifest.HostVersionRange);
        foreach (var dependency in manifest.DependenciesOrEmpty)
        {
            if (!IsValidPluginId(dependency.Id))
            {
                throw new InvalidPluginPackageException($"invalid dependency id '{dependency.Id}'");
            }

            _ = VersionRange.Parse(dependency.VersionRange);
        }

        foreach (var provide in manifest.ProvidesOrEmpty)
        {
            if (string.IsNullOrWhiteSpace(provide))
            {
                throw new InvalidPluginPackageException("empty capability name in provides");
            }
        }

        foreach (var shared in manifest.SharedAssembliesOrEmpty)
        {
            // Simple assembly name only: no path separators, directory segments
            // or '.dll' suffix. The loader appends '.dll' inside the payload.
            if (string.IsNullOrWhiteSpace(shared) ||
                shared.IndexOfAny(['/', '\\']) >= 0 ||
                Path.IsPathRooted(shared) ||
                shared.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginPackageException($"invalid shared assembly name '{shared}'");
            }
        }

        try
        {
            _ = PluginManifest.ParseCapabilities(manifest.CapabilitiesOrEmpty);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidPluginPackageException(
                $"invalid capability declaration in package '{manifest.Id}': {ex.Message}", ex);
        }

        // Backend metadata is optional: an absent backend is the legacy
        // declaration-only package, resolved by a host-provided factory.
        var backend = manifest.Backend;
        if (!string.IsNullOrWhiteSpace(backend))
        {
            if (!string.Equals(backend, "builtin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(backend, "managed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(backend, "wasm", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(backend, "process", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginPackageException($"unsupported backend '{backend}'");
            }

            if (string.Equals(backend, "managed", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) ||
                    string.IsNullOrWhiteSpace(manifest.EntryType))
                {
                    throw new InvalidPluginPackageException(
                        "managed package requires both entry assembly and entry type");
                }

                EnsureSafeRelativePath(manifest.EntryAssembly);
                if (!manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginPackageException(
                        $"managed entry assembly '{manifest.EntryAssembly}' is not a .dll");
                }
            }
            else if (string.Equals(backend, "wasm", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                {
                    throw new InvalidPluginPackageException(
                        "wasm package requires an entry module");
                }

                EnsureSafeRelativePath(manifest.EntryAssembly);
                if (!manifest.EntryAssembly.EndsWith(".wat", StringComparison.OrdinalIgnoreCase) &&
                    !manifest.EntryAssembly.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginPackageException(
                        $"wasm entry module '{manifest.EntryAssembly}' is not .wat or .wasm");
                }
            }
            else if (string.Equals(backend, "process", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                {
                    throw new InvalidPluginPackageException(
                        "process package requires an entry executable");
                }

                EnsureSafeRelativePath(manifest.EntryAssembly);
            }
        }
    }

    private static bool IsValidPluginId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            return false;
        }

        if (!(char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0])))
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    internal static void EnsureSafeSegment(string segment, string field)
    {
        if (string.IsNullOrWhiteSpace(segment) ||
            segment is "." or ".." ||
            segment.IndexOfAny(['/', '\\']) >= 0 ||
            Path.IsPathRooted(segment))
        {
            throw new InvalidPluginPackageException($"unsafe {field} segment '{segment}'");
        }
    }

    internal static string ComputeIntegrityChecksum(PluginPackageManifest manifest)
    {
        var text = new StringBuilder();
        text.Append("id=").Append(manifest.Id).Append('\n');
        text.Append("version=").Append(NuGetVersion.Parse(manifest.Version).ToNormalizedString()).Append('\n');
        text.Append("type=").Append(manifest.Type).Append('\n');
        text.Append("display=").Append(manifest.DisplayName ?? string.Empty).Append('\n');
        text.Append("host=").Append(VersionRange.Parse(manifest.HostVersionRange).ToString()).Append('\n');
        // Version the checksum: packages without backend metadata keep the exact
        // legacy byte sequence, so old artifacts remain verifiable after upgrade.
        if (!string.IsNullOrWhiteSpace(manifest.Backend) ||
            !string.IsNullOrWhiteSpace(manifest.EntryAssembly) ||
            !string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            text.Append("backend=").Append(manifest.Backend ?? string.Empty).Append('\n');
            text.Append("entry-assembly=").Append(manifest.EntryAssembly ?? string.Empty).Append('\n');
            text.Append("entry-type=").Append(manifest.EntryType ?? string.Empty).Append('\n');
        }

        // Shared contract assemblies are versioned into the checksum only when
        // present, so legacy packages keep their exact byte sequence. Names are
        // canonicalized (sorted) so declaration order cannot change the value.
        if (manifest.SharedAssembliesOrEmpty.Count > 0)
        {
            foreach (var shared in manifest.SharedAssembliesOrEmpty
                         .OrderBy(s => s, StringComparer.Ordinal))
            {
                text.Append("shared=").Append(shared).Append('\n');
            }
        }

        // Config schema is part of the declaration (it drives the console's
        // typed config form); version it in the checksum when present.
        if (manifest.ConfigSchema is not null)
        {
            text.Append("config-schema=")
                .Append(PluginPackageUi.CanonicalJson(manifest.ConfigSchema)).Append('\n');
        }

        // Capability declarations are part of the contract; canonicalize the
        // order so packing order cannot change the integrity checksum.
        if (manifest.CapabilitiesOrEmpty.Count > 0)
        {
            foreach (var capability in manifest.CapabilitiesOrEmpty
                         .OrderBy(c => c, StringComparer.Ordinal))
            {
                text.Append("capability=").Append(capability).Append('\n');
            }
        }

        foreach (var dependency in manifest.DependenciesOrEmpty
                     .OrderBy(d => d.Id, StringComparer.Ordinal)
                     .ThenBy(d => d.VersionRange, StringComparer.Ordinal))
        {
            text.Append("dep=").Append(dependency.Id).Append('@').Append(dependency.VersionRange).Append('\n');
        }

        foreach (var provide in manifest.ProvidesOrEmpty.OrderBy(p => p, StringComparer.Ordinal))
        {
            text.Append("provide=").Append(provide).Append('\n');
        }

        foreach (var file in manifest.FilesOrEmpty.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            text.Append("file=").Append(file.Path).Append(':').Append(file.Sha256)
                .Append(':').Append(file.Length).Append('\n');
        }

        // UI declarations are versioned into the checksum only when present:
        // legacy packages keep the exact old byte sequence. The Definition is
        // canonicalized so property order in the manifest cannot change the
        // integrity value.
        foreach (var resource in manifest.UiOrEmpty
                     .OrderBy(r => r.Kind, StringComparer.Ordinal)
                     .ThenBy(r => r.Id, StringComparer.Ordinal)
                     .ThenBy(r => r.Path, StringComparer.Ordinal))
        {
            text.Append("ui=").Append(resource.Kind).Append(':').Append(resource.Id)
                .Append(':').Append(resource.Path).Append(':')
                .Append(resource.Definition is null ? "-" : PluginPackageUi.CanonicalJson(resource.Definition))
                .Append('\n');
        }

        return Sha256(Encoding.UTF8.GetBytes(text.ToString()));
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static string RelativePath(string root, string file)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
        EnsureSafeRelativePath(relative);
        return relative;
    }

    internal static void EnsureSafeRelativePath(string relative)
    {
        if (Path.IsPathRooted(relative) ||
            relative.Split('/', '\\').Any(segment => segment is ".." or "." || segment.StartsWith('.')))
        {
            throw new InvalidPluginPackageException($"unsafe package path '{relative}'");
        }
    }
}

public static class PluginPackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public const long MaxManifestBytes = PluginPackageLimits.MaxManifestBytes;
    public const long MaxPackageBytes = PluginPackageLimits.MaxPackageBytes;
    public const long MaxPayloadFileBytes = PluginPackageLimits.MaxPayloadFileBytes;
    public const int MaxPayloadFiles = PluginPackageLimits.MaxPayloadFiles;
    private const int MaxSignatureBytes = 8 * 1024;

    public static async Task<PluginPackageContent> ReadAsync(
        string packagePath,
        string hostVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!File.Exists(packagePath))
        {
            throw new InvalidPluginPackageException($"package not found: {packagePath}");
        }

        if (new FileInfo(packagePath).Length > PluginPackageLimits.MaxPackageBytes)
        {
            throw new InvalidPluginPackageException("package exceeds the size limit");
        }

        PluginPackageManifest manifest;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? signature = null;
        long total = 0;
        try
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json") ??
                                throw new InvalidPluginPackageException("package is missing manifest.json");
            if (manifestEntry.Length > PluginPackageLimits.MaxManifestBytes)
            {
                throw new InvalidPluginPackageException("manifest exceeds the size limit");
            }

            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                var json = await reader.ReadToEndAsync(cancellationToken);
                if (Encoding.UTF8.GetByteCount(json) > PluginPackageLimits.MaxManifestBytes)
                {
                    throw new InvalidPluginPackageException("manifest exceeds the size limit");
                }

                manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, JsonOptions)
                           ?? throw new InvalidPluginPackageException("manifest.json is empty");
            }

            PluginPackageWriter.ValidateIdentity(manifest);
            if (!VersionRange.Parse(manifest.HostVersionRange).Satisfies(NuGetVersion.Parse(hostVersion)))
            {
                throw new IncompatiblePluginException(
                    $"plugin '{manifest.Id}@{manifest.Version}' requires host {manifest.HostVersionRange}, current {hostVersion}");
            }

            // signature.json is package metadata, not a payload file: keep it out
            // of the "unexpected entry" check and the payload limits, but expose
            // its raw bytes on the content for provenance inspection.
            var payloadEntries = archive.Entries
                .Where(e => e.FullName != "manifest.json" &&
                            e.FullName != PluginPackageSigner.SignatureEntryName)
                .ToArray();
            if (payloadEntries.Length > PluginPackageLimits.MaxPayloadFiles)
            {
                throw new InvalidPluginPackageException(
                    $"package has {payloadEntries.Length} payload files; limit is {PluginPackageLimits.MaxPayloadFiles}");
            }

            foreach (var entry in payloadEntries)
            {
                if (!entry.FullName.StartsWith("payload/", StringComparison.Ordinal))
                {
                    throw new InvalidPluginPackageException($"unexpected package entry '{entry.FullName}'");
                }

                var relative = entry.FullName["payload/".Length..];
                PluginPackageWriter.EnsureSafeRelativePath(relative);
                if (entry.Length > PluginPackageLimits.MaxPayloadFileBytes)
                {
                    throw new InvalidPluginPackageException($"payload file '{relative}' exceeds the size limit");
                }

                total += entry.Length;
                if (total > PluginPackageLimits.MaxPayloadBytes)
                {
                    throw new InvalidPluginPackageException("payload exceeds the total size limit");
                }

                await using var source = entry.Open();
                using var memory = new MemoryStream();
                await source.CopyToAsync(memory, cancellationToken);
                if (!files.TryAdd(relative, memory.ToArray()))
                {
                    throw new InvalidPluginPackageException($"duplicate payload path '{relative}'");
                }
            }

            var signatureEntry = archive.GetEntry(PluginPackageSigner.SignatureEntryName);
            if (signatureEntry is not null)
            {
                if (signatureEntry.Length > MaxSignatureBytes)
                {
                    throw new InvalidPluginPackageException("signature.json exceeds the size limit");
                }

                await using var signatureSource = signatureEntry.Open();
                using var signatureMemory = new MemoryStream();
                await signatureSource.CopyToAsync(signatureMemory, cancellationToken);
                signature = signatureMemory.ToArray();
            }
        }
        catch (InvalidPluginPackageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidPluginPackageException($"cannot read package '{packagePath}'", ex);
        }

        Verify(manifest, files);
        PluginPackageUi.Validate(manifest, files.Keys, path => files[path]);
        return new PluginPackageContent(manifest, files, signature);
    }

    private static void Verify(PluginPackageManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var expected = manifest.FilesOrEmpty;
        if (expected.Count != files.Count)
        {
            throw new PackageIntegrityException("payload file count does not match manifest");
        }

        foreach (var item in expected)
        {
            if (!files.TryGetValue(item.Path, out var bytes))
            {
                throw new PackageIntegrityException($"missing payload file '{item.Path}'");
            }

            if (bytes.LongLength != item.Length || PluginPackageWriter.Sha256(bytes) != item.Sha256)
            {
                throw new PackageIntegrityException($"checksum mismatch for payload file '{item.Path}'");
            }
        }

        if (manifest.Checksum != PluginPackageWriter.ComputeIntegrityChecksum(manifest))
        {
            throw new PackageIntegrityException("package checksum mismatch");
        }
    }
}
