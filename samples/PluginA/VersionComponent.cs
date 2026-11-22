using Gocordis.Core;

namespace PluginA;

public sealed class VersionComponent : Component
{
    public static readonly Key<string> VersionKey = new("version-a");

    public override string Name => "plugin-a";

    public override IReadOnlyList<ICapabilityKey> Provide() => [VersionKey];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(VersionKey, SharedDep.VersionProbe.Version);
        return null;
    }
}
