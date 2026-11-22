using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

public sealed class CliAndSourceTests
{
    [Fact]
    public async Task LocalSourceResolvesHighestMatchingPackage()
    {
        using var temp = new TempDir();
        await PackageAsync(temp, "1.0.0");
        await PackageAsync(temp, "2.0.0");

        var source = new LocalDirectoryPackageSource(temp.Root, "1.5.0");
        var resolved = await source.ResolveAsync("cli-demo", "[1.0,3.0)");
        Assert.Contains("cli-demo-2.0.0.gocordis-plugin", resolved);

        await Assert.ThrowsAsync<MissingPluginDependencyException>(() =>
            source.ResolveAsync("cli-demo", "[3.0,4.0)"));
    }

    [Fact]
    public async Task CliPackInstallListAndUninstallReturnStructuredResults()
    {
        using var temp = new TempDir();
        var source = System.IO.Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "entry.txt"), "cli");
        var package = System.IO.Path.Combine(temp.Root, "cli-demo-1.0.0.gocordis-plugin");
        var store = System.IO.Path.Combine(temp.Root, "store");

        var pack = await RunAsync("plugin", "pack", source, package,
            "--id", "cli-demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)");
        Assert.Equal(0, pack.Code);
        Assert.Equal("cli-demo", JsonNode.Parse(pack.Out)!["Id"]!.GetValue<string>());

        var install = await RunAsync("plugin", "install", package, "--store", store, "--host-version", "1.5.0");
        Assert.Equal(0, install.Code);
        Assert.Equal("Added", JsonNode.Parse(install.Out)!["Changes"]![0]!["Kind"]!.GetValue<string>());

        var list = await RunAsync("plugin", "list", "--store", store, "--host-version", "1.5.0");
        Assert.Equal(0, list.Code);
        Assert.Single(JsonNode.Parse(list.Out)!["plugins"]!.AsArray());

        var uninstall = await RunAsync("plugin", "uninstall", "cli-demo", "--store", store, "--host-version", "1.5.0");
        Assert.Equal(0, uninstall.Code);
        Assert.Equal("Removed", JsonNode.Parse(uninstall.Out)!["Changes"]![0]!["Kind"]!.GetValue<string>());
    }

    private static async Task PackageAsync(TempDir temp, string version)
    {
        var source = System.IO.Path.Combine(temp.Root, $"source-{version}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "entry.txt"), version);
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest("cli-demo", version, "demo", "[1.0,)"),
            System.IO.Path.Combine(temp.Root, $"cli-demo-{version}.gocordis-plugin"));
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var code = await Program.Main(args);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-cli-{Guid.NewGuid():N}");
        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
