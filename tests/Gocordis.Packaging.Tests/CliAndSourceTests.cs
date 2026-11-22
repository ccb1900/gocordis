using System.Diagnostics;
using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

[Collection("CLI")]
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

    [Fact]
    public async Task CliCreateGeneratesBuildableAppScaffold()
    {
        using var temp = new TempDir();
        var output = System.IO.Path.Combine(temp.Root, "sample-app");

        var result = await RunAsync("create", output,
            "--id", "sample-app", "--version", "1.2.3", "--host-version", "1.5.0");
        Assert.Equal(0, result.Code);

        var manifestPath = System.IO.Path.Combine(output, "gocordis.app.json");
        var programPath = System.IO.Path.Combine(output, "Program.cs");
        var projectPath = System.IO.Path.Combine(output, "sample-app.csproj");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(programPath));
        Assert.True(File.Exists(projectPath));

        var app = await ApplicationManifest.LoadAsync(manifestPath);
        Assert.Equal("sample-app", app.Id);
        Assert.Equal("1.2.3", app.Version);
        Assert.Equal("1.5.0", app.HostVersion);

        var program = await File.ReadAllTextAsync(programPath);
        var project = await File.ReadAllTextAsync(projectPath);
        Assert.Contains("PlatformRunner.RunAsync", program);
        Assert.Contains("gocordis-platform", project);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project);
        Assert.DoesNotContain(Environment.CurrentDirectory.Replace('\\', '/'), project);
        Assert.DoesNotContain("ProjectReference", project);
        Assert.True(File.Exists(System.IO.Path.Combine(output, "NuGet.Config")));

        // The generated app is self-contained: copying it elsewhere and building
        // must not depend on the developer's repository checkout.
        var relocated = System.IO.Path.Combine(temp.Root, "relocated-app");
        Directory.Move(output, relocated);
        var build = await RunProcessAsync("dotnet", "build sample-app.csproj --nologo", relocated);
        Assert.True(build.Code == 0,
            $"generated app must build after relocation (exit {build.Code}): {build.Out}\n{build.Error}");
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

    private static async Task<(int Code, string Out, string Error)> RunProcessAsync(
        string file, string arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"cannot start '{file}'");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await outTask, await errorTask);
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
