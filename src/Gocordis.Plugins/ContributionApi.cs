using System.Text.Json.Nodes;
using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Owner identity prevents a stale activation from removing a newer registration.</summary>
public sealed record ContributionOwner(string PluginId, string ComponentId, Guid ActivationId)
{
    public static ContributionOwner For(string pluginId, string componentId) =>
        new(pluginId, componentId, Guid.NewGuid());
}

public sealed record UiContribution(
    string Kind,
    string Name,
    ContributionOwner Owner,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>
/// UI contribution contract. Core has no UI dependency: plugins publish pages,
/// panels and client modules here, and cleanup is owned by the contribution owner.
/// </summary>
public sealed class UiContributionRegistry
{
    public static readonly Key<UiContributionRegistry> Key = new("ui-contribution-registry");
    private readonly List<UiContribution> _contributions = [];

    public IReadOnlyList<UiContribution> Snapshot() => _contributions.ToArray();

    public UiContribution Register(string kind, string name, ContributionOwner owner,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_contributions.Any(entry => entry.Kind == kind && entry.Name == name))
        {
            throw new InvalidOperationException($"{kind} '{name}' is already registered");
        }

        var contribution = new UiContribution(kind, name, owner, metadata ?? new Dictionary<string, object?>());
        _contributions.Add(contribution);
        return contribution;
    }

    public void Unregister(UiContribution contribution)
    {
        // Owner-checked removal: a stale owner can never delete a newer owner's entry.
        var stale = _contributions.FirstOrDefault(entry =>
            entry.Kind == contribution.Kind &&
            entry.Name == contribution.Name &&
            entry.Owner == contribution.Owner);
        if (stale is not null)
        {
            _contributions.Remove(stale);
        }
    }

    public void UnregisterAll(ContributionOwner owner) =>
        _contributions.RemoveAll(entry => entry.Owner == owner);
}

/// <summary>Console/API query and command surface shared by plugins.</summary>
public sealed class ApiGateway
{
    public static readonly Key<ApiGateway> Key = new("console-api-gateway");
    private readonly Dictionary<string, (ContributionOwner Owner, Func<JsonNode?> Handler)> _queries = [];
    private readonly Dictionary<string, (ContributionOwner Owner, Func<JsonNode?, ValueTask> Handler)> _commands = [];

    public IReadOnlyCollection<string> Queries => _queries.Keys;

    public IReadOnlyCollection<string> Commands => _commands.Keys;

    public void RegisterQuery(string name, Func<JsonNode?> handler, ContributionOwner owner)
    {
        if (!_queries.TryAdd(name, (owner, handler)))
        {
            throw new InvalidOperationException($"query '{name}' is already registered");
        }
    }

    public void RegisterCommand(string name, Func<JsonNode?, ValueTask> handler, ContributionOwner owner)
    {
        if (!_commands.TryAdd(name, (owner, handler)))
        {
            throw new InvalidOperationException($"command '{name}' is already registered");
        }
    }

    public JsonNode? Query(string name) =>
        _queries.TryGetValue(name, out var entry) ? entry.Handler() : null;

    public async ValueTask<bool> ExecuteAsync(string name, JsonNode? payload, CancellationToken cancellationToken = default)
    {
        if (!_commands.TryGetValue(name, out var entry))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await entry.Handler(payload);
        return true;
    }

    public void UnregisterAll(ContributionOwner owner)
    {
        foreach (var name in _queries.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
        {
            _queries.Remove(name);
        }

        foreach (var name in _commands.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
        {
            _commands.Remove(name);
        }
    }
}

/// <summary>Composes the contribution surfaces; produce a runtime snapshot as a query.</summary>
public sealed class ConsoleHost
{
    public ConsoleHost(Gocordis.Core.Runtime runtime)
    {
        Runtime = runtime;
        var owner = ContributionOwner.For("framework", "console");
        Api.RegisterQuery("runtime/snapshot", RuntimeSnapshot, owner);
        Api.RegisterQuery("framework/dashboard", Dashboard, owner);
        Api.RegisterQuery("insight/graph", InsightGraph, owner);
    }

    public Gocordis.Core.Runtime Runtime { get; }

    public UiContributionRegistry Ui { get; } = new();

    public ApiGateway Api { get; } = new();

    private JsonObject Dashboard()
    {
        var fibers = Runtime.Snapshot().Fibers;
        return new JsonObject
        {
            ["total"] = fibers.Count,
            ["active"] = fibers.Count(fiber => fiber.State == FiberState.Active),
            ["pending"] = fibers.Count(fiber => fiber.State == FiberState.Pending),
            ["failed"] = fibers.Count(fiber => fiber.State == FiberState.Failed),
        };
    }

    private JsonObject InsightGraph()
    {
        var nodes = new JsonArray();
        var edges = new JsonArray();
        foreach (var fiber in Runtime.Snapshot().Fibers)
        {
            nodes.Add(new JsonObject { ["id"] = fiber.Id.ToString(), ["name"] = fiber.Name });
            foreach (var binding in fiber.BoundProviders)
            {
                edges.Add(new JsonObject { ["from"] = fiber.Id.ToString(), ["binding"] = binding });
            }
        }

        return new JsonObject { ["nodes"] = nodes, ["edges"] = edges };
    }

    public JsonObject RuntimeSnapshot()
    {
        var fibers = new JsonArray();
        foreach (var fiber in Runtime.Snapshot().Fibers)
        {
            fibers.Add(new JsonObject
            {
                ["id"] = fiber.Id.ToString(),
                ["name"] = fiber.Name,
                ["state"] = fiber.State.ToString(),
                ["generation"] = fiber.Generation,
                ["requires"] = new JsonArray(fiber.Requires.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
                ["provides"] = new JsonArray(fiber.Provides.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            });
        }

        return new JsonObject { ["fibers"] = fibers };
    }
}
