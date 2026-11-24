using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// HMR cancellation is preflight-only: the token decides whether the single
/// registry transaction is entered, never how that transaction proceeds.
/// </summary>
public sealed class HmrCancellationTests
{
    [Fact]
    public async Task CancellationBeforeTransactionAbortsReloadWithoutTouchingTheRegistry()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("hmr", _ => new Probe("a"));
        var manifest = Manifest("a");
        await registry.ApplyAsync([manifest]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            HmrEngine.ReloadAsync(registry, Request(manifest),
                candidate => candidate with { Source = "v2" }, cts.Token));

        Assert.Contains("preflight-only", error.Message);
        Assert.Equal("v1", registry.Entries["a"].Manifest.Source);
        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
    }

    [Fact]
    public async Task CancellationAfterPreflightDuringPlanningDoesNotAbortTheReload()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("hmr", _ => new Probe("a"));
        var manifest = Manifest("a");
        await registry.ApplyAsync([manifest]);

        using var cts = new CancellationTokenSource();
        var plan = await HmrEngine.ReloadAsync(registry, Request(manifest), candidate =>
        {
            // Plan runs after the single preflight check: cancelling here must
            // no longer abort the reload (the old late check did).
            cts.Cancel();
            return candidate with { Source = "v2" };
        }, cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.Contains("a", plan.StaleEntryIds);
        Assert.Equal("v2", registry.Entries["a"].Manifest.Source);
        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
    }

    [Fact]
    public async Task CancellationDuringTransactionDoesNotInterruptTheCommit()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        using var cts = new CancellationTokenSource();
        registry.RegisterComponentFactory("hmr", candidate =>
            candidate.Source == "v2" ? (Component)new CancelOnApply("a", cts) : new Probe("a"));

        var manifest = Manifest("a");
        await registry.ApplyAsync([manifest]);
        Assert.False(cts.IsCancellationRequested);

        var plan = await HmrEngine.ReloadAsync(registry, Request(manifest),
            candidate => candidate with { Source = "v2" }, cts.Token);

        Assert.Contains("a", plan.StaleEntryIds);
        Assert.True(cts.IsCancellationRequested, "the staged v2 component cancels the caller's token");
        Assert.Equal("v2", registry.Entries["a"].Manifest.Source);
        Assert.Equal(FiberState.Active, registry.Entries["a"].Fiber!.State);
    }

    private static PluginManifest Manifest(string id) =>
        new(id, "hmr", true, new Dictionary<string, object?>(), [], [], Url: id, Source: "v1");

    private static HmrRequest Request(PluginManifest manifest) =>
        new(new HashSet<string> { manifest.Url! }, new HashSet<string>(),
            new Dictionary<string, IReadOnlyList<string>> { [manifest.Url!] = [] }, [manifest]);

    private sealed class Probe : Component
    {
        public Probe(string name) => Name = name;
        public override string Name { get; }
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class CancelOnApply : Component
    {
        private readonly CancellationTokenSource _cts;

        public CancelOnApply(string name, CancellationTokenSource cts)
        {
            Name = name;
            _cts = cts;
        }

        public override string Name { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _cts.Cancel();
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
