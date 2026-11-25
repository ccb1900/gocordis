using System.Text.Json;
using System.Text.Json.Nodes;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Thirteen validation gates from the collector spec. Every error is
/// located to a node id, edge index or workflow-level path.</summary>
public sealed class WorkflowValidator : IWorkflowValidator
{
    private static readonly string[] FORBIDDEN_CONFIG_KEYS =
        ["connectionstring", "password", "user", "username", "server", "host", "database", "secret"];

    private readonly INodeCatalog _catalog;
    private readonly IResourceRegistry _resources;
    private readonly ICredentialVault _credentials;
    private IElsaActivityCatalog? _activities;

    public IReadOnlyList<string> Gates { get; } =
    [
        "node-exists", "node-version", "required-config", "resource-exists", "resource-type",
        "credential-ref", "port-exists", "port-type", "required-input", "graph-cycle",
        "trigger", "plugin-dependencies", "activity-registrable",
    ];

    public void UseActivityCatalog(IElsaActivityCatalog catalog) => _activities = catalog;

    public WorkflowValidator(
        INodeCatalog catalog, IResourceRegistry resources, ICredentialVault credentials,
        IElsaActivityCatalog? activities = null)
    {
        _catalog = catalog;
        _resources = resources;
        _credentials = credentials;
        _activities = activities;
    }

    public async ValueTask<IReadOnlyList<WorkflowValidationError>> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<WorkflowValidationError>();
        if (string.IsNullOrWhiteSpace(definition.WorkflowId) || definition.Version <= 0)
        {
            errors.Add(new("workflow", "INVALID_IDENTITY", "workflowId and positive version are required"));
        }

        var nodes = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            if (!nodes.TryAdd(node.Id, node))
            {
                errors.Add(new($"nodes.{node.Id}", "DUPLICATE_NODE_ID", $"node id '{node.Id}' is duplicated"));
            }
        }
        var descriptors = new Dictionary<string, NodeDescriptor>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            var descriptor = await _catalog.GetAsync(node.Type, ct);
            if (descriptor is null)
            {
                errors.Add(new($"nodes.{node.Id}", "UNKNOWN_NODE_TYPE", $"node type '{node.Type}' is not registered"));
                continue;
            }

            descriptors[node.Id] = descriptor;
            if (string.IsNullOrWhiteSpace(descriptor.ActivityType))
            {
                errors.Add(new($"nodes.{node.Id}", "MISSING_ACTIVITY", "node descriptor has no Activity type"));
            }
            else if (!(_activities?.Contains(descriptor.ActivityType) ??
                       IndustrialActivityTypes.All.Contains(descriptor.ActivityType, StringComparer.Ordinal)))
            {
                errors.Add(new($"nodes.{node.Id}", "ACTIVITY_NOT_REGISTERED",
                    $"Activity '{descriptor.ActivityType}' is not registered"));
            }

            if (!Version.TryParse(descriptor.Version, out var nodeVersion) || nodeVersion.Major > 1)
            {
                errors.Add(new($"nodes.{node.Id}", "NODE_VERSION_INCOMPATIBLE",
                    $"node version '{descriptor.Version}' is not compatible with this host"));
            }

            foreach (var dependency in descriptor.Dependencies)
            {
                if (await _catalog.GetAsync(dependency, ct) is null)
                {
                    errors.Add(new($"nodes.{node.Id}.dependencies.{dependency}", "DEPENDENCY_MISSING",
                        $"node dependency '{dependency}' is not registered"));
                }
            }

            if (descriptor.ConfigurationSchema.TryGetValue("required", out var requiredValue) &&
                requiredValue is IEnumerable<string> requiredKeys)
            {
                foreach (var key in requiredKeys)
                {
                    if (!node.Config.ContainsKey(key))
                    {
                        errors.Add(new($"nodes.{node.Id}.config.{key}", "REQUIRED_CONFIG_MISSING",
                            $"required configuration '{key}' is missing"));
                    }
                }
            }

            foreach (var port in descriptor.Inputs.Where(port => port.Required))
            {
                if (!definition.Edges.Any(edge => edge.Target == node.Id && edge.TargetPort == port.Name))
                {
                    errors.Add(new($"nodes.{node.Id}.inputs.{port.Name}", "MISSING_INPUT",
                        $"required input port '{port.Name}' is not connected"));
                }
            }

            foreach (var (key, value) in node.Config)
            {
                if (value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
                {
                    errors.Add(new($"nodes.{node.Id}.config.{key}", "EMPTY_CONFIG", $"configuration '{key}' is empty"));
                }
            }

            var configReader = new NodeConfigReader(node.Config);
            var schemaObject = JsonSerializer.SerializeToNode(descriptor.ConfigurationSchema) as JsonObject;
            var properties = schemaObject?["properties"] as JsonObject;
            foreach (var key in node.Config.Keys)
            {
                if (FORBIDDEN_CONFIG_KEYS.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(new($"nodes.{node.Id}.config.{key}", "FORBIDDEN_CONFIG",
                        $"configuration '{key}' must be stored in a resource or credential"));
                }

                var spec = properties?[key] as JsonObject;
                var type = spec?["type"]?.GetValue<string>();
                if (type == "boolean" && !bool.TryParse(configReader.GetString(key), out _))
                {
                    errors.Add(new($"nodes.{node.Id}.config.{key}", "CONFIG_TYPE_MISMATCH",
                        $"configuration '{key}' must be boolean"));
                }
                if (type == "integer" && !int.TryParse(configReader.GetString(key), out _))
                {
                    errors.Add(new($"nodes.{node.Id}.config.{key}", "CONFIG_TYPE_MISMATCH",
                        $"configuration '{key}' must be an integer"));
                }
                if (spec?["enum"] is JsonArray allowed)
                {
                    var current = configReader.GetString(key);
                    if (!allowed.Any(item => string.Equals(item?.GetValue<string>(), current, StringComparison.Ordinal)))
                    {
                        errors.Add(new($"nodes.{node.Id}.config.{key}", "CONFIG_ENUM_INVALID",
                            $"configuration '{key}' must be one of {allowed.ToJsonString()}"));
                    }
                }
                if (key == "delimiter" && configReader.GetString(key).Length != 1)
                {
                    errors.Add(new($"nodes.{node.Id}.config.{key}", "INVALID_DELIMITER",
                        "CSV delimiter must be exactly one character"));
                }
            }

            if (node.Config.TryGetValue("resourceId", out var resourceId) && resourceId is string id)
            {
                var resource = await _resources.GetAsync(id, ct);
                if (resource is null)
                {
                    errors.Add(new($"nodes.{node.Id}.config.resourceId", "RESOURCE_MISSING",
                        $"resource '{id}' does not exist"));
                }
                else if (!resource.Enabled)
                {
                    errors.Add(new($"nodes.{node.Id}.config.resourceId", "RESOURCE_DISABLED",
                        $"resource '{id}' is disabled"));
                }
                else if (descriptor.ConfigurationSchema.TryGetValue("resourceType", out var expectedType) &&
                         !string.Equals(expectedType?.ToString(), resource.Type, StringComparison.Ordinal))
                {
                    errors.Add(new($"nodes.{node.Id}.config.resourceId", "RESOURCE_TYPE_MISMATCH",
                        $"resource '{id}' is '{resource.Type}', expected '{expectedType}'"));
                }
                else if (resource.CredentialId is { } credentialId &&
                         await _credentials.GetAsync(credentialId, ct) is null)
                {
                    errors.Add(new($"nodes.{node.Id}.config.resourceId", "CREDENTIAL_MISSING",
                        $"credential '{credentialId}' referenced by resource '{id}' does not exist"));
                }
            }
        }

        for (var index = 0; index < definition.Edges.Count; index++)
        {
            var edge = definition.Edges[index];
            if (!nodes.TryGetValue(edge.Source, out var source) || !nodes.TryGetValue(edge.Target, out var target))
            {
                errors.Add(new($"edges[{index}]", "UNKNOWN_ENDPOINT", "edge references an unknown node"));
                continue;
            }

            if (!descriptors.TryGetValue(source.Id, out var sourceDescriptor) ||
                !descriptors.TryGetValue(target.Id, out var targetDescriptor))
            {
                continue;
            }

            var output = sourceDescriptor.Outputs.FirstOrDefault(port => port.Name == edge.SourcePort);
            var input = targetDescriptor.Inputs.FirstOrDefault(port => port.Name == edge.TargetPort);
            if (output is null)
            {
                errors.Add(new($"edges[{index}].sourcePort", "UNKNOWN_PORT",
                    $"'{edge.SourcePort}' is not an output of '{source.Type}'"));
            }

            if (input is null)
            {
                errors.Add(new($"edges[{index}].targetPort", "UNKNOWN_PORT",
                    $"'{edge.TargetPort}' is not an input of '{target.Type}'"));
            }

            if (output is not null && input is not null && output.Type != input.Type)
            {
                errors.Add(new($"edges[{index}]", "PORT_TYPE_MISMATCH",
                    $"{output.Type} cannot be connected to {input.Type}"));
            }
        }

        if (HasCycle(definition))
        {
            errors.Add(new("edges", "GRAPH_CYCLE", "workflow graph must be acyclic"));
        }

        if (definition.Trigger.Kind is not ("manual" or "schedule" or "event"))
        {
            errors.Add(new("trigger.kind", "INVALID_TRIGGER", $"unknown trigger kind '{definition.Trigger.Kind}'"));
        }

        if (definition.Trigger.Kind == "event" && string.IsNullOrWhiteSpace(definition.Trigger.EventName))
        {
            errors.Add(new("trigger.eventName", "MISSING_EVENT", "event trigger requires an event name"));
        }

        if (definition.Trigger.Kind == "schedule" && string.IsNullOrWhiteSpace(definition.Trigger.Cron))
        {
            errors.Add(new("trigger.cron", "MISSING_CRON", "schedule trigger requires a cron expression"));
        }

        foreach (var resourceId in definition.ResourceReferences)
        {
            if (await _resources.GetAsync(resourceId, ct) is null)
            {
                errors.Add(new($"resourceReferences.{resourceId}", "RESOURCE_MISSING",
                    $"referenced resource '{resourceId}' does not exist"));
            }
        }

        return errors;
    }

    private static bool HasCycle(WorkflowDefinition definition)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in definition.Nodes)
        {
            adjacency.TryAdd(node.Id, []);
        }
        foreach (var edge in definition.Edges)
        {
            if (adjacency.TryGetValue(edge.Source, out var targets))
            {
                targets.Add(edge.Target);
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var value))
            {
                return value == 1;
            }

            state[id] = 1;
            if (adjacency.TryGetValue(id, out var targets))
            {
                foreach (var next in targets)
                {
                    if (Visit(next))
                    {
                        return true;
                    }
                }
            }

            state[id] = 2;
            return false;
        }

        return definition.Nodes.Any(node => Visit(node.Id));
    }
}
