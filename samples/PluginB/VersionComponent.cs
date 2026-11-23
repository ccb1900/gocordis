using Gocordis.Core;

namespace PluginB;

public sealed class VersionComponent : Component
{
    public static readonly Key<string> VersionKey = new("version-b");

    public override string Name => "plugin-b";

    public override IReadOnlyList<ICapabilityKey> Provide() => [VersionKey];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(VersionKey, SharedDep.VersionProbe.Version);
        return null;
    }
}
