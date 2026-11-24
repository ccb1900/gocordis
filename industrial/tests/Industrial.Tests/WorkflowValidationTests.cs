using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Tests;

/// <summary>Workflow definition is the single editable source. Validation
/// errors are located to node/edge paths, and adding a node never requires
/// changing the workflow core or the designer core.</summary>
public sealed class WorkflowValidationTests
{
    [Fact]
    public async Task ValidDefinitionCanBeSavedPublishedAndReadBack()
    {
        using var fixture = new Fixture();
        await fixture.Catalog.RegisterAsync(Source());
        await fixture.Catalog.RegisterAsync(Reader());
        await fixture.Catalog.RegisterAsync(Sink());
        var definition = Definition();

        var saved = await fixture.Workflows.SaveDraftAsync(definition);
        Assert.Equal(definition, saved);
        var published = await fixture.Workflows.PublishAsync("daily", "{\"elsa\":\"definition\"}");
        Assert.NotNull(published);
        Assert.Equal(1, published.Version);
        var readBack = (await fixture.Workflows.GetPublishedAsync("daily"))!.Definition;
        Assert.Equal("daily", readBack.WorkflowId);
        Assert.Equal(1, readBack.Version);
        Assert.Equal(definition.Nodes.Count, readBack.Nodes.Count);
        Assert.Equal(definition.Edges.Count, readBack.Edges.Count);

        var republished = await fixture.Workflows.PublishAsync("daily", "{\"elsa\":\"v2\"}");
        Assert.NotNull(republished);
        Assert.Equal(2, (await fixture.Workflows.GetPublishedAsync("daily"))!.Version);
    }

    [Fact]
    public async Task ValidationErrorsPointToTheExactNodeOrEdge()
    {
        using var fixture = new Fixture();
        await fixture.Catalog.RegisterAsync(Sink());

        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "daily", "Daily", 1,
            [new WorkflowNode("sink", "industrial.sink.sqlite", new Dictionary<string, object?>(), 0, 0)],
            [],
            new TriggerDefinition("schedule", "0 2 * * *", null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));

        Assert.Contains(errors, error => error.Path == "nodes.sink.inputs.records");
        Assert.DoesNotContain(errors, error => error.Path.StartsWith("trigger.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourceTypeMismatchAndInvalidTriggerAreLocated()
    {
        using var fixture = new Fixture();
        await fixture.Resources.RegisterAsync(new ResourceDefinition(
            "db", "database.sqlserver", "DB", "sqlserver",
            new Dictionary<string, object?>(), null, 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        await fixture.Catalog.RegisterAsync(new NodeDescriptor(
            "typed.source", "Typed Source", "Typed", "source", "Folder", "1.0.0",
            [], [], new Dictionary<string, object?> { ["resourceType"] = "filesystem.local" },
            IndustrialActivityTypes.All[1], []));

        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "bad", "Bad", 1,
            [new WorkflowNode("source", "typed.source",
                new Dictionary<string, object?> { ["resourceId"] = "db" }, 0, 0)],
            [], new TriggerDefinition("unknown", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));

        Assert.Contains(errors, error => error.Code == "RESOURCE_TYPE_MISMATCH");
        Assert.Contains(errors, error => error.Path == "trigger.kind" && error.Code == "INVALID_TRIGGER");
    }

    [Fact]
    public async Task MissingCronIsLocated()
    {
        using var fixture = new Fixture();
        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "scheduled", "Scheduled", 1, [],
            [], new TriggerDefinition("schedule", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));
        Assert.Contains(errors, error => error.Path == "trigger.cron" && error.Code == "MISSING_CRON");
    }

    [Fact]
    public async Task RemainingGatesAreLocated()
    {
        using var fixture = new Fixture();
        await fixture.Resources.RegisterAsync(new ResourceDefinition(
            "sql", "database.sqlserver", "SQL", "sqlserver",
            new Dictionary<string, object?>(), "missing-credential", 0, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        await fixture.Catalog.RegisterAsync(new NodeDescriptor(
            "strict.sql", "Strict SQL", "Strict", "sink", "Database", "1.0.0",
            [], [new NodePort("files", PortType.FileBatch, true)],
            new Dictionary<string, object?>
            {
                ["resourceType"] = "database.sqlserver",
            },
            IndustrialActivityTypes.All[6], []));
        await fixture.Catalog.RegisterAsync(new NodeDescriptor(
            "cycle.node", "Cycle Node", "Cycle", "transform", "Swap", "1.0.0",
            [new NodePort("in", PortType.FileBatch, true)],
            [new NodePort("out", PortType.FileBatch, true)],
            new Dictionary<string, object?>(), "Not.Registered.Activity", []));

        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "remaining", "Remaining", 1,
            [
                new WorkflowNode("source", "strict.sql", new Dictionary<string, object?> { ["resourceId"] = "sql" }, 0, 0),
                new WorkflowNode("a", "cycle.node", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("b", "cycle.node", new Dictionary<string, object?>(), 200, 0),
            ],
            [
                new WorkflowEdge("a", "out", "b", "in"),
                new WorkflowEdge("b", "out", "a", "in"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["missing-resource"], new Dictionary<string, object?>()));

        Assert.Contains(errors, error => error.Code == "CREDENTIAL_MISSING");
        Assert.Contains(errors, error => error.Code == "GRAPH_CYCLE");
        Assert.Contains(errors, error => error.Code == "ACTIVITY_NOT_REGISTERED");
        Assert.Contains(errors, error => error.Path == "resourceReferences.missing-resource");
    }

    [Fact]
    public async Task PortTypeMismatchIsRejectedAtTheEdge()
    {
        using var fixture = new Fixture();
        await fixture.Catalog.RegisterAsync(Source());
        await fixture.Catalog.RegisterAsync(Reader());
        await fixture.Catalog.RegisterAsync(Sink());
        var definition = Definition() with
        {
            Edges = [new WorkflowEdge("source", "files", "sink", "records")],
        };

        var errors = await fixture.Validator.ValidateAsync(definition);
        Assert.Contains(errors, error => error.Path == "edges[0]" && error.Code == "PORT_TYPE_MISMATCH");
    }

    [Fact]
    public async Task NewNodePluginIsDiscoveredWithoutChangingCore()
    {
        using var fixture = new Fixture();
        await fixture.Catalog.RegisterAsync(new NodeDescriptor(
            "industrial.reader.parquet", "Parquet Reader", "Parse Parquet", "reader.parquet",
            "FileText", "1.0.0",
            [new NodePort("input", PortType.FileBatch, true)],
            [new NodePort("records", PortType.RecordSet, true)],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []));

        var descriptor = await fixture.Catalog.GetAsync("industrial.reader.parquet");
        Assert.NotNull(descriptor);
        Assert.Equal(PortType.RecordSet, descriptor.Outputs[0].Type);

        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "parquet", "Parquet", 1,
            [new WorkflowNode("reader", "industrial.reader.parquet", new Dictionary<string, object?>(), 0, 0)],
            [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));
        Assert.DoesNotContain(errors, error => error.Code == "UNKNOWN_NODE_TYPE");
    }

    [Fact]
    public async Task AdditionalValidationGatesAreLocated()
    {
        using var fixture = new Fixture();
        await fixture.Catalog.RegisterAsync(new NodeDescriptor(
            "strict.reader", "Strict Reader", "Strict", "reader", "FileText", "2.0.0",
            [], [], new Dictionary<string, object?>
            {
                ["required"] = new[] { "resourceId" },
                ["resourceType"] = "filesystem.local",
            },
            IndustrialActivityTypes.All[2], [IndustrialActivityTypes.All[1]]));

        var errors = await fixture.Validator.ValidateAsync(new WorkflowDefinition(
            "strict", "Strict", 1,
            [
                new WorkflowNode("a", "strict.reader", new Dictionary<string, object?>(), 0, 0),
                new WorkflowNode("a", "strict.reader", new Dictionary<string, object?>(), 0, 0),
            ],
            [], new TriggerDefinition("event", null, null, new Dictionary<string, object?>()),
            [], new Dictionary<string, object?>()));

        Assert.Contains(errors, error => error.Code == "DUPLICATE_NODE_ID");
        Assert.Contains(errors, error => error.Code == "NODE_VERSION_INCOMPATIBLE");
        Assert.Contains(errors, error => error.Code == "REQUIRED_CONFIG_MISSING");
        Assert.Contains(errors, error => error.Code == "DEPENDENCY_MISSING");
        Assert.Contains(errors, error => error.Code == "MISSING_EVENT");
        Assert.Equal(13, fixture.Validator.Gates.Count);
    }

    private static WorkflowDefinition Definition() => new(
        "daily", "Daily collection", 1,
        [
            new WorkflowNode("source", "industrial.filesystem.local", new Dictionary<string, object?>(), 0, 0),
            new WorkflowNode("reader", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
            new WorkflowNode("sink", "industrial.sink.sqlite", new Dictionary<string, object?>(), 200, 0),
        ],
        [
            new WorkflowEdge("source", "files", "reader", "input"),
            new WorkflowEdge("reader", "records", "sink", "records"),
        ],
        new TriggerDefinition("schedule", "0 2 * * *", null, new Dictionary<string, object?>()),
        [], new Dictionary<string, object?>());

    private static NodeDescriptor Source() => new(
        "industrial.filesystem.local", "Local Files", "Filesystem source", "source.filesystem", "Folder",
        "1.0.0", [], [new NodePort("files", PortType.FileBatch, true)],
        new Dictionary<string, object?>(), IndustrialActivityTypes.All[1], []);

    private static NodeDescriptor Reader() => new(
        "industrial.reader.csv", "CSV Reader", "CSV parser", "reader.csv", "FileText",
        "1.0.0", [new NodePort("input", PortType.FileBatch, true)],
        [new NodePort("records", PortType.RecordSet, true)],
        new Dictionary<string, object?>(), IndustrialActivityTypes.All[2], []);

    private static NodeDescriptor Sink() => new(
        "industrial.sink.sqlite", "SQLite Sink", "SQLite target", "sink.sqlite", "Database",
        "1.0.0", [new NodePort("records", PortType.RecordSet, true)], [],
        new Dictionary<string, object?>(), IndustrialActivityTypes.All[7], []);

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-workflow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DbPath = Path.Combine(Root, "ledger.db");
            using (var db = IndustrialStore.Create(DbPath)) { }
            Catalog = new NodeCatalog();
            Resources = new ResourceRegistry(() => IndustrialStore.Create(DbPath));
            Vault = new CredentialVault(() => IndustrialStore.Create(DbPath), allowDevelopmentKey: true);
            Validator = new WorkflowValidator(Catalog, Resources, Vault);
            Workflows = new WorkflowRegistry(() => IndustrialStore.Create(DbPath), Validator);
            Resources.BindWorkflows(Workflows);
        }

        public string Root { get; }
        public string DbPath { get; }
        public NodeCatalog Catalog { get; }
        public ResourceRegistry Resources { get; }
        public ICredentialVault Vault { get; }
        public WorkflowValidator Validator { get; }
        public WorkflowRegistry Workflows { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
