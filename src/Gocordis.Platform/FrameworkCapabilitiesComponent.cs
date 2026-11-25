using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;

namespace Gocordis.Platform;

/// <summary>
/// Framework-only construction inputs published as capabilities. The host
/// assembles them once; no framework component receives a concrete Console or
/// WebUi instance. The builtin Web Console and the control plane both consume
/// capabilities through their Fiber Context.
/// </summary>
public sealed class FrameworkCapabilitiesComponent : Component
{
    private readonly PluginRegistry _registry;
    private readonly ControlPlaneDependencies _controlPlane;
    private readonly HostRecovery _recovery;
    private readonly WebConsoleOptions _webConsole;

    public FrameworkCapabilitiesComponent(
        PluginRegistry registry,
        ControlPlaneDependencies controlPlane,
        HostRecovery recovery,
        WebConsoleOptions webConsole)
    {
        _registry = registry;
        _controlPlane = controlPlane;
        _recovery = recovery;
        _webConsole = webConsole;
    }

    public override string Name => "framework-capabilities";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
    [
        PluginRegistry.Key,
        ControlPlaneKeys.Dependencies,
        ControlPlaneKeys.Recovery,
        WebConsoleKeys.Options,
    ];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(PluginRegistry.Key, _registry);
        context.Provide(ControlPlaneKeys.Dependencies, _controlPlane);
        context.Provide(ControlPlaneKeys.Recovery, _recovery);
        context.Provide(WebConsoleKeys.Options, _webConsole);
        return ValueTask.FromResult<Cleanup?>(null);
    }
}
