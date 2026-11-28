using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>The named paged query variants must be real windows over the
/// ledger, not copies or mutations: truncation is bounded and the underlying
/// run/file/binding history stays byte-for-byte available to the default UI.</summary>
public sealed class IndustrialQueryPaginationTests
{
    [Fact]
    public async Task PagedQueriesTruncateResultsWithoutChangingUnderlyingData()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-paging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var runtime = new Runtime();
            var console = new ConsoleHost(runtime);
            await using var registry = new PluginRegistry(runtime);
            console.AttachRegistry(registry);
            registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
            registry.ExternalCapabilities.Add(ApiGateway.Key);
            registry.ExternalCapabilities.Add(TimerKeys.Service);
            await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
            await runtime.LoadAsync(new TimerExtensionComponent());
            IndustrialComponents.RegisterFactories(registry, CreateConfig(root));
            await registry.ApplyAsync(IndustrialComponents.Types
                .Select(type => IndustrialComponents.Declaration(type)).ToArray());

            var api = console.Api;
            var ledger = runtime.RequireCapability(IndustrialKeys.CollectionLedger);
            var bindings = runtime.RequireCapability(IndustrialKeys.CollectionBindings);

            var runs = new List<CollectionRun>();
            for (var i = 0; i < 5; i++) runs.Add(await ledger.StartAsync($"workflow-{i}"));
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(root, $"file-{i}.csv");
                await File.WriteAllTextAsync(path, "record_id\n1\n");
                await ledger.DiscoverAsync(runs[0].Id, new DiscoveredFile(
                    FileIdentityFactory.Create(path, root, "source-1"), path,
                    DateTimeOffset.UtcNow, "source-1"));
            }
            for (var i = 0; i < 4; i++)
            {
                await bindings.RegisterAsync(new CollectionBindingDefinition(
                    $"binding-{i}", $"Binding {i}", "machine-1", "workflow-1", 1,
                    "profile-1", "source-1", ["target-1"],
                    new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
                    true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            var fullRuns = Assert.IsType<JsonObject>(api.Query("industrial/execution-center"))["runs"]!.AsArray();
            var fullFiles = Assert.IsType<JsonArray>(api.Query("industrial/file-details"));
            var fullBindings = Assert.IsType<JsonArray>(api.Query("industrial/bindings"));
            Assert.Equal(5, fullRuns.Count);
            Assert.Equal(3, fullFiles.Count);
            Assert.Equal(4, fullBindings.Count);

            // industrialPageSize = 2: page 0 and 1 are full, page 2 has one row,
            // and page 3 is empty. Same window applies to every paged query.
            Assert.Equal(2, RunsOnPage(api, "industrial/execution-center", 0).Count);
            Assert.Equal(2, RunsOnPage(api, "industrial/execution-center", 1).Count);
            Assert.Single(RunsOnPage(api, "industrial/execution-center", 2));
            Assert.Empty(RunsOnPage(api, "industrial/execution-center", 3));
            Assert.Equal(2, Assert.IsType<JsonArray>(api.Query("industrial/file-details/page/0")).Count);
            Assert.Single(Assert.IsType<JsonArray>(api.Query("industrial/file-details/page/1")));
            Assert.Equal(2, Assert.IsType<JsonArray>(api.Query("industrial/bindings/page/0")).Count);
            Assert.Equal(2, Assert.IsType<JsonArray>(api.Query("industrial/bindings/page/1")).Count);

            // Paging is read-only: the default query and the ledger still see
            // every row after the paged variants have been used.
            Assert.Equal(5, Assert.IsType<JsonObject>(api.Query("industrial/execution-center"))["runs"]!.AsArray().Count);
            Assert.Equal(3, Assert.IsType<JsonArray>(api.Query("industrial/file-details")).Count);
            Assert.Equal(4, Assert.IsType<JsonArray>(api.Query("industrial/bindings")).Count);
            Assert.Equal(5, (await ledger.ListRunsAsync()).Count);
            Assert.Equal(3, (await ledger.ListAllFilesAsync()).Count);
            Assert.Equal(4, (await bindings.ListAsync()).Count);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static JsonArray RunsOnPage(ApiGateway api, string query, int page) =>
        Assert.IsType<JsonObject>(api.Query($"{query}/page/{page}"))["runs"]!.AsArray();

    private static IReadOnlyDictionary<string, object?> CreateConfig(string root) =>
        new Dictionary<string, object?>
        {
            ["root"] = root,
            ["allowDevelopmentKey"] = "true",
            ["industrialPageSize"] = "2",
        };
}
