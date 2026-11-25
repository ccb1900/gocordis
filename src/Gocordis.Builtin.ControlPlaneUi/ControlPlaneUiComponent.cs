using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Builtin.ControlPlaneUi;

/// <summary>A protected builtin plugin that owns only the control-plane UI:
/// its page, navigation, event-feed panel and one ESM client module. The
/// control-plane SERVICE remains a separate Fiber capability.</summary>
public static class ControlPlaneUiPlugin
{
    public const string Id = "gocordis-control-plane-ui";
    public const string Type = Id;
    public const string Version = "1.0.0";
    public const string ModuleId = "gocordis-control-plane-ui";
}

public sealed class ControlPlaneUiComponent : Component
{
    public override string Name => ControlPlaneUiPlugin.Id;

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [UiContributionRegistry.Key, ApiGateway.Key, PluginRegistry.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var registry = context.Require(PluginRegistry.Key);
        var owner = ContributionOwner.ForContext(context, ControlPlaneUiPlugin.Id, Name);
        registry.ProtectedIds.Add(ControlPlaneUiPlugin.Id);

        await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
            "plugin-control-plane", "插件控制面", "/plugins/control-plane", "plugin-explorer",
            Icon: "setting", Description: "受保护内置插件提供的控制面 UI",
            Order: -60, Group: "Gocordis/Console"));
        await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
            "plugin-control-plane-route", "/plugins/control-plane", "plugin-control-plane", "shell", -60));
        await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
            "plugin-control-plane-nav", "插件控制面", "/plugins/control-plane", "setting",
            "Gocordis/Console", "plugin-control-plane", -60));
        await context.RegisterUiPanelAsync(ui, owner, new UiPanelDefinition(
            "gocordis-event-feed", "观察事件流", "bottom", "event-feed"));
        await BuiltinUiModule.RegisterAsync(
            context, ui, owner, ControlPlaneUiPlugin.ModuleId,
            ControlPlaneUiPlugin.Id, ControlPlaneUiPlugin.Version);
        return null;
    }
}
