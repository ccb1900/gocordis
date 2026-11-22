using Gocordis.Core;

namespace KoishiCase;

public sealed class LiveDatabase : IDatabase
{
    public LiveDatabase(string backend) => Backend = backend;
    public string Backend { get; }
    public Guid ConnectionId { get; } = Guid.NewGuid();
}

public sealed class LiveAdapter : IChatAdapter
{
    public LiveAdapter(IDatabase database, string? tag = null) => Platform = tag is null ? database.Backend : $"{database.Backend}/{tag}";
    public string Platform { get; }
}

/// <summary>IM adapters and DB drivers provide coeffects; features consume them.</summary>
public sealed class DatabasePlugin : Component
{
    private readonly string _backend;
    private readonly List<LiveDatabase> _live;

    public DatabasePlugin(string backend, List<LiveDatabase> live)
    {
        _backend = backend;
        _live = live;
    }

    public override string Name => "koishi-database";

    public override IReadOnlyList<ICapabilityKey> Provide() => [K.Database];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var database = new LiveDatabase(_backend);
        await context.EffectAsync(
            () =>
            {
                _live.Add(database);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                _live.Remove(database);
                return ValueTask.CompletedTask;
            });
        context.Provide(K.Database, database);
        return null;
    }
}

public sealed class AdapterPlugin : Component
{
    private readonly List<LiveAdapter> _live;

    public AdapterPlugin(List<LiveAdapter> live) => _live = live;

    public string? PlatformTag { get; init; }

    public override string Name => "koishi-adapter";

    public override IReadOnlyList<ICapabilityKey> Inject() => [K.Database];

    public override IReadOnlyList<ICapabilityKey> Provide() => [K.Adapter];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var database = context.Require(K.Database);
        var adapter = new LiveAdapter(database, PlatformTag);
        await context.EffectAsync(
            () =>
            {
                _live.Add(adapter);
                return ValueTask.CompletedTask;
            },
            () =>
            {
                _live.Remove(adapter);
                return ValueTask.CompletedTask;
            });
        context.Provide(K.Adapter, adapter);
        return null;
    }
}

public sealed class FeaturePlugin : Component
{
    public override string Name => "koishi-feature";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [K.Adapter, K.Commands, K.Message];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var adapter = context.Require(K.Adapter);
        var commands = context.Require(K.Commands);
        var handle = commands.Register("echo", text => $"{adapter.Platform}:{text}");

        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () =>
            {
                handle.Dispose();
                return ValueTask.CompletedTask;
            });

        await context.OnAsync(K.Message, payload =>
        {
            // The plugin contributes a domain handler without owning cleanup.
            _ = commands.Execute("echo", payload);
            return ValueTask.CompletedTask;
        });
        return null;
    }
}
