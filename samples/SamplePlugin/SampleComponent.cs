using Gocordis.Core;

namespace SamplePlugin;

/// <summary>Real managed fixture that publishes a capability and then raises;
/// used to verify real-backend error propagation, not only loader failure.</summary>
public sealed class ThrowingSampleComponent : Component
{
    public static readonly Key<string> FailureKey = new("sample.failure");

    public override string Name => "sample-throwing-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [FailureKey];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(FailureKey, "published-before-failure");
        throw new InvalidOperationException("managed-component-failure");
    }
}

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
