using System.Text.Json.Nodes;
using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>
/// Owner identity of one contribution. FiberId + ActivationId bind a
/// registration to the exact Core activation that installed it, so a stale
/// cleanup can never remove a newer activation's contribution.
/// </summary>
public sealed record ContributionOwner(
    string PluginId,
    string ComponentId,
    Guid FiberId,
    long ActivationId)
{
    public static ContributionOwner For(string pluginId, string componentId) =>
        new(pluginId, componentId, Guid.NewGuid(), 0);

    public static ContributionOwner ForContext(Context context, string pluginId, string componentId) =>
        new(pluginId, componentId, context.FiberId, context.ActivationId);
}

public sealed record UiContribution(
    string Kind,
    string Name,
    ContributionOwner Owner,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>
/// The single UI Composition Registry. It stores contribution data only; it
/// never loads, disposes or otherwise controls plugin lifecycles.
/// </summary>
public sealed class UiContributionRegistry
{
    public static readonly Key<UiContributionRegistry> Key = new("ui-contribution-registry");

    private readonly object _sync = new();
    private readonly List<UiContribution> _contributions = [];
    private readonly Dictionary<string, UiPage> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiPanel> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiClientModule> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiContributionHandle> _handles = new(StringComparer.Ordinal);
    private long _revision;

    public long Revision
    {
        get
        {
            lock (_sync)
            {
                return _revision;
            }
        }
    }

    public IReadOnlyList<UiContribution> Snapshot()
    {
        lock (_sync)
        {
            return _contributions.ToArray();
        }
    }

    // ---- typed composition -------------------------------------------------

    public UiContributionHandle RegisterPage(ContributionOwner owner, UiPageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        lock (_sync)
        {
            EnsureFree("page", definition.Id, owner);
            var page = new UiPage(
                definition.Id, definition.Title, definition.Route, definition.Renderer,
                definition.Icon, definition.Description, definition.Views?.DeepClone(),
                definition.Actions ?? [], definition.Order, owner);
            _pages[definition.Id] = page;
            return Track("page", definition.Id, owner);
        }
    }

    public UiContributionHandle RegisterPanel(ContributionOwner owner, UiPanelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        lock (_sync)
        {
            EnsureFree("panel", definition.Id, owner);
            var panel = new UiPanel(
                definition.Id, definition.Title, definition.Position, definition.Renderer,
                definition.Pages ?? [], definition.Views?.DeepClone(), definition.Order, owner);
            _panels[definition.Id] = panel;
            return Track("panel", definition.Id, owner);
        }
    }

    public UiContributionHandle RegisterClientModule(ContributionOwner owner, UiClientModuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        lock (_sync)
        {
            EnsureFree("client-module", definition.Name, owner);
            var module = new UiClientModule(
                definition.Name, definition.Url, definition.Revision, definition.Entry, owner);
            _modules[definition.Name] = module;
            return Track("client-module", definition.Name, owner);
        }
    }

    public IReadOnlyList<UiPage> Pages()
    {
        lock (_sync)
        {
            return _pages.Values
                .OrderBy(page => page.Order)
                .ThenBy(page => page.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<UiPanel> Panels()
    {
        lock (_sync)
        {
            return _panels.Values
                .OrderBy(panel => panel.Order)
                .ThenBy(panel => panel.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<UiClientModule> ClientModules()
    {
        lock (_sync)
        {
            return _modules.Values
                .OrderBy(module => module.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal void Unregister(UiContributionHandle handle)
    {
        lock (_sync)
        {
            var key = $"{handle.Kind}:{handle.Id}";
            if (!_handles.TryGetValue(key, out var current) || current.Owner != handle.Owner)
            {
                return;
            }

            _handles.Remove(key);
            switch (handle.Kind)
            {
                case "page":
                    _pages.Remove(handle.Id);
                    break;
                case "panel":
                    _panels.Remove(handle.Id);
                    break;
                case "client-module":
                    _modules.Remove(handle.Id);
                    break;
            }

            _revision++;
        }
    }

    public void UnregisterPage(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterPanel(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterClientModule(UiContributionHandle handle) => Unregister(handle);

    private void EnsureFree(string kind, string id, ContributionOwner owner)
    {
        var key = $"{kind}:{id}";
        if (_handles.ContainsKey(key))
        {
            throw new InvalidOperationException($"{kind} '{id}' is already registered");
        }

        _handles[key] = new UiContributionHandle(this, kind, id, owner);
        _revision++;
    }

    private UiContributionHandle Track(string kind, string id, ContributionOwner owner) =>
        _handles[$"{kind}:{id}"];

    // ---- legacy generic surface -------------------------------------------

    public UiContribution Register(
        string kind,
        string name,
        ContributionOwner owner,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            if (_contributions.Any(entry => entry.Kind == kind && entry.Name == name))
            {
                throw new InvalidOperationException($"{kind} '{name}' is already registered");
            }

            var contribution = new UiContribution(
                kind, name, owner, metadata ?? new Dictionary<string, object?>());
            _contributions.Add(contribution);
            _revision++;
            return contribution;
        }
    }

    public void Unregister(UiContribution contribution)
    {
        lock (_sync)
        {
            var stale = _contributions.FirstOrDefault(entry =>
                entry.Kind == contribution.Kind &&
                entry.Name == contribution.Name &&
                entry.Owner == contribution.Owner);
            if (stale is not null)
            {
                _contributions.Remove(stale);
                _revision++;
            }
        }
    }

    public void UnregisterAll(ContributionOwner owner)
    {
        lock (_sync)
        {
            _contributions.RemoveAll(entry => entry.Owner == owner);
            foreach (var key in _handles.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
            {
                _handles.Remove(key);
                var separator = key.IndexOf(':');
                var kind = key[..separator];
                var id = key[(separator + 1)..];
                switch (kind)
                {
                    case "page": _pages.Remove(id); break;
                    case "panel": _panels.Remove(id); break;
                    case "client-module": _modules.Remove(id); break;
                }
            }

            _revision++;
        }
    }
}

/// <summary>Console/API query and command surface shared by plugins.</summary>
public sealed class ApiGateway
{
    public static readonly Key<ApiGateway> Key = new("console-api-gateway");

    private readonly object _sync = new();
    private readonly Dictionary<string, (ContributionOwner Owner, Func<JsonNode?> Handler)> _queries = [];
    private readonly Dictionary<string, (ContributionOwner Owner, Func<JsonNode?, ValueTask> Handler)> _commands = [];

    public IReadOnlyCollection<string> Queries
    {
        get { lock (_sync) { return _queries.Keys.ToArray(); } }
    }

    public IReadOnlyCollection<string> Commands
    {
        get { lock (_sync) { return _commands.Keys.ToArray(); } }
    }

    public void RegisterQuery(string name, Func<JsonNode?> handler, ContributionOwner owner)
    {
        lock (_sync)
        {
            if (!_queries.TryAdd(name, (owner, handler)))
            {
                throw new InvalidOperationException($"query '{name}' is already registered");
            }
        }
    }

    public void RegisterCommand(string name, Func<JsonNode?, ValueTask> handler, ContributionOwner owner)
    {
        lock (_sync)
        {
            if (!_commands.TryAdd(name, (owner, handler)))
            {
                throw new InvalidOperationException($"command '{name}' is already registered");
            }
        }
    }

    public JsonNode? Query(string name)
    {
        Func<JsonNode?>? handler;
        lock (_sync)
        {
            handler = _queries.TryGetValue(name, out var entry) ? entry.Handler : null;
        }

        return handler?.Invoke();
    }

    public async ValueTask<bool> ExecuteAsync(string name, JsonNode? payload, CancellationToken cancellationToken = default)
    {
        Func<JsonNode?, ValueTask>? handler;
        lock (_sync)
        {
            handler = _commands.TryGetValue(name, out var entry) ? entry.Handler : null;
        }

        if (handler is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await handler(payload);
        return true;
    }

    public void UnregisterQuery(ContributionOwner owner, string name)
    {
        lock (_sync)
        {
            if (_queries.TryGetValue(name, out var entry) && entry.Owner == owner)
            {
                _queries.Remove(name);
            }
        }
    }

    public void UnregisterAll(ContributionOwner owner)
    {
        lock (_sync)
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
