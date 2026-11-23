using System.Text.Json.Nodes;
using System.Xml.Linq;
using Gocordis.Cli;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Red tests for the product plugin tooling gap: create / doctor / package
/// (pack alias) / sandbox. Every test drives the public <see cref="Program.Main"/>
/// entry point, the same way the existing CLI tests do.
/// </summary>
[Collection("CLI")]
public sealed class PluginToolingTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task PluginCreateGeneratesBuildableScaffoldWithReversibleEffect()
    {
        using var temp = new TempDir();
        var output = temp.Path("demo-plugin");

        var result = await RunAsync("plugin", "create", output,
            "--id", "demo-plugin", "--name", "Demo Plugin", "--type", "demo", "--version", "1.2.3");
        Assert.Equal(0, result.Code);

        var summary = JsonNode.Parse(result.Out)!;
        Assert.Equal("demo-plugin", summary["id"]!.GetValue<string>());
        Assert.Equal("Demo Plugin", summary["name"]!.GetValue<string>());
        Assert.Equal("demo", summary["type"]!.GetValue<string>());
        Assert.Equal("1.2.3", summary["version"]!.GetValue<string>());

        var projectPath = Path.Combine(output, "demo-plugin.csproj");
        var pluginPath = Path.Combine(output, "Plugin.cs");
        var manifestPath = Path.Combine(output, "manifest.toml");
        Assert.True(File.Exists(projectPath));
        Assert.True(File.Exists(pluginPath));
        Assert.True(File.Exists(manifestPath));

        var declarations = PluginManifest.ParseToml(await File.ReadAllTextAsync(manifestPath));
        var declaration = Assert.Single(declarations);
        Assert.Equal("demo-plugin", declaration.Id);
        Assert.Equal("demo", declaration.Type);
        Assert.True(declaration.Enabled);
        Assert.Contains("message", declaration.Config.Keys);

        // Generated outside the repo tree: it must carry its own TFM and resolve
        // the two contract projects by path derived from the CLI assembly.
        var project = XDocument.Load(projectPath);
        Assert.Equal("Project", project.Root!.Name.LocalName);
        var references = project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value)
            .ToArray();
        Assert.Contains(references, path => path.EndsWith("src/Gocordis.Core/Gocordis.Core.csproj", StringComparison.Ordinal));
        Assert.Contains(references, path => path.EndsWith("src/Gocordis.Plugins/Gocordis.Plugins.csproj", StringComparison.Ordinal));

        var source = await File.ReadAllTextAsync(pluginPath);
        Assert.Contains("Component", source);
        Assert.Contains("context.EffectAsync", source);
        Assert.Contains("ValueTask<Cleanup?>", source);
        Assert.Contains("Demo Plugin", source);
    }

    [Fact]
    public async Task PluginCreateRejectsNonEmptyDirectoryAndCleansUpAfterWriteFailure()
    {
        using var temp = new TempDir();

        var occupied = temp.Path("occupied");
        Directory.CreateDirectory(occupied);
        var keep = Path.Combine(occupied, "keep.txt");
        await File.WriteAllTextAsync(keep, "original");
        var refused = await RunAsync("plugin", "create", occupied,
            "--id", "demo", "--name", "Demo", "--type", "demo");
        Assert.NotEqual(0, refused.Code);
        Assert.Equal("original", await File.ReadAllTextAsync(keep));
        Assert.False(File.Exists(Path.Combine(occupied, "demo.csproj")));

        // A file cannot be replaced by a directory: the failure happens before
        // any scaffold file is written and must leave the existing file intact.
        var blocked = temp.Path("blocked");
        await File.WriteAllTextAsync(blocked, "not a directory");
        var failed = await RunAsync("plugin", "create", blocked,
            "--id", "rollback-demo", "--name", "Rollback Demo", "--type", "demo");
        Assert.NotEqual(0, failed.Code);
        Assert.True(File.Exists(blocked));
        Assert.Equal("not a directory", await File.ReadAllTextAsync(blocked));
    }

    [Fact]
    public async Task PluginDoctorReportsDiagnosticsForValidIncompatibleAndCorruptPackages()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "doctor-demo", "[1.0,2.0)");

        var clean = await RunAsync("plugin", "doctor", package, "--host-version", HostVersion);
        Assert.Equal(0, clean.Code);
        var cleanDiagnostics = ParseDiagnostics(clean.Out);
        Assert.DoesNotContain(cleanDiagnostics, IsError);
        Assert.True(HasCode(cleanDiagnostics, "PACKAGE_OK"));
        Assert.True(HasCode(cleanDiagnostics, "CHECKSUM_OK"));
        Assert.True(HasCode(cleanDiagnostics, "HOST_RANGE_OK"));

        var incompatible = await RunAsync("plugin", "doctor", package, "--host-version", "3.0.0");
        Assert.Equal(1, incompatible.Code);
        Assert.True(HasCode(ParseDiagnostics(incompatible.Out), "VERSION_MISMATCH"));

        var corrupt = temp.File("corrupt.gocordis-plugin");
        await File.WriteAllTextAsync(corrupt, "not a zip archive");
        var broken = await RunAsync("plugin", "doctor", corrupt, "--host-version", HostVersion);
        Assert.Equal(1, broken.Code);
        Assert.True(HasCode(ParseDiagnostics(broken.Out), "INVALID_PACKAGE"));
    }

    [Fact]
    public async Task PluginDoctorWarnsOnDeclaredDependenciesWithoutFailing()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "dependent-demo", "[1.0,)",
            new PluginDependency("absent", "[1.0,2.0)"));

        var result = await RunAsync("plugin", "doctor", package, "--host-version", HostVersion);
        Assert.Equal(0, result.Code);
        var diagnostics = ParseDiagnostics(result.Out);
        Assert.True(HasCode(diagnostics, "DEPENDENCY_UNVERIFIED"));
        Assert.DoesNotContain(diagnostics, IsError);
    }

    [Fact]
    public async Task PluginPackageAliasProducesSameOutputAsPack()
    {
        using var temp = new TempDir();
        var source = temp.Path("source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "alias");
        var output = temp.File("alias-demo.gocordis-plugin");
        var pack = await RunAsync("plugin", "pack", source, output,
            "--id", "alias-demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)");
        var package = await RunAsync("plugin", "package", source, output,
            "--id", "alias-demo", "--version", "1.0.0", "--type", "demo", "--host-range", "[1.0,)");
        Assert.Equal(0, pack.Code);
        Assert.Equal(0, package.Code);
        Assert.Equal(pack.Out, package.Out);

        var content = await PluginPackageReader.ReadAsync(output, HostVersion);
        Assert.Equal("alias-demo", content.Manifest.Id);
    }

    [Fact]
    public async Task PluginSandboxLoadsPackageInTemporaryStoreAndReportsSnapshot()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "sandbox-demo", "[1.0,)");

        var result = await RunAsync("plugin", "sandbox", package, "--host-version", HostVersion);
        Assert.Equal(0, result.Code);
        var root = JsonNode.Parse(result.Out)!;
        Assert.False(root["securityBoundary"]!.GetValue<bool>());
        Assert.Contains("not a security sandbox", root["note"]!.GetValue<string>());

        var diagnostics = root["diagnostics"]!.AsArray();
        Assert.DoesNotContain(diagnostics, IsError);
        Assert.True(HasCode(diagnostics, "NO_HOST_FACTORY"));

        var fibers = root["snapshot"]!["Fibers"]!.AsArray();
        Assert.NotEmpty(fibers);
        Assert.Contains(fibers, fiber =>
            fiber!["Name"]!.GetValue<string>().Contains("demo", StringComparison.Ordinal));
        Assert.Contains(fibers, fiber =>
            string.Equals(fiber!["State"]!.GetValue<string>(), "Active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PluginSandboxRejectsPackageWithMissingDependency()
    {
        using var temp = new TempDir();
        var package = await CreatePackageAsync(temp, "needs-dependency", "[1.0,)",
            new PluginDependency("absent", "[1.0,2.0)"));

        var result = await RunAsync("plugin", "sandbox", package, "--host-version", HostVersion);
        Assert.Equal(1, result.Code);
        var diagnostics = JsonNode.Parse(result.Out)!["diagnostics"]!.AsArray();
        Assert.True(HasCode(diagnostics, "MISSING_DEPENDENCY"));
    }

    private static async Task<string> CreatePackageAsync(
        TempDir temp, string id, string hostRange, params PluginDependency[] dependencies)
    {
        var source = temp.Path($"source-{id}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), id);
        var package = temp.File($"{id}.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(id, "1.0.0", "demo", hostRange, Dependencies: dependencies), package);
        return package;
    }

    private static JsonArray ParseDiagnostics(string json) => JsonNode.Parse(json)!.AsArray();

    private static bool IsError(JsonNode? diagnostic) =>
        string.Equals(diagnostic!["severity"]!.GetValue<string>(), "error", StringComparison.Ordinal);

    private static bool HasCode(JsonArray diagnostics, string code) =>
        diagnostics.Any(diagnostic =>
            string.Equals(diagnostic!["code"]!.GetValue<string>(), code, StringComparison.Ordinal));

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
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-cli-tooling-{Guid.NewGuid():N}");
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
