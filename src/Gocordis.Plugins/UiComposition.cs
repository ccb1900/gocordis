using System.Text.Json.Nodes;
using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Stable UI page contribution contract (Go registry.PageDefinition equivalent).</summary>
public sealed record UiPageDefinition(
    string Id,
    string Title,
    string Route,
    string Renderer,
    string? Icon = null,
    string? Description = null,
    JsonNode? Views = null,
    IReadOnlyList<UiPageAction>? Actions = null,
    int Order = 0,
    string? Group = null);

public sealed record UiPageAction(string Label, string Command, bool DatePicker = false);

/// <summary>
/// First-class route contribution. A route binds a URL to an existing page id;
/// it never installs a page, so navigation and deep links can be added by one
/// plugin to a page owned by another without taking over its lifecycle.
/// </summary>
public sealed record UiRouteDefinition(
    string Id,
    string Path,
    string PageId,
    string? Layout = null,
    int Order = 0,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record UiRoute(
    string Id,
    string Path,
    string PageId,
    string? Layout,
    int Order,
    ContributionOwner Owner,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>First-class navigation contribution (menu item / group path).</summary>
public sealed record UiNavigationDefinition(
    string Id,
    string Label,
    string Route,
    string? Icon = null,
    string? Group = null,
    string? PageId = null,
    int Order = 0);

public sealed record UiNavigation(
    string Id,
    string Label,
    string Route,
    string? Icon,
    string? Group,
    string? PageId,
    int Order,
    ContributionOwner Owner);

/// <summary>First-class extension contribution (named slot on a page/shell).</summary>
public sealed record UiExtensionDefinition(
    string Id,
    string Slot,
    string Renderer,
    string? PageId = null,
    JsonNode? Props = null,
    int Order = 0);

public sealed record UiExtension(
    string Id,
    string Slot,
    string Renderer,
    string? PageId,
    JsonNode? Props,
    int Order,
    ContributionOwner Owner);

/// <summary>First-class static resource contribution served by the host's
/// guarded asset endpoints. The host owns serving; the plugin owns the
/// declaration and its reversal.</summary>
public sealed record UiResourceDefinition(
    string Id,
    string Kind,
    string Path,
    string? MediaType = null,
    JsonNode? Metadata = null);

public sealed record UiResource(
    string Id,
    string Kind,
    string Path,
    string? MediaType,
    JsonNode? Metadata,
    ContributionOwner Owner);

public sealed record UiPage(
    string Id,
    string Title,
    string Route,
    string Renderer,
    string? Icon,
    string? Description,
    JsonNode? Views,
    IReadOnlyList<UiPageAction> Actions,
    int Order,
    ContributionOwner Owner,
    string? Group = null);

/// <summary>Stable UI panel contribution contract (Go registry.PanelDefinition equivalent).</summary>
public sealed record UiPanelDefinition(
    string Id,
    string Title,
    string Position,
    string Renderer,
    IReadOnlyList<string>? Pages = null,
    JsonNode? Views = null,
    int Order = 0);

public sealed record UiPanel(
    string Id,
    string Title,
    string Position,
    string Renderer,
    IReadOnlyList<string> Pages,
    JsonNode? Views,
    int Order,
    ContributionOwner Owner);

/// <summary>
/// One plugin frontend module contribution served same-origin by the console.
/// Source is the installed plugin's version directory: the WebUi adapter serves
/// bytes from there and never from AppContext.BaseDirectory/client-modules.
/// ContentHash is the digest of the installed module files; together with
/// Revision it forms the URL's cache key so an upgrade re-imports the module.
/// </summary>
public sealed record UiClientModuleDefinition(
    string Name,
    string Url,
    string Revision,
    string Entry,
    string? Source = null,
    string? PackageId = null,
    string? PackageVersion = null,
    string? ContentHash = null);

public sealed record UiClientModule(
    string Name,
    string Url,
    string Revision,
    string Entry,
    ContributionOwner Owner,
    string? Source = null,
    string? PackageId = null,
    string? PackageVersion = null,
    string? ContentHash = null);

/// <summary>Idempotent handle whose identity is verified before an owner may undo it.</summary>
public sealed class UiContributionHandle : IDisposable
{
    private readonly UiContributionRegistry _registry;
    private bool _released;

    internal UiContributionHandle(
        UiContributionRegistry registry, string kind, string id, ContributionOwner owner)
    {
        _registry = registry;
        Kind = kind;
        Id = id;
        Owner = owner;
    }

    public string Kind { get; }
    public string Id { get; }
    public ContributionOwner Owner { get; }

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _registry.Unregister(this);
    }
}

/// <summary>
/// Context-side registration helpers. Every registration is installed and
/// undone through the owning Activation's reversible Effect; no cleanup stack
/// of its own and no second plugin lifecycle is introduced.
/// </summary>
public static class UiCompositionContextExtensions
{
    public static ValueTask<UiContributionHandle> RegisterUiPageAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiPageDefinition page) =>
        RegisterEffectAsync(context, () => registry.RegisterPage(owner, page));

    public static ValueTask<UiContributionHandle> RegisterUiPanelAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiPanelDefinition panel) =>
        RegisterEffectAsync(context, () => registry.RegisterPanel(owner, panel));

    public static ValueTask<UiContributionHandle> RegisterUiClientModuleAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiClientModuleDefinition module) =>
        RegisterEffectAsync(context, () => registry.RegisterClientModule(owner, module));

    public static ValueTask<UiContributionHandle> RegisterUiRouteAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiRouteDefinition route) =>
        RegisterEffectAsync(context, () => registry.RegisterRoute(owner, route));

    public static ValueTask<UiContributionHandle> RegisterUiNavigationAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiNavigationDefinition navigation) =>
        RegisterEffectAsync(context, () => registry.RegisterNavigation(owner, navigation));

    public static ValueTask<UiContributionHandle> RegisterUiExtensionAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiExtensionDefinition extension) =>
        RegisterEffectAsync(context, () => registry.RegisterExtension(owner, extension));

    public static ValueTask<UiContributionHandle> RegisterUiResourceAsync(
        this Context context,
        UiContributionRegistry registry,
        ContributionOwner owner,
        UiResourceDefinition resource) =>
        RegisterEffectAsync(context, () => registry.RegisterResource(owner, resource));

    public static ValueTask RegisterApiQueryAsync(
        this Context context,
        ApiGateway api,
        ContributionOwner owner,
        string name,
        Func<JsonNode?> handler) =>
        context.EffectAsync(
            () =>
            {
                api.RegisterQuery(name, handler, owner);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                api.UnregisterQuery(owner, name);
                return ValueTask.CompletedTask;
            });

    private static async ValueTask<UiContributionHandle> RegisterEffectAsync(
        Context context,
        Func<UiContributionHandle> install)
    {
        UiContributionHandle? handle = null;
        await context.EffectAsync(
            () =>
            {
                handle = install();
                return ValueTask.CompletedTask;
            },
            () =>
            {
                handle?.Dispose();
                return ValueTask.CompletedTask;
            });
        return handle!;
    }
}
