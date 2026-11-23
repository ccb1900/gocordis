using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Read-only remote package registry: HTTP resolution, checksum enforcement,
/// index/package serving with traversal rejection, and CLI wiring.
/// </summary>
[Collection("CLI")]
public sealed class PluginSourceTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task HttpSourceResolvesHighestMatchingPackageAndVerifiesChecksum()
    {
        using var temp = new TempDir();
        await CreatePackageAsync(temp, "1.0.0");
        await CreatePackageAsync(temp, "2.0.0");

        await using var server = new PluginSourceServer(temp.Root);
        await server.StartAsync();

        var source = new HttpPluginPackageSource(server.Address);
        var resolved = await source.ResolveAsync("remote-demo", "[1.0,3.0)");
        try
        {
            Assert.True(File.Exists(resolved));
            var content = await PluginPackageReader.ReadAsync(resolved, HostVersion);
            Assert.Equal("remote-demo", content.Manifest.Id);
            Assert.Equal("2.0.0", content.Manifest.Version);
        }
        finally
        {
            File.Delete(resolved);
        }

        // Same registry, corrupted index: every advertised sha256 is wrong, so
        // the downloaded temp file must be rejected instead of being returned.
        using var tamperedClient = new HttpClient(new WrongChecksumHandler(new HttpClient()));
        var tamperedSource = new HttpPluginPackageSource(server.Address, tamperedClient);
        await Assert.ThrowsAsync<PackageIntegrityException>(() =>
            tamperedSource.ResolveAsync("remote-demo", "[1.0,3.0)"));
    }

    [Fact]
    public async Task LocalRegistryServesIndexAndPackageWithTraversalRejected()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "1.0.0");
        // A bad package is skipped, not turned into a 500 for the whole index.
        await File.WriteAllTextAsync(temp.File("broken.gocordis-plugin"), "not a zip");

        await using var server = new PluginSourceServer(temp.Root);
        await server.StartAsync();
        Assert.Contains("broken.gocordis-plugin", server.SkippedPackages.Keys);

        using var client = new HttpClient();
        var index = JsonNode.Parse(await client.GetStringAsync(server.IndexUrl))!.AsArray();
        var entry = Assert.Single(index)!;
        Assert.Equal("remote-demo", entry["id"]!.GetValue<string>());
        Assert.Equal("1.0.0", entry["version"]!.GetValue<string>());
        Assert.Equal(64, entry["sha256"]!.GetValue<string>().Length);

        var packageUrl = new Uri(new Uri(server.Address), entry["url"]!.GetValue<string>());
        Assert.Equal(await File.ReadAllBytesAsync(package), await client.GetByteArrayAsync(packageUrl));

        foreach (var badPath in new[]
                 {
                     "/packages/..%2fsecret",
                     "/packages/%2e%2e%2fsecret",
                     "/packages/missing.gocordis-plugin",
                 })
        {
            var response = await client.GetAsync(new Uri(new Uri(server.Address), badPath));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task CliFetchAndRegistryServeAreWired()
    {
        using var temp = new TempDir();
        await CreatePackageAsync(temp, "1.0.0");

        await using var server = new PluginSourceServer(temp.Root);
        await server.StartAsync();

        var output = temp.File("fetched.gocordis-plugin");
        var fetch = await RunAsync("plugin", "fetch", "remote-demo", "[1.0,2.0)",
            "--source", server.Address, "--output", output, "--host-version", HostVersion);
        Assert.Equal(0, fetch.Code);
        Assert.True(File.Exists(output));
        var summary = JsonNode.Parse(fetch.Out)!;
        Assert.Equal(output, summary["path"]!.GetValue<string>());
        var content = await PluginPackageReader.ReadAsync(output, HostVersion);
        Assert.Equal("remote-demo", content.Manifest.Id);

        var help = await RunAsync("--help");
        Assert.Contains("plugin fetch", help.Error);
        Assert.Contains("registry serve", help.Error);
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
            var code = await Program.Main(args);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            System.Console.SetOut(originalOut);
            System.Console.SetError(originalError);
        }
    }

    /// <summary>Rewrites the index so the advertised checksum no longer matches the bytes.</summary>
    private sealed class WrongChecksumHandler : HttpMessageHandler
    {
        private readonly HttpClient _forwarder;

        public WrongChecksumHandler(HttpClient forwarder) => _forwarder = forwarder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/index.json", StringComparison.Ordinal))
            {
                var json = await _forwarder.GetStringAsync(request.RequestUri, cancellationToken);
                var entries = JsonNode.Parse(json)!.AsArray();
                foreach (var entry in entries)
                {
                    entry!["sha256"] = new string('0', 64);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(entries.ToJsonString(), Encoding.UTF8, "application/json"),
                };
            }

            // Forward with a fresh message: an HttpRequestMessage cannot be sent twice.
            using var forwarded = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
            return await _forwarder.SendAsync(forwarded, cancellationToken);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() =>
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-remote-source-{Guid.NewGuid():N}");

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
