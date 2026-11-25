using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Red tests for first-class UI contribution kinds. Route / Navigation /
/// Page / Extension / Resource are explicit declarations, installed through
/// the owning activation's reversible effects (never a second registry).
/// </summary>
public sealed class UiContributionContractTests
{
    [Fact]
    public async Task RouteNavigationExtensionAndResourceAreOwnerBoundAndReversible()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var plugin = await runtime.LoadAsync(new FullUiFeature());

        var route = Assert.Single(host.Ui.Routes());
        Assert.Equal("plugin-route", route.Id);
        Assert.Equal("/plugin", route.Path);
        Assert.Equal("plugin-page", route.PageId);
        Assert.Equal(plugin.Id, route.Owner.FiberId);
        Assert.True(route.Owner.ActivationId > 0);

        var navigation = Assert.Single(host.Ui.Navigations());
        Assert.Equal("运营/插件", navigation.Group);
        Assert.Equal("/plugin", navigation.Route);
        Assert.Equal(plugin.Id, navigation.Owner.FiberId);

        var page = Assert.Single(host.Ui.Pages());
        Assert.Equal("plugin-page", page.Id);
        Assert.Equal("运营", page.Group);
        Assert.Equal(2, page.Order);

        var extension = Assert.Single(host.Ui.Extensions());
        Assert.Equal("sidebar.footer", extension.Slot);
        Assert.Equal("contract", extension.Renderer);
        Assert.Equal("ready", extension.Props!["state"]!.GetValue<string>());
        Assert.Equal(plugin.Id, extension.Owner.FiberId);

        var resource = Assert.Single(host.Ui.Resources());
        Assert.Equal("plugin-assets", resource.Id);
        Assert.Equal("assets", resource.Kind);
        Assert.Equal("/client-modules/plugin-ui", resource.Path);
        Assert.Equal(plugin.Id, resource.Owner.FiberId);

        var revision = host.Ui.Revision;
        await runtime.DisposeAsync(plugin);

        Assert.Empty(host.Ui.Routes());
        Assert.Empty(host.Ui.Navigations());
        Assert.Empty(host.Ui.Pages());
        Assert.Empty(host.Ui.Extensions());
        Assert.Empty(host.Ui.Resources());
        Assert.True(host.Ui.Revision > revision);
    }

    [Fact]
    public void StaleOwnerCannotUndoANewerContributionOfAnyKind()
    {
        var registry = new UiContributionRegistry();
        var oldOwner = ContributionOwner.For("plugin", "component");
        var newOwner = ContributionOwner.For("plugin", "component");

        var oldRoute = registry.RegisterRoute(oldOwner,
            new UiRouteDefinition("route", "/old", "page"));
        var oldNavigation = registry.RegisterNavigation(oldOwner,
            new UiNavigationDefinition("nav", "Old", "/old"));
        var oldExtension = registry.RegisterExtension(oldOwner,
            new UiExtensionDefinition("ext", "slot", "old"));
        var oldResource = registry.RegisterResource(oldOwner,
            new UiResourceDefinition("res", "assets", "/old"));

        registry.UnregisterRoute(oldRoute);
        registry.UnregisterNavigation(oldNavigation);
        registry.UnregisterExtension(oldExtension);
        registry.UnregisterResource(oldResource);

        var route = registry.RegisterRoute(newOwner, new UiRouteDefinition("route", "/new", "page"));
        var navigation = registry.RegisterNavigation(newOwner, new UiNavigationDefinition("nav", "New", "/new"));
        var extension = registry.RegisterExtension(newOwner, new UiExtensionDefinition("ext", "slot", "new"));
        var resource = registry.RegisterResource(newOwner, new UiResourceDefinition("res", "assets", "/new"));

        registry.UnregisterRoute(oldRoute);
        registry.UnregisterNavigation(oldNavigation);
        registry.UnregisterExtension(oldExtension);
        registry.UnregisterResource(oldResource);

        Assert.Equal("/new", Assert.Single(registry.Routes()).Path);
        Assert.Equal("New", Assert.Single(registry.Navigations()).Label);
        Assert.Equal("new", Assert.Single(registry.Extensions()).Renderer);
        Assert.Equal("/new", Assert.Single(registry.Resources()).Path);

        registry.UnregisterRoute(route);
        registry.UnregisterNavigation(navigation);
        registry.UnregisterExtension(extension);
        registry.UnregisterResource(resource);
        Assert.Empty(registry.Routes());
        Assert.Empty(registry.Navigations());
        Assert.Empty(registry.Extensions());
        Assert.Empty(registry.Resources());
    }

    private sealed class HostComponent : Component
    {
        private readonly ConsoleHost _host;
        public HostComponent(ConsoleHost host) => _host = host;

        public override string Name => "ui-contract-host";
        public override IReadOnlyList<ICapabilityKey> Provide() => [UiContributionRegistry.Key, ApiGateway.Key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _host.Ui);
            context.Provide(ApiGateway.Key, _host.Api);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class FullUiFeature : Component
    {
        public override string Name => "ui-contract-plugin";

        public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var ui = context.Require(UiContributionRegistry.Key);
            var owner = ContributionOwner.ForContext(context, "ui-contract-plugin", Name);

            await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
                "plugin-page", "Plugin", "/plugin", "generic", Order: 2, Group: " 运营 "));
            await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
                "plugin-route", "/plugin", "plugin-page", Layout: "shell", Order: 3));
            await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
                "plugin-nav", "插件", "/plugin", Group: "运营 / 插件", Order: 1));
            await context.RegisterUiExtensionAsync(ui, owner, new UiExtensionDefinition(
                "plugin-ext", "sidebar.footer", "contract", "plugin-page",
                new JsonObject { ["state"] = "ready" }, 4));
            await context.RegisterUiResourceAsync(ui, owner, new UiResourceDefinition(
                "plugin-assets", "assets", "/client-modules/plugin-ui", "text/javascript"));
            return null;
        }
    }
}
