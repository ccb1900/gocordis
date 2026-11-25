using Gocordis.Core;
using Gocordis.Plugins;

namespace Industrial.Plugins;

/// <summary>
/// Contributes the industrial G6 workflow designer as a Cordis client module
/// and a page that selects its renderer. All registrations are installed as
/// effects of this activation, so disabling the plugin withdraws them in LIFO
/// order. This component only declares UI composition — it never registers a
/// raw capability provider, constructs a service, or starts a server.
/// </summary>
public sealed class IndustrialClientModuleComponent : Component
{
    private const string ModuleName = "industrial-designer";
    private const string ModuleEntry = "module.js";
    private const string ModuleUrl = "/client-modules/industrial-designer/module.js";
    private const string PageId = "industrial.designer.g6";
    private const string PageRenderer = "industrial-designer";

    public override string Name => "industrial-client-module";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
    [
        UiContributionRegistry.Key,
        ApiGateway.Key,
    ];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        // Declared because the designer's toolbar talks to the gateway through
        // /api/query + /api/command; no query or command is registered here.
        _ = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.ForContext(context, "industrial", "client-module");

        await context.RegisterUiClientModuleAsync(ui, owner, new UiClientModuleDefinition(
            ModuleName,
            ModuleUrl,
            "0.1.1",
            ModuleEntry));

        await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
            PageId,
            "Workflow Designer (G6)",
            "/industrial/designer/g6",
            PageRenderer,
            Icon: "Branches",
            Description: "Industrial workflow designer supplied by the industrial-designer client module",
            Order: 41,
            Group: "Industrial"));

        return null;
    }
}
