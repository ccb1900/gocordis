using Gocordis.Plugins;

namespace Gocordis.UI.Hosts;

public enum UiHostKind
{
    Web,
    WinForms,
    Avalonia,
}

public sealed record UiRenderTarget(string Kind, string Id, string Descriptor, string? Reason = null);

public sealed record UiRenderPlan(
    UiHostKind Host,
    IReadOnlyList<UiRenderTarget> Rendered,
    IReadOnlyList<UiRenderTarget> Degraded);

/// <summary>
/// Stateless renderer profile. A host profile never loads, disposes or declares
/// plugins; it only maps current contributions to a host-specific render plan.
/// </summary>
public abstract class UiHostProfile
{
    public abstract UiHostKind Kind { get; }
    public abstract string Name { get; }

    public UiRenderPlan Plan(UiContributionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var rendered = new List<UiRenderTarget>();
        var degraded = new List<UiRenderTarget>();

        foreach (var page in registry.Pages())
        {
            RenderPage(page, rendered, degraded);
        }

        foreach (var panel in registry.Panels())
        {
            RenderPanel(panel, rendered, degraded);
        }

        foreach (var module in registry.ClientModules())
        {
            RenderClientModule(module, rendered, degraded);
        }

        return new UiRenderPlan(Kind, rendered, degraded);
    }

    protected abstract void RenderPage(UiPage page, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded);

    protected abstract void RenderPanel(UiPanel panel, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded);

    protected abstract void RenderClientModule(
        UiClientModule module, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded);
}

public sealed class WebUiHostProfile : UiHostProfile
{
    public static WebUiHostProfile Instance { get; } = new();
    public override UiHostKind Kind => UiHostKind.Web;
    public override string Name => "Web Console";

    protected override void RenderPage(UiPage page, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("page", page.Id, $"web:{page.Route}"));

    protected override void RenderPanel(UiPanel panel, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("panel", panel.Id, $"web:{panel.Position}"));

    protected override void RenderClientModule(
        UiClientModule module, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("client-module", module.Name, $"esm:{module.Url}"));
}

public sealed class WinFormsUiHostProfile : UiHostProfile
{
    public static WinFormsUiHostProfile Instance { get; } = new();
    public override UiHostKind Kind => UiHostKind.WinForms;
    public override string Name => "WinForms Host";

    protected override void RenderPage(UiPage page, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("page", page.Id, $"winforms:{page.Renderer}"));

    protected override void RenderPanel(UiPanel panel, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("panel", panel.Id, $"winforms:{panel.Renderer}"));

    protected override void RenderClientModule(
        UiClientModule module, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        degraded.Add(new UiRenderTarget(
            "client-module", module.Name, string.Empty,
            "web client modules are not executable in a WinForms host"));
}

public sealed class AvaloniaUiHostProfile : UiHostProfile
{
    public static AvaloniaUiHostProfile Instance { get; } = new();
    public override UiHostKind Kind => UiHostKind.Avalonia;
    public override string Name => "Avalonia Host";

    protected override void RenderPage(UiPage page, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("page", page.Id, $"avalonia:{page.Renderer}"));

    protected override void RenderPanel(UiPanel panel, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        rendered.Add(new UiRenderTarget("panel", panel.Id, $"avalonia:{panel.Renderer}"));

    protected override void RenderClientModule(
        UiClientModule module, List<UiRenderTarget> rendered, List<UiRenderTarget> degraded) =>
        degraded.Add(new UiRenderTarget(
            "client-module", module.Name, string.Empty,
            "web client modules are not executable in an Avalonia host"));
}

public static class UiHostProfiles
{
    public static IReadOnlyList<UiHostProfile> All { get; } =
    [
        WebUiHostProfile.Instance,
        WinFormsUiHostProfile.Instance,
        AvaloniaUiHostProfile.Instance,
    ];

    public static UiHostProfile Get(UiHostKind kind) => kind switch
    {
        UiHostKind.Web => WebUiHostProfile.Instance,
        UiHostKind.WinForms => WinFormsUiHostProfile.Instance,
        UiHostKind.Avalonia => AvaloniaUiHostProfile.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
