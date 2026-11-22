using System.Collections.Concurrent;
using System.Security.Cryptography;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Remote package resolution can delegate outbound HTTP to the shared
/// Gocordis.Extensions.HttpService, so reversible interceptors (and SOCKS links)
/// actually observe registry traffic instead of a private static HttpClient.
/// </summary>
public sealed class HttpPluginSourceInjectionTests
{
    [Fact]
    public async Task InjectedHttpServiceInterceptorsObservePackageRequests()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "1.0.0");

        await using var server = new PluginSourceServer(temp.Root);
        await server.StartAsync();

        var http = new HttpService();
        try
        {
            var observed = new ConcurrentQueue<string>();
            using var interceptor = http.AddInterceptor(request =>
            {
                observed.Enqueue(request.RequestUri!.AbsolutePath);
                return ValueTask.CompletedTask;
            });

            // Injection is opt-in: the named argument must reach HttpService while
            // the legacy HttpClient path stays available.
            var source = new HttpPluginPackageSource(server.Address, httpService: http);
            var resolved = await source.ResolveAsync("remote-demo", "[1.0,2.0)");
            try
            {
                Assert.True(File.Exists(resolved));

                var paths = observed.ToArray();
                Assert.Equal(2, paths.Length);
                Assert.Contains("/index.json", paths);
                Assert.Contains(paths, path => path.StartsWith("/packages/", StringComparison.Ordinal));

                // Bytes on disk really are the advertised package, not just a
                // structurally valid zip that happened to be returned.
                Assert.Equal(await Sha256Async(package), await Sha256Async(resolved));

                // A streamed package body must hand its service-owned lease back
                // immediately; otherwise HttpService teardown can stall on it.
                Assert.Equal(0, http.State().OpenStreams);
                Assert.Equal(0, http.State().Active);
            }
            finally
            {
                File.Delete(resolved);
            }
        }
        finally
        {
            // Bound teardown: a leaked lease would turn a failed assertion into a
            // hung test instead of a readable failure.
            var dispose = http.DisposeAsync().AsTask();
            Assert.Same(dispose, await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10))));
            await dispose;
            Assert.True(http.State().Closed);
        }
    }

    private static async Task<string> CreatePackageAsync(TempDir temp, string version)
    {
        var source = temp.Path($"source-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), version);
        var output = temp.File($"remote-demo-{version}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("remote-demo", version, "demo", "[1.0,)"), output);
        return output;
    }

    private static async Task<string> Sha256Async(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() =>
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-http-inject-{Guid.NewGuid():N}");

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
