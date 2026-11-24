using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Koishi.Contracts;

namespace Koishi.FeaturePlugin;

public sealed class Feature : Component
{
    public override string Name => "koishi-feature";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [KoishiKeys.Adapter, KoishiKeys.Commands, KoishiKeys.Message, ApiGateway.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var adapter = context.Require(KoishiKeys.Adapter);
        var commands = context.Require(KoishiKeys.Commands);
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.For("koishi", "feature");

        // Both registrations are installed AND withdrawn as effects. When this
        // fiber withdraws, the command handler and the query vanish with it.
        IDisposable? command = null;
        await context.EffectAsync(
            () =>
            {
                command = commands.Register("echo", text => adapter.SendAsync("demo", text));
                return ValueTask.CompletedTask;
            },
            () =>
            {
                command?.Dispose();
                return ValueTask.CompletedTask;
            });
        await context.RegisterApiQueryAsync(api, owner, "koishi/echo", EchoQuery(commands));
        return null;
    }

    private static Func<JsonNode?> EchoQuery(ICommandRegistry commands) => () =>
    {
        // The query walks the real typed chain: command → adapter → database.
        var value = commands.InvokeAsync("echo", "ping").AsTask().GetAwaiter().GetResult();
        return new JsonObject { ["value"] = value };
    };
}
