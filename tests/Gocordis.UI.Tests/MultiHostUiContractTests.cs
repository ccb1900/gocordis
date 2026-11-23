using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.UI.Tests;

/// <summary>
/// Batch 5: one plugin identity and one Fiber, rendered/degraded by each host
/// profile. Hosts are renderers, not plugin lifecycle owners.
/// </summary>
public sealed class MultiHostUiContractTests
{
    [Fact]
    public async Task AllThreeHostProfilesAreAvailable()
    {
        var kinds = UiHostProfiles.All.Select(profile => profile.Kind).ToArray();
        Assert.Equal([UiHostKind.Web, UiHostKind.WinForms, UiHostKind.Avalonia], kinds);
        Assert.All(UiHostProfiles.All, profile => Assert.False(string.IsNullOrWhiteSpace(profile.Name)));
    }

    [Fact]
    public async Task SamePluginRendersOnEachHostAndDegradesWebOnlyResources()
    {
        await using var runtime = new Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));
        var key = new Key<string>("multi-host-value");
        var plugin = await runtime.LoadAsync(new HostCapablePlugin(key, host.Ui));

        var web = WebUiHostProfile.Instance.Plan(host.Ui);
        Assert.Equal(UiHostKind.Web, web.Host);
        Assert.Contains(web.Rendered, t => t.Kind == "page" && t.Id == "multi-host");
        Assert.Contains(web.Rendered, t => t.Kind == "panel" && t.Id == "multi-host-panel");
        Assert.Contains(web.Rendered, t => t.Kind == "client-module" && t.Id == "multi-host-ui");
        Assert.Contains(web.Rendered, t => t.Descriptor.StartsWith("esm:", StringComparison.Ordinal));
        Assert.Empty(web.Degraded);

        var winforms = WinFormsUiHostProfile.Instance.Plan(host.Ui);
        Assert.Equal(UiHostKind.WinForms, winforms.Host);
        Assert.Contains(winforms.Rendered, t => t.Kind == "page" && t.Descriptor.StartsWith("winforms:", StringComparison.Ordinal));
        Assert.Contains(winforms.Rendered, t => t.Kind == "panel" && t.Descriptor.StartsWith("winforms:", StringComparison.Ordinal));
        var winformsDegraded = Assert.Single(winforms.Degraded);
        Assert.Equal("client-module", winformsDegraded.Kind);
        Assert.Contains("web", winformsDegraded.Reason, StringComparison.OrdinalIgnoreCase);

        var avalonia = AvaloniaUiHostProfile.Instance.Plan(host.Ui);
        Assert.Equal(UiHostKind.Avalonia, avalonia.Host);
        Assert.Contains(avalonia.Rendered, t => t.Kind == "page" && t.Descriptor.StartsWith("avalonia:", StringComparison.Ordinal));
        var avaloniaDegraded = Assert.Single(avalonia.Degraded);
        Assert.Equal("client-module", avaloniaDegraded.Kind);

        // Profiles are renderers only: no extra fibers and no plugin lifecycle writes.
        Assert.Equal(2, runtime.Snapshot().Fibers.Count);
        Assert.Equal(FiberState.Active, plugin.State);
    }

    [Fact]
    public async Task ProfilesFollowOnePluginLifecycleWithoutExtraRegistries()
    {
        await using var runtime = new Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));
        var key = new Key<string>("lifecycle-host-value");
        var plugin = await runtime.LoadAsync(new HostCapablePlugin(key, host.Ui));

        foreach (var profile in UiHostProfiles.All)
        {
            Assert.NotEmpty(profile.Plan(host.Ui).Rendered);
        }

        await runtime.DisposeAsync(plugin);

        foreach (var profile in UiHostProfiles.All)
        {
            Assert.Empty(profile.Plan(host.Ui).Rendered);
            Assert.Empty(profile.Plan(host.Ui).Degraded);
        }

        var again = await runtime.LoadAsync(new HostCapablePlugin(key, host.Ui));
        Assert.Equal(FiberState.Active, again.State);
        foreach (var profile in UiHostProfiles.All)
        {
            var plan = profile.Plan(host.Ui);
            Assert.Contains(plan.Rendered, t => t.Kind == "page");
        }
    }

    private sealed class HostComponent : Component
    {
        private readonly ConsoleHost _host;
        public HostComponent(ConsoleHost host) => _host = host;
        public override string Name => "console-host";
        public override IReadOnlyList<ICapabilityKey> Provide() => [UiContributionRegistry.Key, ApiGateway.Key];
        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _host.Ui);
            context.Provide(ApiGateway.Key, _host.Api);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class HostCapablePlugin : Component
    {
        private readonly Key<string> _key;
        private readonly UiContributionRegistry _ui;

        public HostCapablePlugin(Key<string> key, UiContributionRegistry ui)
        {
            _key = key;
            _ui = ui;
        }

        public override string Name => "multi-host-plugin";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];
        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, "from-plugin");
            var owner = ContributionOwner.ForContext(context, "multi-host", Name);
            await context.RegisterUiPageAsync(_ui, owner,
                new UiPageDefinition("multi-host", "Multi Host", "/multi-host", "generic"));
            await context.RegisterUiPanelAsync(_ui, owner,
                new UiPanelDefinition("multi-host-panel", "Panel", "main", "generic"));
            await context.RegisterUiClientModuleAsync(_ui, owner,
                new UiClientModuleDefinition("multi-host-ui", "/client-modules/multi-host/ui.js", "v1", "ui.js"));
            return null;
        }
    }
}
