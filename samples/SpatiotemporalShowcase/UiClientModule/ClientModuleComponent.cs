using Gocordis.Core;
using Showcase.Contracts;

namespace Showcase.UiClientModule;

/// <summary>
/// Frontend-only plugin. Its UI contribution (the JS client module plus the
/// custom page/panel that use its renderers) is declared in this PACKAGE's
/// gocordis-ui.json — NOT in framework or console code. The control-plane
/// UiPackageBinder registers those resources as effects of this fiber, so
/// installing the package installs the UI and uninstalling it removes the UI.
///
/// The component itself only publishes an install/undo marker; it injects no
/// framework UI key (UiPackageBinder.WithUiKey supplies that when resources
/// exist).
/// </summary>
public sealed class ClientModuleComponent : Component
{
    public override string Name => "showcase-client-module";

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        await context.EffectAsync(
            () => { EffectLog.Write("client-module:install"); return ValueTask.CompletedTask; },
            () => { EffectLog.Write("client-module:undo"); return ValueTask.CompletedTask; });
        return null;
    }
}
