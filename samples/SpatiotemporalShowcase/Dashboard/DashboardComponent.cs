using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Showcase.Contracts;

namespace Showcase.Dashboard;

/// <summary>
/// Runtime DATA capabilities for the showcase: API queries (the data source
/// every declarative view binds to) and API commands (the target of form/json
/// blocks and page actions).
///
/// This component registers NO UI. Pages, panels and the client module are
/// declared in this PACKAGE's gocordis-ui.json. The control plane wraps the
/// component with UiPackageBinder, which registers/undoes those contributions
/// as effects of this exact fiber. So installing the package installs its UI;
/// uninstalling it removes the UI — nothing is hardcoded into the console or
/// the framework. (UiPackageBinder.WithUiKey injects the UI key automatically.)
///
/// Multiple cross-plugin dependencies (store + metrics) still gate this fiber:
/// it is Active only while BOTH upstream providers are Active.
/// </summary>
public sealed class DashboardComponent : Component
{
    private readonly ShowcaseConfig _config = new();

    public override string Name => "showcase-dashboard";

    public override IReadOnlyList<ICapabilityKey> Inject() => [K.Store, K.Metrics, ApiGateway.Key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var store = context.Require(K.Store);
        var metrics = context.Require(K.Metrics);
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.ForContext(context, "showcase", Name);
        var commandOwner = ContributionOwner.ForContext(context, "showcase", Name + ":commands");

        await context.EffectAsync(
            () => { EffectLog.Write("dashboard:install"); return ValueTask.CompletedTask; },
            () => { EffectLog.Write("dashboard:undo"); return ValueTask.CompletedTask; });

        // API queries — named data channel bound by the package's views.
        await context.RegisterApiQueryAsync(api, owner, "showcase/dashboard", () => DashboardQuery(store, metrics));
        await context.RegisterApiQueryAsync(api, owner, "showcase/config", ConfigQuery);
        await context.RegisterApiQueryAsync(api, owner, "showcase/keys", KeysQuery);

        // API commands — targets of form/json blocks and page actions.
        await context.EffectAsync(
            () =>
            {
                api.RegisterCommand("showcase/config.set", ApplyConfig, commandOwner);
                api.RegisterCommand("showcase/report", _ => ValueTask.CompletedTask, commandOwner);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                api.UnregisterAll(commandOwner);
                return ValueTask.CompletedTask;
            });

        return null;
    }

    private ValueTask ApplyConfig(JsonNode? payload)
    {
        if (payload is JsonObject obj)
        {
            var source = obj["doc"] as JsonObject ?? obj;
            var label = source["label"]?.GetValue<string>();
            var threshold = source["threshold"] is { } t && int.TryParse(t.ToString(), out var n) ? n : (int?)null;
            _config.Apply(label, threshold);
            EffectLog.Write($"config:set label={_config.Label} threshold={_config.Threshold}");
        }

        return ValueTask.CompletedTask;
    }

    private JsonNode ConfigQuery() => new JsonObject
    {
        ["doc"] = new JsonObject
        {
            ["label"] = _config.Label,
            ["threshold"] = _config.Threshold,
        },
    };

    private JsonNode KeysQuery()
    {
        var rows = new JsonArray();
        foreach (var key in new[] { "alpha", "beta", "gamma", "delta" })
        {
            rows.Add(new JsonObject { ["id"] = key });
        }

        return rows;
    }

    private static JsonNode DashboardQuery(IStore store, IMetrics metrics) => new JsonObject
    {
        ["storeRevision"] = store.Revision,
        ["storeWrites"] = store.Writes,
        ["workerObserved"] = metrics.Observed,
        ["workerSubscribed"] = metrics.Subscribed,
        ["healthy"] = store.Writes > 0 && metrics.Observed > 0,
    };
}
