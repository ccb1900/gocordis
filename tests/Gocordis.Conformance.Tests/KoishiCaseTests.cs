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

        Assert.True(host.Commands.Names.Contains("echo"));
        Assert.Single(host.LiveDatabases);
        Assert.Single(host.LiveAdapters);
        var databaseFiber = host.Registry.Entries["db"].Fiber!;
        var adapterFiber = host.Registry.Entries["adapter"].Fiber!;

        await host.SetEnabledAsync("feature", false);

        Assert.False(host.Commands.Names.Contains("echo"));
        Assert.Equal(FiberState.Gone, host.Registry.Entries["feature"].Fiber?.State ?? FiberState.Gone);
        Assert.Equal(FiberState.Active, databaseFiber.State);
        Assert.Equal(FiberState.Active, adapterFiber.State);
        Assert.Single(host.LiveAdapters);

        await host.SetEnabledAsync("feature", true);
        Assert.True(host.Commands.Names.Contains("echo"));
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
        Assert.Equal("koishi-console", Assert.Single(browser.Console.Ui.Snapshot()).Name);

        await server.SetEnabledAsync("feature", false);

        Assert.Equal(3, server.Runtime.Snapshot().Fibers.Count);
        Assert.Equal(2, browser.Runtime.Snapshot().Fibers.Count);
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
