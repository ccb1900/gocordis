using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Gocordis.Platform;
using Industrial.Contracts;
using Industrial.Elsa;
using Industrial.Host;
using Industrial.Plugins;

namespace Industrial.Tests;

/// <summary>A plugin can contribute an exact node type and its execution
/// handler without modifying the designer, Elsa converter or runtime core.</summary>
public sealed class CustomNodeHandlerTests
{
    [Fact]
    public async Task ContributedHandlerExecutesItsExactNodeType()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Root, "source"));
        await File.WriteAllTextAsync(Path.Combine(temp.Root, "source", "20261126.csv"),
            "record_id,reading\n1,42\n");
        File.SetLastWriteTimeUtc(Path.Combine(temp.Root, "source", "20261126.csv"),
            DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime);
        var marker = Path.Combine(temp.Root, "handler-ran.txt");

        await using var runtime = new Gocordis.Core.Runtime();
        var console = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        console.AttachRegistry(registry);
        registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
        registry.ExternalCapabilities.Add(ApiGateway.Key);
        registry.ExternalCapabilities.Add(TimerKeys.Service);
        await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
        await runtime.LoadAsync(new TimerExtensionComponent());
        registry.RegisterComponentFactory("test.handler", _ => new HandlerComponent(marker));
        IndustrialComponents.RegisterFactories(registry, new Dictionary<string, object?>
        {
            ["allowDevelopmentKey"] = "true",
            ["root"] = temp.Root,
            ["seedResource"] = "true",
            ["sourceRoot"] = Path.Combine(temp.Root, "source"),
            ["resourceId"] = "source-1",
        });
        var types = IndustrialComponents.Types.ToList();
        types.Insert(types.IndexOf("industrial.elsa"), "test.handler");
        await registry.ApplyAsync(types.Select(type => IndustrialComponents.Declaration(type)).ToArray());

        var catalog = runtime.RequireCapability(IndustrialKeys.NodeCatalog);
        await catalog.RegisterAsync(new NodeDescriptor(
            "test.custom.validate", "Custom", "Custom", "transform", "Custom", "1.0.0",
            [new NodePort("input", PortType.RecordSet, true)], [],
            new Dictionary<string, object?>(), IndustrialActivityTypes.All[5], []));
        var api = console.Api;
        var definition = new WorkflowDefinition(
            "custom-flow", "Custom flow", 1,
            [
                new WorkflowNode("src", "industrial.filesystem.local",
                    new Dictionary<string, object?> { ["resourceId"] = "source-1" }, 0, 0),
                new WorkflowNode("read", "industrial.reader.csv", new Dictionary<string, object?>(), 100, 0),
                new WorkflowNode("custom", "test.custom.validate", new Dictionary<string, object?>(), 200, 0),
            ],
            [
                new WorkflowEdge("src", "files", "read", "input"),
                new WorkflowEdge("read", "records", "custom", "input"),
            ],
            new TriggerDefinition("manual", null, null, new Dictionary<string, object?>()),
            ["source-1"], new Dictionary<string, object?>());
        Assert.True(await api.ExecuteAsync("industrial/save-workflow",
            JsonSerializer.SerializeToNode(definition)));
        Assert.True(await api.ExecuteAsync("industrial/publish-workflow",
            new JsonObject { ["workflowId"] = "custom-flow" }));
        Assert.True(await api.ExecuteAsync("industrial/run-workflow", new JsonObject
        {
            ["workflowId"] = "custom-flow", ["resourceId"] = "source-1",
        }));
        Assert.True(File.Exists(marker), "contributed node handler did not execute");
        var executed = ((JsonObject)api.Query("industrial/execution")!)["executed"]!.AsArray()
            .Select(node => node!.GetValue<string>()).ToArray();
        Assert.Contains("custom", executed);
    }

    private sealed class HandlerComponent(string marker) : Component
    {
        public override string Name => "test.node-handler";
        public override IReadOnlyList<ICapabilityKey> Inject() => [IndustrialKeys.NodeExecutionHandlers];

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var broker = context.Require(IndustrialKeys.NodeExecutionHandlers);
            IDisposable? handle = null;
            await context.EffectAsync(
                () =>
                {
                    handle = broker.Register("test.custom.validate", new MarkerHandler(marker));
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    handle?.Dispose();
                    return ValueTask.CompletedTask;
                });
            return null;
        }
    }

    private sealed class MarkerHandler(string marker) : IIndustrialNodeHandler
    {
        public string NodeType => "test.custom.validate";

        public async ValueTask ExecuteAsync(IndustrialNodeExecutionContext context, CancellationToken ct = default)
        {
            var count = 0;
            await foreach (var _ in context.Records(context.Node.Id, ct).WithCancellation(ct)) count++;
            await File.WriteAllTextAsync(marker, count.ToString(), ct);
            context.Complete(count, [$"custom:{count}"]);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"industrial-handler-{Guid.NewGuid():N}");
        public TempDir() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }
}
