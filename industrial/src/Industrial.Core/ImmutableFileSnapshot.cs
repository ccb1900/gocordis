using System.Security.Cryptography;
using Industrial.Contracts;

namespace Industrial.Core;

public sealed record FileSnapshot(string FullPath, FileIdentity Identity);

/// <summary>Copies a discovered file into an immutable, content-addressed
/// snapshot before it enters the ledger. The ledger identity and every
/// downstream reader then describe the same bytes, even if the producer
/// rewrites or deletes the original file.</summary>
public static class ImmutableFileSnapshot
{
    public static async ValueTask<FileSnapshot?> CreateAsync(
        string sourcePath, FileIdentity discovered, string snapshotRoot,
        CancellationToken ct = default)
    {
        var before = new FileInfo(sourcePath);
        if (!before.Exists || before.Length != discovered.Length ||
            before.LastWriteTimeUtc != discovered.LastWriteUtc)
            return null;

        var hashPrefix = discovered.ContentHash.Length >= 4
            ? discovered.ContentHash[..4]
            : discovered.ContentHash;
        var directory = Path.Combine(Path.GetFullPath(snapshotRoot), hashPrefix[..2], hashPrefix);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(
            directory, discovered.ContentHash + Path.GetExtension(sourcePath));
        var temporary = finalPath + ".partial-" + Guid.NewGuid().ToString("N");

        try
        {
            if (File.Exists(finalPath))
            {
                var existing = FileIdentityFactory.CreateFromSnapshot(
                    finalPath, discovered.RelativePath, discovered.Key,
                    discovered.SourceResourceId, discovered.ContentHash);
                return new FileSnapshot(finalPath, existing);
            }

            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                             FileShare.ReadWrite, 64 * 1024, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, ct);
            }

            var copiedHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(temporary, ct)));
            if (!string.Equals(copiedHash, discovered.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                return null;
            }

            File.Move(temporary, finalPath, overwrite: true);
            var identity = FileIdentityFactory.CreateFromSnapshot(
                finalPath, discovered.RelativePath, discovered.Key,
                discovered.SourceResourceId, discovered.ContentHash);
            return new FileSnapshot(finalPath, identity);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { }
            }
        }
    }
}
