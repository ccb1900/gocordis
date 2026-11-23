using System.Text.Json.Nodes;
using Gocordis.Client;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Client.Tests;

public sealed class ConsoleClientTests
{
    [Fact]
    public async Task ClientReadsMetaPagesPanelsModulesAndNamedQueries()
    {
        await using var runtime = new Runtime();
        var host = new ConsoleHost(runtime);
        host.Ui.RegisterPage(ContributionOwner.For("test", "sdk"),
            new UiPageDefinition("sdk-page", "SDK Page", "/sdk", "generic"));
        await using var server = new WebUiServer(host);
        await server.StartAsync();

        using var client = new ConsoleClient(server.Address!);

        var meta = await client.GetMetaAsync();
        Assert.Equal("gocordis-console", meta.Kind);
        Assert.Equal("1", meta.FrameworkVersion);

        var page = Assert.Single(await client.GetPagesAsync());
        Assert.Equal("sdk-page", page.Id);
        Assert.Equal("/sdk", page.Route);
        Assert.Empty(await client.GetPanelsAsync());

        var modules = await client.GetClientModulesAsync();
        Assert.True(modules.Revision > 0);
        Assert.Empty(modules.Modules);

        var snapshot = await client.QueryAsync("runtime/snapshot");
        Assert.NotNull(snapshot?["data"]?["fibers"]);
    }

    [Fact]
    public async Task ClientExecutesNamedCommandsThroughEnvelope()
    {
        await using var runtime = new Runtime();
        var host = new ConsoleHost(runtime);
        var owner = ContributionOwner.For("test", "sdk-command");
        host.Api.RegisterCommand("sdk/echo", payload =>
        {
            JsonNode? captured = payload?["message"]?.DeepClone();
            host.Api.RegisterQuery("sdk/captured", () => captured, owner);
            return ValueTask.CompletedTask;
        }, owner);
        await using var server = new WebUiServer(host);
        await server.StartAsync();

        using var client = new ConsoleClient(server.Address!);
        var accepted = await client.ExecuteAsync("sdk/echo",
            new JsonObject { ["message"] = "hello" });
        Assert.True(accepted);

        var captured = await client.QueryAsync("sdk/captured");
        Assert.Equal("hello", captured?["data"]?.GetValue<string>());
    }
}
