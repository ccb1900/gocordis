using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Versioning;

namespace Gocordis.Packaging;

public sealed record PluginDependency(string Id, string VersionRange);

public sealed record PackageFile(string Path, string Sha256, long Length);

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
    IReadOnlyList<PackageFile> Files = null!)
{
    public IReadOnlyList<PluginDependency> DependenciesOrEmpty => Dependencies ?? [];
    public IReadOnlyList<string> ProvidesOrEmpty => Provides ?? [];
    public IReadOnlyList<PackageFile> FilesOrEmpty => Files ?? [];
}

public sealed record PluginPackageContent(
    PluginPackageManifest Manifest,
    IReadOnlyDictionary<string, byte[]> Files);

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
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        ValidateIdentity(manifest);

        var files = new List<PackageFile>();
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => RelativePath(sourceDirectory, path), StringComparer.Ordinal))
        {
            var relative = RelativePath(sourceDirectory, file);
            EnsureSafeRelativePath(relative);
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            files.Add(new PackageFile(relative, Sha256(bytes), bytes.Length));
        }

        var checksum = Sha256(files
            .SelectMany(file => System.Text.Encoding.UTF8.GetBytes($"{file.Path}:{file.Sha256}\n"))
            .ToArray());
        var completed = manifest with { Checksum = checksum, Files = files };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var temp = outputPath + ".tmp";
        using (var stream = File.Create(temp))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            entry.LastWriteTime = FixedTimestamp;
            await using (var writer = new StreamWriter(entry.Open()))
            {
                await writer.WriteAsync(JsonSerializer.Serialize(completed, JsonOptions));
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

        File.Move(temp, outputPath, overwrite: true);
        return completed;
    }

    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void ValidateIdentity(PluginPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        if (!NuGetVersion.TryParse(manifest.Version, out _))
        {
            throw new InvalidPluginPackageException($"invalid plugin version '{manifest.Version}'");
        }

        _ = VersionRange.Parse(manifest.HostVersionRange);
        foreach (var dependency in manifest.DependenciesOrEmpty)
        {
            _ = VersionRange.Parse(dependency.VersionRange);
        }
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

        PluginPackageManifest manifest;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json") ??
                                throw new InvalidPluginPackageException("package is missing manifest.json");
            using (var reader = new StreamReader(manifestEntry.Open()))
            {
                manifest = JsonSerializer.Deserialize<PluginPackageManifest>(await reader.ReadToEndAsync(), JsonOptions)
                           ?? throw new InvalidPluginPackageException("manifest.json is empty");
            }

            PluginPackageWriter.ValidateIdentity(manifest);
            if (!VersionRange.Parse(manifest.HostVersionRange).Satisfies(NuGetVersion.Parse(hostVersion)))
            {
                throw new IncompatiblePluginException(
                    $"plugin '{manifest.Id}@{manifest.Version}' requires host {manifest.HostVersionRange}, current {hostVersion}");
            }

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "manifest.json")
                {
                    continue;
                }

                if (!entry.FullName.StartsWith("payload/", StringComparison.Ordinal))
                {
                    throw new InvalidPluginPackageException($"unexpected package entry '{entry.FullName}'");
                }

                var relative = entry.FullName["payload/".Length..];
                PluginPackageWriter.EnsureSafeRelativePath(relative);
                await using var source = entry.Open();
                using var memory = new MemoryStream();
                await source.CopyToAsync(memory, cancellationToken);
                files[relative] = memory.ToArray();
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
        return new PluginPackageContent(manifest, files);
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

        var combined = expected
            .SelectMany(file => System.Text.Encoding.UTF8.GetBytes($"{file.Path}:{file.Sha256}\n"))
            .ToArray();
        if (manifest.Checksum != PluginPackageWriter.Sha256(combined))
        {
            throw new PackageIntegrityException("package checksum mismatch");
        }
    }
}
