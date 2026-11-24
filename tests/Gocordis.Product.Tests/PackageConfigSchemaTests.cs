using System.Diagnostics;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Product-level proof that a plugin package can declare its own config schema
/// (so the console renders a typed config form) and that editing config on an
/// ACTIVE managed plugin restages its ALC — no "already loaded" collision.
/// </summary>
public sealed class PackageConfigSchemaTests
{
    private const string HostVersion = "1.0.0";
    private const string PluginId = "config-demo";
    private const string Assembly = "configdemo";

    [Fact]
    public async Task PackageSchemaIsServedAndConfigChangeRestagesTheManagedPlugin()
    {
        var root = FindRepositoryRoot();
        var workspace = Path.Combine(Path.GetTempPath(), "gocordis-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var source = Path.Combine(workspace, "source");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, Assembly + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>{Assembly}</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <Reference Include="Gocordis.Core"><HintPath>{root}/src/Gocordis.Core/bin/Debug/net10.0/Gocordis.Core.dll</HintPath></Reference>
                    <Reference Include="Gocordis.Plugins"><HintPath>{root}/src/Gocordis.Plugins/bin/Debug/net10.0/Gocordis.Plugins.dll</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(source, "Component.cs"), """
                using Gocordis.Core;
                namespace configdemo;
                public sealed class ConfigComponent : Component
                {
                    public override string Name => "config-demo";
                    public override ValueTask<Cleanup?> ApplyAsync(Context context)
                        => ValueTask.FromResult<Cleanup?>(null);
                }
                """);
            await BuildAsync(source);

            var schema = new JsonObject
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["type"] = "object",
                ["required"] = new JsonArray("intervalMs"),
                ["properties"] = new JsonObject
                {
                    ["intervalMs"] = new JsonObject { ["type"] = "integer", ["default"] = 1500 },
                    ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("fast", "slow") },
                },
            };
            var schemaPath = Path.Combine(workspace, "schema.json");
            await File.WriteAllTextAsync(schemaPath, schema.ToJsonString());

            var payload = Path.Combine(source, "bin", "Debug", "net10.0");
            var store = Path.Combine(workspace, "store");
            var package = Path.Combine(workspace, PluginId + "-1.0.0.gocordis-plugin");
            await PluginPackageWriter.CreateAsync(payload,
                new PluginPackageManifest(PluginId, "1.0.0", PluginId, "[1.0,)",
                    Backend: "managed", EntryAssembly: Assembly + ".dll",
                    EntryType: "configdemo.ConfigComponent", ConfigSchema: schema),
                package);
            await new PluginStore(store, HostVersion).InstallAsync(package);

            var manifestPath = Path.Combine(workspace, "gocordis.app.json");
            await new ApplicationManifest("config-app", "1.0.0", HostVersion,
            [
                new ApplicationPlugin(PluginId, "[1.0,2.0)", true,
                    new Dictionary<string, object?> { ["intervalMs"] = 1500, ["mode"] = "slow" }),
            ]).SaveAsync(manifestPath);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
            await app.StartAsync();

            // 1) package-declared schema is served by config/schema
            var served = app.Console.Api.Query("config/schema")!["components"]!.AsArray();
            var entry = Assert.Single(served, item => item?["type"]?.GetValue<string>() == PluginId);
            Assert.Equal(1500, entry!["schema"]!["properties"]!["intervalMs"]!["default"]!.GetValue<int>());

            // 2) editing config on an ACTIVE managed plugin restages it (the
            //    loader key now includes the config fingerprint — no collision).
            var control = Assert.IsAssignableFrom<IPluginControlPlane>(app.Control!);
            var mutation = await control.SetConfigAsync(PluginId,
                new Dictionary<string, object?> { ["intervalMs"] = 2500, ["mode"] = "fast" });
            Assert.True(mutation.Accepted, mutation.Error);

            var fiber = app.Registry.Entries[PluginId].Fiber!;
            Assert.Equal(FiberState.Active, fiber.State);
            Assert.Equal("2500", app.Registry.Entries[PluginId].Manifest.Config["intervalMs"]?.ToString());

            await app.StopAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task BuildAsync(string directory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Debug");
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("q");
        // Reused MSBuild nodes inherit the redirected pipes; disable reuse so
        // ReadToEndAsync observes EOF when the build exits.
        start.ArgumentList.Add("-nodeReuse:false");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(process.ExitCode == 0, $"dotnet build failed: {await stdout}\n{await stderr}");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }
}
