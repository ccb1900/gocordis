using System.Text.Json.Nodes;
using Gocordis.Core;
using Industrial.Contracts;

namespace Industrial.Plugins;

/// <summary>Contributes built-in node descriptions through Cordis effects.
/// Adding a new node requires no change to the UI host or workflow core.</summary>
public sealed class IndustrialNodesComponent : Component
{
    public override string Name => "industrial-nodes";

    public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.NodeCatalog];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var catalog = context.Require(IndustrialKeys.NodeCatalog);
        foreach (var descriptor in Descriptors())
        {
            await context.EffectAsync(
                async () => { await catalog.RegisterAsync(descriptor); },
                async () => { await catalog.UnregisterAsync(descriptor.NodeTypeId); });
        }

        return null;
    }

    private static IEnumerable<NodeDescriptor> Descriptors()
    {
        var types = IndustrialActivityTypes.All;
        yield return Node("industrial.trigger.manual", "Manual Trigger", "Trigger", "trigger", "Play", types[0],
            [], [new NodePort("control", PortType.Control, false)]);
        yield return Node("industrial.filesystem.local", "Local Files", "Filesystem", "source", "Folder", types[1],
            [], [new NodePort("files", PortType.FileBatch, true)]);
        yield return Node("industrial.filesystem.unc", "UNC Share", "Filesystem", "source", "Cloud", types[1],
            [], [new NodePort("files", PortType.FileBatch, true)]);
        yield return Node("industrial.reader.csv", "CSV Reader", "Parsing", "reader", "FileText", types[2],
            [new NodePort("input", PortType.FileBatch, true)], [new NodePort("records", PortType.RecordSet, true)]);
        yield return Node("industrial.reader.excel", "Excel Reader", "Parsing", "reader", "FileExcel", types[3],
            [new NodePort("input", PortType.FileBatch, true)], [new NodePort("records", PortType.RecordSet, true)]);
        yield return Node("industrial.transform.mapping", "Field Mapping", "Transform", "transform", "Swap", types[4],
            [new NodePort("input", PortType.RecordSet, true)], [new NodePort("output", PortType.RecordSet, true)]);
        yield return Node("industrial.transform.validate", "Validation", "Transform", "transform", "CheckCircle", types[5],
            [new NodePort("input", PortType.RecordSet, true)], [new NodePort("output", PortType.RecordSet, true)]);
        yield return Node("industrial.sink.sqlserver", "SQL Server", "Delivery", "sink", "Database", types[6],
            [new NodePort("records", PortType.RecordSet, true)], [new NodePort("result", PortType.DeliveryResult, true)]);
        yield return Node("industrial.sink.sqlite", "SQLite", "Delivery", "sink", "Database", types[7],
            [new NodePort("records", PortType.RecordSet, true)], [new NodePort("result", PortType.DeliveryResult, true)]);
        yield return Node("industrial.sink.postgresql", "PostgreSQL", "Delivery", "sink", "Database", types[9],
            [new NodePort("records", PortType.RecordSet, true)], [new NodePort("result", PortType.DeliveryResult, true)]);
        yield return Node("industrial.sink.mysql", "MySQL", "Delivery", "sink", "Database", types[10],
            [new NodePort("records", PortType.RecordSet, true)], [new NodePort("result", PortType.DeliveryResult, true)]);
        yield return Node("industrial.sink.archive", "Archive", "Delivery", "sink", "Archive", types[8],
            [new NodePort("files", PortType.FileBatch, false)], [new NodePort("result", PortType.DeliveryResult, false)]);
    }

    private static NodeDescriptor Node(
        string id, string name, string category, string group, string icon, string activityType,
        IReadOnlyList<NodePort> inputs, IReadOnlyList<NodePort> outputs)
    {
        IReadOnlyDictionary<string, object?> schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceId"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["title"] = "Resource",
                },
            },
        };
        return new NodeDescriptor(
            id, name, $"{name} node", $"{group}.{category}", icon, "1.0.0",
            inputs, outputs, schema, activityType, []);
    }
}
