using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Tests;

public sealed class BindingPreviewAccountingTests
{
    [Fact]
    public async Task PreviewCountsOnlyPairsThatCanBeCreated()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync(secondSource: false);

        var preview = await fixture.PreviewAsync();

        Assert.Equal(4, preview.Total);
        Assert.Equal(2, preview.Created);
        Assert.Contains("resource-m2", preview.MissingResources);
        Assert.Empty(preview.Errors);
        // Missing resources block creation even for otherwise creatable pairs.
        var created = await fixture.CreateAsync();
        Assert.Equal(0, created.Created);
        Assert.Empty(await fixture.Bindings.ListAsync());
    }

    [Fact]
    public async Task GlobalPreviewErrorsNeverProduceNegativeCreatedCounts()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync(publish: false);

        var preview = await fixture.PreviewAsync();

        Assert.Equal(4, preview.Total);
        Assert.Equal(0, preview.Created);
        Assert.Contains(preview.Errors, error => error.Contains("not published", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedBulkCreationIsIdempotent()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        var first = await fixture.CreateAsync();
        var second = await fixture.CreateAsync();

        Assert.Equal(4, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(4, (await fixture.Bindings.ListAsync()).Count);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "industrial-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "ledger.db");
            using var bootstrap = NewDb();
            Machines = new MachineRegistry(NewDb);
            Profiles = new FileProfileRegistry(NewDb);
            Bindings = new CollectionBindingRegistry(NewDb);
            Resources = new ResourceRegistry(NewDb);
            Workflows = new WorkflowRegistry(NewDb,
                new WorkflowValidator(new NodeCatalog(), Resources,
                    new CredentialVault(NewDb, allowDevelopmentKey: true)));
        }

        public string Root { get; }
        public string DatabasePath { get; }
        public MachineRegistry Machines { get; }
        public FileProfileRegistry Profiles { get; }
        public CollectionBindingRegistry Bindings { get; }
        public ResourceRegistry Resources { get; }
        public WorkflowRegistry Workflows { get; }
        public IndustrialDbContext NewDb() => IndustrialStore.Create(DatabasePath);

        public async Task SeedAsync(bool publish = true, bool secondSource = true)
        {
            await Workflows.SaveDraftAsync(new WorkflowDefinition(
                "template", "Template", 1, [], [],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                [], new Dictionary<string, object?>()));
            if (publish) await Workflows.PublishAsync("template", "{}");
            foreach (var machine in new[] { "m1", "m2" })
            {
                await Machines.RegisterAsync(new MachineDefinition(
                    machine, machine, "10.0.0.1", null, true,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                if (secondSource || machine == "m1") await RegisterResourceAsync($"resource-{machine}");
            }
            await RegisterResourceAsync("target-db");
            foreach (var profile in new[] { "p1", "p2" })
            {
                await Profiles.RegisterAsync(new FileProfileDefinition(
                    profile, profile, 0, "*.csv", "csv", ",", true, "utf-8",
                    new Dictionary<string, object?>(), [], true,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }
        }

        private Task RegisterResourceAsync(string id) => Resources.RegisterAsync(
            new ResourceDefinition(id, "filesystem.local", id, "local",
                new Dictionary<string, object?> { ["root"] = Root }, null, 0, true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).AsTask();

        public Task<BindingPreview> PreviewAsync() => Bindings.PreviewBulkAsync(
            ["m1", "m2"], ["p1", "p2"], "template", 1, "resource-{machine}", ["target-db"]).AsTask();

        public Task<BindingPreview> CreateAsync() => Bindings.CreateBulkAsync(
            ["m1", "m2"], ["p1", "p2"], "template", 1, "resource-{machine}", ["target-db"]).AsTask();

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
