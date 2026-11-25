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

    // Control flow is typed exactly like data flow. Every non-trigger node
    // accepts an optional Control input so a Manual Trigger can start it
    // without pretending that the trigger produces a FileBatch.
    private static IReadOnlyList<NodePort> WithControl(params NodePort[] dataInputs) =>
        [.. dataInputs, new NodePort("control", PortType.Control, false)];

    private static IEnumerable<NodeDescriptor> Descriptors()
    {
        var types = IndustrialActivityTypes.All;
        yield return Node("industrial.trigger.manual", "Manual Trigger", "Trigger", "trigger", "Play", types[0],
            [], [new NodePort("control", PortType.Control, false)]);
        yield return Node("industrial.filesystem.local", "Local Files", "Filesystem", "source", "Folder", types[1],
            WithControl(), [new NodePort("files", PortType.FileBatch, true)],
            ResourceSchema("resourceId", "Source resource", "filesystem.local"));
        yield return Node("industrial.filesystem.unc", "UNC Share", "Filesystem", "source", "Cloud", types[1],
            WithControl(), [new NodePort("files", PortType.FileBatch, true)],
            ResourceSchema("resourceId", "Source resource", "filesystem.unc"));
        yield return Node("industrial.reader.csv", "CSV Reader", "Parsing", "reader", "FileText", types[2],
            WithControl(new NodePort("input", PortType.FileBatch, true)), [new NodePort("records", PortType.RecordSet, true)]);
        yield return Node("industrial.reader.excel", "Excel Reader", "Parsing", "reader", "FileExcel", types[3],
            WithControl(new NodePort("input", PortType.FileBatch, true)), [new NodePort("records", PortType.RecordSet, true)]);
        yield return Node("industrial.transform.mapping", "Field Mapping", "Transform", "transform", "Swap", types[4],
            WithControl(new NodePort("input", PortType.RecordSet, true)), [new NodePort("output", PortType.RecordSet, true)],
            new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["mapping"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["title"] = "Field mapping (target ← source)",
                        ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["default"] = new Dictionary<string, object?>(),
                    },
                },
            });
        yield return Node("industrial.transform.validate", "Validation", "Transform", "transform", "CheckCircle", types[5],
            WithControl(new NodePort("input", PortType.RecordSet, true)), [new NodePort("output", PortType.RecordSet, true)],
            new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["required"] = new Dictionary<string, object?>
                    {
                        ["type"] = "array",
                        ["title"] = "Required fields",
                        ["items"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["default"] = Array.Empty<string>(),
                    },
                    ["types"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["title"] = "Field types",
                        ["additionalProperties"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["enum"] = new[] { "int", "integer", "decimal", "double", "datetime" },
                        },
                        ["default"] = new Dictionary<string, object?>(),
                    },
                },
            });
        yield return Node("industrial.sink.sqlserver", "SQL Server", "Delivery", "sink", "Database", types[6],
            WithControl(new NodePort("records", PortType.RecordSet, true)), [new NodePort("result", PortType.DeliveryResult, true)],
            TargetSchema("sqlserver"));
        yield return Node("industrial.sink.sqlite", "SQLite", "Delivery", "sink", "Database", types[7],
            WithControl(new NodePort("records", PortType.RecordSet, true)), [new NodePort("result", PortType.DeliveryResult, true)],
            ResourceSchema("targetResourceId", "Target resource"));
        yield return Node("industrial.sink.postgresql", "PostgreSQL", "Delivery", "sink", "Database", types[9],
            WithControl(new NodePort("records", PortType.RecordSet, true)), [new NodePort("result", PortType.DeliveryResult, true)],
            TargetSchema("postgresql"));
        yield return Node("industrial.sink.mysql", "MySQL", "Delivery", "sink", "Database", types[10],
            WithControl(new NodePort("records", PortType.RecordSet, true)), [new NodePort("result", PortType.DeliveryResult, true)],
            TargetSchema("mysql"));
        yield return Node("industrial.sink.archive", "Archive", "Delivery", "sink", "Archive", types[8],
            WithControl(new NodePort("files", PortType.FileBatch, false)), [new NodePort("result", PortType.DeliveryResult, false)]);
    }

    private static IReadOnlyDictionary<string, object?> ResourceSchema(
        string key, string title, string? resourceType = null)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                [key] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["title"] = title,
                    ["format"] = "resource",
                },
            },
        };
        if (resourceType is not null) schema["resourceType"] = resourceType;
        return schema;
    }

    private static IReadOnlyDictionary<string, object?> TargetSchema(string resourceType) =>
        ResourceSchema("targetResourceId", "Target resource", resourceType);

    private static NodeDescriptor Node(
        string id, string name, string category, string group, string icon, string activityType,
        IReadOnlyList<NodePort> inputs, IReadOnlyList<NodePort> outputs,
        IReadOnlyDictionary<string, object?>? schema = null)
    {
        schema ??= new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
        };
        return new NodeDescriptor(
            id, name, $"{name} node", $"{group}.{category}", icon, "1.0.0",
            inputs, outputs, schema, activityType, []);
    }
}
