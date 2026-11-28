using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Host;
using Industrial.Plugins;
using Microsoft.Data.Sqlite;

namespace Industrial.Tests;

/// <summary>A FileProfile is only real if it changes parsed/mapped output.
/// This test uses a semicolon CSV, a mapped field, a required field and a
/// non-matching file, then inspects the persisted sink row.</summary>
public sealed class FileProfileRuntimeTests
{
    [Fact]
    public async Task FileProfileDrivesPatternParsingMappingAndValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-file-profile-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        var completedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var sourceFile = $"{completedDate:yyyyMMdd}.csv";
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, sourceFile),
            "record_id;reading\n1;42\n2;\n");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "ignored.txt"), "record_id;reading\n9;99\n");
        var targetDb = Path.Combine(root, "target.db");
        try
        {
            await using var runtime = CreateRuntime(root, sourceRoot, out var api);
            Assert.True(await api.ExecuteAsync("industrial/save-resource", new JsonObject
            {
                ["id"] = "fp-target", ["name"] = "Target", ["type"] = "sqlite", ["provider"] = "local",
                ["config"] = new JsonObject { ["database"] = targetDb }, ["enabled"] = true,
            }));
            Assert.True(await api.ExecuteAsync("industrial/save-file-profile", new JsonObject
            {
                ["id"] = "fp-semicolon", ["name"] = "Semicolon", ["fileNamePattern"] = "*.csv",
                ["format"] = "csv", ["delimiter"] = ";", ["hasHeader"] = true, ["encoding"] = "utf-8",
                ["mapping"] = new JsonObject { ["value"] = "reading" },
                ["required"] = new JsonArray("value"), ["enabled"] = true,
            }));

            var definition = new WorkflowDefinition(
                "fp-template", "File profile template", 1,
                [
                    new WorkflowNode("src", "industrial.filesystem.local",
                        new Dictionary<string, object?> { ["resourceId"] = "{{SourceResource}}" }, 0, 0),
                    new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                    new WorkflowNode("map", "industrial.transform.mapping",
                        new Dictionary<string, object?> { ["mapping"] = new Dictionary<string, object?>() }, 200, 0),
                    new WorkflowNode("check", "industrial.transform.validate",
                        new Dictionary<string, object?> { ["required"] = Array.Empty<string>() }, 300, 0),
                    new WorkflowNode("store", "industrial.sink.sqlite",
                        new Dictionary<string, object?> { ["targetResourceId"] = "{{TargetResource}}" }, 400, 0),
                ],
                [
                    new WorkflowEdge("src", "files", "read", "input"),
                    new WorkflowEdge("read", "records", "map", "input"),
                    new WorkflowEdge("map", "output", "check", "input"),
                    new WorkflowEdge("check", "output", "store", "records"),
                ],
                new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                ["{{SourceResource}}"], new Dictionary<string, object?>());
            Assert.True(await api.ExecuteAsync("industrial/save-workflow",
                JsonSerializer.SerializeToNode(definition)));
            Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
                new JsonObject { ["workflowId"] = "fp-template" }));
            Assert.True(await api.ExecuteAsync("industrial/import-bindings", new JsonObject
            {
                ["csv"] = "id,name,machineId,workflowId,workflowVersion,fileProfileId," +
                          "sourceResourceId,targetResourceIds,triggerKind,cron\n" +
                          "fp-binding,Profile binding,fp-machine,fp-template,1,fp-semicolon," +
                          "source-1,fp-target,manual,\n",
            }));
            Assert.True(await api.ExecuteAsync("industrial/run-binding", new JsonObject
            {
                ["bindingId"] = "fp-binding", ["lookbackDays"] = 1,
            }));

            var collections = Assert.IsType<JsonArray>(api.Query("industrial/collections"));
            Assert.Contains(collections, row => row!["path"]!.GetValue<string>() == sourceFile);
            Assert.DoesNotContain(collections, row => row!["path"]!.GetValue<string>() == "ignored.txt");

            await using var connection = new SqliteConnection($"Data Source={targetDb}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Records;";
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT PayloadJson FROM Records LIMIT 1;";
            var payload = (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
            Assert.Contains("\"value\"", payload, StringComparison.Ordinal);
            Assert.Contains("42", payload, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(string root, string sourceRoot, out ApiGateway api)
    {
        var runtime = new Runtime();
        var console = new ConsoleHost(runtime);
        var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        runtime.LoadAsync(new ConsoleCapabilitiesComponent(console)).GetAwaiter().GetResult();
        runtime.LoadAsync(new TimerExtensionComponent()).GetAwaiter().GetResult();
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "true",
            ["sourceRoot"] = sourceRoot, ["resourceId"] = "source-1",
            ["schedulerIntervalSeconds"] = "3600",
        });
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        return runtime;
    }
}
