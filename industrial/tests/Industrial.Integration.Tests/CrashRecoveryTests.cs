using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Industrial.Tests;

/// <summary>The product acceptance scenario: one CSV, one Reader and Mapping,
/// two database targets. A hard exit after the first partial write must resume
/// the original Elsa instance and retry only the incomplete delivery.</summary>
public sealed class CrashRecoveryTests
{
    [Fact]
    public async Task TwoTargetMidWriteCrashResumesAndRetriesOnlyTheIncompleteTarget()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "20261126.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, "20261126.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var ledgerPath = Path.Combine(temp.Root, "industrial-ledger.db");
        var factories = new FactorySet(targetA, targetB) { CrashTargetB = true };

        var definition = new WorkflowDefinition(
            "two-target-flow", "Two target flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv",
                    new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("storeA", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 300, 0),
                new WorkflowNode("storeB", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "storeA", "records"),
                new WorkflowEdge("map", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        // First process: the second sink commits one row and then the process
        // vanishes before the delivery or the Elsa activity can complete.
        await Compose(temp.Root, factories, async (catalog, workflows, resources, _, api) =>
        {
            await RegisterTargetAsync(resources, "target-a", DateTimeOffset.UtcNow);
            await RegisterTargetAsync(resources, "target-b", DateTimeOffset.UtcNow);
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "two-target-flow" }));
            try
            {
                await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = "two-target-flow",
                    ["resourceId"] = "source-1",
                    ["instanceId"] = "two-target-instance",
                });
            }
            catch (OperationCanceledException)
            {
                // The simulated hard exit deliberately aborts this call.
            }
            Assert.NotNull(await workflows.GetPublishedAsync("two-target-flow"));
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(1, await CountRowsAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.True(run.Status is CollectionStatus.Running or CollectionStatus.Interrupted);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.Equal(2, deliveries.Count);
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(delivery => delivery.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.InProgress,
                deliveries.Single(delivery => delivery.TargetId == "target-b").Status);
        }

        // Second process: recovery runs as an effect of loading the same plugin,
        // not as a manual API call. Only target-b had an incomplete delivery.
        factories.CrashTargetB = false;
        await Compose(temp.Root, factories, async (_, _, _, _, api) =>
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.Equal(1, recovery["recovered"]?.GetValue<int>());
            Assert.Empty(recovery["failures"]!.AsArray());
            var execution = Assert.IsType<JsonObject>(api.Query("industrial/execution"));
            Assert.Equal("Finished", execution["status"]!.GetValue<string>());
            Assert.Equal(new[] { "src", "read", "map", "storeA", "storeB" },
                execution["executed"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
            var nodes = execution["nodes"]!.AsArray()
                .Select(node => (JsonObject)node!)
                .ToDictionary(node => node["nodeId"]!.GetValue<string>());
            Assert.Equal("Reused", nodes["storeA"]["status"]!.GetValue<string>());
            Assert.Equal("Succeeded", nodes["storeB"]["status"]!.GetValue<string>());
            Assert.Equal(5, nodes["storeB"]["records"]!.GetValue<int>());
            Assert.Equal(3, nodes["storeB"]["samples"]!.AsArray().Count);
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(6, await CountRowsAsync(targetB));
        Assert.Equal(6, await DistinctRowCountAsync(targetA));
        Assert.Equal(6, await DistinctRowCountAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.Equal(CollectionStatus.Succeeded, run.Status);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.All(deliveries, delivery =>
                Assert.Equal(CollectionStatus.Succeeded, delivery.Status));
            Assert.Equal(6, deliveries.Single(delivery => delivery.TargetId == "target-a").Records);
            Assert.Equal(5, deliveries.Single(delivery => delivery.TargetId == "target-b").Records);
        }
    }

    [Fact]
    public async Task RealProcessKillRecoversOnlyTheIncompleteDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var csv = string.Join('\n', new[] { "record_id,reading" }
            .Concat(Enumerable.Range(1, 6).Select(index => $"{index},{index * 10}")));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "20261126.csv"), csv);
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, "20261126.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);

        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var ledgerPath = Path.Combine(temp.Root, "industrial-ledger.db");
        var marker = Path.Combine(temp.Root, "crash-ready");
        var fixtureProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../Industrial.ProcessCrashFixture/Industrial.ProcessCrashFixture.csproj"));

        var start = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{fixtureProject}\" --no-build -- \"{temp.Root}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var child = Process.Start(start)
                           ?? throw new InvalidOperationException("failed to start crash fixture");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            // Do not await stdout/stderr here: those tasks only complete after
            // the child exits, and this child is intentionally still alive.
            Assert.True(await WaitForMarkerAsync(marker, TimeSpan.FromSeconds(60)),
                "crash fixture never reached its partial-write marker");
        }
        finally
        {
            if (!child.HasExited)
            {
                // Real OS process termination, not an exception injected into
                // the workflow: no ledger completion, effect unwind or EOF.
                child.Kill(entireProcessTree: true);
            }
            await child.WaitForExitAsync();
        }
        _ = await stdout;
        _ = await stderr;

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(1, await CountRowsAsync(targetB));
        await using (var ledger = IndustrialStore.Create(ledgerPath))
        {
            var run = Assert.Single(ledger.CollectionRuns.AsNoTracking().ToList());
            Assert.True(run.Status is CollectionStatus.Running or CollectionStatus.Interrupted);
            var deliveries = ledger.FileDeliveries.AsNoTracking().ToList();
            Assert.Equal(CollectionStatus.Succeeded,
                deliveries.Single(delivery => delivery.TargetId == "target-a").Status);
            Assert.Equal(CollectionStatus.InProgress,
                deliveries.Single(delivery => delivery.TargetId == "target-b").Status);
        }

        // New process: startup recovery sees the orphaned Elsa instance and
        // resumes it from committed stages; only target-b still needs work.
        var factories = new FactorySet(targetA, targetB);
        await Compose(temp.Root, factories, async (_, _, _, _, api) =>
        {
            var recovery = Assert.IsType<JsonObject>(api.Query("industrial/recovery"));
            Assert.Equal(1, recovery["recovered"]?.GetValue<int>());
            Assert.Empty(recovery["failures"]!.AsArray());
        });

        Assert.Equal(6, await CountRowsAsync(targetA));
        Assert.Equal(6, await CountRowsAsync(targetB));
        Assert.Equal(6, await DistinctRowCountAsync(targetA));
        Assert.Equal(6, await DistinctRowCountAsync(targetB));
        await using var finalLedger = IndustrialStore.Create(ledgerPath);
        Assert.Equal(CollectionStatus.Succeeded,
            Assert.Single(finalLedger.CollectionRuns.AsNoTracking().ToList()).Status);
    }

    private static async Task<bool> WaitForMarkerAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(50);
        }
        return File.Exists(path);
    }

    private static async Task<int> CountRowsAsync(string path)
    {
        await using var db = IndustrialStore.CreateTarget(path);
        return await db.Records.AsNoTracking().CountAsync();
    }

    private static async Task<int> DistinctRowCountAsync(string path)
    {
        await using var db = IndustrialStore.CreateTarget(path);
        return await db.Records.AsNoTracking().Select(row => row.RecordId).Distinct().CountAsync();
    }

    private static async Task RegisterTargetAsync(
        IResourceRegistry resources, string id, DateTimeOffset now) =>
        await resources.RegisterAsync(new ResourceDefinition(
            id, id, id, "test", new Dictionary<string, object?>(), null,
            0, true, now, now));

    private static async Task Compose(
        string root,
        FactorySet factories,
        Func<INodeCatalog, IWorkflowRegistry, IResourceRegistry, ICollectionLedger, ApiGateway, Task> act)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());

        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
            ["root"] = root,
            ["seedResource"] = "true",
            ["sourceRoot"] = Path.Combine(root, "source"),
            ["resourceId"] = "source-1",
        });
        registry.RegisterComponentFactory("test.sinks", _ => new SinkFactoriesComponent(factories.Factories));
        var types = IndustrialComponents.Types
            .Where(type => type is not ("industrial.ui" or "industrial.reader.json"))
            .ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.sinks");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());

        await act(
            runtime.RequireCapability(IndustrialKeys.NodeCatalog),
            runtime.RequireCapability(IndustrialKeys.WorkflowRegistry),
            runtime.RequireCapability(IndustrialKeys.ResourceRegistry),
            runtime.RequireCapability(IndustrialKeys.CollectionLedger),
            console.Api);
    }

    [Fact]
    public async Task DeliveryRetryReusesPublishedMappingAndValidation()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "20261126.csv"),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, "20261126.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB) { FailTargetBOnce = true };

        var definition = new WorkflowDefinition(
            "retry-flow", "Retry flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv",
                    new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("validate", "industrial.transform.validate",
                    new Dictionary<string, object?>
                    {
                        ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                    }, 300, 0),
                new WorkflowNode("storeA", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 400, 0),
                new WorkflowNode("storeB", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 400, 200),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "validate", "input"),
                new WorkflowEdge("validate", "output", "storeA", "records"),
                new WorkflowEdge("validate", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        string failedDeliveryId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
            }));

            var delivery = (await ledger.ListAllDeliveriesAsync()).Single(candidate =>
                candidate.TargetId == "target-b" && candidate.Status == "Failed");
            failedDeliveryId = delivery.Id;
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        await using var target = IndustrialStore.CreateTarget(targetB);
        var rows = target.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"reading\"", row.PayloadJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DeliveryRetryResumesTheRunThatOwnsTheFailedDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var firstFile = $"{now:yyyyMMdd}.csv";
        var secondFile = $"{now.AddDays(-25):yyyyMMdd}.csv";
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, firstFile),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, firstFile), now.AddHours(-1).UtcDateTime);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, secondFile),
            "record_id,reading\n4,77\n5,88\n6,99\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, secondFile), now.AddDays(-25).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB);

        WorkflowDefinition Definition(int version) => new(
            "multi-run-retry-flow", "Multi run retry flow", version,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                version == 1
                    ? new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 300, 0)
                    : new WorkflowNode("storeB", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                version == 1
                    ? new WorkflowEdge("map", "output", "storeA", "records")
                    : new WorkflowEdge("map", "output", "storeB", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?> { ["lookbackDays"] = version == 1 ? 1 : 30 });

        string failedDeliveryId = string.Empty;
        string firstDeliveryId = string.Empty;
        string firstRunId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(1))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "multi-run-retry-flow" }));
            // This id sorts ABOVE the failing run's id: the old string-sort
            // heuristic would resume this unrelated execution.
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "multi-run-retry-flow", ["version"] = 1,
                ["resourceId"] = "source-1", ["instanceId"] = "zzz-unrelated-instance",
            }));
            var firstDelivery = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.TargetId == "target-a" && candidate.Status == CollectionStatus.Succeeded);
            firstDeliveryId = firstDelivery.Id;
            firstRunId = (await ledger.GetRunIdsByDeliveryAsync(firstDelivery.Id)).Single();
            File.Delete(Path.Combine(sourceRoot, firstFile));

            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(2))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "multi-run-retry-flow" }));
            factories.FailTargetBOnce = true;
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "multi-run-retry-flow", ["version"] = 2,
                ["resourceId"] = "source-1", ["instanceId"] = "aaa-owning-instance",
            }));

            var delivery = (await ledger.ListAllDeliveriesAsync()).Single(candidate =>
                candidate.TargetId == "target-b" && candidate.Status == CollectionStatus.Failed);
            failedDeliveryId = delivery.Id;
            var runIds = await ledger.GetRunIdsByDeliveryAsync(delivery.Id);
            var owner = await ledger.GetRunAsync(runIds.Single());
            Assert.Equal("aaa-owning-instance", owner!.InstanceId);
            var unrelated = (await ledger.ListRunsAsync()).Single(run => run.InstanceId == "zzz-unrelated-instance");
            Assert.NotEqual(owner.Id, unrelated.Id);

            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = failedDeliveryId,
            }));

            // The first run's successful delivery is untouched by the retry.
            var firstAfter = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.Id == firstDeliveryId);
            Assert.Equal(CollectionStatus.Succeeded, firstAfter.Status);
            Assert.Equal(firstRunId, (await ledger.GetRunIdsByDeliveryAsync(firstDeliveryId)).Single());
            Assert.Single(await ledger.ListAllDeliveriesAsync(), candidate => candidate.TargetId == "target-a");
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        await using var firstTarget = IndustrialStore.CreateTarget(targetA);
        var firstRows = firstTarget.Records.AsNoTracking().ToList();
        Assert.Equal(3, firstRows.Count);
        Assert.All(firstRows, row => Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal));
        Assert.Equal(new[] { "11", "22", "33" }, ValuesOf(firstRows));
        await using var target = IndustrialStore.CreateTarget(targetB);
        var rows = target.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"reading\"", row.PayloadJson, StringComparison.Ordinal);
        });
        Assert.Equal(new[] { "77", "88", "99" }, ValuesOf(rows));
    }

    [Fact]
    public async Task RetryUsesTheLastRunThatAttemptedTheSameDelivery()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv"),
            "record_id,reading,reading2\n1,11,101\n2,22,202\n3,33,303\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv"),
            now.AddHours(-1).UtcDateTime);
        var target = Path.Combine(temp.Root, "target.db");
        var factories = new FactorySet(Path.Combine(temp.Root, "unused.db"), target);

        WorkflowDefinition Definition(int version, string sourceField) => new(
            "last-attempt-flow", "Last attempt flow", version,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = sourceField }),
                    }, 200, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 300, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "store", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        string deliveryId = string.Empty;
        string lastRunId = string.Empty;
        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, api) =>
        {
            var registered = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "target-b", "target-b", "target-b", "fixture",
                new Dictionary<string, object?> { ["database"] = target }, null, 0, true, registered, registered));

            foreach (var (version, sourceField, instanceId) in new[]
                     {
                         (1, "reading", "aaa-old-mapping"),
                         (2, "reading2", "zzz-new-mapping"),
                     })
            {
                Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                    JsonSerializer.SerializeToNode(Definition(version, sourceField))));
                Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                    new JsonObject { ["workflowId"] = "last-attempt-flow" }));
                factories.FailTargetBOnce = true;
                Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
                {
                    ["workflowId"] = "last-attempt-flow", ["version"] = version,
                    ["resourceId"] = "source-1", ["instanceId"] = instanceId,
                }));
                var failed = (await ledger.ListAllDeliveriesAsync()).Single(candidate =>
                    candidate.TargetId == "target-b" && candidate.Status == CollectionStatus.Failed);
                deliveryId = failed.Id;
                lastRunId = (await ledger.GetRunIdsByDeliveryAsync(deliveryId)).Last();
                Assert.Equal(instanceId, await ledger.GetRunInstanceAsync(lastRunId));
                Assert.Equal(lastRunId, failed.LastAttemptRunId);
            }

            Assert.Single(await ledger.ListAllDeliveriesAsync());
            Assert.True(await api.ExecuteAsync("industrial/retry-failed", new JsonObject
            {
                ["deliveryId"] = deliveryId,
            }));
        });

        await using var db = IndustrialStore.CreateTarget(target);
        var rows = db.Records.AsNoTracking().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain("reading", row.PayloadJson, StringComparison.Ordinal);
            Assert.Contains("\"value\"", row.PayloadJson, StringComparison.Ordinal);
        });
        Assert.Equal(new[] { "101", "202", "303" }, rows
            .Select(row => JsonDocument.Parse(row.PayloadJson).RootElement
                .GetProperty("value").GetString() ?? string.Empty)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(lastRunId));
    }

    [Fact]
    public async Task LargeFileStreamsThroughReaderMappingValidationAndSink()
    {
        const int rowCount = 20_000;
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(sourceRoot, $"{now:yyyyMMdd}.csv");
        await using (var writer = new StreamWriter(file))
        {
            await writer.WriteLineAsync("record_id,reading");
            for (var i = 1; i <= rowCount; i++)
                await writer.WriteLineAsync($"{i},{i * 3}");
        }
        File.SetLastWriteTimeUtc(file, now.AddHours(-1).UtcDateTime);
        var target = Path.Combine(temp.Root, "target.db");
        var factories = new FactorySet(target, Path.Combine(temp.Root, "unused.db"));

        var definition = new WorkflowDefinition(
            "streaming-flow", "Streaming flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("map", "industrial.transform.mapping",
                    new Dictionary<string, object?>
                    {
                        ["mapping"] = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string> { ["value"] = "reading" }),
                    }, 200, 0),
                new WorkflowNode("validate", "industrial.transform.validate",
                    new Dictionary<string, object?>
                    {
                        ["required"] = JsonSerializer.SerializeToElement(new[] { "value" }),
                    }, 300, 0),
                new WorkflowNode("store", "industrial.sink.sqlite",
                    new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 400, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "map", "input"),
                new WorkflowEdge("map", "output", "validate", "input"),
                new WorkflowEdge("validate", "output", "store", "records"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, api) =>
        {
            var registered = DateTimeOffset.UtcNow;
            await resources.RegisterAsync(new ResourceDefinition(
                "target-a", "target-a", "target-a", "fixture",
                new Dictionary<string, object?> { ["database"] = target }, null, 0, true, registered, registered));
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = definition.WorkflowId }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = definition.WorkflowId,
                ["resourceId"] = "source-1",
            }));
            var delivery = (await ledger.ListAllDeliveriesAsync())
                .Single(candidate => candidate.TargetId == "target-a");
            Assert.Equal(CollectionStatus.Succeeded, delivery.Status);
            Assert.Equal(rowCount, delivery.Records);
        });

        Assert.Equal(rowCount, await CountRowsAsync(target));
        Assert.Equal(rowCount, await DistinctRowCountAsync(target));
    }

    [Fact]
    public async Task PublishANewSinkBackfillsOnlyThatTargetForHistoricalFiles()
    {
        using var temp = new TempDir();
        var sourceRoot = Path.Combine(temp.Root, "source");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "20261126.csv"),
            "record_id,reading\n1,11\n2,22\n3,33\n");
        File.SetLastWriteTimeUtc(Path.Combine(sourceRoot, "20261126.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var targetA = Path.Combine(temp.Root, "target-a.db");
        var targetB = Path.Combine(temp.Root, "target-b.db");
        var factories = new FactorySet(targetA, targetB);

        WorkflowDefinition Definition(int version, bool includeSecondSink) => new(
            "backfill-flow", "Backfill flow", version,
            includeSecondSink
                ? [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
                    new WorkflowNode("storeB", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-b" }, 200, 200),
                ]
                : [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("storeA", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "target-a" }, 200, 0),
                ],
            includeSecondSink
                ? [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "storeA", "records"),
                    new WorkflowEdge("read", "records", "storeB", "records"),
                ]
                : [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "storeA", "records"),
                ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());

        await Compose(temp.Root, factories, async (_, workflows, resources, ledger, api) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "target-a", "target-b" })
            {
                await resources.RegisterAsync(new ResourceDefinition(
                    id, id, id, "fixture", new Dictionary<string, object?>(), null,
                    0, true, now, now));
            }
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(1, false))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "backfill-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "backfill-flow", ["version"] = 1, ["resourceId"] = "source-1",
            }));
            Assert.Equal(3, await CountRowsAsync(targetA));
            Assert.False(File.Exists(targetB));

            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(Definition(2, true))));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "backfill-flow" }));
            Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
            {
                ["workflowId"] = "backfill-flow", ["version"] = 2, ["resourceId"] = "source-1",
            }));

            var deliveries = await ledger.ListAllDeliveriesAsync();
            Assert.Equal(2, deliveries.Count(delivery => delivery.Status == CollectionStatus.Succeeded));
            Assert.Single(deliveries, delivery => delivery.TargetId == "target-a");
            Assert.Single(deliveries, delivery => delivery.TargetId == "target-b");
        });

        Assert.Equal(3, await CountRowsAsync(targetA));
        Assert.Equal(3, await CountRowsAsync(targetB));
    }

    private static string[] ValuesOf(IEnumerable<TargetRecordEntity> rows) => rows
        .Select(row => JsonDocument.Parse(row.PayloadJson).RootElement
            .GetProperty("value").GetString() ?? string.Empty)
        .OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private sealed class FactorySet(string targetA, string targetB)
    {
        public bool CrashTargetB { get; set; }
        public bool FailTargetBOnce { get; set; }

        public IReadOnlyList<IRecordSinkFactory> Factories =>
        [
            new TargetFactory("target-a", () => new SqliteRecordSink(
                () => IndustrialStore.CreateTarget(targetA), "target-a")),
            new TargetFactory("target-b", () =>
            {
                if (CrashTargetB)
                {
                    return new PartialThenExitSink(() => IndustrialStore.CreateTarget(targetB), "target-b", 1);
                }
                if (FailTargetBOnce)
                {
                    FailTargetBOnce = false;
                    return new FailOnceSink(() => IndustrialStore.CreateTarget(targetB), "target-b");
                }
                return new SqliteRecordSink(() => IndustrialStore.CreateTarget(targetB), "target-b");
            }),
        ];
    }

    private sealed class FailOnceSink : IRecordSink
    {
        private readonly string _targetId;

        public FailOnceSink(Func<IndustrialTargetDbContext> _, string targetId) => _targetId = targetId;

        public string TargetId => _targetId;

        public ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default) =>
            throw new IOException("connection refused");
    }

    private sealed class TargetFactory(string resourceType, Func<IRecordSink> create) : IRecordSinkFactory
    {
        public string ResourceType => resourceType;

        public ValueTask<IRecordSink> CreateAsync(
            ResourceDefinition resource, ICredentialVault vault,
            IReadOnlyDictionary<string, object?>? nodeConfig = null,
            CancellationToken ct = default) => ValueTask.FromResult(create());
    }

    /// <summary>Commits one real row, then dies like a killed process: the
    /// ledger claim stays held and the Elsa activity never completes.</summary>
    private sealed class PartialThenExitSink(
        Func<IndustrialTargetDbContext> db, string targetId, int rowsBeforeExit) : IRecordSink
    {
        public string TargetId => targetId;

        public async ValueTask<DeliveryResultRef> WriteAsync(
            string collectionFileId, IAsyncEnumerable<RecordEnvelope> records,
            string idempotencyKey, CancellationToken ct = default)
        {
            await using var context = db();
            var written = 0;
            await foreach (var record in records.WithCancellation(ct))
            {
                var recordId = new SinkWriteOptions("append", null).ResolveId(record);
                if (!await context.Records.AnyAsync(row => row.RecordId == recordId, ct))
                {
                    context.Records.Add(new TargetRecordEntity
                    {
                        RecordId = recordId,
                        SourceIdentityKey = record.Source.Key,
                        RecordTime = record.RecordTime,
                        PayloadJson = JsonSerializer.Serialize(record.Values),
                    });
                    written++;
                }
                await context.SaveChangesAsync(ct);
                if (written == rowsBeforeExit)
                {
                    throw new OperationCanceledException("simulated process termination");
                }
            }
            return new DeliveryResultRef(idempotencyKey, targetId, written);
        }
    }

    private sealed class SinkFactoriesComponent(IReadOnlyList<IRecordSinkFactory> factories) : Component
    {
        public override string Name => "test.sink-factories";

        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.RecordSinkFactories];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.RecordSinkFactories);
            var handles = factories.Select(factory => broker.Register(factory)).ToArray();
            await context.EffectAsync(
                () => ValueTask.CompletedTask,
                () =>
                {
                    foreach (var handle in handles.Reverse()) handle.Dispose();
                    return ValueTask.CompletedTask;
                });
            return null;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = Path.Combine(Path.GetTempPath(), "industrial-crash-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
