using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// End-to-end proof that the default product host executes a real managed
/// Component from a compiled C# plugin package: the declaration drives Core
/// activation, and disable/enable/upgrade/uninstall retire the same fibers.
/// No sleeps: the compiler is awaited with WaitForExitAsync + cancellation.
/// </summary>
public sealed class ManagedPluginProductE2ETests
{
    private const string PluginId = "e2e-plugin";
    private const string LoaderKeyV1 = PluginId + "@1.0.0";
    private const string LoaderKeyV2 = PluginId + "@2.0.0";

    private readonly Workspace _temp = new();

    [Fact]
    public async Task RealManagedPluginFollowsDeclarationLifecycle()
    {
        var root = FindRepositoryRoot();
        var logPath = _temp.File("effects.log");
        Environment.SetEnvironmentVariable("GOCORDIS_E2E_LOG", logPath);
        try
        {
            var storePath = _temp.Path("store");
            var manifestPath = _temp.File("gocordis.app.json");
            await new ApplicationManifest("e2e-app", "1.0.0", "1.0.0",
                [new ApplicationPlugin(PluginId, "[1.0,2.0)", false)]).SaveAsync(manifestPath);

            // Install bytes first; the declaration disables the plugin so startup
            // must not open an AssemblyLoadContext for it.
            var v1Package = await BuildAndPackAsync(root, "1.0.0", "v1");
            await new PluginStore(storePath, "1.0.0").InstallAsync(v1Package);

            await using var app = new PlatformApplication(new PlatformHostOptions(
                manifestPath, storePath, Host: UiHostKind.WinForms, HostVersion: "1.0.0"));
            await app.StartAsync();

            Assert.NotNull(app.Control);
            Assert.False(app.Managed.Loader.Plugins.ContainsKey(LoaderKeyV1));
            Assert.DoesNotContain(app.Console.Ui.Pages(),
                page => page.Id.StartsWith("e2e-page-", StringComparison.Ordinal));
            Assert.Null(app.Registry.Entries[PluginId].Fiber);

            // ---- Enable: real assembly is loaded, Apply/Effect/Provide run ----
            var enabled = await app.Control.SetEnabledAsync(PluginId, true);
            Assert.True(enabled.Accepted, enabled.Error);
            var v1Fiber = app.Registry.Entries[PluginId].Fiber!;
            Assert.Equal(FiberState.Active, v1Fiber.State);
            Assert.Equal("e2e-plugin",
                Assert.Single(app.Runtime.Snapshot().Fibers, fiber => fiber.Id == v1Fiber.Id).Name);
            Assert.Equal("v1", ReadValue(app, LoaderKeyV1));
            Assert.Contains("effect:v1", await File.ReadAllTextAsync(logPath), StringComparison.Ordinal);
            Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "e2e-page-v1");
            Assert.Equal("v1", app.Console.Api.Query("e2e/value")?["value"]?.GetValue<string>());
            var weakV1 = CaptureContext(app, LoaderKeyV1);
            var v1Key = GetKey(app, LoaderKeyV1);

            // ---- Disable: Core unwinds the effect and releases the ALC ----
            var disabled = await app.Control.SetEnabledAsync(PluginId, false);
            Assert.True(disabled.Accepted, disabled.Error);
            await v1Fiber.GoneAsync();
            Assert.Null(app.Registry.Entries[PluginId].Fiber);
            AssertCapabilityGone(app, v1Key);
            v1Key = null!; // drop the ALC-rooting key before the reclaim check
            Assert.Contains("undo:v1", await File.ReadAllTextAsync(logPath), StringComparison.Ordinal);
            Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "e2e-page-v1");
            Assert.Null(app.Console.Api.Query("e2e/value"));
            Assert.False(app.Managed.Loader.Plugins.ContainsKey(LoaderKeyV1));
            v1Fiber = null!; // the Fiber roots its Component and therefore the ALC
            Reclaimed(weakV1);

            // ---- Enable again: a fresh activation installs the effects anew ----
            var reenabled = await app.Control.SetEnabledAsync(PluginId, true);
            Assert.True(reenabled.Accepted, reenabled.Error);
            Assert.Equal(FiberState.Active, app.Registry.Entries[PluginId].Fiber!.State);
            Assert.Equal("v1", ReadValue(app, LoaderKeyV1));
            var effectsAfterReenable = await File.ReadAllTextAsync(logPath);
            Assert.Equal(2, CountLines(effectsAfterReenable, "effect:v1"));
            Assert.Equal(1, CountLines(effectsAfterReenable, "undo:v1"));
            var weakBeforeUpgrade = CaptureContext(app, LoaderKeyV1);
            var keyBeforeUpgrade = GetKey(app, LoaderKeyV1);

            // ---- Upgrade: new code, new key; old fiber retires before deletion ----
            var v2Package = await BuildAndPackAsync(root, "2.0.0", "v2");
            await using (var stream = File.OpenRead(v2Package))
            {
                var upload = await app.Control.UploadAsync(stream, "e2e-2.0.0.gocordis-plugin");
                var upgraded = await app.Control.UpgradeAsync(PluginId, upload.UploadKey, "[2.0.0]");
                Assert.True(upgraded.Accepted, upgraded.Error);
            }

            Assert.Equal("v2", ReadValue(app, LoaderKeyV2));
            Assert.False(app.Managed.Loader.Plugins.ContainsKey(LoaderKeyV1));
            Assert.False(Directory.Exists(Path.Combine(storePath, PluginId, "1.0.0")));
            var effectsAfterUpgrade = await File.ReadAllTextAsync(logPath);
            Assert.Contains("undo:v1", effectsAfterUpgrade, StringComparison.Ordinal);
            Assert.Contains("effect:v2", effectsAfterUpgrade, StringComparison.Ordinal);
            Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "e2e-page-v1");
            Assert.Contains(app.Console.Ui.Pages(), page => page.Id == "e2e-page-v2");
            AssertCapabilityGone(app, keyBeforeUpgrade);
            keyBeforeUpgrade = null!;
            Reclaimed(weakBeforeUpgrade);
            var weakV2 = CaptureContext(app, LoaderKeyV2);
            var v2Key = GetKey(app, LoaderKeyV2);

            // ---- Uninstall: no declaration, no fiber, no package bytes ----
            var uninstalled = await app.Control.UninstallAsync(PluginId, "[2.0.0]");
            Assert.True(uninstalled.Accepted, uninstalled.Error);
            Assert.False(app.Registry.Entries.ContainsKey(PluginId));
            AssertCapabilityGone(app, v2Key);
            v2Key = null!;
            Assert.False(app.Managed.Loader.Plugins.ContainsKey(LoaderKeyV2));
            Assert.False(Directory.Exists(Path.Combine(storePath, PluginId, "2.0.0")));
            Assert.DoesNotContain(app.Console.Ui.Pages(), page => page.Id == "e2e-page-v2");
            Reclaimed(weakV2);

            // ---- Install via the control plane after startup: this exercises
            // the injected manifest-aware factory for a type not known at boot.
            await using (var stream = File.OpenRead(v2Package))
            {
                var upload = await app.Control.UploadAsync(stream, "e2e-2.0.0-reinstall.gocordis-plugin");
                var reinstalled = await app.Control.InstallAsync(upload.UploadKey);
                Assert.True(reinstalled.Accepted, reinstalled.Error);
            }

            Assert.Equal(FiberState.Active, app.Registry.Entries[PluginId].Fiber!.State);
            Assert.Equal("v2", ReadValue(app, LoaderKeyV2));
            Assert.Equal(2, CountLines(await File.ReadAllTextAsync(logPath), "effect:v2"));

            await app.StopAsync();
            Assert.Empty(app.Runtime.Snapshot().Fibers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOCORDIS_E2E_LOG", null);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadValue(PlatformApplication app, string loaderKey) =>
        app.Runtime.RequireCapability(GetKey(app, loaderKey));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Key<string> GetKey(PlatformApplication app, string loaderKey)
    {
        var component = app.Managed.Loader.Plugins[loaderKey].Component;
        return (Key<string>)component.GetType()
            .GetField("LogKey", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertCapabilityGone(PlatformApplication app, Key<string> key) =>
        Assert.Throws<DependencyMissingException>(() => app.Runtime.RequireCapability(key));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureContext(PlatformApplication app, string loaderKey) =>
        new(app.Managed.Loader.Plugins[loaderKey].Context);

    private static int CountLines(string text, string line) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(entry => entry == line);

    private static void Reclaimed(WeakReference weak)
    {
        for (var attempt = 0; attempt < 12 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "managed plugin AssemblyLoadContext was not reclaimed");
    }

    private async Task<string> BuildAndPackAsync(string root, string version, string value)
    {
        var source = _temp.Path($"plugin-source-{version}");
        Directory.CreateDirectory(source);
        var project = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>e2e</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{root}/src/Gocordis.Core/Gocordis.Core.csproj" />
                <ProjectReference Include="{root}/src/Gocordis.Plugins/Gocordis.Plugins.csproj" />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "e2e.csproj"), project);

        var code = """
            using System.Text.Json.Nodes;
            using Gocordis.Core;
            using Gocordis.Plugins;

            namespace E2ePlugin;

            public sealed class Plugin : Component
            {
                public static readonly Key<string> LogKey = new("e2e.log");

                public override string Name => "e2e-plugin";

                public override IReadOnlyList<ICapabilityKey> Inject() =>
                    [UiContributionRegistry.Key, ApiGateway.Key];

                public override IReadOnlyList<ICapabilityKey> Provide() => [LogKey];

                public override async ValueTask<Cleanup?> ApplyAsync(Context context)
                {
                    var ui = context.Require(UiContributionRegistry.Key);
                    var api = context.Require(ApiGateway.Key);
                    var value = "__VALUE__";
                    var log = Environment.GetEnvironmentVariable("GOCORDIS_E2E_LOG");
                    if (log is not null) File.AppendAllText(log, $"apply:{value}\n");

                    await context.EffectAsync(
                        install: () =>
                        {
                            if (log is not null) File.AppendAllText(log, $"effect:{value}\n");
                            context.Provide(LogKey, value);
                            return ValueTask.CompletedTask;
                        },
                        inverse: () =>
                        {
                            if (log is not null) File.AppendAllText(log, $"undo:{value}\n");
                            return ValueTask.CompletedTask;
                        });

                    var owner = ContributionOwner.ForContext(context, "e2e", Name);
                    await context.RegisterUiPageAsync(ui, owner,
                        new UiPageDefinition("e2e-page-" + value, "E2E " + value, "/e2e", "generic"));
                    await context.RegisterApiQueryAsync(api, owner, "e2e/value",
                        () => new JsonObject { ["value"] = value });
                    return null;
                }
            }
            """.Replace("__VALUE__", value, StringComparison.Ordinal);
        await File.WriteAllTextAsync(System.IO.Path.Combine(source, "Plugin.cs"), code);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = source, // outside the repo: no global.json SDK pin
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add("e2e.csproj");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("q");
        // Reused MSBuild nodes inherit the redirected stdout/stderr pipes and
        // keep them open after the build exits, so ReadToEndAsync never finishes.
        start.ArgumentList.Add("-nodeReuse:false");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeout.Token);
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(30));
        var buildOut = await stdout;
        var buildError = await stderr;
        Assert.True(process.ExitCode == 0, $"dotnet build failed: {buildOut}\n{buildError}");

        var bin = System.IO.Path.Combine(source, "bin", "Release", "net10.0");
        var payload = _temp.Path($"payload-{version}");
        Directory.CreateDirectory(payload);
        foreach (var name in new[] { "e2e.dll", "e2e.deps.json" })
        {
            var built = System.IO.Path.Combine(bin, name);
            Assert.True(File.Exists(built), $"missing build artifact '{built}'");
            File.Copy(built, System.IO.Path.Combine(payload, name), overwrite: true);
        }

        var package = _temp.File($"e2e-{version}.gocordis-plugin");
        var manifest = new PluginPackageManifest(
            PluginId, version, "e2e", "[1.0,)",
            DisplayName: "E2E Plugin",
            Backend: "managed",
            EntryAssembly: "e2e.dll",
            EntryType: "E2ePlugin.Plugin");
        await PluginPackageWriter.CreateAsync(payload, manifest, package);
        return package;
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

    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-managed-e2e-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

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
