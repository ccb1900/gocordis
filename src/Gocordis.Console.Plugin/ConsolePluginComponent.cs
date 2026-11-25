using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Console.Plugin;

/// <summary>
/// The product's built-in Console plugin. It is a real managed Component in a
/// real package: its API query and command are registered as reversible effects
/// of this fiber and its TSX frontend ships in the same package as ui.js.
/// The host only provides ApiGateway; every console-specific capability lives
/// here and disappears when the plugin is disabled or uninstalled.
/// </summary>
public sealed class ConsolePluginComponent : Component
{
    private int _installed;
    private int _undone;
    private int _pings;

    public override string Name => ConsolePlugin.Id;

    public override IReadOnlyList<ICapabilityKey> Inject() => [ApiGateway.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.ForContext(context, ConsolePlugin.Id, Name);

        // A reversible state effect: the status query reads the same state the
        // inverse changes. Unwind is owned by Core, so a failed or retired
        // activation cannot leave a stale installed marker behind.
        await context.EffectAsync(
            () =>
            {
                _installed = 1;
                return ValueTask.CompletedTask;
            },
            () =>
            {
                _installed = 0;
                _undone++;
                return ValueTask.CompletedTask;
            });

        await context.RegisterApiQueryAsync(api, owner, "gocordis-console/status", StatusQuery);
        // A distinct owner keeps the command's inverse from removing the query
        // that sits below it on the same fiber's LIFO effect stack.
        var commandOwner = owner with { ComponentId = Name + "-command" };
        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("gocordis-console/ping", PingCommand, commandOwner);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                api.UnregisterAll(commandOwner);
                return ValueTask.CompletedTask;
            });
        return null;
    }

    private JsonNode StatusQuery() => new JsonObject
    {
        ["plugin"] = ConsolePlugin.Id,
        ["component"] = Name,
        ["installed"] = _installed == 1,
        ["undone"] = _undone,
        ["pings"] = _pings,
        ["frontend"] = "gocordis-console-ui",
    };

    private ValueTask PingCommand(JsonNode? payload)
    {
        _pings++;
        return ValueTask.CompletedTask;
    }
}
