using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch A: UI contribution is one reversible, owner-scoped effect of the
/// same Component that contributes backend capabilities.
/// </summary>
public sealed class UiCompositionTests
{
    [Fact]
    public async Task PluginRegistersPageModuleAndApiThenLifecycleReversesAll()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var plugin = await runtime.LoadAsync(new UiFeature("demo", "/demo"));

        Assert.Equal("demo", Assert.Single(host.Ui.Pages()).Id);
        Assert.Equal("demo-ui", Assert.Single(host.Ui.ClientModules()).Name);
        Assert.Equal("hello", host.Api.Query("demo/hello")!["message"]!.GetValue<string>());
        var revisionAfterEnable = host.Ui.Revision;
        Assert.True(revisionAfterEnable > 0);

        await runtime.DisposeAsync(plugin);

        Assert.Empty(host.Ui.Pages());
        Assert.Empty(host.Ui.ClientModules());
        Assert.Null(host.Api.Query("demo/hello"));
        Assert.True(host.Ui.Revision > revisionAfterEnable);
        var revisionAfterDisable = host.Ui.Revision;

        var again = await runtime.LoadAsync(new UiFeature("demo", "/demo"));
        Assert.Equal("demo", Assert.Single(host.Ui.Pages()).Id);
        Assert.Equal("demo-ui", Assert.Single(host.Ui.ClientModules()).Name);
        Assert.True(host.Ui.Revision > revisionAfterDisable);
        await runtime.DisposeAsync(again);
    }

    [Fact]
    public async Task SamePluginIdentityCanBeReloadedWithoutDuplicateRegistration()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        for (var i = 0; i < 3; i++)
        {
            var plugin = await runtime.LoadAsync(new UiFeature("repeat", "/repeat"));
            Assert.Single(host.Ui.Pages());
            Assert.Single(host.Ui.ClientModules());
            await runtime.DisposeAsync(plugin);
            Assert.Empty(host.Ui.Pages());
        }
    }

    [Fact]
    public async Task DuplicateIdFromAnotherActivationIsRejected()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var first = await runtime.LoadAsync(new UiFeature("same", "/one"));
        var second = await runtime.LoadAsync(new UiFeature("same", "/two"));

        Assert.Equal(FiberState.Active, first.State);
        Assert.Equal(FiberState.Failed, second.State);
        Assert.Equal("same", Assert.Single(host.Ui.Pages()).Id);
    }

    [Fact]
    public void StaleOwnerCannotRemoveNewRegistration()
    {
        var registry = new UiContributionRegistry();
        var oldOwner = ContributionOwner.For("plugin", "component");
        var newOwner = ContributionOwner.For("plugin", "component");

        var old = registry.RegisterPage(oldOwner, new UiPageDefinition("page", "Old", "/old", "generic"));
        registry.UnregisterPage(old);

        var current = registry.RegisterPage(newOwner, new UiPageDefinition("page", "New", "/new", "generic"));
        registry.UnregisterPage(old);

        Assert.Equal("New", Assert.Single(registry.Pages()).Title);
        registry.UnregisterPage(current);
        Assert.Empty(registry.Pages());
    }

    [Fact]
    public void PageGroupIsNormalizedAndRoundTrips()
    {
        var registry = new UiContributionRegistry();
        var owner = ContributionOwner.For("plugin", "component");
        var handle = registry.RegisterPage(owner,
            new UiPageDefinition("page", "Grouped", "/grouped", "generic",
                Group: " 运维 / 监控 / "));
        try
        {
            Assert.Equal("运维/监控", Assert.Single(registry.Pages()).Group);
        }
        finally
        {
            registry.UnregisterPage(handle);
        }

        // Null / empty groups stay top-level (no submenu).
        var plain = registry.RegisterPage(owner,
            new UiPageDefinition("plain", "Plain", "/plain", "generic", Group: "///"));
        try
        {
            Assert.Null(Assert.Single(registry.Pages()).Group);
        }
        finally
        {
            registry.UnregisterPage(plain);
        }
    }

    [Fact]
    public async Task OwnerCarriesActivationIdentity()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var probe = new OwnerProbe(host.Ui);
        var plugin = await runtime.LoadAsync(probe);
        Assert.NotEqual(Guid.Empty, probe.FiberIdFromContext);
        Assert.True(probe.ActivationIdFromContext > 0);

        var card = Assert.Single(probe.OwnerCards!);
        Assert.Equal(probe.FiberIdFromContext, card.FiberId);
        Assert.Equal(probe.ActivationIdFromContext, card.ActivationId);
        Assert.Equal(plugin.Id, card.FiberId);

        Assert.Equal(plugin.Id, Assert.Single(host.Ui.Pages()).Owner.FiberId);
    }

    [Fact]
    public void ConcurrentRegistrationAndSnapshotsAreLinearizable()
    {
        var registry = new UiContributionRegistry();
        var owner = ContributionOwner.For("plugin", "component");

        Parallel.For(0, 64, i =>
        {
            var handle = registry.RegisterPage(owner,
                new UiPageDefinition($"page-{i}", $"Page {i}", $"/p{i}", "generic"));
            _ = registry.Pages();
            registry.UnregisterPage(handle);
        });

        Assert.Empty(registry.Pages());
        Assert.True(registry.Revision >= 128);
    }

    [Fact]
    public async Task RuntimeCloseRevokesAllUiContributions()
    {
        var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));
        await runtime.LoadAsync(new UiFeature("close", "/close"));

        Assert.Single(host.Ui.Pages());
        await runtime.DisposeAsync();
        Assert.Empty(host.Ui.Pages());
        Assert.Empty(host.Ui.ClientModules());
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

    private sealed class UiFeature : Component
    {
        private readonly string _route;

        public UiFeature(string id, string route)
        {
            Name = id;
            _route = route;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key, ApiGateway.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            var api = context.Require(ApiGateway.Key);
            var owner = ContributionOwner.ForContext(context, "ui-plugin", Name);

            await context.RegisterUiPageAsync(ui, owner,
                new UiPageDefinition(Name, Name, _route, "generic"));
            await context.RegisterUiClientModuleAsync(ui, owner,
                new UiClientModuleDefinition($"{Name}-ui", "/client-modules/demo-ui/ui.js", "v1", "ui.js"));
            await context.RegisterApiQueryAsync(api, owner, $"{Name}/hello",
                () => new JsonObject { ["message"] = "hello" });
            return null;
        }
    }

    private sealed class OwnerProbe : Component
    {
        private readonly UiContributionRegistry _ui;
        public OwnerProbe(UiContributionRegistry ui) => _ui = ui;

        public Guid FiberIdFromContext { get; private set; }
        public long ActivationIdFromContext { get; private set; }
        public IReadOnlyList<ContributionOwner>? OwnerCards { get; private set; }

        public override string Name => "owner-probe";
        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            FiberIdFromContext = context.FiberId;
            ActivationIdFromContext = context.ActivationId;
            var owner = ContributionOwner.ForContext(context, "probe", Name);
            OwnerCards = [owner];
            await context.RegisterUiPageAsync(_ui, owner,
                new UiPageDefinition("probe", "Probe", "/probe", "generic"));
            return null;
        }
    }
}
