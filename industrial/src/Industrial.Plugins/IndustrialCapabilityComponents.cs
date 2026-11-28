using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Plugins;

/// <summary>Smaller, independently packaged platform plugins matching the
/// collector §5.1 taxonomy. Each owns only its own business data and lifetime;
/// all share the one Cordis Runtime. Developer and packaged hosts register
/// these same component types through Industrial.Host.IndustrialComponents.</summary>
public abstract class IndustrialCapabilityComponent : Component
{
    private readonly IReadOnlyDictionary<string, object?> _config;

    protected IndustrialCapabilityComponent(IReadOnlyDictionary<string, object?>? config = null) =>
        _config = config ?? FromEnvironment();

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
            ["sourceRoot"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SOURCE_ROOT") ?? string.Empty,
            ["resourceId"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE") ?? "source-1",
        };

    protected string Setting(string key, string fallback) =>
        _config.TryGetValue(key, out var value) && value is not null &&
        !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()!
            : fallback;

    protected string Root => EnsurePersistentRoot(
        Setting("root", Path.Combine(Path.GetTempPath(), "gocordis-industrial")), AllowDevelopmentKey);

    /// <summary>Delegates to the single shared root policy.</summary>
    public static string EnsurePersistentRoot(string root, bool allowDevelopmentKey) =>
        IndustrialPaths.NormalizeRoot(root, allowDevelopmentKey);

    protected Func<IndustrialDbContext> LedgerDb =>
        () => IndustrialStore.Create(Path.Combine(Root, "industrial-ledger.db"));

    protected Func<IndustrialTargetDbContext> TargetDb =>
        () => IndustrialStore.CreateTarget(Path.Combine(Root, "industrial-target.db"));

    protected bool AllowDevelopmentKey => string.Equals(
        Setting("allowDevelopmentKey", "false"), "true", StringComparison.OrdinalIgnoreCase);
}

public sealed class IndustrialResourceComponent : IndustrialCapabilityComponent
{
    public IndustrialResourceComponent() : this(null) { }

    public IndustrialResourceComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-resource";
    public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.CredentialVault];
    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [IndustrialKeys.ResourceRegistry, IndustrialKeys.ResourceConnectionTester];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var registry = new ResourceRegistry(LedgerDb);
        context.Provide(IndustrialKeys.ResourceRegistry, registry);
        context.Provide(IndustrialKeys.ResourceConnectionTester,
            new ResourceConnectionTester(context.Require(IndustrialKeys.CredentialVault)));
        if (string.Equals(Setting("seedResource", "false"), "true", StringComparison.OrdinalIgnoreCase))
        {
            var id = Setting("resourceId", "source-1");
            if (registry.GetAsync(id).GetAwaiter().GetResult() is null)
            {
                registry.RegisterAsync(new ResourceDefinition(
                    id, "filesystem.local", "Default source", "local",
                    new Dictionary<string, object?> { ["root"] = Setting("sourceRoot", Path.Combine(Root, "source")) },
                    null, 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ).GetAwaiter().GetResult();
            }
        }

        return ValueTask.FromResult<Cleanup?>(null);
    }
}

public sealed class IndustrialMachineComponent : IndustrialCapabilityComponent
{
    public IndustrialMachineComponent() : this(null) { }

    public IndustrialMachineComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-machine";
    public override IReadOnlyList<ICapabilityKey> Provide() => [IndustrialKeys.MachineRegistry];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(IndustrialKeys.MachineRegistry, new MachineRegistry(LedgerDb));
        return ValueTask.FromResult<Cleanup?>(null);
    }
}

public sealed class IndustrialCredentialComponent : IndustrialCapabilityComponent
{
    public IndustrialCredentialComponent() : this(null) { }

    public IndustrialCredentialComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-credential";
    public override IReadOnlyList<ICapabilityKey> Provide() => [IndustrialKeys.CredentialVault];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(IndustrialKeys.CredentialVault,
            new CredentialVault(LedgerDb, AllowDevelopmentKey));
        return ValueTask.FromResult<Cleanup?>(null);
    }
}

public sealed class IndustrialWorkflowComponent : IndustrialCapabilityComponent
{
    public IndustrialWorkflowComponent() : this(null) { }

    private readonly NodeCatalog _catalog = new();

    public IndustrialWorkflowComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-workflow";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.ResourceRegistry, IndustrialKeys.CredentialVault];
    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [IndustrialKeys.NodeCatalog, IndustrialKeys.WorkflowRegistry, IndustrialKeys.WorkflowValidator];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var validator = new WorkflowValidator(
            _catalog,
            context.Require(IndustrialKeys.ResourceRegistry),
            context.Require(IndustrialKeys.CredentialVault));
        var workflows = new WorkflowRegistry(LedgerDb, validator);
        var resources = context.Require(IndustrialKeys.ResourceRegistry);
        resources.BindWorkflows(workflows);
        await context.EffectAsync(
            () => ValueTask.CompletedTask,
            () => { resources.UnbindWorkflows(); return ValueTask.CompletedTask; });
        context.Provide(IndustrialKeys.NodeCatalog, _catalog);
        context.Provide(IndustrialKeys.WorkflowValidator, validator);
        context.Provide(IndustrialKeys.WorkflowRegistry, workflows);
        return null;
    }
}

public sealed class IndustrialExecutionComponent : IndustrialCapabilityComponent
{
    public IndustrialExecutionComponent() : this(null) { }

    public IndustrialExecutionComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-execution";
    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [IndustrialKeys.CollectionLedger, IndustrialKeys.HistoricalRangePlanner,
         IndustrialKeys.StageOutputStore];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(IndustrialKeys.CollectionLedger, new CollectionLedger(LedgerDb));
        context.Provide(IndustrialKeys.HistoricalRangePlanner, new HistoricalRangePlanner());
        context.Provide(IndustrialKeys.StageOutputStore,
            new StageOutputStore(() => IndustrialStore.CreateStages(Root), Root));
        return ValueTask.FromResult<Cleanup?>(null);
    }
}

public sealed class IndustrialCollectionComponent : IndustrialCapabilityComponent
{
    public IndustrialCollectionComponent() : this(null) { }

    public IndustrialCollectionComponent(IReadOnlyDictionary<string, object?>? config = null) : base(config) { }

    public override string Name => "industrial-collection";
    public override IReadOnlyList<ICapabilityKey> Inject() =>
        [IndustrialKeys.ResourceRegistry, IndustrialKeys.CredentialVault,
         IndustrialKeys.CollectionLedger];
    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [IndustrialKeys.FileSource,
         IndustrialKeys.RecordReader, IndustrialKeys.RecordReaderBroker,
         IndustrialKeys.RecordSinkFactories, IndustrialKeys.NodeExecutionHandlers,
         IndustrialKeys.FileProfiles, IndustrialKeys.CollectionBindings,
         IndustrialKeys.RecordSink, IndustrialKeys.ArchiveSink];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var resources = context.Require(IndustrialKeys.ResourceRegistry);
        var ledger = context.Require(IndustrialKeys.CollectionLedger);
        var broker = new Broker<IRecordReaderProvider>();
        var source = new RoutingFileSource(
            resources, context.Require(IndustrialKeys.CredentialVault), broker);
        var reader = new BrokerRecordReader(broker);
        var routes = new List<IDisposable>();
        IRecordSink sink = Setting("sink", "sqlite").ToLowerInvariant() switch
        {
            "sqlserver" => new SqlServerRecordSink(Setting("connectionString", string.Empty)),
            "postgresql" => new PostgreSqlRecordSink(Setting("connectionString", string.Empty)),
            "mysql" => new MySqlRecordSink(Setting("connectionString", string.Empty)),
            _ => new SqliteRecordSink(TargetDb),
        };
        var archive = new FileArchiveSink(Path.Combine(Root, "archive"));
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
        var sinkFactories = new Broker<IRecordSinkFactory>();
        var sinkRoutes = new List<IDisposable>();
        await context.EffectAsync(
            () =>
            {
                sinkRoutes.Add(sinkFactories.Register("sqlserver", new SqlServerSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("postgresql", new PostgreSqlSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("mysql", new MySqlSinkFactory()));
                sinkRoutes.Add(sinkFactories.Register("sqlite", new SqliteSinkFactory(TargetDb)));
                return ValueTask.CompletedTask;
            },
            () =>
            {
                for (var i = sinkRoutes.Count - 1; i >= 0; i--) sinkRoutes[i].Dispose();
                sinkRoutes.Clear();
                return ValueTask.CompletedTask;
            });
        context.Provide(IndustrialKeys.FileSource, source);
        context.Provide(IndustrialKeys.RecordReaderBroker, broker);
        context.Provide(IndustrialKeys.RecordSinkFactories, sinkFactories);
        // Extension point for contributed node execution handlers. Registration
        // is Fiber-owned: disabling the plugin withdraws its exact node route.
        context.Provide(IndustrialKeys.NodeExecutionHandlers,
            new Broker<IIndustrialNodeHandler>());
        context.Provide(IndustrialKeys.RecordReader, reader);
        context.Provide(IndustrialKeys.RecordSink, sink);
        context.Provide(IndustrialKeys.ArchiveSink, archive);
        context.Provide(IndustrialKeys.FileProfiles, new FileProfileRegistry(LedgerDb));
        context.Provide(IndustrialKeys.CollectionBindings, new CollectionBindingRegistry(LedgerDb));
        return null;
    }
}
