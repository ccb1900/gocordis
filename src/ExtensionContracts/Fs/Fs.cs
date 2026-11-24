using Gocordis.Core;

namespace Gocordis.Extensions;

public static class FsKeys
{
    public static readonly Key<IFileSystemService> Service = new("gocordis.extension.fs");
}

public sealed record FileWriteOptions(bool Exclusive = false, bool Atomic = false);

public sealed record FileCopyOptions(bool Overwrite = false, bool Exclusive = false, bool Atomic = false);

public interface IFileSystemProvider : IAsyncDisposable
{
    string Scheme { get; }
    Task<byte[]> ReadAsync(string path);
    Task<byte[]> ReadRangeAsync(string path, long offset, int count);
    Task WriteAsync(string path, byte[] content, FileWriteOptions options);
    Task<bool> ExistsAsync(string path);
    Task<IReadOnlyList<string>> ListAsync(string path);
    Task MkdirAsync(string path, bool recursive);
    Task RemoveAsync(string path, bool recursive);
    Task CopyAsync(string source, string destination, FileCopyOptions options);
    Task MoveAsync(string source, string destination);
    IDisposable Watch(string path, Action<string> onChange);
}

public interface IFileSystemService : IAsyncDisposable
{
    void Mount(string prefix, string root);
    IDisposable MountScoped(string prefix, string root);
    bool Unmount(string prefix);
    Task WriteAllTextAsync(string path, string content);
    Task WriteAllTextAsync(string path, string content, FileWriteOptions options);
    Task WriteAsync(string path, byte[] content);
    Task WriteAsync(string path, byte[] content, FileWriteOptions options);
    Task<string> ReadAllTextAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task<bool> ExistsAsync(string path);
    Task<IReadOnlyList<string>> ListAsync(string path);
    IDisposable Watch(string path, Action<string> onChange);
}
