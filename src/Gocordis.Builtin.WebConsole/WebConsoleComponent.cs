using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Plugins;

namespace Gocordis.Builtin.WebConsole;

/// <summary>
/// The Web Console as a real builtin plugin. Every transport, API, UI
/// contribution and frontend asset belongs to this fiber. The host only
/// supplies framework capabilities; it neither constructs nor starts the HTTP
/// server. Start/stop are one reversible Effect, so Runtime owns the lifecycle
/// and unwind is strict LIFO like every other plugin.
/// </summary>
public sealed class WebConsoleComponent : Component
{
    public const string ModuleId = "gocordis-console-ui";
    private const string StatusQuery = "gocordis-console/status";
    private const string PingCommand = "gocordis-console/ping";

    private WebUiServer? _server;
    private int _pings;

    private static readonly (string Id, string Title, string Route, string Renderer, string Icon, int Order)[]
        BuiltinPages =
        [
            ("gocordis-console-page", "内置控制台", "/console", "gocordis-console-overview", "dashboard", -100),
            ("gocordis-framework", "框架概览", "/console/framework", "framework-dashboard", "appstore", -90),
            ("gocordis-dependencies", "依赖与运行图", "/console/dependencies", "insight-graph", "block", -80),
            ("gocordis-runtime", "运行时检视", "/console/inspector", "runtime-inspector", "profile", -70),
            ("plugin-control-plane", "插件控制面", "/plugins/control-plane", "plugin-explorer", "setting", -60),
        ];

    public override string Name => WebConsolePlugin.Id;

    public override IReadOnlyList<ICapabilityKey> Inject() =>
    [
        ConsoleHost.Key,
        UiContributionRegistry.Key,
        ApiGateway.Key,
        PluginRegistry.Key,
        ControlPlaneKeys.Service,
        ControlPlaneKeys.Recovery,
        WebConsoleKeys.Options,
        RouteGuardKeys.Service,
    ];

    public override IReadOnlyList<ICapabilityKey> Provide() => [WebConsoleKeys.Service];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var options = context.Require(WebConsoleKeys.Options);
        context.Provide(WebConsoleKeys.Service, new WebConsoleHandle(() => _server));

        // UI/API contributions belong to the builtin plugin even when the HTTP
        // transport is disabled (native render profiles, degraded recovery).
        var host = context.Require(ConsoleHost.Key);
        var ui = context.Require(UiContributionRegistry.Key);
        var api = context.Require(ApiGateway.Key);
        var registry = context.Require(PluginRegistry.Key);
        var control = context.Require(ControlPlaneKeys.Service);
        var recovery = context.Require(ControlPlaneKeys.Recovery);
        var owner = ContributionOwner.ForContext(context, WebConsolePlugin.Id, Name);

        registry.ProtectedIds.Add(WebConsolePlugin.Id);

        await context.RegisterApiQueryAsync(api, owner, StatusQuery, StatusQueryHandler);
        var commandOwner = owner with { ComponentId = Name + "-command" };
        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand(PingCommand, _ =>
                {
                    Interlocked.Increment(ref _pings);
                    return ValueTask.CompletedTask;
                }, commandOwner);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                api.UnregisterAll(commandOwner);
                return ValueTask.CompletedTask;
            });

        await RegisterUiContributionsAsync(context, ui, owner);

        if (options.StartupDisabled)
        {
            return null; // degraded/native host: UI contributions remain, no listener
        }

        var routeGuard = context.Require(RouteGuardKeys.Service) as ServerRouteGuard
            ?? throw new InvalidOperationException("web console route guard is not an ASP.NET implementation");
        var server = new WebUiServer(
            host,
            options.Address,
            options.AllowRemote,
            registry,
            options.ClientModuleRootResolver,
            options.WebDistPath,
            control,
            options.AccessPolicy,
            routeGuard,
            options.Hmr,
            recovery);
        await context.EffectAsync(
            async () =>
            {
                await server.StartAsync(context.CancellationToken);
                _server = server;
            },
            async () =>
            {
                _server = null;
                await server.StopAsync(CancellationToken.None);
                await server.DisposeAsync();
            });

        return null;
    }

    private JsonNode StatusQueryHandler() => new JsonObject
    {
        ["plugin"] = WebConsolePlugin.Id,
        ["component"] = Name,
        ["installed"] = _server is not null,
        ["address"] = _server?.Address,
        ["frontend"] = ModuleId,
        ["pings"] = Interlocked.CompareExchange(ref _pings, 0, 0),
    };

    private static async ValueTask RegisterUiContributionsAsync(
        Context context, UiContributionRegistry ui, ContributionOwner owner)
    {
        foreach (var (id, title, route, renderer, icon, order) in BuiltinPages)
        {
            await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
                id, title, route, renderer,
                Icon: icon,
                Description: "Web Console 内置插件提供的产品页面",
                Order: order,
                Group: "Gocordis/Console"));
            await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
                id + "-route", route, id, "shell", order));
            await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
                id + "-nav", title, route, icon, "Gocordis/Console", id, order));
        }

        await context.RegisterUiPanelAsync(ui, owner, new UiPanelDefinition(
            "gocordis-console-status", "Console 插件状态", "bottom", "gocordis-console-status",
            Pages: [BuiltinPages[0].Id],
            Views: null));
        await context.RegisterUiPanelAsync(ui, owner, new UiPanelDefinition(
            "gocordis-event-feed", "观察事件流", "bottom", "event-feed",
            Pages: BuiltinPages.Select(page => page.Id).ToArray()));
        await context.RegisterUiExtensionAsync(ui, owner, new UiExtensionDefinition(
            "gocordis-console-footer", "shell.footer", "host-status", BuiltinPages[0].Id));
        await context.RegisterUiResourceAsync(ui, owner, new UiResourceDefinition(
            "gocordis-console-assets", "assets", "/assets/", "text/javascript"));

        var source = Path.Combine(AppContext.BaseDirectory, "client-modules", ModuleId);
        var entry = Path.Combine(source, "ui.js");
        var hash = File.Exists(entry)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))[..12].ToLowerInvariant()
            : "unavailable";
        await context.RegisterUiClientModuleAsync(ui, owner, new UiClientModuleDefinition(
            ModuleId,
            $"/client-modules/{ModuleId}/ui.js",
            hash,
            "ui.js",
            source,
            WebConsolePlugin.Id,
            WebConsolePlugin.Version,
            hash));
    }
}
