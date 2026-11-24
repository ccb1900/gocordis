using Gocordis.Core;
using Koishi.Contracts;

namespace Koishi.AdapterPlugin;

public sealed class Adapter : Component
{
    public override string Name => "koishi-adapter";

    public override IReadOnlyList<ICapabilityKey> Inject() => [KoishiKeys.Database];

    public override IReadOnlyList<ICapabilityKey> Provide() => [KoishiKeys.Adapter];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var database = context.Require(KoishiKeys.Database);
        context.Provide(KoishiKeys.Adapter, new Chat(database));
        return ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class Chat : IChatAdapter
    {
        private readonly IDatabase _database;
        public Chat(IDatabase database) => _database = database;

        public async ValueTask<string> SendAsync(string channel, string text) =>
            await _database.QueryAsync($"{channel}:{text}");
    }
}
