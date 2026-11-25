using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using KoishiCase;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Paper §5.3 Case Study: Koishi reproduced on the C# stack. The host supplies
/// only domain vocabulary; features are plugins composed through Core.
/// </summary>
public sealed class KoishiCaseTests
{
    [Fact]
    public async Task Koishi_DisablePluginRevertsEffectsWithoutTouchingProviders()
    {
        await using var host = new KoishiHost();
        await host.StartAsync();

        Assert.Contains("echo", host.Commands.Names);
        Assert.Single(host.LiveDatabases);
        Assert.Single(host.LiveAdapters);
        var databaseFiber = host.Registry.Entries["db"].Fiber!;
        var adapterFiber = host.Registry.Entries["adapter"].Fiber!;

        await host.SetEnabledAsync("feature", false);

        Assert.DoesNotContain("echo", host.Commands.Names);
        Assert.Equal(FiberState.Gone, host.Registry.Entries["feature"].Fiber?.State ?? FiberState.Gone);
        Assert.Equal(FiberState.Active, databaseFiber.State);
        Assert.Equal(FiberState.Active, adapterFiber.State);
        Assert.Single(host.LiveAdapters);

        await host.SetEnabledAsync("feature", true);
        Assert.Contains("echo", host.Commands.Names);
        Assert.Same(adapterFiber, host.Registry.Entries["adapter"].Fiber);
    }

    [Fact]
    public async Task Koishi_MissingProviderStaysPendingWithoutFailure()
    {
        await using var host = new KoishiHost();
        await host.Runtime.LoadAsync(new VocabularyOnly());

        var feature = await host.Runtime.LoadAsync(new FeaturePlugin());

        Assert.Equal(FiberState.Pending, feature.State);
        Assert.Null(feature.Failure);
    }

    [Fact]
    public async Task Koishi_ProviderSwitchReactivatesOnlyItsDependents()
    {
        await using var host = new KoishiHost();
        await host.StartAsync();

        var databaseFiber = host.Registry.Entries["db"].Fiber!;
        var adapterFiber = host.Registry.Entries["adapter"].Fiber!;
        var featureFiber = host.Registry.Entries["feature"].Fiber!;
        var originalConnection = host.LiveDatabases.Single().ConnectionId;
        var echoBefore = host.Commands.Execute("echo", "hi");

        await host.ReplaceAdapterAsync("v2");

        var echoAfter = host.Commands.Execute("echo", "hi");
        Assert.Equal("sqlite/v1:hi", echoBefore);
        Assert.Equal("sqlite/v2:hi", echoAfter);
        Assert.Same(databaseFiber, host.Registry.Entries["db"].Fiber);
        Assert.NotSame(adapterFiber, host.Registry.Entries["adapter"].Fiber);
        Assert.Same(featureFiber, host.Registry.Entries["feature"].Fiber);
        Assert.True(featureFiber.Generation > 1);
        Assert.Equal(originalConnection, host.LiveDatabases.Single().ConnectionId);
        Assert.Equal(FiberState.Active, host.Registry.Entries["feature"].Fiber!.State);
    }

    [Fact]
    public async Task Koishi_WebConsoleIsAnIndependentSecondApplication()
    {
        await using var server = new KoishiHost();
        await using var browser = new BrowserConsoleApp();
        await server.StartAsync();
        await browser.StartAsync();

        Assert.Equal(4, server.Runtime.Snapshot().Fibers.Count);
        Assert.Equal(2, browser.Runtime.Snapshot().Fibers.Count);
        Assert.Equal("koishi-console", Assert.Single(browser.Console.Ui.Pages()).Id);

        await server.SetEnabledAsync("feature", false);

        Assert.Equal(3, server.Runtime.Snapshot().Fibers.Count);
        Assert.Equal(2, browser.Runtime.Snapshot().Fibers.Count);
    }

    [Fact]
    public async Task Koishi_DisablingProviderRetractsDependentWithoutFailure()
    {
        await using var host = new KoishiHost();
        await host.StartAsync();

        var databaseFiber = host.Registry.Entries["db"].Fiber!;
        Assert.Equal("sqlite/v1:hi", host.Commands.Execute("echo", "hi"));

        await host.SetEnabledAsync("db", false);

        await WaitUntilAsync(() =>
            host.Registry.Entries["db"].Fiber is null &&
            host.Registry.Entries["adapter"].Fiber?.State == FiberState.Pending &&
            host.Registry.Entries["feature"].Fiber?.State == FiberState.Pending,
            "provider retraction");

        Assert.Null(host.Registry.Entries["adapter"].Fiber!.Failure);
        Assert.Null(host.Registry.Entries["feature"].Fiber!.Failure);
        Assert.DoesNotContain("echo", host.Commands.Names);
        Assert.Empty(host.LiveDatabases);
        Assert.Empty(host.LiveAdapters);

        await host.SetEnabledAsync("db", true);
        await WaitUntilAsync(() =>
            host.Registry.Entries["adapter"].Fiber?.State == FiberState.Active &&
            host.Registry.Entries["feature"].Fiber?.State == FiberState.Active &&
            host.Commands.Names.Contains("echo"),
            "provider reactivation");

        Assert.NotSame(databaseFiber, host.Registry.Entries["db"].Fiber);
        Assert.Equal(FiberState.Active, host.Registry.Entries["db"].Fiber!.State);
        Assert.Equal("sqlite/v1:hi", host.Commands.Execute("echo", "hi"));
        Assert.Single(host.LiveDatabases);
        Assert.Single(host.LiveAdapters);
    }

    [Fact]
    public async Task Koishi_MessageEventDrivesFeatureEchoAndIsRevokedOnDisable()
    {
        await using var host = new KoishiHost();
        await host.StartAsync();

        await host.Runtime.LoadAsync(new MessageTrigger("first"));

        var invocation = Assert.Single(host.Commands.Invocations);
        Assert.Equal("echo", invocation.Name);
        Assert.Equal("first", invocation.Argument);
        Assert.Equal("sqlite/v1:first", invocation.Result);

        await host.SetEnabledAsync("feature", false);
        await WaitUntilAsync(() => !host.Commands.Names.Contains("echo"), "feature revocation");
        var count = host.Commands.Invocations.Count;

        await host.Runtime.LoadAsync(new MessageTrigger("second", "second-trigger"));
        Assert.Equal(count, host.Commands.Invocations.Count);
    }

    [Fact]
    public async Task Koishi_WebUiServesSecondApplicationPagesAndSnapshot()
    {
        await using var server = new KoishiHost();
        await server.StartAsync();

        var browser = new BrowserConsoleApp();
        await browser.StartAsync();
        await using var ui = new WebUiServer(browser.Console);
        await ui.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(ui.Address!) };

        var pages = JsonNode.Parse(await client.GetStringAsync("/api/ui/pages"))!["data"]!["pages"]!.AsArray();
        Assert.Contains(pages, page => page?["id"]?.GetValue<string>() == "koishi-console");

        var snapshot = JsonNode.Parse(await client.GetStringAsync("/api/query/runtime/snapshot"))!["data"]!;
        Assert.Contains(snapshot["fibers"]!.AsArray(),
            fiber => fiber?["name"]?.GetValue<string>() == "browser-page");
        Assert.Equal(4, server.Runtime.Snapshot().Fibers.Count);

        await browser.DisposeAsync();

        Assert.Empty(browser.Runtime.Snapshot().Fibers);
        Assert.Equal(4, server.Runtime.Snapshot().Fibers.Count);
        var meta = JsonNode.Parse(await client.GetStringAsync("/api/meta"))!;
        Assert.Equal("gocordis-console", meta["data"]!["kind"]!.GetValue<string>());
    }

    private sealed class MessageTrigger : Component
    {
        private readonly string _payload;

        public MessageTrigger(string payload, string? name = null)
        {
            _payload = payload;
            Name = name ?? $"message-trigger-{payload}";
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => [K.Message];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await context.EmitAsync(K.Message, _payload);
            return null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"timed out waiting for {description}");
            }

            await Task.Delay(25);
        }
    }

    private sealed class VocabularyOnly : Component
    {
        public override string Name => "vocabulary";

        public override IReadOnlyList<ICapabilityKey> Provide() => [K.Commands, K.Message];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(K.Commands, new CommandRegistry());
            context.Provide(K.Message, "channel");
            return null;
        }
    }
}
