using System.Diagnostics;
using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;
using CliProgram = Gocordis.Cli.Program;

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
    public async Task CliPackDeclaresManagedBackendAndEntry()
    {
        using var temp = new TempDir();
        var source = System.IO.Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "demo.dll"), "binary");
        var package = System.IO.Path.Combine(temp.Root, "demo.gocordis-plugin");

        var result = await RunAsync("plugin", "pack", source, package,
            "--id", "demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)",
            "--backend", "managed", "--entry-assembly", "demo.dll", "--entry-type", "Demo.Plugin");
        Assert.Equal(0, result.Code);

        var content = await PluginPackageReader.ReadAsync(package, "1.5.0");
        Assert.Equal("managed", content.Manifest.Backend);
        Assert.Equal("demo.dll", content.Manifest.EntryAssembly);
        Assert.Equal("Demo.Plugin", content.Manifest.EntryType);
    }

    [Fact]
    public async Task CliPackDeclaresSharedContractAssemblies()
    {
        using var temp = new TempDir();
        var source = System.IO.Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "demo.dll"), "binary");
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "Demo.Contracts.dll"), "binary");
        var package = System.IO.Path.Combine(temp.Root, "demo.gocordis-plugin");

        // Two names: one space-separated value form, one '=' form. Both must be
        // captured as repeated option values, not treated as positional args.
        var result = await RunAsync("plugin", "pack", source, package,
            "--id", "demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)",
            "--backend", "managed", "--entry-assembly", "demo.dll", "--entry-type", "Demo.Plugin",
            "--shared-assembly", "Demo.Contracts");
        Assert.Equal(0, result.Code);

        var content = await PluginPackageReader.ReadAsync(package, "1.5.0");
        Assert.Equal(["Demo.Contracts"], content.Manifest.SharedAssembliesOrEmpty);

        // A declared shared assembly MUST ship in the payload.
        var missingSource = System.IO.Path.Combine(temp.Root, "missing");
        Directory.CreateDirectory(missingSource);
        await File.WriteAllTextAsync(System.IO.Path.Combine(missingSource, "demo.dll"), "binary");
        var bad = await RunAsync("plugin", "pack", missingSource, System.IO.Path.Combine(temp.Root, "bad.gocordis-plugin"),
            "--id", "bad", "--backend", "managed", "--entry-assembly", "demo.dll", "--entry-type", "Demo.Plugin",
            "--shared-assembly", "Demo.Contracts");
        Assert.NotEqual(0, bad.Code);
    }

    [Fact]
    public async Task CliPackDeclaresProvidesAndDependencies()
    {
        using var temp = new TempDir();
        var source = System.IO.Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "entry.txt"), "fixture");
        var package = System.IO.Path.Combine(temp.Root, "demo.gocordis-plugin");

        var result = await RunAsync("plugin", "pack", source, package,
            "--id", "demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)",
            "--backend", "builtin", "--entry-assembly", "entry.txt",
            "--provides", "demo.a", "--provides", "demo.b",
            "--dependency", "dep-one@[1.0,2.0)", "--dependency", "dep-two@[2.0,)");
        Assert.Equal(0, result.Code);

        var content = await PluginPackageReader.ReadAsync(package, "1.5.0");
        Assert.Equal(["demo.a", "demo.b"], content.Manifest.ProvidesOrEmpty);
        Assert.Equal(
            [("dep-one", "[1.0,2.0)"), ("dep-two", "[2.0,)")],
            content.Manifest.DependenciesOrEmpty.Select(d => (d.Id, d.VersionRange)));

        var bad = await RunAsync("plugin", "pack", source, System.IO.Path.Combine(temp.Root, "bad.gocordis-plugin"),
            "--id", "bad", "--dependency", "no-range");
        Assert.NotEqual(0, bad.Code);
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
            var code = await CliProgram.Main(args);
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
