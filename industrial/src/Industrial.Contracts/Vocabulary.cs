using Gocordis.Core;
using Gocordis.Extensions;

namespace Industrial.Contracts;

/// <summary>
/// Public contract vocabulary of the industrial application. Implementations
/// never appear here; plugins reference only this assembly and bind through
/// Cordis Key&lt;T&gt; identity at runtime.
/// </summary>
public static class IndustrialKeys
{
    public static readonly Key<IResourceRegistry> ResourceRegistry = new("industrial.resource.registry");
    public static readonly Key<IMachineRegistry> MachineRegistry = new("industrial.machine.registry");
    public static readonly Key<ICredentialVault> CredentialVault = new("industrial.credential.vault");
    public static readonly Key<IWorkflowRegistry> WorkflowRegistry = new("industrial.workflow.registry");
    public static readonly Key<INodeCatalog> NodeCatalog = new("industrial.workflow.node-catalog");
    public static readonly Key<IWorkflowValidator> WorkflowValidator = new("industrial.workflow.validator");
    public static readonly Key<ICollectionLedger> CollectionLedger = new("industrial.collection.ledger");
    public static readonly Key<ICollectionCoordinator> CollectionCoordinator = new("industrial.collection.coordinator");
    public static readonly Key<IHistoricalRangePlanner> HistoricalRangePlanner = new("industrial.collection.range-planner");
    public static readonly Key<IRecordSink> RecordSink = new("industrial.delivery.record-sink");
    public static readonly Key<IArchiveSink> ArchiveSink = new("industrial.delivery.archive-sink");
    /// <summary>Target resources select a sink implementation by resource TYPE.
    /// Credentials stay in the vault; node configuration only carries an id.</summary>
    public static readonly Key<Broker<IRecordSinkFactory>> RecordSinkFactories =
        new("industrial.delivery.sink-factories");
    /// <summary>Plugins may contribute an exact-node-type execution handler.</summary>
    public static readonly Key<Broker<IIndustrialNodeHandler>> NodeExecutionHandlers =
        new("industrial.execution.node-handlers");
    public static readonly Key<IFileSource> FileSource = new("industrial.source.file");
    public static readonly Key<IFileProfileRegistry> FileProfiles = new("industrial.config.file-profiles");
    public static readonly Key<ICollectionBindingRegistry> CollectionBindings =
        new("industrial.config.collection-bindings");
    public static readonly Key<IElsaActivityCatalog> ElsaActivityCatalog = new("industrial.elsa.activity-catalog");
    public static readonly Key<IRecordReader> RecordReader = new("industrial.reader.record");
    /// <summary>Multi-implementation reader routes. A new Reader plugin adds a
    /// named file-extension route here; it never edits Core or the designer.</summary>
    public static readonly Key<Broker<IRecordReaderProvider>> RecordReaderBroker =
        new("industrial.reader.broker");
    public static readonly Key<IStageOutputStore> StageOutputStore =
        new("industrial.execution.stage-output-store");
    public static readonly Key<IResourceConnectionTester> ResourceConnectionTester =
        new("industrial.resource.connection-tester");
}

/// <summary>Durable identity for a binding's scheduled cron occurrences. It
/// reuses the ledger's single at-most-once fire table without pretending a
/// binding is a workflow definition: the sentinel version 0 and this prefix
/// keep the two namespaces apart.</summary>
public static class BindingSchedule
{
    public const string FirePrefix = "binding-schedule:";
    public const int FireVersion = 0;

    public static string FireWorkflowId(string bindingId) => FirePrefix + bindingId;

    public static string? BindingIdFromFireWorkflowId(string value) =>
        value.StartsWith(FirePrefix, StringComparison.Ordinal)
            ? value[FirePrefix.Length..]
            : null;
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
        "Industrial.Activities.Sink.PostgreSql",
        "Industrial.Activities.Sink.MySql",
    ];
}

/// <summary>Activities registered by industrial plugins into Elsa. This is a
/// read-only catalog; Elsa remains the execution authority.</summary>
public interface IElsaActivityCatalog
{
    bool Contains(string activityType);
    IReadOnlyList<string> List();
    void Register(string activityType);
    void Unregister(string activityType);
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
    DateTimeOffset UpdatedAt,
    string? MachineId = null);

/// <summary>A collectable machine is an independent domain object, distinct
/// from a resource (a connection target) and a credential (secret material).</summary>
public sealed record MachineDefinition(
    string Id,
    string Name,
    string Endpoint,
    string? CredentialId,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IMachineRegistry
{
    ValueTask<MachineDefinition> RegisterAsync(MachineDefinition machine, CancellationToken ct = default);
    ValueTask<MachineDefinition?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<MachineDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default);
}

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
    void BindWorkflows(IWorkflowRegistry workflows);
    void UnbindWorkflows();
}

/// <summary>Secret material is returned only to internal consumers through
/// RevealAsync; UI/API layers must use CredentialView instead.</summary>
/// <summary>Read-only probe used by the resource management UI. It opens a
/// connection or checks a directory without writing business data, so a user
/// can validate configuration before publishing a workflow.</summary>
public sealed record ResourceTestResult(
    bool Ok, string Detail, string Code = "OK", DateTimeOffset? TestedAt = null,
    string? CredentialVersion = null);

/// <summary>Credential shapes understood by the runtime. Structured UI fields
/// are normalized into the secret JSON consumed by sinks and UNC leases.</summary>
public static class CredentialKinds
{
    public const string UsernamePassword = "username-password";
    public const string DomainAccount = "domain-account";
    public const string ApiToken = "api-token";
    public static readonly string[] All = [UsernamePassword, DomainAccount, ApiToken];
}

public interface IResourceConnectionTester
{
    ValueTask<ResourceTestResult> TestAsync(
        ResourceDefinition resource, CancellationToken ct = default);
}

public interface ICredentialVault
{
    ValueTask<CredentialView> PutAsync(string id, string name, string kind, string secret, CancellationToken ct = default);
    ValueTask<string> RevealAsync(string id, CancellationToken ct = default);
    ValueTask<CredentialView?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<CredentialView>> ListAsync(CancellationToken ct = default);
    ValueTask<CredentialView> RotateAsync(string id, string secret, CancellationToken ct = default);
    ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default);
}

/// <summary>Reusable file parsing/mapping/validation configuration. Does not
/// contain machine identity or connection secrets.</summary>
public sealed record MetadataRule(string From, string Pattern, string Key, bool Required = true);

public sealed record FileProfileDefinition(
    string Id, string Name, int Version,
    string FileNamePattern, string Format, string Delimiter,
    bool HasHeader, string Encoding,
    IReadOnlyDictionary<string, object?> Mapping,
    IReadOnlyList<string> RequiredFields,
    bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string DirectoryPattern = "", string DatePattern = "", bool Flat = false,
    IReadOnlyList<MetadataRule>? MetadataRules = null);

/// <summary>Binds a machine + file profile + workflow template version to the
/// concrete resources used at runtime. A binding never stores a copied workflow
/// definition and never stores secrets.</summary>
public sealed record CollectionBindingDefinition(
    string Id, string Name, string MachineId,
    string WorkflowId, int WorkflowVersion,
    string FileProfileId, string SourceResourceId,
    IReadOnlyList<string> TargetResourceIds,
    TriggerDefinition Trigger,
    bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, object?>? Parameters = null);

public sealed record BindingPreview(
    int Total, int Created, IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> MissingResources, IReadOnlyList<string> Errors);

public interface IFileProfileRegistry
{
    ValueTask<FileProfileDefinition> RegisterAsync(FileProfileDefinition profile, CancellationToken ct = default);
    ValueTask<FileProfileDefinition?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<FileProfileDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default);
}

public interface ICollectionBindingRegistry
{
    ValueTask<CollectionBindingDefinition> RegisterAsync(CollectionBindingDefinition binding, CancellationToken ct = default);
    ValueTask<CollectionBindingDefinition?> GetAsync(string id, CancellationToken ct = default);
    ValueTask<IReadOnlyList<CollectionBindingDefinition>> ListAsync(CancellationToken ct = default);
    ValueTask<BindingPreview> PreviewBulkAsync(
        IReadOnlyList<string> machineIds, IReadOnlyList<string> fileProfileIds,
        string workflowId, int workflowVersion, string sourceResourcePattern,
        IReadOnlyList<string> targetResourceIds, IReadOnlyList<string>? exclusions = null,
        CancellationToken ct = default);
    ValueTask<BindingPreview> CreateBulkAsync(
        IReadOnlyList<string> machineIds, IReadOnlyList<string> fileProfileIds,
        string workflowId, int workflowVersion, string sourceResourcePattern,
        IReadOnlyList<string> targetResourceIds, string triggerKind = "manual",
        string? cron = null, IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyList<string>? exclusions = null, CancellationToken ct = default);
    ValueTask<bool> RemoveAsync(string id, CancellationToken ct = default);
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
    /// <summary>Next immutable published version for a workflow.</summary>
    ValueTask<int> NextPublishVersionAsync(string workflowId, CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, string elsaDefinitionJson, CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, int version, string elsaDefinitionJson, CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, WorkflowDefinition snapshot, string elsaDefinitionJson,
        CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> PublishAsync(
        string workflowId, WorkflowDefinition snapshot, int version, string elsaDefinitionJson,
        CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> GetPublishedAsync(string workflowId, CancellationToken ct = default);
    ValueTask<PublishedWorkflow?> GetPublishedVersionAsync(
        string workflowId, int version, CancellationToken ct = default);
    ValueTask<IReadOnlyList<PublishedWorkflow>> ListPublishedAsync(string workflowId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<PublishedWorkflow>> ListAllPublishedAsync(CancellationToken ct = default);
}

// ---- collection / delivery -------------------------------------------------

public static class CollectionStatus
{
    public const string Created = "Created";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string PartialFailure = "PartialFailure";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Interrupted = "Interrupted";

    public const string Pending = "Pending";
    public const string InProgress = "InProgress";

    public static bool IsTerminal(string status) =>
        status is Succeeded or PartialFailure or Failed or Cancelled;
}

public static class ScheduleFireStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public static bool IsTerminal(string status) => status is Succeeded or Failed;
}

public sealed record ScheduleFireClaim(string Key, string Token, int Attempts);

public sealed record ScheduleFireView(
    string Key, string WorkflowId, int Version, DateTimeOffset DueAtUtc,
    string Status, int Attempts, string? Error, DateTimeOffset? FinishedAtUtc,
    long LeaseExpiresAtUnixMs = 0)
{
    /// <summary>All predicates live in one place so workflow/version scoping
    /// cannot be lost to operator-precedence mistakes at a call site.</summary>
    public bool IsRecoverable(string workflowId, int version, long nowUnixMs) =>
        string.Equals(WorkflowId, workflowId, StringComparison.Ordinal) &&
        Version == version &&
        ((Status is ScheduleFireStatus.Pending or ScheduleFireStatus.Failed) ||
         (Status == ScheduleFireStatus.Running && LeaseExpiresAtUnixMs <= nowUnixMs));
}

public sealed record FileIdentity(
    string Key,
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteUtc,
    string ContentHash,
    string SourceResourceId = "");

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

public sealed record BatchFileRef(
    string CollectionFileId, string ResourceId, string IdentityKey,
    string RelativePath, string ContentHash, DateTimeOffset SourceDate,
    long Length = 0, DateTimeOffset? LastWriteUtc = null,
    string? SnapshotPath = null);

public sealed record FileBatchRef(
    string Id, string InstanceId, string NodeId, string Fingerprint,
    IReadOnlyList<BatchFileRef> Files, string? RunId = null);

public sealed record DatasetRef(
    string Id, string InstanceId, string NodeId, string Fingerprint,
    string SpoolPath, int RecordCount, string SourceBatchRefId);

public sealed record TransformResultRef(
    string Id, string InstanceId, string NodeId, string Fingerprint,
    DatasetRef Dataset, int Accepted, int Rejected);

public sealed record DeliveryResultRef(
    string Id, string TargetId, int Records,
    string? IdempotencyKey = null, string? DeliveryId = null, string? DatasetRefId = null);

public sealed record RejectedRecord(string RecordId, string Code, string Message);

public sealed record ProcessingResult(
    int Accepted,
    int Rejected,
    int Warnings,
    IReadOnlyList<RejectedRecord> RejectedSamples,
    IReadOnlyDictionary<string, object?> Statistics);

public sealed record StageOutputView(
    string InstanceId, string NodeId, string Kind, string Fingerprint, string RefId);

public interface IStageOutputStore
{
    ValueTask<T?> TryGetAsync<T>(string instanceId, string nodeId, string fingerprint,
        CancellationToken ct = default) where T : class;
    ValueTask<T> CommitAsync<T>(string instanceId, string nodeId, string fingerprint,
        T value, CancellationToken ct = default) where T : class;
    ValueTask<DatasetRef> SpoolAsync(string instanceId, string nodeId, string fingerprint,
        IAsyncEnumerable<RecordEnvelope> records, CancellationToken ct = default);
    IAsyncEnumerable<RecordEnvelope> ReadAsync(DatasetRef dataset, CancellationToken ct = default);
    ValueTask<IReadOnlyList<StageOutputView>> ListAsync(string instanceId, CancellationToken ct = default);
}

public sealed record CollectionRun(
    string Id,
    string WorkflowId,
    DateTimeOffset StartedAt,
    string Status,
    string? Error,
    DateTimeOffset? FinishedAt = null,
    string? InstanceId = null);

public sealed record CollectionFile(
    string Id,
    string CollectionId,
    FileIdentity Identity,
    DateTimeOffset SourceDate,
    DateTimeOffset CollectedAt,
    string Status);

/// <summary>Business date identity: one source/binding day, independent of
/// WorkflowRun. This is the industrial equivalent of Go's CollectionDate.
/// </summary>
public sealed record CollectionDateState(
    string Id,
    string BindingId,
    DateOnly BusinessDate,
    string Status,
    int Attempts,
    string? LeaseToken,
    DateTimeOffset? LeaseExpiresAt,
    string? LastRunId,
    int Files,
    string? Error);

public static class CollectionDateStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string NoData = "NoData";
    public const string Failed = "Failed";
    public const string Interrupted = "Interrupted";
}

public sealed record FileDelivery(
    string Id,
    string CollectionFileId,
    string TargetId,
    string Status,
    int Attempts,
    string? Error,
    string IdempotencyKey,
    int Records = 0,
    string TargetKind = "record",
    DateTimeOffset? ClaimedAt = null,
    string? ClaimToken = null,
    DateTimeOffset? LeaseExpiresAt = null,
    string? LastError = null,
    int Accepted = 0,
    int Rejected = 0,
    int Warnings = 0,
    string? StatisticsJson = null,
    string? LastAttemptRunId = null);

public sealed record DeliveryClaim(
    string DeliveryId,
    string ClaimToken,
    DateTimeOffset LeaseExpiresAt,
    int Attempts);

public sealed record DeliveryOutcome(bool Accepted, string Reason)
{
    public static DeliveryOutcome Ok() => new(true, string.Empty);
    public static DeliveryOutcome Rejected(string reason) => new(false, reason);
}

public interface IFileSource
{
    ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default);

    /// <summary>Profile-aware discovery. Date-directory/filename rules and
    /// planned business dates are applied before the range filter so a late
    /// file cannot be discarded because of an old mtime.</summary>
    ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, FileProfileDefinition? profile,
        IReadOnlySet<DateOnly>? businessDates, CancellationToken ct = default);

    /// <summary>Holds any credentialed source session (SMB) for the whole
    /// workflow execution. Sources without a session return null.</summary>
    ValueTask<IAsyncDisposable?> LeaseScopeAsync(
        string resourceId, CancellationToken ct = default) =>
        ValueTask.FromResult<IAsyncDisposable?>(null);

    ValueTask<bool> IsStableAsync(DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default);
}

public interface IRecordReader
{
    IAsyncEnumerable<RecordEnvelope> ReadAsync(DiscoveredFile file, CancellationToken ct = default);
}

/// <summary>One named route in the shared reader broker. The file extension is
/// the routing key; the reader itself stays a pure capability.</summary>
public interface IRecordReaderProvider
{
    string FileExtension { get; }
    IRecordReader Reader { get; }
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

/// <summary>Creates a sink bound to one configured target Resource. The
/// credential is revealed here from the vault, never from node configuration.</summary>
public interface IRecordSinkFactory
{
    string ResourceType { get; }
    ValueTask<IRecordSink> CreateAsync(
        ResourceDefinition resource, ICredentialVault vault,
        IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default);
}

public interface IArchiveSink
{
    string TargetId { get; }
    ValueTask ArchiveAsync(DiscoveredFile file, string idempotencyKey, CancellationToken ct = default);
}

public enum IndustrialNodeCompletion
{
    None,
    /// <summary>The handler owns one or more durable deliveries and therefore
    /// participates in the run completion decision.</summary>
    Delivery,
}

public interface IIndustrialNodeHandler
{
    string NodeType { get; }
    IndustrialNodeCompletion Completion => IndustrialNodeCompletion.None;
    ValueTask ExecuteAsync(IndustrialNodeExecutionContext context, CancellationToken ct = default);
}

public sealed record IndustrialNodeExecutionContext(
    WorkflowDefinition Definition,
    WorkflowNode Node,
    string InstanceId,
    string? RunId,
    IReadOnlyList<string> Inputs,
    Func<string, CancellationToken, IAsyncEnumerable<RecordEnvelope>> Records,
    IStageOutputStore Stage,
    ICollectionLedger Ledger,
    IResourceRegistry Resources,
    ICredentialVault Vault,
    Broker<IRecordSinkFactory> SinkFactories,
    IRecordSink DefaultSink,
    IFileSource Source,
    IRecordReader Reader,
    Action<int, IReadOnlyList<string>> Complete);

public interface ICollectionLedger
{
    ValueTask<CollectionRun> StartAsync(string workflowId, CancellationToken ct = default);
    ValueTask<CollectionRun> BeginRunAsync(string runId, CancellationToken ct = default);
    ValueTask<CollectionRun> CompleteRunAsync(
        string runId, string status, string? error, CancellationToken ct = default);
    ValueTask<CollectionRun> FinalizeRunAsync(string runId, CancellationToken ct = default);
    ValueTask<CollectionRun> CancelRunAsync(string runId, string reason, CancellationToken ct = default);
    /// <summary>Business-date ledger: ids are bindingId|YYYY-MM-DD.</summary>
    ValueTask<IReadOnlyList<CollectionDateState>> ListDatesAsync(CancellationToken ct = default);
    ValueTask<CollectionDateState?> ClaimDateAsync(
        string bindingId, DateOnly date, string runId, TimeSpan lease, CancellationToken ct = default);
    ValueTask CompleteDateAsync(
        string id, string token, string status, int files, string? error, CancellationToken ct = default);
    ValueTask<int> RecoverExpiredLeasesAsync(DateTimeOffset now, CancellationToken ct = default);
    ValueTask<int> RecoverAbandonedLeasesAsync(CancellationToken ct = default);
    /// <summary>Atomic at-most-once claim for a scheduled cron occurrence.
    /// The durable row survives restarts and multiple hosts.</summary>
    ValueTask<bool> TryClaimScheduleFireAsync(
        string workflowId, int version, DateTimeOffset dueAtUtc, CancellationToken ct = default);
    /// <summary>Materialize planned cron instants before claiming them. Rows are
    /// insert-or-ignore so a restart never duplicates a planned fire.</summary>
    ValueTask EnsureScheduleFiresAsync(
        string workflowId, int version, IEnumerable<DateTimeOffset> dueInstants,
        CancellationToken ct = default);
    /// <summary>Atomically acquire a renewable claim on a planned fire. The
    /// caller must CompleteScheduleFireAsync with the returned token.</summary>
    ValueTask<ScheduleFireClaim?> TryStartScheduleFireAsync(
        string workflowId, int version, DateTimeOffset dueAtUtc, TimeSpan lease,
        CancellationToken ct = default);
    ValueTask CompleteScheduleFireAsync(
        string key, string token, string status, string? error, CancellationToken ct = default);
    /// <summary>Startup-only: turn claims whose owner died into recoverable failures.</summary>
    ValueTask<int> AbandonScheduleFiresAsync(CancellationToken ct = default);
    ValueTask<IReadOnlyList<ScheduleFireView>> ListScheduleFiresAsync(CancellationToken ct = default);
    /// <summary>Per-version scan position for cron catch-up. Skipped is the
    /// cumulative count of overdue fires exceeding the bounded catch-up limit.
    /// </summary>
    ValueTask<(DateTimeOffset? Checkpoint, int Skipped, bool BacklogTruncated)> GetScheduleCheckpointAsync(
        string workflowId, int version, CancellationToken ct = default);
    ValueTask SetScheduleCheckpointAsync(
        string workflowId, int version, DateTimeOffset checkpoint, int skippedIncrement,
        CancellationToken ct = default);
    /// <summary>Atomically materializes planned fires and advances the scan
    /// checkpoint. Returns false when another tick already moved the checkpoint.
    /// </summary>
    ValueTask<bool> CommitScheduleWindowAsync(
        string workflowId, int version, DateTimeOffset? expectedCheckpoint,
        DateTimeOffset nextCheckpoint, IReadOnlyList<DateTimeOffset> dueInstants,
        int skippedIncrement, bool backlogTruncated, CancellationToken ct = default);
    ValueTask MarkDeliveryContestedAsync(
        string runId, string deliveryId, string reason, CancellationToken ct = default);
    ValueTask<CollectionRun?> GetRunAsync(string runId, CancellationToken ct = default);
    /// <summary>Bind a run to the Elsa instance that produced it. Idempotent:
    /// rebinding a different instance is rejected so a run can never be
    /// silently repointed at another execution's stage data.</summary>
    ValueTask<bool> SetRunInstanceAsync(string runId, string instanceId, CancellationToken ct = default);
    ValueTask<string?> GetRunInstanceAsync(string runId, CancellationToken ct = default);
    /// <summary>The exact definition snapshot used by the run, including
    /// binding parameters and FileProfile substitution.</summary>
    ValueTask SetRunDefinitionAsync(string runId, string definitionJson, CancellationToken ct = default);
    ValueTask<string?> GetRunDefinitionAsync(string runId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<CollectionRun>> ListRunsAsync(CancellationToken ct = default);
    ValueTask<CollectionFile> DiscoverAsync(string collectionId, DiscoveredFile file, CancellationToken ct = default);
    ValueTask<CollectionFile?> GetFileByIdentityAsync(string identityKey, CancellationToken ct = default);
    ValueTask<CollectionFile?> GetFileByIdentityAsync(
        string resourceId, string identityKey, CancellationToken ct = default);
    ValueTask<CollectionFile?> GetFileAsync(string fileId, CancellationToken ct = default);
    ValueTask MarkFileAsync(string fileId, string status, CancellationToken ct = default);
    ValueTask<FileDelivery> StartDeliveryAsync(
        string collectionFileId, string targetId, string idempotencyKey,
        string targetKind = "record", string? runId = null, CancellationToken ct = default);
    ValueTask<DeliveryClaim?> TryClaimDeliveryAsync(
        string runId, string deliveryId, TimeSpan lease, CancellationToken ct = default);
    ValueTask<bool> RenewDeliveryLeaseAsync(
        string deliveryId, string claimToken, TimeSpan lease, CancellationToken ct = default);
    ValueTask<bool> TryClaimDeliveryAsync(string deliveryId, CancellationToken ct = default);
    ValueTask<DeliveryOutcome> CompleteDeliveryAsync(
        string deliveryId, string claimToken, int records, CancellationToken ct = default);
    ValueTask<DeliveryOutcome> CompleteDeliveryAsync(
        string deliveryId, string claimToken, ProcessingResult result, CancellationToken ct = default);
    ValueTask CompleteDeliveryAsync(string deliveryId, int records, CancellationToken ct = default);
    ValueTask<DeliveryOutcome> FailDeliveryAsync(
        string deliveryId, string claimToken, string error, CancellationToken ct = default);
    ValueTask FailDeliveryAsync(string deliveryId, string error, CancellationToken ct = default);
    ValueTask<IReadOnlyList<FileDelivery>> GetRetrableDeliveriesAsync(CancellationToken ct = default);
    ValueTask<IReadOnlyList<FileDelivery>> GetRetriableDeliveriesAsync(
        string? targetId = null, CancellationToken ct = default);
    ValueTask<FileDelivery?> GetDeliveryAsync(string deliveryId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<string>> GetRunIdsByDeliveryAsync(
        string deliveryId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<FileDelivery>> ListAllDeliveriesAsync(CancellationToken ct = default);
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
    ValueTask<int> RetryFailedAsync(
        string? targetId,
        IReadOnlyList<IRecordSink> sinks,
        IArchiveSink? archive,
        CancellationToken ct = default);
}
