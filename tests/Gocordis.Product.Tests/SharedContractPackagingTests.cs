using System.Diagnostics;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Product-level proof that the framework fix for shared contract assemblies
/// makes cross-plugin dependency gating work in the PACKAGED form — two
/// independent managed .gocordis-plugin packages resolve the same Key&lt;T&gt;
/// identity and the consumer withdraws/reactivates with its provider.
///
/// This is the defect the SpatiotemporalShowcase surfaces: before the fix each
/// package carried its own Contracts.dll in a separate ALC, so the consumer's
/// Inject could never resolve the provider's Provide.
/// </summary>
public sealed class SharedContractPackagingTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task SharedContractAcrossTwoPackagesGatesTheConsumer()
    {
        var root = FindRepositoryRoot();
        var workspace = Path.Combine(Path.GetTempPath(), "gocordis-shared-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            // 1) Build a shared contract assembly that DEFINES the Key, then
            //    two plugin assemblies that reference it (separate packages/ALCs).
            var contractDll = await BuildContractsAsync(root, workspace);
            var providerPayload = await BuildPluginAsync(root, workspace, "provider", ProviderCode);
            var consumerPayload = await BuildPluginAsync(root, workspace, "consumer", ConsumerCode);
            File.Copy(contractDll, Path.Combine(providerPayload, "Shared.Contracts.dll"), overwrite: true);
            File.Copy(contractDll, Path.Combine(consumerPayload, "Shared.Contracts.dll"), overwrite: true);

            var store = Path.Combine(workspace, "store");
            await InstallAsync(providerPayload, store, "shared-provider", "provider.dll", "Shared.Provider.ProviderComponent");
            await InstallAsync(consumerPayload, store, "shared-consumer", "consumer.dll", "Shared.Consumer.ConsumerComponent");

            var manifestPath = Path.Combine(workspace, "gocordis.app.json");
            await new ApplicationManifest("shared-contract-app", "1.0.0", HostVersion,
            [
                new ApplicationPlugin("shared-provider", "[1.0,2.0)", true),
                new ApplicationPlugin("shared-consumer", "[1.0,2.0)", true),
            ]).SaveAsync(manifestPath);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
            await app.StartAsync();

            var providerFiber = app.Registry.Entries["shared-provider"].Fiber;
            var consumerFiber = app.Registry.Entries["shared-consumer"].Fiber;
            Assert.NotNull(providerFiber);
            Assert.NotNull(consumerFiber);
            Assert.Equal(FiberState.Active, providerFiber!.State);
            Assert.Equal(FiberState.Active, consumerFiber!.State);

            // The consumer resolved the provider's value through the shared Key
            // identity — impossible if the two Contracts.dll copies were loaded
            // into separate ALCs with separate static Key instances.
            Assert.Equal("shared-value", app.Console.Api.Query("shared/observed")?["value"]?.GetValue<string>());

            // Runtime-level dependency withdrawal (C22): disposing the provider
            // fiber withdraws the consumer to Pending, not Failed. (The product
            // control plane separately refuses an inconsistent declaration set.)
            await app.Runtime.DisposeAsync(providerFiber);
            // A withdrawn consumer returns to Pending (not Gone), so there is no
            // terminal task to await — poll the state through the decision loop.
            for (var i = 0; i < 100 && consumerFiber.State != FiberState.Pending; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(FiberState.Pending, consumerFiber.State);
            Assert.Null(consumerFiber.Failure);

            // Re-loading a provider with the same shared key reactivates it.
            var replacement = app.CreateComponent(new PluginManifest(
                "shared-provider-2", "shared-provider", true,
                new Dictionary<string, object?>(), [], [], "1.0.0",
                providerPayload, null, "managed", "provider.dll", "Shared.Provider.ProviderComponent",
                ["Shared.Contracts"]));
            await app.Runtime.LoadAsync(replacement);
            for (var i = 0; i < 100 && consumerFiber.State != FiberState.Active; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(FiberState.Active, consumerFiber.State);
            Assert.Null(consumerFiber.Failure);

            await app.StopAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private const string ProviderCode = """
        using Gocordis.Core;
        using Shared.Contracts;
        namespace Shared.Provider;
        public sealed class ProviderComponent : Component
        {
            public override string Name => "shared-provider";
            public override IReadOnlyList<ICapabilityKey> Provide() => [K.Value];
            public override ValueTask<Cleanup?> ApplyAsync(Context context)
            {
                context.Provide(K.Value, "shared-value");
                return ValueTask.FromResult<Cleanup?>(null);
            }
        }
        """;

    private const string ConsumerCode = """
        using System.Text.Json.Nodes;
        using Gocordis.Core;
        using Gocordis.Plugins;
        using Shared.Contracts;
        namespace Shared.Consumer;
        public sealed class ConsumerComponent : Component
        {
            public override string Name => "shared-consumer";
            public override IReadOnlyList<ICapabilityKey> Inject() => [K.Value, ApiGateway.Key];
            public override async ValueTask<Cleanup?> ApplyAsync(Context context)
            {
                var value = context.Require(K.Value);
                var api = context.Require(ApiGateway.Key);
                var owner = ContributionOwner.ForContext(context, "shared", Name);
                await context.RegisterApiQueryAsync(api, owner, "shared/observed",
                    () => new JsonObject { ["value"] = value });
                return null;
            }
        }
        """;

    private static async Task<string> BuildContractsAsync(string root, string workspace)
    {
        var source = Path.Combine(workspace, "contracts");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "contracts.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>Shared.Contracts</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Gocordis.Core"><HintPath>{root}/src/Gocordis.Core/bin/Debug/net10.0/Gocordis.Core.dll</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "Vocabulary.cs"), """
            using Gocordis.Core;
            namespace Shared.Contracts;
            public static class K
            {
                public static readonly Key<string> Value = new("shared.contract.value");
            }
            """);
        await BuildAsync(source);
        return Path.Combine(source, "bin", "Debug", "net10.0", "Shared.Contracts.dll");
    }

    private static async Task<string> BuildPluginAsync(string root, string workspace, string name, string code)
    {
        var source = Path.Combine(workspace, name);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>{name}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Gocordis.Core"><HintPath>{root}/src/Gocordis.Core/bin/Debug/net10.0/Gocordis.Core.dll</HintPath></Reference>
                <Reference Include="Gocordis.Plugins"><HintPath>{root}/src/Gocordis.Plugins/bin/Debug/net10.0/Gocordis.Plugins.dll</HintPath></Reference>
                <ProjectReference Include="{Path.Combine(workspace, "contracts", "contracts.csproj")}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "Plugin.cs"), code);
        await BuildAsync(source);
        return Path.Combine(source, "bin", "Debug", "net10.0");
    }

    private static async Task InstallAsync(
        string payloadDir, string store, string id, string entryAssembly, string entryType)
    {
        var package = Path.Combine(store + ".packages", id + "-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(payloadDir,
            new PluginPackageManifest(id, "1.0.0", id, "[1.0,)",
                Backend: "managed", EntryAssembly: entryAssembly,
                EntryType: entryType, SharedAssemblies: ["Shared.Contracts"]),
            package);
        await new PluginStore(store, HostVersion).InstallAsync(package);
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
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"dotnet build failed in {directory}: {output}\n{error}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }
}
