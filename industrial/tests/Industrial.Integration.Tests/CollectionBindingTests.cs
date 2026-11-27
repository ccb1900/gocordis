using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Tests;

/// <summary>80 machines x 10 file profiles must produce 800 reusable bindings,
/// not 800 workflow definitions.</summary>
public sealed class CollectionBindingTests
{
    [Fact]
    public async Task BulkBindingsCreateEightHundredRowsWithoutCopyingWorkflows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-bindings-{Guid.NewGuid():N}.db");
        try
        {
            var newDb = () => IndustrialStore.Create(dbPath);
            await using (var db = newDb()) { }
            var machines = new MachineRegistry(newDb);
            var profiles = new FileProfileRegistry(newDb);
            var bindings = new CollectionBindingRegistry(newDb);
            var validator = new WorkflowValidator(new NodeCatalog(),
                new ResourceRegistry(newDb),
                new CredentialVault(newDb, allowDevelopmentKey: true));
            var workflows = new WorkflowRegistry(newDb, validator);
            await workflows.SaveDraftAsync(new WorkflowDefinition(
                "template", "Template", 1, [], [],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                [], new Dictionary<string, object?>()));
            await workflows.PublishAsync("template", "{}");

            var machineIds = Enumerable.Range(0, 80).Select(i => $"machine-{i}").ToArray();
            var profileIds = Enumerable.Range(0, 10).Select(i => $"profile-{i}").ToArray();
            var resourceIds = new HashSet<string>(machineIds.Select(m => $"resource-{m}"));
            resourceIds.Add("target-db");
            foreach (var machine in machineIds)
            {
                await machines.RegisterAsync(new MachineDefinition(machine, machine, "10.0.0.1",
                    null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                // Source resources are validated by id; seed them through the registry.
                var resources = new ResourceRegistry(newDb);
                await resources.RegisterAsync(new ResourceDefinition($"resource-{machine}",
                    "filesystem.local", machine, "local",
                    new Dictionary<string, object?> { ["root"] = "/tmp" }, null, 0, true,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }
            await resourcesSeedTarget(newDb);
            foreach (var profile in profileIds)
            {
                await profiles.RegisterAsync(new FileProfileDefinition(profile, profile, 0,
                    "*.csv", "csv", ",", true, "utf-8",
                    new Dictionary<string, object?> { ["value"] = "reading" },
                    ["value"], true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            var preview = await bindings.PreviewBulkAsync(machineIds, profileIds,
                "template", 1, "resource-{machine}", ["target-db"]);
            Assert.Equal(800, preview.Total);
            Assert.Empty(preview.Errors);
            Assert.Empty(preview.MissingResources);
            Assert.Empty(preview.Conflicts);

            var result = await bindings.CreateBulkAsync(machineIds, profileIds,
                "template", 1, "resource-{machine}", ["target-db"]);
            Assert.Equal(800, result.Created);
            Assert.Equal(800, (await bindings.ListAsync()).Count);
            // One immutable published workflow; bindings do not create definitions.
            Assert.Single(await workflows.ListPublishedAsync("template"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public void TemplateParametersAreSubstitutedWithoutMutatingThePublishedDefinition()
    {
        var definition = new WorkflowDefinition(
            "t", "t", 1,
            [new WorkflowNode("sink", "industrial.sink.sqlite",
                new Dictionary<string, object?> { ["targetResourceId"] = "{{TargetResource}}" }, 0, 0)],
            [], new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["{{SourceResource}}"], new Dictionary<string, object?>());
        var bound = Industrial.Elsa.WorkflowBinding.BindParameters(definition,
            new Dictionary<string, string>
            {
                ["SourceResource"] = "machine-42-source", ["TargetResource"] = "target-db",
            });
        Assert.Equal("target-db", bound.Nodes[0].Config["targetResourceId"]);
        Assert.Equal(["machine-42-source"], bound.ResourceReferences);
        Assert.Equal("{{TargetResource}}", definition.Nodes[0].Config["targetResourceId"]);
    }

    [Fact]
    public async Task BulkPreviewReportsConflictsAndMissingResources()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-binding-preview-{Guid.NewGuid():N}.db");
        try
        {
            var newDb = () => IndustrialStore.Create(dbPath);
            await using (var db = newDb()) { }
            var bindings = new CollectionBindingRegistry(newDb);
            var preview = await bindings.PreviewBulkAsync(["missing-machine"], ["missing-profile"],
                "unknown", 7, "resource-{machine}", ["missing-target"]);
            Assert.Equal(1, preview.Total);
            Assert.Contains(preview.Errors, error => error.Contains("unknown"));
            Assert.Contains(preview.MissingResources, resource => resource == "missing-target");
            Assert.Contains(preview.Errors, error => error.Contains("missing-machine"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static async Task resourcesSeedTarget(Func<IndustrialDbContext> newDb)
    {
        var resources = new ResourceRegistry(newDb);
        await resources.RegisterAsync(new ResourceDefinition("target-db", "sqlite", "target", "local",
            new Dictionary<string, object?>(), null, 0, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }


}
