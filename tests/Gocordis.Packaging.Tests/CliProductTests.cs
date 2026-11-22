using System.Text.Json.Nodes;
using Gocordis.Cli;
using Gocordis.Packaging;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Product CLI close loop: a manifest declares plugins, the CLI toggles those
/// declarations, and the same CLI resolves/activates them through the real
/// PluginStore + PluginRegistry + ApplicationLoader chain (validate-only mode
/// stops before the web transport starts). Shares the existing CLI test
/// collection because both classes capture the global Console.
/// </summary>
[Collection("CLI")]
public sealed class CliProductTests
{
    private const string HostVersion = "1.5.0";

    [Fact]
    public async Task AppEnableAndDisableToggleManifestFlagsWithoutChangingOtherFields()
    {
        using var temp = new TempDir();
        var path = await WriteManifestAsync(temp,
            new ApplicationPlugin("alpha", "[1.0,2.0)", true),
            new ApplicationPlugin("beta", "[2.0,3.0)", false));
        var original = JsonNode.Parse(await File.ReadAllTextAsync(path))!.DeepClone();

        var enable = await RunAsync("app", "enable", path, "beta",
            "--store", temp.Path("store"), "--host-version", HostVersion);
        Assert.Equal(0, enable.Code);
        Assert.Equal("beta", JsonNode.Parse(enable.Out)!["id"]!.GetValue<string>());
        Assert.True(JsonNode.Parse(enable.Out)!["enabled"]!.GetValue<bool>());

        var enabled = original.DeepClone();
        enabled["Plugins"]!.AsArray()[1]!["Enabled"] = true;
        Assert.Equal(enabled.ToJsonString(), CurrentJson(path));

        var disable = await RunAsync("app", "disable", path, "alpha",
            "--store", temp.Path("store"), "--host-version", HostVersion);
        Assert.Equal(0, disable.Code);
        Assert.False(JsonNode.Parse(disable.Out)!["enabled"]!.GetValue<bool>());

        var disabled = enabled.DeepClone();
        disabled["Plugins"]!.AsArray()[0]!["Enabled"] = false;
        Assert.Equal(disabled.ToJsonString(), CurrentJson(path));

        var app = await ApplicationManifest.LoadAsync(path);
        Assert.Equal("product-app", app.Id);
        Assert.Equal(new[] { "alpha", "beta" }, app.Plugins.Select(p => p.Id));
        Assert.False(app.Plugins[0].Enabled);
        Assert.True(app.Plugins[1].Enabled);
        Assert.Equal("[2.0,3.0)", app.Plugins[1].VersionRange);
    }

    [Fact]
    public async Task AppEnableAndDisableRejectUnknownPluginWithoutChangingManifest()
    {
        using var temp = new TempDir();
        var path = await WriteManifestAsync(temp, new ApplicationPlugin("alpha", "[1.0,2.0)", true));
        var original = await File.ReadAllTextAsync(path);

        foreach (var command in new[] { "enable", "disable" })
        {
            var result = await RunAsync("app", command, path, "ghost",
                "--store", temp.Path("store"), "--host-version", HostVersion);
            Assert.NotEqual(0, result.Code);
            Assert.Contains("ghost", result.Error, StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
    }

    [Fact]
    public async Task AppRunValidateOnlyResolvesInstalledPluginAndHonorsDisable()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        await InstallDemoAsync(temp, store, "product-demo");
        var path = await WriteManifestAsync(temp, new ApplicationPlugin("product-demo", "[1.0,2.0)", true));

        var run = await RunAsync("app", "run", path, "--store", store,
            "--host-version", HostVersion, "--validate-only");
        Assert.Equal(0, run.Code);

        var summary = JsonNode.Parse(run.Out)!;
        Assert.Equal("product-app", summary["Id"]!.GetValue<string>());
        Assert.Equal(HostVersion, summary["HostVersion"]!.GetValue<string>());
        var resolved = Assert.Single(summary["resolved"]!.AsArray())!;
        Assert.Equal("product-demo", resolved["Id"]!.GetValue<string>());
        Assert.Equal("1.0.0", resolved["Version"]!.GetValue<string>());
        Assert.True(resolved["Enabled"]!.GetValue<bool>());

        var disable = await RunAsync("app", "disable", path, "product-demo",
            "--store", store, "--host-version", HostVersion);
        Assert.Equal(0, disable.Code);

        var rerun = await RunAsync("app", "run", path, "--store", store,
            "--host-version", HostVersion, "--validate-only");
        Assert.Equal(0, rerun.Code);
        var disabled = Assert.Single(JsonNode.Parse(rerun.Out)!["resolved"]!.AsArray())!;
        Assert.Equal("product-demo", disabled["Id"]!.GetValue<string>());
        Assert.False(disabled["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AppValidateKeepsItsExistingResolutionSummary()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        await InstallDemoAsync(temp, store, "legacy-demo");
        var path = await WriteManifestAsync(temp, new ApplicationPlugin("legacy-demo", "[1.0,2.0)", true));

        var result = await RunAsync("app", "validate", path, "--store", store, "--host-version", HostVersion);
        Assert.Equal(0, result.Code);
        var summary = JsonNode.Parse(result.Out)!;
        Assert.Equal("product-app", summary["Id"]!.GetValue<string>());
        var resolved = Assert.Single(summary["resolved"]!.AsArray())!;
        Assert.Equal("legacy-demo", resolved["Id"]!.GetValue<string>());
        Assert.Equal("1.0.0", resolved["Version"]!.GetValue<string>());
        Assert.True(resolved["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AppRunValidateOnlyRejectsPluginMissingFromStore()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        await InstallDemoAsync(temp, store, "installed-demo");
        var path = await WriteManifestAsync(temp, new ApplicationPlugin("not-installed", "[1.0,2.0)", true));

        var run = await RunAsync("app", "run", path, "--store", store,
            "--host-version", HostVersion, "--validate-only");
        Assert.NotEqual(0, run.Code);
        Assert.Contains("not-installed", run.Error, StringComparison.Ordinal);
        Assert.Contains("not installed", run.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static string CurrentJson(string path) => JsonNode.Parse(
        File.ReadAllText(path))!.ToJsonString();

    private static async Task<string> WriteManifestAsync(TempDir temp, params ApplicationPlugin[] plugins)
    {
        var path = temp.File("app.gocordis.json");
        await new ApplicationManifest("product-app", "1.0.0", HostVersion, plugins).SaveAsync(path);
        return path;
    }

    private static async Task InstallDemoAsync(TempDir temp, string store, string id)
    {
        var source = temp.Path($"source-{id}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), "v1");
        var package = temp.File($"{id}-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(id, "1.0.0", "product-demo", "[1.0,)"), package);

        var install = await RunAsync("plugin", "install", package,
            "--store", store, "--host-version", HostVersion);
        Assert.Equal(0, install.Code);
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
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-cli-product-{Guid.NewGuid():N}");
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
