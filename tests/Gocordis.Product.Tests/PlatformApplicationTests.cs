using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

public sealed class PlatformApplicationTests
{
    [Fact]
    public async Task WebHostStartsServesConsoleAndStopsCleanly()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Address: "http://127.0.0.1:0", HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        Assert.NotNull(app.Address);
        using var http = new HttpClient { BaseAddress = app.Address };
        var meta = JsonNode.Parse(await http.GetStringAsync("/api/meta"));
        Assert.Equal("gocordis-console", meta?["data"]?["kind"]?.GetValue<string>());
        Assert.Equal(UiHostKind.Web, app.RenderPlan().Host);

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task NativeProfileStartsBackendWithoutWebServerAndRendersByProfile()
    {
        using var temp = new TempDir();
        var manifestPath = await WriteManifestAsync(temp);
        var options = new PlatformHostOptions(manifestPath, temp.Path("store"),
            Host: UiHostKind.WinForms, HostVersion: "1.5.0");

        await using var app = new PlatformApplication(options);
        await app.StartAsync();

        Assert.Null(app.Address);
        app.Console.Ui.RegisterPage(ContributionOwner.For("test", "platform"),
            new UiPageDefinition("product-page", "Product", "/product", "generic"));
        var plan = app.RenderPlan();
        Assert.Equal(UiHostKind.WinForms, plan.Host);
        var page = Assert.Single(plan.Rendered);
        Assert.StartsWith("winforms:", page.Descriptor, StringComparison.Ordinal);
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, f => f.Name == "webui");
    }

    [Fact]
    public void OptionsRejectRemoteBindWithoutOptIn()
    {
        Assert.Throws<InvalidOperationException>(() => new PlatformHostOptions(
            "app.json", "store", "http://0.0.0.0:8080", AllowRemote: false));
        var allowed = new PlatformHostOptions(
            "app.json", "store", "http://0.0.0.0:8080", AllowRemote: true);
        Assert.Equal("http://0.0.0.0:8080", allowed.Address);
    }

    private static async Task<string> WriteManifestAsync(TempDir temp)
    {
        var path = temp.File("gocordis.app.json");
        await new ApplicationManifest("product-app", "1.0.0", "1.5.0", []).SaveAsync(path);
        return path;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-platform-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string File(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
