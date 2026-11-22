using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class ContributionTests
{
    [Fact]
    public async Task PluginContributesUiAndApiWithoutChangingCore()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await runtime.LoadAsync(new HostComponent(host));

        var plugin = await runtime.LoadAsync(new UiPlugin.UiPageComponent());

        Assert.Equal("plugin-page", Assert.Single(host.Ui.Snapshot()).Name);
        Assert.Equal("hello from plugin", host.Api.Query("plugin/hello")!["message"]!.GetValue<string>());

        await runtime.DisposeAsync(plugin);

        Assert.Empty(host.Ui.Snapshot());
        Assert.Null(host.Api.Query("plugin/hello"));
    }

    [Fact]
    public void StaleOwnerCannotRemoveANewContribution()
    {
        var registry = new UiContributionRegistry();
        var oldOwner = ContributionOwner.For("plugin", "component");
        var newOwner = ContributionOwner.For("plugin", "component");
        var contribution = registry.Register("page", "same-name", oldOwner);

        var stale = new UiContribution("page", "same-name", newOwner, new Dictionary<string, object?>());
        registry.Unregister(stale);

        Assert.Same(contribution, Assert.Single(registry.Snapshot()));
        Assert.Throws<InvalidOperationException>(() => registry.Register("page", "same-name", newOwner));
        registry.Unregister(contribution);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task ApiGatewayDispatchesQueriesAndCommands()
    {
        var executed = false;
        var owner = ContributionOwner.For("plugin", "component");
        var api = new ApiGateway();
        api.RegisterQuery("health", () => new JsonObject { ["ok"] = true }, owner);
        api.RegisterCommand("reload", _ =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        }, owner);

        Assert.True(api.Query("health")!["ok"]!.GetValue<bool>());
        Assert.True(await api.ExecuteAsync("reload", null));
        Assert.True(executed);
        Assert.False(await api.ExecuteAsync("missing", null));

        api.UnregisterAll(owner);
        Assert.Null(api.Query("health"));
        Assert.Empty(api.Commands);
    }

    private sealed class HostComponent : Component
    {
        private readonly ConsoleHost _host;

        public HostComponent(ConsoleHost host) => _host = host;

        public override string Name => "console-host";

        public override IReadOnlyList<ICapabilityKey> Provide() =>
            [UiContributionRegistry.Key, ApiGateway.Key];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(UiContributionRegistry.Key, _host.Ui);
            context.Provide(ApiGateway.Key, _host.Api);
            return null;
        }
    }
}
