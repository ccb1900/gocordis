using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Plugins;

namespace Gocordis.Builtin.WebConsole;

/// <summary>
/// The Web Console transport plugin. It owns only its own Fiber: the HTTP
/// server, its overview page/status panel and its ESM. Other product UI
/// features are separate protected builtin plugins, not contributions stuffed
/// into this one. Start/stop are one reversible Effect under Runtime.
/// </summary>
public sealed class WebConsoleComponent : Component
{
    public const string ModuleId = "gocordis-console-ui";
    private const string StatusQuery = "gocordis-console/status";
    private const string PingCommand = "gocordis-console/ping";
    private const string PageId = "gocordis-console-page";

    private WebUiServer? _server;
    private int _pings;

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
        await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
            PageId, "内置控制台", "/console", "gocordis-console-overview",
            Icon: "dashboard", Description: "Web Console 传输插件自身的页面",
            Order: -100, Group: "Gocordis/Console"));
        await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
            PageId + "-route", "/console", PageId, "shell", -100));
        await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
            PageId + "-nav", "内置控制台", "/console", "dashboard",
            "Gocordis/Console", PageId, -100));
        await context.RegisterUiPanelAsync(ui, owner, new UiPanelDefinition(
            "gocordis-console-status", "Console 插件状态", "bottom", "gocordis-console-status",
            Pages: [PageId]));
        await context.RegisterUiExtensionAsync(ui, owner, new UiExtensionDefinition(
            "gocordis-console-footer", "shell.footer", "host-status", PageId));
        await context.RegisterUiResourceAsync(ui, owner, new UiResourceDefinition(
            "gocordis-console-assets", "assets", "/assets/", "text/javascript"));
        await BuiltinUiModule.RegisterAsync(context, ui, owner, ModuleId, WebConsolePlugin.Id, WebConsolePlugin.Version);
    }
}
