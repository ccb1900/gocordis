using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Paper §5.2.1: the declarative configuration tree is the authoritative
/// desired state. Group children are reconciled by stable id; Include grafts an
/// external entry list; every fiber transition still goes through Core.
/// </summary>
public sealed class ConfigurationTreeTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task GroupChildrenUseKeyedDiffAndChangingOneEntryLeavesOthersAlone()
    {
        using var temp = new TempDir();
        await InstallAsync(temp, "tree-provider");
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterComponentFactory("tree-provider", manifest =>
            new Probe(manifest.Id, manifest.Config.TryGetValue("value", out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty));
        var loader = new ApplicationLoader(new PluginStore(temp.Path("store"), HostVersion), registry);

        var p1 = Child("p1", "one");
        var p2 = Child("p2", "two");
        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("group-a", Children: [p1, p2])]));

        var first1 = registry.Entries["p1"].Fiber!;
        var first2 = registry.Entries["p2"].Fiber!;
        Assert.Equal(FiberState.Active, first1.State);
        Assert.Equal(FiberState.Active, first2.State);

        // Reordering and changing one child must not disturb the other.
        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("group-a", Children: [Child("p2", "two"), Child("p1", "changed")])]));
        Assert.Same(first2, registry.Entries["p2"].Fiber);
        Assert.NotSame(first1, registry.Entries["p1"].Fiber);
        Assert.Equal("changed", ((Probe)registry.Entries["p1"].Component!).Value);

        // Removing a child unloads only that fiber.
        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("group-a", Children: [Child("p1", "changed")])]));
        Assert.False(registry.Entries.ContainsKey("p2"));
        Assert.Null(registry.Entries["p1"].Fiber?.Failure);
    }

    [Fact]
    public async Task IncludeFileChangesGraftNewEntriesIntoTheTree()
    {
        using var temp = new TempDir();
        await InstallAsync(temp, "tree-provider");
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("tree-provider", _ => new Probe("included", "v"));
        var loader = new ApplicationLoader(new PluginStore(temp.Path("store"), HostVersion), registry);
        var include = temp.File("children.json");

        await File.WriteAllTextAsync(include, """[{"id":"inc-1","url":"tree-provider@1.0.0"}]""");
        await loader.ApplyAsync(IncludeManifest(include));
        Assert.Equal(FiberState.Active, registry.Entries["inc-1"].Fiber!.State);

        await File.WriteAllTextAsync(include,
            """[{"id":"inc-1","url":"tree-provider@1.0.0"},{"id":"inc-2","url":"tree-provider@1.0.0"}]""");
        await loader.ApplyAsync(IncludeManifest(include));
        Assert.Equal(FiberState.Active, registry.Entries["inc-1"].Fiber!.State);
        Assert.Equal(FiberState.Active, registry.Entries["inc-2"].Fiber!.State);
    }

    [Fact]
    public async Task DisableThenEnableCreatesAFreshFiberInsteadOfRevivingARetiredOne()
    {
        using var temp = new TempDir();
        await InstallAsync(temp, "tree-provider");
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("tree-provider", _ => new Probe("toggle", "v"));
        var loader = new ApplicationLoader(new PluginStore(temp.Path("store"), HostVersion), registry);

        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("toggle", Url: "tree-provider@1.0.0")]));
        var original = registry.Entries["toggle"].Fiber!;

        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("toggle", Url: "tree-provider@1.0.0", Disabled: true)]));
        Assert.Null(registry.Entries["toggle"].Fiber);
        await original.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Gone, original.State);

        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("toggle", Url: "tree-provider@1.0.0")]));
        var replacement = registry.Entries["toggle"].Fiber!;
        Assert.NotSame(original, replacement);
        Assert.Equal(FiberState.Active, replacement.State);
    }

    [Fact]
    public async Task DisableWithConfigChangeThroughTheLoaderRetiresTheFiber()
    {
        using var temp = new TempDir();
        await InstallAsync(temp, "tree-provider");
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var applied = new List<string>();
        registry.RegisterComponentFactory("tree-provider", _ => new ConfigurableProbe("toggle", applied));
        var loader = new ApplicationLoader(new PluginStore(temp.Path("store"), HostVersion), registry);
        var enabled = new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("toggle", Url: "tree-provider@1.0.0",
                Config: new Dictionary<string, object?> { ["value"] = "one" })]);

        await loader.ApplyAsync(enabled);
        var original = registry.Entries["toggle"].Fiber!;

        await loader.ApplyAsync(new ApplicationManifest("tree", "1.0.0", HostVersion, [],
            [new EntryNode("toggle", Url: "tree-provider@1.0.0", Disabled: true,
                Config: new Dictionary<string, object?> { ["value"] = "two" })]));

        Assert.Empty(applied);
        Assert.Null(registry.Entries["toggle"].Fiber);
        await original.GoneAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FiberState.Gone, original.State);
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    private static ApplicationManifest IncludeManifest(string path) =>
        new("tree", "1.0.0", HostVersion, [], [new EntryNode("external", Include: path)]);

    private static EntryNode Child(string id, string value) =>
        new(id, Url: "tree-provider@1.0.0", Config: new Dictionary<string, object?> { ["value"] = value });

    private static async Task InstallAsync(TempDir temp, string id)
    {
        var source = temp.Path($"source-{id}");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.txt"), id);
        var package = temp.File($"{id}-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, new PluginPackageManifest(
            id, "1.0.0", id, "[1.0,)"), package);
        await new PluginStore(temp.Path("store"), HostVersion).InstallAsync(package);
    }

    private sealed class Probe : Component
    {
        public Probe(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Value { get; }
        public override string Name { get; }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class ConfigurableProbe : Component, IConfigurableComponent
    {
        private readonly List<string> _applied;

        public ConfigurableProbe(string name, List<string> applied)
        {
            Name = name;
            _applied = applied;
        }

        public override string Name { get; }

        public ValueTask<bool> TryApplyConfigAsync(
            IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default)
        {
            _applied.Add(config["value"]?.ToString() ?? string.Empty);
            return ValueTask.FromResult(true);
        }

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gocordis-tree-" + Guid.NewGuid().ToString("N"));

        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, name!);
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
