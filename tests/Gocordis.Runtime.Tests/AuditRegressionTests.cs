using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Runtime.Managed;
using Gocordis.Runtime.Process;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>Regressions for defects found in independent review round 1.</summary>
public sealed class AuditRegressionTests
{
    [Fact]
    public async Task ManagedPluginSharesHostContractAssemblies()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        await using var loader = new ManagedPluginLoader();
        var plugin = loader.Load("ui", SamplePath("UiPlugin"), "UiPlugin.UiPageComponent");
        var fiber = await runtime.LoadAsync(plugin.Component);

        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal("plugin-page", Assert.Single(host.Ui.Snapshot()).Name);
        Assert.Same(UiContributionRegistry.Key, plugin.Component.Inject()[0]);
    }

    [Fact]
    public async Task ConcurrentProcessCallsDoNotCorruptTheReader()
    {
        await using var client = await ProcessPluginClient.StartAsync(ProcessExecutable());

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            client.CallAsync("echo", new { text = index.ToString() })));

        Assert.Equal(12, results.Length);
        Assert.Equal(
            Enumerable.Range(0, 12).Select(index => $"echo:{index}").OrderBy(value => value),
            results.Select(result => result!["text"]!.GetValue<string>()).OrderBy(value => value));
    }

    [Fact]
    public async Task ProtectedPluginsSurviveADeclarationThatOmitsThem()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));

        await registry.ApplyAsync([Manifest("system", "echo", true, "A")]);
        registry.ProtectedIds.Add("system");

        await registry.ApplyAsync([]);

        Assert.True(registry.Entries.ContainsKey("system"));
        Assert.Equal(FiberState.Active, registry.Entries["system"].Fiber!.State);
    }

    [Fact]
    public async Task FailedFactoryLeavesNoPartialState()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));
        registry.RegisterFactory("boom", _ => throw new InvalidOperationException("factory failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyAsync([
            Manifest("ok", "echo", true, "A"),
            Manifest("bad", "boom", true, "B"),
        ]));

        Assert.Empty(registry.Entries);
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task DuplicateIdsAreRejectedBeforeAnyChange()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", config => new Echo((string)config["value"]!));

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyAsync([
            Manifest("same", "echo", true, "A"),
            Manifest("same", "echo", true, "B"),
        ]));
        Assert.Empty(registry.Entries);
    }


    [Fact]
    public async Task AlcIsReclaimedAfterARealPluginLifecycle()
    {
        var reference = await LoadActivateDisposeUnload();
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(reference.IsAlive, "ALC was not reclaimed after plugin lifecycle");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static async Task<WeakReference> LoadActivateDisposeUnload()
        {
            await using var runtime = new Gocordis.Core.Runtime();
            var host = new ConsoleHost(runtime);
            await runtime.LoadAsync(new HostComponent(host));
            var loader = new ManagedPluginLoader();
            var plugin = loader.Load("lifecycle", SamplePath("UiPlugin"), "UiPlugin.UiPageComponent");
            var weak = new WeakReference(plugin.Context);
            var fiber = await runtime.LoadAsync(plugin.Component);
            Assert.Equal(FiberState.Active, fiber.State);
            await runtime.DisposeAsync(fiber);
            loader.Unload("lifecycle");
            await loader.DisposeAsync();
            return weak;
        }
    }

    private static PluginManifest Manifest(string id, string type, bool enabled, string value) =>
        new(id, type, enabled, new Dictionary<string, object?> { ["value"] = value }, [], []);

    private static string SamplePath(string project) => Path.Combine(
        FindRoot(), "samples", project, "bin", "Debug", "net10.0", $"{project}.dll");

    private static string ProcessExecutable()
    {
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return Path.Combine(FindRoot(), "samples", "EchoProcPlugin", "bin", "Debug", "net10.0", $"EchoProcPlugin{suffix}");
    }

    private static string FindRoot()
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

    private sealed class HostComponent : Component
    {
        private readonly ConsoleHost _host;

        public HostComponent(ConsoleHost host) => _host = host;

        public override string Name => "audit-host";

        public override IReadOnlyList<ICapabilityKey> Provide() =>
            [UiContributionRegistry.Key, ApiGateway.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _host.Ui);
            context.Provide(ApiGateway.Key, _host.Api);
            return null;
        }
    }

    private sealed class Echo : Component
    {
        private readonly Key<string> _key;

        public Echo(string value) => _key = new Key<string>($"echo-{value}");

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, "value");
            return null;
        }
    }
}
