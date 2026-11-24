using System.Diagnostics;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// External-developer evidence for the pluginized extension layer: a plugin
/// compiled outside the repo references the official extension capability key,
/// is packaged with Gocordis.Extensions as a shared assembly, and is gated by
/// the host-provided Timer provider through declaration reconciliation.
/// </summary>
public sealed class ExtensionCapabilityProductTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task PackagedConsumerUsesHostExtensionCapabilityAndWithdrawsOnLoss()
    {
        var root = FindRepositoryRoot();
        var workspace = Path.Combine(Path.GetTempPath(), "gocordis-ext-capability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var source = Path.Combine(workspace, "consumer");
            var payload = Path.Combine(workspace, "payload");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(payload);

            await File.WriteAllTextAsync(Path.Combine(source, "ext-consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>ext-consumer</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{root}/src/Gocordis.Core/Gocordis.Core.csproj" />
                    <ProjectReference Include="{root}/src/Gocordis.Plugins/Gocordis.Plugins.csproj" />
                    <ProjectReference Include="{root}/src/Gocordis.Extensions/Gocordis.Extensions.csproj" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(Path.Combine(source, "Consumer.cs"), """
                using Gocordis.Core;
                using Gocordis.Extensions;

                namespace ExtConsumer;

                public sealed class Consumer : Component
                {
                    public override string Name => "ext-consumer";
                    public override IReadOnlyList<ICapabilityKey> Inject() => [ExtensionKeys.Timer];
                    public override ValueTask<Cleanup?> ApplyAsync(Context context)
                    {
                        _ = context.Require(ExtensionKeys.Timer);
                        return ValueTask.FromResult<Cleanup?>(null);
                    }
                }
                """);

            await BuildAsync(source);
            var bin = Path.Combine(source, "bin", "Debug", "net10.0");
            foreach (var name in new[] { "ext-consumer.dll", "ext-consumer.deps.json", "Gocordis.Extensions.dll" })
            {
                File.Copy(Path.Combine(bin, name), Path.Combine(payload, name), overwrite: true);
            }

            var store = Path.Combine(workspace, "store");
            var package = Path.Combine(workspace, "ext-consumer.gocordis-plugin");
            await PluginPackageWriter.CreateAsync(payload, new PluginPackageManifest(
                "ext-consumer", "1.0.0", "ext-consumer", "[1.0,)",
                Backend: "managed", EntryAssembly: "ext-consumer.dll",
                EntryType: "ExtConsumer.Consumer",
                SharedAssemblies: ["Gocordis.Extensions"]), package);
            await new PluginStore(store, HostVersion).InstallAsync(package);

            var manifestPath = Path.Combine(workspace, "gocordis.app.json");
            await new ApplicationManifest("ext-capability-app", "1.0.0", HostVersion,
                [new ApplicationPlugin("ext-consumer", "[1.0,2.0)", true)]).SaveAsync(manifestPath);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
            await app.StartAsync();

            var consumerFiber = app.Registry.Entries["ext-consumer"].Fiber!;
            Assert.Equal(FiberState.Active, consumerFiber.State);
            Assert.NotNull(app.Runtime.RequireCapability(ExtensionKeys.Timer));

            var timerFiber = app.Runtime.Snapshot().Fibers.Single(fiber => fiber.Name == "extension.timer");
            await app.Runtime.DisposeAsync(app.Runtime.GetFiber(timerFiber.Id)!);
            for (var i = 0; i < 100 && consumerFiber.State != FiberState.Pending; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(FiberState.Pending, consumerFiber.State);
            Assert.Null(consumerFiber.Failure);
            await app.StopAsync();
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
        foreach (var argument in new[] { "build", "-c", "Debug", "--nologo", "-v", "q", "-nodeReuse:false" })
        {
            start.ArgumentList.Add(argument);
        }

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
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }
}
