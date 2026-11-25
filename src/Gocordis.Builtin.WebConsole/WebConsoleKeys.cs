using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;

namespace Gocordis.Builtin.WebConsole;

/// <summary>Identity of the product's builtin Web Console plugin entry.</summary>
public static class WebConsolePlugin
{
    public const string Id = "gocordis-web-console";
    public const string Type = "gocordis-web-console";
    public const string Version = "1.0.0";
}

/// <summary>
/// Host-supplied bootstrap inputs. The plugin consumes them as capabilities;
/// it never references the product host assembly or reaches into host fields.
/// </summary>
public sealed record WebConsoleOptions(
    string Address,
    bool AllowRemote,
    ConsoleAccessPolicy? AccessPolicy,
    ServerRouteGuard? RouteGuard,
    Func<HmrRequest, JsonObject?, CancellationToken, Task<HmrPlan>>? Hmr = null,
    string? WebDistPath = null,
    Func<string, string?>? ClientModuleRootResolver = null,
    bool StartupDisabled = false);

/// <summary>Read-only transport status published by the Web Console fiber.</summary>
public interface IWebConsole
{
    string? Address { get; }
    WebUiServer? Server { get; }
}

public sealed record WebConsoleHandle(Func<WebUiServer?> ServerAccessor) : IWebConsole
{
    public WebUiServer? Server => ServerAccessor();
    public string? Address => Server?.Address;
}

public static class WebConsoleKeys
{
    public static readonly Key<WebConsoleOptions> Options = new("web-console-options");
    public static readonly Key<IWebConsole> Service = new("web-console-service");
}
