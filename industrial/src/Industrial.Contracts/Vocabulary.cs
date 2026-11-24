using Gocordis.Core;

namespace Industrial.Contracts;

/// <summary>
/// Public contract vocabulary of the industrial application. Implementations
/// never appear here; plugins reference only this assembly and bind through
/// Cordis Key&lt;T&gt; identity at runtime.
/// </summary>
public static class IndustrialKeys
{
    public static readonly Key<IResourceRegistry> ResourceRegistry = new("industrial.resource.registry");
    public static readonly Key<ICredentialVault> CredentialVault = new("industrial.credential.vault");
    public static readonly Key<IWorkflowRegistry> WorkflowRegistry = new("industrial.workflow.registry");
    public static readonly Key<INodeCatalog> NodeCatalog = new("industrial.workflow.node-catalog");
    public static readonly Key<IWorkflowValidator> WorkflowValidator = new("industrial.workflow.validator");
    public static readonly Key<ICollectionLedger> CollectionLedger = new("industrial.collection.ledger");
    public static readonly Key<ICollectionCoordinator> CollectionCoordinator = new("industrial.collection.coordinator");
    public static readonly Key<IHistoricalRangePlanner> HistoricalRangePlanner = new("industrial.collection.range-planner");
    public static readonly Key<IRecordSink> RecordSink = new("industrial.delivery.record-sink");
    public static readonly Key<IArchiveSink> ArchiveSink = new("industrial.delivery.archive-sink");
    public static readonly Key<IFileSource> FileSource = new("industrial.source.file");
    public static readonly Key<IElsaActivityCatalog> ElsaActivityCatalog = new("industrial.elsa.activity-catalog");
    public static readonly Key<IRecordReader> RecordReader = new("industrial.reader.record");
}

/// <summary>Canonical Activity type ids contributed by the built-in node
/// plugins. Both the node catalog and the Elsa catalog use this list.</summary>
public static class IndustrialActivityTypes
{
    public static readonly string[] All =
    [
        "Industrial.Activities.Trigger.Manual",
        "Industrial.Activities.Source.Filesystem",
        "Industrial.Activities.Reader.Csv",
        "Industrial.Activities.Reader.Excel",
        "Industrial.Activities.Transform.Mapping",
        "Industrial.Activities.Transform.Validate",
        "Industrial.Activities.Sink.SqlServer",
        "Industrial.Activities.Sink.Sqlite",
        "Industrial.Activities.Sink.Archive",
    ];
}

/// <summary>Activities registered by industrial plugins into Elsa. This is a
/// read-only catalog; Elsa remains the execution authority.</summary>
public interface IElsaActivityCatalog
{
    bool Contains(string activityType);
    IReadOnlyList<string> List();
    void Register(string activityType);
}

// ---- resource / credential -------------------------------------------------

public sealed record ResourceDefinition(
    string Id,
    string Type,
    string Name,
    string Provider,
    IReadOnlyDictionary<string, object?> Configuration,
    string? CredentialId,
    long Version,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CredentialView(
    string Id,
    string Name,
    string Kind,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record ConnectionLease(
    string Id,
    string ResourceId,
    DateTimeOffset AcquiredAt,
    string Owner);

/// <summary>Independent resource registry. Credentials are referenced by id,
/// never embedded in resource configuration.</summary>
public interface IResourceRegistry
{
    ValueTask<ResourceDefinition> RegisterAsync(ResourceDefinition resource, CancellationToken ct = default);
    ValueTask<ResourceDefinition?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<ResourceDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default);
}

/// <summary>Secret material is returned only to internal consumers through
/// RevealAsync; UI/API layers must use CredentialView instead.</summary>
public interface ICredentialVault
{
    ValueTask<CredentialView> PutAsync(string id, string name, string kind, string secret, CancellationToken ct = default);
    ValueTask<string> RevealAsync(string id, CancellationToken ct = default);
    ValueTask<CredentialView?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<CredentialView>> ListAsync(CancellationToken ct = default);
    ValueTask<CredentialView> RotateAsync(string id, string secret, CancellationToken ct = default);
}

// ---- workflow ---------------------------------------------------------------

public enum PortType
{
    Control,
    File,
    FileBatch,
    Dataset,
    RecordSet,
    DeliveryResult,
    Text,
}

public sealed record NodePort(string Name, PortType Type, bool Required);

public sealed record NodeDescriptor(
    string NodeTypeId,
    string Name,
    string Description,
    string Category,
    string Icon,
    string Version,
    IReadOnlyList<NodePort> Inputs,
    IReadOnlyList<NodePort> Outputs,
    IReadOnlyDictionary<string, object?> ConfigurationSchema,
    string ActivityType,
    IReadOnlyList<string> Dependencies);

public sealed record WorkflowNode(
    string Id,
    string Type,
    IReadOnlyDictionary<string, object?> Config,
    double X,
    double Y);

public sealed record WorkflowEdge(string Source, string SourcePort, string Target, string TargetPort);

public sealed record TriggerDefinition(
    string Kind,
    string? Cron,
    string? EventName,
    IReadOnlyDictionary<string, object?> Config);

/// <summary>The single editable business source of truth. Layout is metadata
/// and never affects execution semantics.</summary>
public sealed record WorkflowDefinition(
    string WorkflowId,
    string Name,
    int Version,
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges,
    TriggerDefinition Trigger,
    IReadOnlyList<string> ResourceReferences,
    IReadOnlyDictionary<string, object?> Layout);

public sealed record WorkflowValidationError(string Path, string Code, string Message);

public sealed record PublishedWorkflow(
    string WorkflowId,
    int Version,
    WorkflowDefinition Definition,
    string ElsaDefinitionJson,
    DateTimeOffset PublishedAt);

public interface INodeCatalog
{
    ValueTask RegisterAsync(NodeDescriptor descriptor, CancellationToken ct = default);
    ValueTask<NodeDescriptor?> GetAsync(string nodeTypeId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<NodeDescriptor>> ListAsync(CancellationToken ct = default);
    ValueTask<bool> UnregisterAsync(string nodeTypeId, CancellationToken ct = default);
}

public interface IWorkflowValidator
{
    ValueTask<IReadOnlyList<WorkflowValidationError>> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default);
    IReadOnlyList<string> Gates { get; }
    void UseActivityCatalog(IElsaActivityCatalog catalog);
}

public interface IWorkflowRegistry
{
    ValueTask<WorkflowDefinition> SaveDraftAsync(WorkflowDefinition definition, CancellationToken ct = default);
    ValueTask<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, string elsaDefinitionJson, CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> GetPublishedAsync(string workflowId, CancellationToken ct = default);
}

// ---- collection / delivery -------------------------------------------------

public sealed record FileIdentity(
    string Key,
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteUtc,
    string ContentHash);

public sealed record DiscoveredFile(
    FileIdentity Identity,
    string FullPath,
    DateTimeOffset SourceDate,
    string ResourceId);

public sealed record SourceDateRange(DateTimeOffset FromInclusive, DateTimeOffset ToExclusive);

public sealed record RecordEnvelope(
    IReadOnlyDictionary<string, object?> Values,
    FileIdentity Source,
    DateTimeOffset RecordTime);

public sealed record DeliveryResultRef(string Id, string TargetId, int Records);

public sealed record CollectionRun(
    string Id,
    string WorkflowId,
    DateTimeOffset StartedAt,
    string Status,
    string? Error);

public sealed record CollectionFile(
    string Id,
    string CollectionId,
    FileIdentity Identity,
    DateTimeOffset SourceDate,
    DateTimeOffset CollectedAt,
    string Status);

public sealed record FileDelivery(
    string Id,
    string CollectionFileId,
    string TargetId,
    string Status,
    int Attempts,
    string? Error,
    string IdempotencyKey);

public interface IFileSource
{
    ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default);
    ValueTask<bool> IsStableAsync(DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default);
}

public interface IRecordReader
{
    IAsyncEnumerable<RecordEnvelope> ReadAsync(DiscoveredFile file, CancellationToken ct = default);
}

public interface IRecordSink
{
    string TargetId { get; }
    ValueTask<DeliveryResultRef> WriteAsync(
        string collectionFileId,
        IAsyncEnumerable<RecordEnvelope> records,
        string idempotencyKey,
        CancellationToken ct = default);
}

public interface IArchiveSink
{
    string TargetId { get; }
    ValueTask ArchiveAsync(DiscoveredFile file, string idempotencyKey, CancellationToken ct = default);
}

public interface ICollectionLedger
{
    ValueTask<CollectionRun> StartAsync(string workflowId, CancellationToken ct = default);
    ValueTask<CollectionFile> DiscoverAsync(string collectionId, DiscoveredFile file, CancellationToken ct = default);
    ValueTask<CollectionFile?> GetFileByIdentityAsync(string identityKey, CancellationToken ct = default);
    ValueTask<CollectionFile?> GetFileAsync(string fileId, CancellationToken ct = default);
    ValueTask MarkFileAsync(string fileId, string status, CancellationToken ct = default);
    ValueTask<FileDelivery> StartDeliveryAsync(
        string collectionFileId, string targetId, string idempotencyKey, CancellationToken ct = default);
    ValueTask CompleteDeliveryAsync(string deliveryId, int records, CancellationToken ct = default);
    ValueTask FailDeliveryAsync(string deliveryId, string error, CancellationToken ct = default);
    ValueTask<IReadOnlyList<FileDelivery>> GetRetrableDeliveriesAsync(CancellationToken ct = default);
    ValueTask<IReadOnlyList<CollectionFile>> ListFilesAsync(string collectionId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<CollectionFile>> ListAllFilesAsync(CancellationToken ct = default);
}

public interface IHistoricalRangePlanner
{
    SourceDateRange Plan(DateTimeOffset now, TimeSpan exclusiveLookback);
}

/// <summary>One application-level coordinator; it uses ledger state rather than
/// an in-memory "last success date" so downtime can be backfilled.</summary>
public interface ICollectionCoordinator
{
    ValueTask<string> RunAsync(
        string workflowId,
        string resourceId,
        SourceDateRange range,
        IRecordReader reader,
        IReadOnlyList<IRecordSink> sinks,
        IArchiveSink? archive,
        CancellationToken ct = default);

    /// <summary>Retries ledger rows left failed by a previous process. Only the
    /// failed target is retried; already-succeeded deliveries are untouched.</summary>
    ValueTask<int> RetryFailedAsync(
        IReadOnlyList<IRecordSink> sinks,
        IArchiveSink? archive,
        CancellationToken ct = default);
}
