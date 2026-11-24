using Gocordis.Core;
using Koishi.Contracts;

namespace Koishi.DatabasePlugin;

public sealed class Database : Component
{
    public override string Name => "koishi-database";

    public override IReadOnlyList<ICapabilityKey> Provide() => [KoishiKeys.Database];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(KoishiKeys.Database, new Store());
        return ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class Store : IDatabase
    {
        public ValueTask<string> QueryAsync(string sql) =>
            ValueTask.FromResult($"row:{sql}");
    }
}
