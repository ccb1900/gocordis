using Gocordis.Core;
using Gocordis.Extensions;
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
            ["sink"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SINK") ?? "sqlite",
            ["connectionString"] =
                Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_CONNECTION_STRING") ?? string.Empty,
            ["seedResource"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SEED_RESOURCE") ?? "false",
            ["sourceRoot"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SOURCE_ROOT")
                             ?? Path.Combine(rootPath, "source"),
        };

    private static string rootPath =>
        Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ROOT")
        ?? Path.Combine(Path.GetTempPath(), "gocordis-industrial");

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
        IndustrialKeys.RecordReaderBroker,
        IndustrialKeys.RecordSinkFactories,
        IndustrialKeys.RecordSink,
        IndustrialKeys.ArchiveSink,
        IndustrialKeys.FileSource,
    ];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
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
        if (string.Equals(Setting(_config, "seedResource", "false"), "true", StringComparison.OrdinalIgnoreCase))
        {
            var id = Setting(_config, "resourceId", "source-1");
            if (resources.GetAsync(id).GetAwaiter().GetResult() is null)
            {
                resources.RegisterAsync(new Industrial.Contracts.ResourceDefinition(
                    id, "filesystem.local", "Default source", "local",
                    new Dictionary<string, object?>
                    {
                        ["root"] = Setting(_config, "sourceRoot", Path.Combine(root, "source")),
                    },
                    null, 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ).GetAwaiter().GetResult();
            }
        }
        ICollectionLedger ledger = new CollectionLedger(ledgerDb);
        var broker = new Broker<IRecordReaderProvider>();
        var routes = new List<IDisposable>();
        IFileSource source = new RoutingFileSource(resources, vault, broker);
        IHistoricalRangePlanner planner = new HistoricalRangePlanner();
        IRecordReader reader = new BrokerRecordReader(broker);
        IRecordSink sink = Setting(_config, "sink", "sqlite").ToLowerInvariant() switch
        {
            "sqlserver" => new SqlServerRecordSink(Setting(_config, "connectionString", string.Empty)),
            "postgresql" => new PostgreSqlRecordSink(Setting(_config, "connectionString", string.Empty)),
            "mysql" => new MySqlRecordSink(Setting(_config, "connectionString", string.Empty)),
            "excel" => new SqliteRecordSink(targetDb),
            _ => new SqliteRecordSink(targetDb),
        };
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
        var sinkFactories = new Broker<IRecordSinkFactory>();
        var sinkRoutes = new List<IDisposable>();
        await context.EffectAsync(
            () =>
            {
                sinkRoutes.Add(sinkFactories.Register("sqlserver", new SqlServerSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("postgresql", new PostgreSqlSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("mysql", new MySqlSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("sqlite", new SqliteSinkFactory(targetDb)));
                return ValueTask.CompletedTask;
            },
            () =>
            {
                for (var i = sinkRoutes.Count - 1; i >= 0; i--) sinkRoutes[i].Dispose();
                sinkRoutes.Clear();
                return ValueTask.CompletedTask;
            });
        context.Provide(IndustrialKeys.RecordReaderBroker, broker);
        context.Provide(IndustrialKeys.RecordSinkFactories, sinkFactories);
        context.Provide(IndustrialKeys.RecordReader, reader);
        context.Provide(IndustrialKeys.RecordSink, sink);
        context.Provide(IndustrialKeys.ArchiveSink, archive);
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            async () => { if (source is IAsyncDisposable disposable) await disposable.DisposeAsync(); });
        await context.EffectAsync(
            () =>
            {
                routes.Add(broker.Register(".csv", new RecordReaderProvider(".csv", new CsvRecordReader())));
                routes.Add(broker.Register(".xlsx", new RecordReaderProvider(".xlsx", new ExcelRecordReader())));
                return ValueTask.CompletedTask;
            },
            () =>
            {
                for (var i = routes.Count - 1; i >= 0; i--) routes[i].Dispose();
                routes.Clear();
                return ValueTask.CompletedTask;
            });
        context.Provide(IndustrialKeys.FileSource, source);
        return null;
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
