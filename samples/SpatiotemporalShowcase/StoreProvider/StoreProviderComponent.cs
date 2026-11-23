using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;
using Showcase.Contracts;

namespace Showcase.StoreProvider;

/// <summary>
/// Spatial root of the showcase: provides the store and the change event under
/// the SHARED contract keys. Any packaged consumer that declares the same
/// shared assembly resolves these exact keys and is gated on this fiber.
///
/// The background writer is a reversible effect: it starts on activation and
/// is cooperatively cancelled in the inverse, so a disable/enable cycle leaves
/// no orphan writer. Writes emit StoreChanged, driving the event subscriber.
/// </summary>
public sealed class StoreProviderComponent : Component
{
    private static readonly string[] Keys = ["alpha", "beta", "gamma", "delta"];
    private readonly MemoryStore _store = new();
    private readonly EventHistory _history = new();

    public override string Name => "showcase-store-provider";

    public override IReadOnlyList<ICapabilityKey> Inject() => [ApiGateway.Key];

    public override IReadOnlyList<ICapabilityKey> Provide() => [K.Store, K.StoreChanged];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var api = context.Require(ApiGateway.Key);
        var owner = ContributionOwner.ForContext(context, "showcase", Name);

        // Provide both the store value and the event-key value. Providing an
        // event key establishes it as a resolvable dependency root; emitting
        // and subscribing then reference the same reference-identity key.
        context.Provide(K.Store, (IStore)_store);
        context.Provide(K.StoreChanged, new StoreChanged("(init)", "(none)", 0));

        // Effect 1: visible install/undo marker for LIFO proof.
        await context.EffectAsync(
            () => { EffectLog.Write("store:install"); return ValueTask.CompletedTask; },
            () => { EffectLog.Write("store:undo"); return ValueTask.CompletedTask; });

        // Effect 2: the background writer; cancelled, awaited and disposed in
        // the inverse. The CTS must NOT be `using var` here — it has to outlive
        // ApplyAsync for the writer (and the inverse) for the fiber's lifetime.
        var stop = new CancellationTokenSource();
        var writer = Task.Run(() => WriteLoopAsync(context, stop.Token));
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            async () =>
            {
                stop.Cancel();
                try { await writer; }
                catch (OperationCanceledException) { }
                finally { stop.Dispose(); }
                EffectLog.Write("writer:undo");
            });

        await context.RegisterApiQueryAsync(api, owner, "showcase/store", StoreQuery);
        // Data sources for the declarative view blocks (table/trend/stats/list).
        await context.RegisterApiQueryAsync(api, owner, "showcase/events", EventsQuery);
        await context.RegisterApiQueryAsync(api, owner, "showcase/trend", TrendQuery);
        return null;
    }

    private async Task WriteLoopAsync(Context context, CancellationToken token)
    {
        var index = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), token);
                var key = Keys[index++ % Keys.Length];
                var value = $"{key}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                _store.Put(key, value);
                _history.Add(new ShowcaseEvent(
                    DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    key,
                    "Active",
                    value,
                    _store.Revision));
                await context.EmitAsync(K.StoreChanged, new StoreChanged(key, value, _store.Revision));
            }
        }
        catch (OperationCanceledException)
        {
            // cooperative stop — the only expected exit
        }
    }

    private JsonNode StoreQuery()
    {
        var values = new JsonObject();
        foreach (var (key, value) in _store.Snapshot().OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            values[key] = value;
        }

        return new JsonObject
        {
            ["provider"] = "showcase-store-provider",
            ["revision"] = _store.Revision,
            ["writes"] = _store.Writes,
            ["entries"] = values,
        };
    }

    // An array of row objects — the shape every table-like block consumes.
    private JsonNode EventsQuery()
    {
        var rows = new JsonArray();
        foreach (var item in _history.Snapshot().AsEnumerable().Reverse())
        {
            rows.Add(new JsonObject
            {
                ["date"] = item.Date,
                ["sourceId"] = item.SourceId,
                ["status"] = item.Status,
                ["value"] = item.Value,
                ["revision"] = item.Revision,
            });
        }

        return rows;
    }

    // TrendBlock reads dateKey + numeric series keys from the same row shape.
    private JsonNode TrendQuery()
    {
        var rows = new JsonArray();
        foreach (var group in _history.Snapshot()
                     .GroupBy(item => item.Date)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            rows.Add(new JsonObject
            {
                ["date"] = group.Key,
                ["writes"] = group.Count(),
                ["revisions"] = group.Max(item => item.Revision),
            });
        }

        return rows;
    }
}
