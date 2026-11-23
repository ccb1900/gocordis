using Gocordis.Core;
using KoishiCase;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Product-level Koishi reference scenario: a real composed application with
/// provider, adapter and feature plugins. The host only supplies vocabulary;
/// all behaviour comes from declarations and Core lifecycle.
/// </summary>
public sealed class KoishiProductE2ETests
{
    [Fact]
    public async Task KoishiFeatureCompositionAndProviderSwitchCloseTheProductLoop()
    {
        await using var host = new KoishiHost();
        await host.StartAsync();

        Assert.Contains("echo", host.Commands.Names);
        Assert.Equal("sqlite/v1:hi", host.Commands.Execute("echo", "hi"));
        // Host vocabulary fiber + three declared plugin fibers.
        Assert.Equal(4, host.Runtime.Snapshot().Fibers.Count(
            fiber => fiber.State == FiberState.Active));

        await host.SetEnabledAsync("feature", false);
        Assert.DoesNotContain("echo", host.Commands.Names);
        Assert.Equal(3, host.Runtime.Snapshot().Fibers.Count(
            fiber => fiber.State == FiberState.Active));
        Assert.Single(host.LiveDatabases);
        Assert.Single(host.LiveAdapters);

        await host.SetEnabledAsync("feature", true);
        Assert.Contains("echo", host.Commands.Names);
        Assert.Equal("sqlite/v1:hi", host.Commands.Execute("echo", "hi"));

        var adapterBefore = host.Registry.Entries["adapter"].Fiber;
        await host.ReplaceAdapterAsync("v2");
        Assert.Equal("sqlite/v2:hi", host.Commands.Execute("echo", "hi"));
        Assert.NotSame(adapterBefore, host.Registry.Entries["adapter"].Fiber);
        Assert.Equal(FiberState.Gone, adapterBefore!.State);

        await host.DisposeAsync();
        Assert.Empty(host.Runtime.Snapshot().Fibers);
    }
}
