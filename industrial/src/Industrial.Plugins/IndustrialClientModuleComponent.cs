using Gocordis.Core;
using Gocordis.Plugins;

namespace Industrial.Plugins;

/// <summary>
/// Contributes ONLY the industrial client module. The page itself is a
/// backend-owned declarative UiPageDefinition contributed by
/// IndustrialUiComponent, whose views name the block kind this module
/// registers. Following Koishi's plugin shape: the plugin supplies a backend
/// service and a frontend extension, not a second application shell, a
/// hardcoded frontend route, or an iframe around a standalone app.
/// The registration is installed as an effect of this activation, so
/// disabling the plugin withdraws the renderer through the console's own
/// ClientModuleManager lifecycle. This component never constructs a service.
/// </summary>
public sealed class IndustrialClientModuleComponent : Component
{
    private const string ModuleName = "industrial-designer";
    private const string ModuleEntry = "module.js";
    private const string ModuleUrl = "/client-modules/industrial-designer/module.js";

    public override string Name => "industrial-client-module";

    public override IReadOnlyList<ICapabilityKey> Inject() => [UiContributionRegistry.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var owner = ContributionOwner.ForContext(context, "industrial", "client-module");

        await context.RegisterUiClientModuleAsync(ui, owner, new UiClientModuleDefinition(
            ModuleName,
            ModuleUrl,
            "0.1.2",
            ModuleEntry));

        return null;
    }
}
