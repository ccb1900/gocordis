using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Builtin.InsightUi;

/// <summary>A protected builtin plugin that owns only insight UI: framework
/// dashboard, declared/runtime dependency graph, runtime inspector and local
/// inventory. Each page/renderer dies with this Fiber, not with the transport.</summary>
public static class InsightUiPlugin
{
    public const string Id = "gocordis-insight-ui";
    public const string Type = Id;
    public const string Version = "1.0.0";
    public const string ModuleId = "gocordis-insight-ui";
}

public sealed class InsightUiComponent : Component
{
    private static readonly (string Id, string Title, string Route, string Renderer, string Icon, int Order)[]
        Pages =
        [
            ("gocordis-framework", "框架概览", "/console/framework", "framework-dashboard", "appstore", -90),
            ("gocordis-dependencies", "依赖与运行图", "/console/dependencies", "insight-graph", "block", -80),
            ("gocordis-runtime", "运行时检视", "/console/inspector", "runtime-inspector", "profile", -70),
            ("gocordis-inventory", "本地插件仓库", "/console/inventory", "market-catalog", "database", -60),
        ];

    public override string Name => InsightUiPlugin.Id;

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [UiContributionRegistry.Key, ApiGateway.Key, PluginRegistry.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var registry = context.Require(PluginRegistry.Key);
        var owner = ContributionOwner.ForContext(context, InsightUiPlugin.Id, Name);
        registry.ProtectedIds.Add(InsightUiPlugin.Id);

        foreach (var (id, title, route, renderer, icon, order) in Pages)
        {
            await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
                id, title, route, renderer, Icon: icon,
                Description: "受保护内置洞察插件提供的页面",
                Order: order, Group: "Gocordis/Console"));
            await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
                id + "-route", route, id, "shell", order));
            await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
                id + "-nav", title, route, icon, "Gocordis/Console", id, order));
        }

        await BuiltinUiModule.RegisterAsync(
            context, ui, owner, InsightUiPlugin.ModuleId, InsightUiPlugin.Id, InsightUiPlugin.Version);
        return null;
    }
}
