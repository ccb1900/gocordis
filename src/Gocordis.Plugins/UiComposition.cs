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
    int Order = 0);

public sealed record UiPageAction(string Label, string Command, bool DatePicker = false);

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
    ContributionOwner Owner);

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

/// <summary>One plugin frontend module contribution served same-origin by the console.</summary>
public sealed record UiClientModuleDefinition(
    string Name,
    string Url,
    string Revision,
    string Entry);

public sealed record UiClientModule(
    string Name,
    string Url,
    string Revision,
    string Entry,
    ContributionOwner Owner);

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
