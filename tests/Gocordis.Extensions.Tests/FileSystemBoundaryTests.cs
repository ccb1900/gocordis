using Gocordis.Extensions;
using Xunit;

namespace Gocordis.Extensions.Tests;

/// <summary>Migration review P1-4: mount prefix boundary and symlink escape.</summary>
public sealed class FileSystemBoundaryTests
{
    [Fact]
    public async Task MountPrefixRequiresPathBoundary()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "gocordis-fs-a-" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "gocordis-fs-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        try
        {
            await using var fs = new FileSystemService();
            fs.Mount("data", rootA);
            fs.Mount("database", rootB);
            await File.WriteAllBytesAsync(Path.Combine(rootA, "x.txt"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(rootB, "x.txt"), [2]);

            var a = await fs.ReadAsync("data/x.txt");
            var b = await fs.ReadAsync("database/x.txt");
            Assert.Equal(1, a[0]);
            Assert.Equal(2, b[0]); // longest, boundary-correct prefix wins
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Fact]
    public async Task IntermediateDirectorySymlinkCannotEscapeMount()
    {
        var temp = Path.Combine(Path.GetTempPath(), "gocordis-fs-link-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "root");
        var outside = Path.Combine(temp, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var link = Path.Combine(root, "escape");
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Windows may require developer mode / elevated privileges.
                return;
            }

            await using var fs = new FileSystemService();
            fs.Mount("data", root);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fs.WriteAsync("data/escape/new-file.txt", [7]));
            Assert.Contains("escapes the mount root", error.Message);
            Assert.False(File.Exists(Path.Combine(outside, "new-file.txt")));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
