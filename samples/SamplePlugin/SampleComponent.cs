using Gocordis.Core;

namespace SamplePlugin;

public sealed class SampleComponent : Component
{
    public static readonly Key<string> SampleKey = new("sample");

    public override string Name => "sample-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [SampleKey];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(SampleKey, "from-plugin");
        return null;
    }
}
