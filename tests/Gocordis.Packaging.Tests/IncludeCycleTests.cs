using System.Text.Json;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Packaging.Tests;

/// <summary>
/// Include expansion is a pure declaration step: cycles, excessive nesting and
/// repeated paths must fail or resolve explicitly before any package or fiber
/// work happens.
/// </summary>
public sealed class IncludeCycleTests
{
    [Fact]
    public async Task IncludeCycleFailsWithReadableDiagnosticInsteadOfStackOverflow()
    {
        using var temp = new TempDir();
        temp.Write("a.json", "[{\"id\":\"a\",\"include\":\"b.json\"}]");
        temp.Write("b.json", "[{\"id\":\"b\",\"include\":\"./a.json\"}]");
        var app = App(temp, new EntryNode("root", Include: "a.json"));

        var error = await Assert.ThrowsAsync<IncludeCycleException>(
            () => ApplicationLoader.EffectivePluginsAsync(app));

        Assert.Equal(3, error.Cycle.Count);
        Assert.EndsWith("a.json", error.Cycle[0]);
        Assert.EndsWith("b.json", error.Cycle[1]);
        Assert.Equal(error.Cycle[0], error.Cycle[2]);
        Assert.Contains("Include cycle", error.Message);
    }

    [Fact]
    public async Task ApplyRejectsIncludeCycleBeforeAnyDeclarationIsResolved()
    {
        using var temp = new TempDir();
        temp.Write("a.json", "[{\"id\":\"a\",\"include\":\"b.json\"}]");
        temp.Write("b.json", "[{\"id\":\"b\",\"include\":\"a.json\"}]");
        var app = App(temp, new EntryNode("root", Include: "a.json"));

        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        var loader = new ApplicationLoader(new PluginStore(temp.At("store"), "1.0.0"), registry);

        await Assert.ThrowsAsync<IncludeCycleException>(() => loader.ApplyAsync(app));
        Assert.Empty(registry.Entries);
    }

    [Fact]
    public async Task IncludePathsAreNormalizedBeforeCycleDetection()
    {
        using var temp = new TempDir();
        temp.Write("a.json", "[{\"id\":\"a\",\"include\":\"sub/../b.json\"}]");
        temp.Write("b.json", "[{\"id\":\"b\",\"include\":\"./a.json\"}]");
        var app = App(temp, new EntryNode("root", Include: "a.json"));

        var error = await Assert.ThrowsAsync<IncludeCycleException>(
            () => ApplicationLoader.EffectivePluginsAsync(app));

        Assert.Equal(3, error.Cycle.Count);
        Assert.Equal(error.Cycle[0], error.Cycle[2]);
        Assert.DoesNotContain("..", error.Cycle[1]);
    }

    [Fact]
    public async Task IncludeDepthLimitFailsExplicitly()
    {
        using var temp = new TempDir();
        for (var level = 0; level < ApplicationLoader.MaxIncludeDepth; level++)
        {
            temp.Write($"level{level}.json", JsonSerializer.Serialize(new[]
            {
                new EntryNode($"node{level}", Include: $"level{level + 1}.json"),
            }));
        }

        temp.Write($"level{ApplicationLoader.MaxIncludeDepth}.json", "[]");
        var app = App(temp, new EntryNode("root", Include: "level0.json"));

        var error = await Assert.ThrowsAsync<IncludeDepthLimitException>(
            () => ApplicationLoader.EffectivePluginsAsync(app));

        Assert.Equal(ApplicationLoader.MaxIncludeDepth, error.MaxDepth);
    }

    [Fact]
    public async Task RepeatedIncludeIsExpandedOnceAndFirstDeclarationWins()
    {
        using var temp = new TempDir();
        temp.Write("child.json", JsonSerializer.Serialize(new[]
        {
            new EntryNode("child", Config: new Dictionary<string, object?> { ["source"] = "first" }),
        }));
        temp.Write("root.json", JsonSerializer.Serialize(new[]
        {
            new EntryNode("first", Include: "child.json"),
            // A different parent linkage makes the ignored duplicate observable.
            new EntryNode("second",
                Children: [new EntryNode("dup", Include: "child.json")]),
        }));
        var app = App(temp, new EntryNode("root", Include: "root.json"));

        var effective = Assert.Single(await ApplicationLoader.EffectivePluginsAsync(app));
        Assert.Equal("child", effective.Id);
        // The duplicate path is ignored, so the first expansion keeps its parent.
        Assert.Null(effective.ParentId);
        Assert.Equal("first", effective.Config!["source"]?.ToString());

        // Explicit flat declarations remain authoritative for the same id.
        var withFlat = app with
        {
            Plugins = [new ApplicationPlugin("child", "[1.0,)", true,
                new Dictionary<string, object?> { ["source"] = "flat" })],
        };
        var overridden = Assert.Single(await ApplicationLoader.EffectivePluginsAsync(withFlat));
        Assert.Equal("flat", overridden.Config!["source"]);
        Assert.Null(overridden.ParentId);
    }

    private static ApplicationManifest App(TempDir temp, params EntryNode[] entries) =>
        new ApplicationManifest("app", "1.0.0", "1.0.0",
            Array.Empty<ApplicationPlugin>(), entries) with { SourcePath = temp.At("app.json") };

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-include-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => Path.Combine(_root, name);

        public void Write(string name, string content) => File.WriteAllText(At(name), content);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
