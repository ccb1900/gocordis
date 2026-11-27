using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>Imports use a standard CSV parser: quoted commas, quotes and
/// embedded newlines are data, not record separators.</summary>
public sealed class CsvImportTests
{
    [Fact]
    public async Task QuotedCsvFieldsRoundTripThroughMachineImport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-csv-import-{Guid.NewGuid():N}");
        try
        {
            await using var runtime = CreateRuntime(root, out var api);
            var csv = "id,name,endpoint\n" +
                      "csv-comma,\"Line, A\",\"C:\\data, zone\"\n" +
                      "csv-newline,\"Line\nB\",10.0.0.2\n" +
                      "csv-quote,\"Line \"\"Q\"\"\",10.0.0.3\n";
            Assert.True(await api.ExecuteAsync("industrial/import-machines", new JsonObject { ["csv"] = csv }));

            var machines = Assert.IsType<JsonArray>(api.Query("industrial/machines"));
            var byId = machines.ToDictionary(
                row => row!["id"]!.GetValue<string>(), row => row!.AsObject(), StringComparer.Ordinal);
            Assert.Equal("Line, A", byId["csv-comma"]["name"]!.GetValue<string>());
            Assert.Equal("C:\\data, zone", byId["csv-comma"]["endpoint"]!.GetValue<string>());
            Assert.Equal("Line\nB", byId["csv-newline"]["name"]!.GetValue<string>());
            Assert.Equal("Line \"Q\"", byId["csv-quote"]["name"]!.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static Runtime CreateRuntime(string root, out ApiGateway api)
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
            ["root"] = root, ["allowDevelopmentKey"] = "true", ["seedResource"] = "false",
        });
        registry.ApplyAsync(IndustrialComponents.Types
            .Select(type => IndustrialComponents.Declaration(type)).ToArray())
            .GetAwaiter().GetResult();
        api = console.Api;
        return runtime;
    }
}
