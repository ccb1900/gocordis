using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

/// <summary>
/// The control plane as a first-class plugin contribution. One Core fiber owns
/// its reversible effects: providing the typed service and, when the host has a
/// UI, one stable page. Its construction inputs and UI registry are resolved as
/// capabilities, so neither the host nor this component hands a concrete
/// Console instance to the other. No second registry and no host-managed
/// lifetime.
/// </summary>
public sealed class PluginControlPlaneComponent : Component
{
    /// <summary>Stable protected id. The control plane is a host contribution,
    /// never a manifest entry that a catalog action may retire or reconfigure.
    /// </summary>
    public const string ControlPlaneComponentId = "plugin-control-plane";

    private readonly ControlPlaneDependencies _dependencies;

    public PluginControlPlaneComponent(ControlPlaneDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public override string Name => ControlPlaneComponentId;

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [ControlPlaneKeys.Dependencies, UiContributionRegistry.Key];

    public override IReadOnlyList<ICapabilityKey> Provide() => [ControlPlaneKeys.Service];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var dependencies = context.Require(ControlPlaneKeys.Dependencies);
        var ui = context.Require(UiContributionRegistry.Key);

        // Protection is part of loading the contribution, not an accident of
        // finding its id in the application manifest.
        dependencies.Registry.ProtectedIds.Add(ControlPlaneComponentId);
        var service = new PluginControlService(
            dependencies.ManifestPath,
            dependencies.Store,
            dependencies.Registry,
            ui,
            dependencies.ComponentFactory,
            dependencies.SharedAssemblyApproval,
            dependencies.DeclarationPlan)
        {
            DeclarationFingerprint = dependencies.DeclarationFingerprint,
            Trust = dependencies.Trust ?? PluginTrustPolicy.Development,
        };
        // Context.Provide installs its own LIFO inverse; no cleanup stack here.
        context.Provide(ControlPlaneKeys.Service, service);

        await context.RegisterUiPageAsync(
            ui,
            ContributionOwner.ForContext(context, "plugin-control-plane", Name),
            new UiPageDefinition(
                "plugin-control-plane", "Plugin control plane", "/plugins/control-plane", "plugin-explorer",
                Description: "Plugin declarations, packages and lifecycle control"));

        return null;
    }
}
