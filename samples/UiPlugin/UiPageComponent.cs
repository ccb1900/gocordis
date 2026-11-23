using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;

namespace UiPlugin;

/// <summary>A plugin contributing UI and API without modifying Core or the host.</summary>
public sealed class UiPageComponent : Component
{
    private UiContribution? _page;
    private ContributionOwner? _owner;

    public override string Name => "ui-page-plugin";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [UiContributionRegistry.Key, ApiGateway.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.For("ui-plugin", Name);
        _owner = owner;
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () =>
            {
                if (_page is not null)
                {
                    ui.Unregister(_page);
                }

                api.UnregisterAll(owner);
                return ValueTask.CompletedTask;
            });

        _page = ui.Register("page", "plugin-page", owner);
        api.RegisterQuery("plugin/hello", () => new JsonObject { ["message"] = "hello from plugin" }, owner);
        return null;
    }
}
