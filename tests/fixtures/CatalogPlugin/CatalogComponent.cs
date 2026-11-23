using Gocordis.Core;

namespace CatalogPlugin;

/// <summary>
/// Fixture for the cross-backend capability protocol. This assembly shares only
/// Gocordis.Core with the host — it never loads a shared contracts DLL, and it
/// receives its capability identity from the host-owned catalog at construction.
/// </summary>
public sealed class CatalogComponent : Component
{
    private static readonly CapabilityId Ping = new("org.example.catalog", "ping");

    private readonly Key<ICapabilityEndpoint> _key;

    public CatalogComponent(ICapabilityCatalog catalog)
    {
        _key = catalog.KeyFor<ICapabilityEndpoint>(Ping, CapabilityValueKind.Endpoint);
    }

    public override string Name => "catalog-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public async override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var endpoint = new DelegatingCapabilityEndpoint(
            Ping,
            new CapabilityContract(Ping, CapabilityValueKind.Endpoint, null),
            (request, _) => ValueTask.FromResult(CapabilityEnvelope.Text("pong")));
        context.Provide(_key, endpoint);
        await context.EffectAsync(
            static () => ValueTask.CompletedTask,
            async () => await endpoint.DisposeAsync());
        return null;
    }
}
