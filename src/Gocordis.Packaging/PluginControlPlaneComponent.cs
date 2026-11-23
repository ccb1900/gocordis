using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

/// <summary>
/// The control plane as a first-class plugin contribution. One Core fiber owns
/// its reversible effects: providing the typed service and, when the host has a
/// UI, one stable page. No second registry and no host-managed lifetime.
/// </summary>
public sealed class PluginControlPlaneComponent : Component
{
    private readonly string _manifestPath;
    private readonly PluginStore _store;
    private readonly PluginRegistry _registry;
    private readonly UiContributionRegistry? _ui;
    private readonly Func<IReadOnlyList<string>, bool>? _sharedAssemblyApproval;
    private readonly Func<IReadOnlyList<string>, Task>? _declarationPlan;
    private readonly Func<Task>? _declarationRollback;

    public PluginControlPlaneComponent(
        string ManifestPath,
        PluginStore Store,
        PluginRegistry Registry,
        UiContributionRegistry? Ui = null,
        Func<PluginManifest, Component>? ComponentFactory = null,
        Func<IReadOnlyList<string>, bool>? SharedAssemblyApproval = null,
        Func<IReadOnlyList<string>, Task>? DeclarationPlan = null,
        Func<Task>? DeclarationRollback = null)
    {
        _manifestPath = ManifestPath ?? throw new ArgumentNullException(nameof(ManifestPath));
        _store = Store ?? throw new ArgumentNullException(nameof(Store));
        _registry = Registry ?? throw new ArgumentNullException(nameof(Registry));
        _ui = Ui;
        _componentFactory = ComponentFactory;
        _sharedAssemblyApproval = SharedAssemblyApproval;
        _declarationPlan = DeclarationPlan;
        _declarationRollback = DeclarationRollback;
    }

    private readonly Func<PluginManifest, Component>? _componentFactory;

    public override string Name => "plugin-control-plane";

    public override IReadOnlyList<ICapabilityKey> Provide() => [ControlPlaneKeys.Service];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var service = new PluginControlService(
            _manifestPath, _store, _registry, _ui, _componentFactory, _sharedAssemblyApproval,
            _declarationPlan, _declarationRollback);
        // Context.Provide installs its own LIFO inverse; no cleanup stack here.
        context.Provide(ControlPlaneKeys.Service, service);

        if (_ui is not null)
        {
            await context.RegisterUiPageAsync(
                _ui,
                ContributionOwner.ForContext(context, "plugin-control-plane", Name),
                new UiPageDefinition(
                    "plugin-control-plane", "Plugin control plane", "/plugins/control-plane", "plugin-explorer",
                    Description: "Plugin declarations, packages and lifecycle control"));
        }

        return null;
    }
}
