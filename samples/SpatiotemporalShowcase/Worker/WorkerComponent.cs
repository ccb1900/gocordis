using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Showcase.Contracts;

namespace Showcase.Worker;

/// <summary>
/// Consumer of the shared store key. Inject() declares a cross-plugin
/// dependency: the Runtime keeps this fiber Pending until StoreProvider's
/// Provide(K.Store) is Active, withdraws it to Pending when that provider is
/// disabled, and reactivates it automatically on re-enable — no orchestration,
/// no retry loop. It also subscribes to the shared event key and publishes
/// derived metrics under another shared key for the dashboard.
/// </summary>
public sealed class WorkerComponent : Component
{
    private readonly MetricsState _metrics = new();

    public override string Name => "showcase-worker";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [K.Store, K.StoreChanged, ApiGateway.Key];

    public override IReadOnlyList<ICapabilityKey> Provide() => [K.Metrics];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var store = context.Require(K.Store); // reference-identity resolve across ALCs
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.ForContext(context, "showcase", Name);

        context.Provide(K.Metrics, (IMetrics)_metrics);

        // The event subscription is itself an effect-owned registration: the
        // Runtime removes exactly this activation's handler during unwind.
        await context.OnAsync(K.StoreChanged, change =>
        {
            _metrics.Record(change.Key, change.Value, change.Revision);
            return ValueTask.CompletedTask;
        });

        await context.EffectAsync(
            () =>
            {
                _metrics.SetSubscribed(true);
                EffectLog.Write("worker:install");
                return ValueTask.CompletedTask;
            },
            () =>
            {
                _metrics.SetSubscribed(false);
                EffectLog.Write("worker:undo");
                return ValueTask.CompletedTask;
            });

        await context.RegisterApiQueryAsync(api, owner, "showcase/worker", () => WorkerQuery(store));
        return null;
    }

    private JsonNode WorkerQuery(IStore store) => new JsonObject
    {
        ["worker"] = "showcase-worker",
        ["subscribed"] = _metrics.Subscribed,
        ["observed"] = _metrics.Observed,
        ["lastEvent"] = _metrics.LastEvent,
        ["derived"] = _metrics.Derived,
        ["storeRevision"] = store.Revision,
    };
}
