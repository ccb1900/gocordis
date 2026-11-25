using Gocordis.Core;
using Industrial.Contracts;
using Industrial.Core;

namespace Industrial.Plugins;

/// <summary>Scenario A proof: a Reader plugin added AFTER the product is built.
/// It only binds three public capabilities — node catalog, Elsa activity
/// catalog and the shared reader broker. Core, Workflow Core and the designer
/// are untouched; the palette query immediately publishes the new node.</summary>
public sealed class IndustrialJsonReaderComponent : Component
{
    public const string NodeTypeId = "industrial.reader.json";
    public const string ActivityType = "Industrial.Activities.Reader.Json";

    public override string Name => "industrial-reader-json";

    public override IReadOnlyList<ICapabilityKey> Inject() =>
    [
        IndustrialKeys.NodeCatalog,
        IndustrialKeys.ElsaActivityCatalog,
        IndustrialKeys.RecordReaderBroker,
    ];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var catalog = context.Require(IndustrialKeys.NodeCatalog);
        var activities = context.Require(IndustrialKeys.ElsaActivityCatalog);
        var broker = context.Require(IndustrialKeys.RecordReaderBroker);
        var descriptor = Descriptor();
        IDisposable? route = null;

        await context.EffectAsync(
            async () =>
            {
                await catalog.RegisterAsync(descriptor);
                activities.Register(ActivityType);
                route = broker.Register(".json",
                    new RecordReaderProvider(".json", new JsonRecordReader()));
            },
            async () =>
            {
                route?.Dispose();
                activities.Unregister(ActivityType);
                await catalog.UnregisterAsync(descriptor.NodeTypeId);
            });
        return null;
    }

    private static NodeDescriptor Descriptor() => new(
        NodeTypeId, "JSON Reader", "Reads a JSON array of flat records", "Parsing.Reader",
        "FileJson", "1.0.0",
        [new NodePort("input", PortType.FileBatch, true)],
        [new NodePort("records", PortType.RecordSet, true)],
        new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceId"] = new Dictionary<string, object?>
                {
                    ["type"] = "string", ["title"] = "Resource", ["format"] = "resource",
                },
            },
        },
        ActivityType, []);
}
