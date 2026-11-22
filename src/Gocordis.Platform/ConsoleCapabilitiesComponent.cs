using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Platform;

/// <summary>
/// Framework capabilities that product plugins may inject. The registries stay
/// data-only; the values are provided by one ordinary Core fiber so their
/// lifetime is owned by the Runtime, like every other capability.
/// </summary>
public sealed class ConsoleCapabilitiesComponent : Component
{
    private readonly ConsoleHost _console;

    public ConsoleCapabilitiesComponent(ConsoleHost console) =>
        _console = console ?? throw new ArgumentNullException(nameof(console));

    public override string Name => "console-capabilities";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [UiContributionRegistry.Key, ApiGateway.Key];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(UiContributionRegistry.Key, _console.Ui);
        context.Provide(ApiGateway.Key, _console.Api);
        return ValueTask.FromResult<Cleanup?>(null);
    }
}
