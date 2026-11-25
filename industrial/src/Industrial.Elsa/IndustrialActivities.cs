using System.Runtime.CompilerServices;
using Elsa.Extensions;
using Elsa.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Industrial.Elsa;

/// <summary>Common behavior for persisted industrial activities. Each concrete
/// subclass is a distinct Elsa Activity type; the narrow executor bridge is the
/// only place where Cordis-owned data work is invoked.</summary>
public abstract class IndustrialNodeActivityBase : CodeActivity
{
    protected IndustrialNodeActivityBase(
        [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null)
        : base(source, line) { }

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var executor = context.GetRequiredService<IIndustrialNodeExecutor>();
        var nodeId = context.Activity.Id;
        var instanceId = context.WorkflowExecutionContext.Properties.TryGetValue("industrial.instanceId", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
        var nodeType = GetType().Name switch
        {
            nameof(ManualTriggerActivity) => "trigger",
            nameof(LocalFileSourceActivity) or nameof(UncFileSourceActivity) => "source",
            nameof(CsvReaderActivity) or nameof(ExcelReaderActivity) => "reader",
            nameof(MappingActivity) => "mapping",
            nameof(ValidationActivity) => "validate",
            nameof(ArchiveSinkActivity) => "archive",
            _ => "sink",
        };
        return executor.ExecuteAsync(instanceId, nodeId, nodeType, nodeType, context.CancellationToken);
    }
}

public sealed class ManualTriggerActivity : IndustrialNodeActivityBase
{
    public ManualTriggerActivity() : this(null, null) { }
    public ManualTriggerActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class LocalFileSourceActivity : IndustrialNodeActivityBase
{
    public LocalFileSourceActivity() : this(null, null) { }
    public LocalFileSourceActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class UncFileSourceActivity : IndustrialNodeActivityBase
{
    public UncFileSourceActivity() : this(null, null) { }
    public UncFileSourceActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class CsvReaderActivity : IndustrialNodeActivityBase
{
    public CsvReaderActivity() : this(null, null) { }
    public CsvReaderActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class ExcelReaderActivity : IndustrialNodeActivityBase
{
    public ExcelReaderActivity() : this(null, null) { }
    public ExcelReaderActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class MappingActivity : IndustrialNodeActivityBase
{
    public MappingActivity() : this(null, null) { }
    public MappingActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class ValidationActivity : IndustrialNodeActivityBase
{
    public ValidationActivity() : this(null, null) { }
    public ValidationActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class SqlServerSinkActivity : IndustrialNodeActivityBase
{
    public SqlServerSinkActivity() : this(null, null) { }
    public SqlServerSinkActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class SqliteSinkActivity : IndustrialNodeActivityBase
{
    public SqliteSinkActivity() : this(null, null) { }
    public SqliteSinkActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class ArchiveSinkActivity : IndustrialNodeActivityBase
{
    public ArchiveSinkActivity() : this(null, null) { }
    public ArchiveSinkActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class PostgreSqlSinkActivity : IndustrialNodeActivityBase
{
    public PostgreSqlSinkActivity() : this(null, null) { }
    public PostgreSqlSinkActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}
public sealed class MySqlSinkActivity : IndustrialNodeActivityBase
{
    public MySqlSinkActivity() : this(null, null) { }
    public MySqlSinkActivity([CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : base(source, line) { }
}

/// <summary>Maps an industrial node type to its real Elsa Activity type. The
/// mapping is persisted at publish time, so a later plugin change cannot alter
/// the Activity shape of an already published workflow.</summary>
public static class IndustrialNodeActivityFactory
{
    private static readonly Dictionary<string, Type> ByNodeType = new(StringComparer.Ordinal)
    {
        ["industrial.trigger.manual"] = typeof(ManualTriggerActivity),
        ["industrial.filesystem.local"] = typeof(LocalFileSourceActivity),
        ["industrial.filesystem.unc"] = typeof(UncFileSourceActivity),
        ["industrial.reader.csv"] = typeof(CsvReaderActivity),
        ["industrial.reader.excel"] = typeof(ExcelReaderActivity),
        ["industrial.transform.mapping"] = typeof(MappingActivity),
        ["industrial.transform.validate"] = typeof(ValidationActivity),
        ["industrial.sink.sqlserver"] = typeof(SqlServerSinkActivity),
        ["industrial.sink.sqlite"] = typeof(SqliteSinkActivity),
        ["industrial.sink.archive"] = typeof(ArchiveSinkActivity),
        ["industrial.sink.postgresql"] = typeof(PostgreSqlSinkActivity),
        ["industrial.sink.mysql"] = typeof(MySqlSinkActivity),
        ["industrial.reader.json"] = typeof(CsvReaderActivity),
    };

    private static readonly Type[] ActivityTypes =
    [
        typeof(ManualTriggerActivity), typeof(LocalFileSourceActivity), typeof(UncFileSourceActivity),
        typeof(CsvReaderActivity), typeof(ExcelReaderActivity), typeof(MappingActivity),
        typeof(ValidationActivity), typeof(SqlServerSinkActivity), typeof(SqliteSinkActivity),
        typeof(ArchiveSinkActivity), typeof(PostgreSqlSinkActivity), typeof(MySqlSinkActivity),
    ];

    public static IReadOnlyList<Type> RegisteredActivityTypes => ActivityTypes;

    private static Type Resolve(string nodeType)
    {
        if (ByNodeType.TryGetValue(nodeType, out var type)) return type;
        // Test descriptors and future plugins may use a local node id. Map by
        // the declared node category so the published Activity is still real.
        if (nodeType.Contains("archive", StringComparison.OrdinalIgnoreCase)) return typeof(ArchiveSinkActivity);
        if (nodeType.Contains("mapping", StringComparison.OrdinalIgnoreCase)) return typeof(MappingActivity);
        if (nodeType.Contains("validate", StringComparison.OrdinalIgnoreCase)) return typeof(ValidationActivity);
        if (nodeType.Contains("sink", StringComparison.OrdinalIgnoreCase)) return typeof(SqliteSinkActivity);
        if (nodeType.Contains("source", StringComparison.OrdinalIgnoreCase)) return typeof(LocalFileSourceActivity);
        if (nodeType.Contains("reader", StringComparison.OrdinalIgnoreCase)) return typeof(CsvReaderActivity);
        if (nodeType.Contains("trigger", StringComparison.OrdinalIgnoreCase)) return typeof(ManualTriggerActivity);
        throw new InvalidOperationException($"no Elsa Activity is contributed for node type '{nodeType}'");
    }

    public static string ElsaActivityName(string nodeType) => Resolve(nodeType).Name;

    public static IndustrialNodeActivityBase Create(
        string nodeId, string nodeType, string activityType, string edgeSummary)
    {
        var type = Resolve(nodeType);
        var activity = (IndustrialNodeActivityBase)Activator.CreateInstance(type)!;
        activity.Id = nodeId;
        // Name carries the persisted node type; the serializer round-trips it,
        // so resumed instances still know which capability to invoke.
        activity.Name = nodeType;
        return activity;
    }
}
