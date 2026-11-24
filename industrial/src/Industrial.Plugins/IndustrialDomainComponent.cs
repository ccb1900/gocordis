using Gocordis.Core;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Plugins;

/// <summary>All domain capabilities are ordinary Cordis providers. The plugin
/// owns no lifecycle of its own: instances live only while this fiber is Active.</summary>
public sealed class IndustrialDomainComponent : Component
{
    private readonly IReadOnlyDictionary<string, object?> _config;

    public IndustrialDomainComponent() : this(FromEnvironment()) { }

    public IndustrialDomainComponent(IReadOnlyDictionary<string, object?>? config) =>
        _config = config ?? new Dictionary<string, object?>();

    private static IReadOnlyDictionary<string, object?> FromEnvironment() =>
        new Dictionary<string, object?>
        {
            ["root"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ROOT")
                       ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial"),
            ["allowDevelopmentKey"] =
                Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY") ?? "false",
        };

    public override string Name => "industrial-domain";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
    [
        IndustrialKeys.ResourceRegistry,
        IndustrialKeys.CredentialVault,
        IndustrialKeys.WorkflowRegistry,
        IndustrialKeys.NodeCatalog,
        IndustrialKeys.WorkflowValidator,
        IndustrialKeys.CollectionLedger,
        IndustrialKeys.CollectionCoordinator,
        IndustrialKeys.HistoricalRangePlanner,
        IndustrialKeys.RecordReader,
        IndustrialKeys.RecordSink,
        IndustrialKeys.ArchiveSink,
        IndustrialKeys.FileSource,
    ];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var root = Setting(_config, "root", Path.Combine(Path.GetTempPath(), "gocordis-industrial"));
        Directory.CreateDirectory(root);

        Func<IndustrialDbContext> ledgerDb = () =>
            IndustrialStore.Create(Path.Combine(root, "industrial-ledger.db"));
        Func<IndustrialTargetDbContext> targetDb = () =>
            IndustrialStore.CreateTarget(Path.Combine(root, "industrial-target.db"));

        var resources = new ResourceRegistry(ledgerDb);
        var allowDevelopmentKey = string.Equals(
            Setting(_config, "allowDevelopmentKey", "false"), "true", StringComparison.OrdinalIgnoreCase);
        ICredentialVault vault = new CredentialVault(ledgerDb, allowDevelopmentKey);
        INodeCatalog catalog = new NodeCatalog();
        IWorkflowValidator validator = new WorkflowValidator(catalog, resources, vault);
        IWorkflowRegistry workflows = new WorkflowRegistry(ledgerDb, validator);
        resources.BindWorkflows(workflows);
        ICollectionLedger ledger = new CollectionLedger(ledgerDb);
        IFileSource source = new LocalFileSource(resources);
        IHistoricalRangePlanner planner = new HistoricalRangePlanner();
        IRecordReader reader = new CsvRecordReader();
        IRecordSink sink = new SqliteRecordSink(targetDb);
        IArchiveSink archive = new FileArchiveSink(Path.Combine(root, "archive"));
        ICollectionCoordinator coordinator = new CollectionCoordinator(resources, source, ledger, reader);

        context.Provide(IndustrialKeys.ResourceRegistry, resources);
        context.Provide(IndustrialKeys.CredentialVault, vault);
        context.Provide(IndustrialKeys.WorkflowRegistry, workflows);
        context.Provide(IndustrialKeys.NodeCatalog, catalog);
        context.Provide(IndustrialKeys.WorkflowValidator, validator);
        context.Provide(IndustrialKeys.CollectionLedger, ledger);
        context.Provide(IndustrialKeys.CollectionCoordinator, coordinator);
        context.Provide(IndustrialKeys.HistoricalRangePlanner, planner);
        context.Provide(IndustrialKeys.RecordReader, reader);
        context.Provide(IndustrialKeys.RecordSink, sink);
        context.Provide(IndustrialKeys.ArchiveSink, archive);
        context.Provide(IndustrialKeys.FileSource, source);
        return ValueTask.FromResult<Cleanup?>(null);
    }

    private static string Setting(IReadOnlyDictionary<string, object?> config, string key, string fallback)
    {
        if (!config.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }
}
