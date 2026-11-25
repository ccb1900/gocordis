using Gocordis.Core;
using Gocordis.Plugins;
using Industrial.Elsa;
using Industrial.Plugins;

namespace Industrial.Host;

/// <summary>One factory/declaration map shared by the developer host and the
/// packaged host plugin identities. Tests use it too, so development and
/// package composition cannot drift into two business implementations.</summary>
public static class IndustrialComponents
{
    public static readonly string[] Types =
    [
        "industrial.resource",
        "industrial.machine",
        "industrial.credential",
        "industrial.workflow",
        "industrial.execution",
        "industrial.collection",
        "industrial.nodes",
        "industrial.elsa",
        "industrial.reader.json",
        "industrial.ui",
    ];

    public static void RegisterFactories(
        PluginRegistry registry, IReadOnlyDictionary<string, object?>? config = null)
    {
        var captured = config is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(config);
        foreach (var type in Types)
        {
            registry.RegisterComponentFactory(type, _ => Create(type, captured));
        }
    }

    public static Component Create(string type, IReadOnlyDictionary<string, object?>? config = null) =>
        type switch
        {
            "industrial.resource" => new IndustrialResourceComponent(config),
            "industrial.machine" => new IndustrialMachineComponent(config),
            "industrial.credential" => new IndustrialCredentialComponent(config),
            "industrial.workflow" => new IndustrialWorkflowComponent(config),
            "industrial.execution" => new IndustrialExecutionComponent(config),
            "industrial.collection" => new IndustrialCollectionComponent(config),
            "industrial.nodes" => new IndustrialNodesComponent(),
            "industrial.elsa" => new IndustrialElsaComponent(config),
            "industrial.reader.json" => new IndustrialJsonReaderComponent(),
            "industrial.ui" => new IndustrialUiComponent(config ?? new Dictionary<string, object?>()),
            _ => throw new InvalidOperationException($"unknown industrial component type '{type}'"),
        };

    public static PluginManifest Declaration(string type, IReadOnlyDictionary<string, object?>? config = null) =>
        new(type, type, true, config is null ? [] : new Dictionary<string, object?>(config), [], []);
}
