using System.Diagnostics;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.Runtime.Managed;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Product-level proof of the THREE dependency layers for three independent
/// managed plugin packages:
///   - package dependency: declared with --dependency, checked at install
///   - contract dependency: one shared contracts assembly in the Default ALC
///   - capability dependency: Inject()/Provide() over typed keys gates activation
///
/// Each test uses a UNIQUE contract assembly name because SharedAssemblyRegistry
/// seals contracts for the whole process (no in-process unload/replace).
/// </summary>
public sealed class TransitiveContractProductTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task ThreePluginCapabilityChainWithdrawsAndReactivatesTransitively()
    {
        var root = FindRepositoryRoot();
        var contract = "Chain.Contracts." + Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(Path.GetTempPath(), "gocordis-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var contractDll = await BuildContractsAsync(root, workspace, contract);
            var a = await BuildPluginAsync(root, workspace, contract, "chaina", ACode(Ns(contract)));
            var b = await BuildPluginAsync(root, workspace, contract, "chainb", BCode(Ns(contract)));
            var c = await BuildPluginAsync(root, workspace, contract, "chainc", CCode(Ns(contract)));
            foreach (var dir in new[] { a, b, c })
            {
                File.Copy(contractDll, Path.Combine(dir, contract + ".dll"), overwrite: true);
            }

            var store = Path.Combine(workspace, "store");
            await InstallAsync(a, store, contract, "chain-a", "chaina.AComponent");
            await InstallAsync(b, store, contract, "chain-b", "chainb.BComponent");
            await InstallAsync(c, store, contract, "chain-c", "chainc.CComponent");

            var manifestPath = Path.Combine(workspace, "gocordis.app.json");
            await new ApplicationManifest("chain-app", "1.0.0", HostVersion,
            [
                new ApplicationPlugin("chain-a", "[1.0,2.0)", true),
                new ApplicationPlugin("chain-b", "[1.0,2.0)", true),
                new ApplicationPlugin("chain-c", "[1.0,2.0)", true),
            ]).SaveAsync(manifestPath);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
            await app.StartAsync();

            // All three fibers Active: C depends on B depends on A by typed keys.
            var fa = app.Registry.Entries["chain-a"].Fiber!;
            var fb = app.Registry.Entries["chain-b"].Fiber!;
            var fc = app.Registry.Entries["chain-c"].Fiber!;
            Assert.Equal(FiberState.Active, fa.State);
            Assert.Equal(FiberState.Active, fb.State);
            Assert.Equal(FiberState.Active, fc.State);
            Assert.Equal("A→B→C", app.Console.Api.Query("chain/result")?["value"]?.GetValue<string>());

            // Withdraw the root provider: B loses its capability → Pending; C
            // transitively loses B's capability → Pending. Neither is Failed.
            await app.Runtime.DisposeAsync(fa);
            await WaitFor(() => fb.State == FiberState.Pending && fc.State == FiberState.Pending);
            Assert.Null(fb.Failure);
            Assert.Null(fc.Failure);

            // Re-load the provider: the chain reactivates automatically.
            var replacement = app.CreateComponent(new PluginManifest(
                "chain-a-2", "chain-a", true, new Dictionary<string, object?>(), [], [],
                "1.0.0", a, null, "managed", "chaina.dll", "chaina.AComponent", [contract]));
            await app.Runtime.LoadAsync(replacement);
            await WaitFor(() => fb.State == FiberState.Active && fc.State == FiberState.Active);
            Assert.Equal("A→B→C", app.Console.Api.Query("chain/result")?["value"]?.GetValue<string>());

            await app.StopAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ConflictingContractCopiesAcrossPackagesAreRejectedAtHostSeal()
    {
        var root = FindRepositoryRoot();
        var contract = "Conflict.Contracts." + Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(Path.GetTempPath(), "gocordis-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var v1 = await BuildContractsAsync(root, workspace, contract, marker: "ONE");
            var v2 = await BuildContractsAsync(root, workspace, contract, marker: "TWO", outputSubDir: "contracts2");
            var a = await BuildPluginAsync(root, workspace, contract, "conflicta", ACode(Ns(contract)));
            var b = await BuildPluginAsync(root, workspace, contract, "conflictb", BCode(Ns(contract)));
            File.Copy(v1, Path.Combine(a, contract + ".dll"), overwrite: true);
            File.Copy(v2, Path.Combine(b, contract + ".dll"), overwrite: true); // different bytes, same name

            var store = Path.Combine(workspace, "store");
            await InstallAsync(a, store, contract, "conflict-a", "conflicta.AComponent");
            await InstallAsync(b, store, contract, "conflict-b", "conflictb.BComponent");

            var manifestPath = Path.Combine(workspace, "gocordis.app.json");
            await new ApplicationManifest("conflict-app", "1.0.0", HostVersion,
            [
                new ApplicationPlugin("conflict-a", "[1.0,2.0)", true),
                new ApplicationPlugin("conflict-b", "[1.0,2.0)", true),
            ]).SaveAsync(manifestPath);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
            var ex = await Assert.ThrowsAsync<SharedAssemblyConflictException>(() => app.StartAsync());
            Assert.Contains("more than one identity", ex.Message);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    // ---- component sources --------------------------------------------------

    private static string ACode(string ns) => $$"""
        using Gocordis.Core;
        namespace chaina;
        public sealed class AComponent : Component
        {
            public override string Name => "chain-a";
            public override IReadOnlyList<ICapabilityKey> Provide() => [{{ns}}.K.A];
            public override ValueTask<Cleanup?> ApplyAsync(Context context)
            { context.Provide({{ns}}.K.A, "A"); return ValueTask.FromResult<Cleanup?>(null); }
        }
        """;

    private static string BCode(string ns) => $$"""
        using Gocordis.Core;
        namespace chainb;
        public sealed class BComponent : Component
        {
            public override string Name => "chain-b";
            public override IReadOnlyList<ICapabilityKey> Inject() => [{{ns}}.K.A];
            public override IReadOnlyList<ICapabilityKey> Provide() => [{{ns}}.K.B];
            public override ValueTask<Cleanup?> ApplyAsync(Context context)
            { context.Provide({{ns}}.K.B, context.Require({{ns}}.K.A) + "→B"); return ValueTask.FromResult<Cleanup?>(null); }
        }
        """;

    private static string CCode(string ns) => $$"""
        using System.Text.Json.Nodes;
        using Gocordis.Core;
        using Gocordis.Plugins;
        namespace chainc;
        public sealed class CComponent : Component
        {
            public override string Name => "chain-c";
            public override IReadOnlyList<ICapabilityKey> Inject() => [{{ns}}.K.B, ApiGateway.Key];
            public override async ValueTask<Cleanup?> ApplyAsync(Context context)
            {
                var value = context.Require({{ns}}.K.B) + "→C";
                var api = context.Require(ApiGateway.Key);
                var owner = ContributionOwner.ForContext(context, "chain", Name);
                await context.RegisterApiQueryAsync(api, owner, "chain/result",
                    () => new JsonObject { ["value"] = value });
                return null;
            }
        }
        """;

    private static string Ns(string contract) => "n" + contract.Replace('.', '_');

    // ---- build/install helpers ---------------------------------------------

    private static async Task<string> BuildContractsAsync(
        string root, string workspace, string name, string marker = "BASE", string outputSubDir = "contracts")
    {
        var source = Path.Combine(workspace, outputSubDir);
        Directory.CreateDirectory(source);
        var ns = Ns(name);
        await File.WriteAllTextAsync(Path.Combine(source, "contracts.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{name}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{root}/src/Gocordis.Core/Gocordis.Core.csproj" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "Vocabulary.cs"), $$"""
            using Gocordis.Core;
            namespace {{ns}};
            public static class K
            {
                public static readonly Key<string> A = new("{{marker}}.a");
                public static readonly Key<string> B = new("{{marker}}.b");
            }
            """);
        await BuildAsync(source);
        return Path.Combine(source, "bin", "Debug", "net10.0", name + ".dll");
    }

    private static async Task<string> BuildPluginAsync(
        string root, string workspace, string contract, string name, string code)
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
                <ProjectReference Include="{root}/src/Gocordis.Core/Gocordis.Core.csproj" />
                <ProjectReference Include="{root}/src/Gocordis.Plugins/Gocordis.Plugins.csproj" />
                <ProjectReference Include="{Path.Combine(workspace, "contracts", "contracts.csproj")}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "Plugin.cs"), code);
        await BuildAsync(source);
        return Path.Combine(source, "bin", "Debug", "net10.0");
    }

    private static async Task InstallAsync(
        string payloadDir, string store, string contract, string id, string entryType)
    {
        var assembly = Path.GetFileName(Directory.GetFiles(payloadDir, "*.dll")
            .First(p => Path.GetFileNameWithoutExtension(p).EndsWith("a", StringComparison.Ordinal) ||
                        Path.GetFileNameWithoutExtension(p).EndsWith("b", StringComparison.Ordinal) ||
                        Path.GetFileNameWithoutExtension(p).EndsWith("c", StringComparison.Ordinal)));
        var executable = assembly;
        var package = Path.Combine(store + ".packages", id + "-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(payloadDir,
            new PluginPackageManifest(id, "1.0.0", id, "[1.0,)",
                Backend: "managed", EntryAssembly: executable,
                EntryType: entryType, SharedAssemblies: [contract]),
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

    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++)
        {
            await Task.Delay(20);
        }
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
