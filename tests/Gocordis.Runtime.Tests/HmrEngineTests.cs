using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>Paper §5.2.2: classification, stale-entry detection and
/// transactional reload. The engine only plans and submits declarations; Core
/// still owns every fiber transition.</summary>
public sealed class HmrEngineTests
{
    [Fact]
    public void ClassificationAcceptsChangedModuleAndItsImporters()
    {
        var plan = HmrEngine.Plan(new HmrRequest(
            new HashSet<string> { "changed" },
            new HashSet<string> { "core", "contract" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["changed"] = ["contract"],
                ["importer"] = ["changed", "core"],
                ["indirect"] = ["importer"],
                ["unrelated"] = ["core"],
            },
            []));

        Assert.Contains("changed", plan.Accepted);
        Assert.Contains("importer", plan.Accepted);
        Assert.Contains("indirect", plan.Accepted);
        Assert.DoesNotContain("unrelated", plan.Accepted);
        Assert.Contains("core", plan.Declined);
        Assert.Contains("contract", plan.Declined);
    }

    [Fact]
    public void ImportCycleWithoutAnAcceptedModuleDefaultsToDeclined()
    {
        var plan = HmrEngine.Plan(new HmrRequest(
            new HashSet<string> { "changed" },
            new HashSet<string>(),
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["changed"] = [],
                ["cycle-a"] = ["cycle-b"],
                ["cycle-b"] = ["cycle-a"],
            },
            []));

        Assert.Contains("cycle-a", plan.Declined);
        Assert.Contains("cycle-b", plan.Declined);
        Assert.DoesNotContain("cycle-a", plan.Accepted);
    }

    [Fact]
    public void StaleDetectionRespectsDeclinedBoundary()
    {
        var entries = new[]
        {
            Manifest("consumer", "consumer", "consumer"),
            Manifest("other", "other", "other"),
        };
        var plan = HmrEngine.Plan(new HmrRequest(
            new HashSet<string> { "changed" },
            new HashSet<string> { "non-replaceable" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["changed"] = [],
                ["consumer"] = ["changed"],
                ["other"] = ["non-replaceable"],
            },
            entries));

        Assert.Equal(new[] { "consumer" }, plan.StaleEntryIds);
        Assert.Contains("consumer", plan.Accepted);
        Assert.DoesNotContain("other", plan.StaleEntryIds);
    }

    [Fact]
    public async Task FailedSecondReloadRollsBackEveryStaleEntryToAConsistentState()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var loads = new Dictionary<string, int>(StringComparer.Ordinal);
        registry.RegisterComponentFactory("hmr", manifest =>
        {
            var count = loads.GetValueOrDefault(manifest.Id) + 1;
            loads[manifest.Id] = count;
            if (manifest.Id == "second" && manifest.Source == "v2" && count > 1)
            {
                throw new InvalidOperationException("new module failed to load");
            }

            return new Probe(manifest.Id);
        });

        var first = Manifest("first", "hmr", "module-a") with { Source = "v1" };
        var second = Manifest("second", "hmr", "module-b") with { Source = "v1" };
        await registry.ApplyAsync([first, second]);
        var originalFirst = registry.Entries["first"].Fiber!;
        var originalSecond = registry.Entries["second"].Fiber!;

        var request = new HmrRequest(
            new HashSet<string> { "module-a", "module-b" },
            new HashSet<string>(),
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["module-a"] = [],
                ["module-b"] = [],
            },
            [first, second]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await HmrEngine.ReloadAsync(registry, request,
                manifest => manifest with { Source = "v2" }));

        Assert.Equal("v1", registry.Entries["first"].Manifest.Source);
        Assert.Equal("v1", registry.Entries["second"].Manifest.Source);
        Assert.All(registry.Entries.Values, entry =>
            Assert.Equal(FiberState.Active, entry.Fiber!.State));
        Assert.NotNull(originalFirst);
        Assert.NotNull(originalSecond);
    }

    [Fact]
    public void SharedPrivateDependencyChangeInvalidatesAllConsumers()
    {
        var entries = new[]
        {
            Manifest("a", "plugin", "a"),
            Manifest("b", "plugin", "b"),
        };
        var plan = HmrEngine.Plan(new HmrRequest(
            new HashSet<string> { "private-dep" },
            new HashSet<string> { "shared-contract" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["private-dep"] = ["shared-contract"],
                ["a"] = ["private-dep"],
                ["b"] = ["private-dep"],
            },
            entries));

        Assert.Contains("a", plan.StaleEntryIds);
        Assert.Contains("b", plan.StaleEntryIds);
        Assert.Contains("private-dep", plan.Accepted);
        Assert.DoesNotContain("shared-contract", plan.Accepted);
    }

    [Fact]
    public void ExternalAssemblyChangeRequiresRestartInsteadOfHotReload()
    {
        var entries = new[] { Manifest("a", "plugin", "a") };
        var plan = HmrEngine.Plan(new HmrRequest(
            new HashSet<string> { "shared-contract" },
            new HashSet<string> { "shared-contract" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["shared-contract"] = [],
                ["a"] = ["shared-contract"],
            },
            entries));

        Assert.Empty(plan.StaleEntryIds);
        Assert.Contains("shared-contract", plan.Declined);
    }

    [Fact]
    public async Task ConcurrentHmrAndDeclarationChangeSerializeThroughTheRegistry()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("plugin", _ => new Probe("plugin"));
        var manifest = Manifest("a", "plugin", "a");
        await registry.ApplyAsync([manifest]);

        var request = new HmrRequest(
            new HashSet<string> { "a" }, new HashSet<string>(),
            new Dictionary<string, IReadOnlyList<string>> { ["a"] = [] }, [manifest]);

        var hmr = HmrEngine.ReloadAsync(registry, request,
            candidate => candidate with { Version = "2.0.0" });
        var toggle = registry.ApplyAsync([manifest with { Enabled = false }]);
        await Task.WhenAll(hmr, toggle);

        Assert.NotEmpty(registry.Entries);
        Assert.All(registry.Entries.Values, entry => Assert.NotNull(entry.Manifest));
    }

    private static PluginManifest Manifest(string id, string type, string url) =>
        new(id, type, true, new Dictionary<string, object?>(), [], [], Url: url);

    private sealed class Probe : Component
    {
        public Probe(string name) => Name = name;
        public override string Name { get; }
        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }
}
