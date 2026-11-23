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

    /// <summary>
    /// Normalizes a menu group path: '/'-separated nonempty segments, trimmed,
    /// with a bounded depth so a malformed declaration cannot blow up the menu.
    /// Null/empty means a top-level menu item (no submenu).
    /// </summary>
    internal static string? NormalizeGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return null;
        }

        var segments = group.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment.Length > 0)
            .Take(8)
            .ToArray();
        return segments.Length == 0 ? null : string.Join('/', segments);
    }

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
                definition.Actions ?? [], definition.Order, owner,
                NormalizeGroup(definition.Group));
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
                definition.Name, definition.Url, definition.Revision, definition.Entry, owner,
                definition.Source, definition.PackageId, definition.PackageVersion, definition.ContentHash);
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
        Api.RegisterQuery("runtime/events", RuntimeEvents, owner);
        Api.RegisterQuery("framework/dashboard", Dashboard, owner);
        Api.RegisterQuery("insight/graph", InsightGraph, owner);
        Api.RegisterQuery("config/schema", ConfigSchema, owner);
    }

    public Gocordis.Core.Runtime Runtime { get; }

    public UiContributionRegistry Ui { get; } = new();

    public ApiGateway Api { get; } = new();

    /// <summary>
    /// Canonical runtime/events wire shape: {events:[{sequence,timestamp,type,
    /// fiberID,activationID,data}], sequence}. Migrated from Go runtime/dto.go.
    /// The frontend's framework-dashboard timeline consumes this query.
    /// </summary>
    private JsonObject RuntimeEvents()
    {
        var events = new JsonArray();
        var snapshot = Runtime.Events.Snapshot();
        foreach (var item in snapshot.Events)
        {
            events.Add(new JsonObject
            {
                ["sequence"] = item.Sequence,
                ["timestamp"] = item.Timestamp.UtcDateTime.ToString("O"),
                ["type"] = item.Type,
                ["fiberID"] = item.FiberId.ToString(),
                ["activationID"] = item.ActivationId,
                ["data"] = item.Data is null ? null : JsonValue.Create(item.Data.ToString()),
            });
        }

        return new JsonObject { ["events"] = events, ["sequence"] = snapshot.Sequence };
    }

    /// <summary>
    /// Canonical framework/dashboard wire shape (Go runtime/dto.go DashboardDTO):
    /// counts are nested (fibers/active/providers/dependencies/effects/failed/
    /// pending) alongside sequence, state, recentEvents and diagnostics. The
    /// console's parseDashboard reads counts + recentEvents, so the old flat
    /// {total,active,...} shape rendered zeros.
    /// </summary>
    private JsonObject Dashboard()
    {
        var snapshot = Runtime.Snapshot();
        var fibers = snapshot.Fibers;
        var counts = new JsonObject
        {
            ["fibers"] = fibers.Count,
            ["active"] = fibers.Count(f => f.State == FiberState.Active),
            ["pending"] = fibers.Count(f => f.State == FiberState.Pending),
            ["failed"] = fibers.Count(f => f.State == FiberState.Failed),
            ["providers"] = fibers.Sum(f => f.Provides.Count),
            ["dependencies"] = fibers.Sum(f => f.Requires.Count),
            ["effects"] = fibers.Sum(f => f.BoundProviders.Count),
        };

        var events = new JsonArray();
        foreach (var item in Runtime.Events.Snapshot().Events.TakeLast(8))
        {
            events.Add(new JsonObject
            {
                ["sequence"] = item.Sequence,
                ["timestamp"] = item.Timestamp.UtcDateTime.ToString("O"),
                ["type"] = item.Type,
                ["fiberID"] = item.FiberId.ToString(),
                ["activationID"] = item.ActivationId,
            });
        }

        return new JsonObject
        {
            ["sequence"] = Runtime.Events.Sequence,
            ["state"] = "active",
            ["counts"] = counts,
            ["recentEvents"] = events,
            ["diagnostics"] = new JsonArray(),
        };
    }

    /// <summary>
    /// Canonical insight/graph wire shape (Go insight/dto.go Graph): a DECLARED
    /// view (from registry entries) and a RUNTIME view (from one coherent
    /// Snapshot). The console's InsightGraphBlock reads declared/runtime, so the
    /// old flat {nodes,edges} shape rendered "no graph data".
    /// </summary>
    private JsonObject InsightGraph()
    {
        var declaredNodes = new JsonArray();
        var declaredEdges = new JsonArray();
        foreach (var (id, entry) in RegistryForInsight().Entries)
        {
            declaredNodes.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = id,
                ["type"] = entry.Manifest.Type,
                ["requires"] = new JsonArray(entry.Manifest.Requires.Select(d => (JsonNode?)d).ToArray()),
                ["provides"] = new JsonArray(entry.Manifest.Provides.Select(p => (JsonNode?)p).ToArray()),
            });
            foreach (var dependency in entry.Manifest.Requires)
            {
                declaredEdges.Add(new JsonObject
                {
                    ["from"] = id,
                    ["to"] = dependency,
                    ["key"] = dependency,
                });
            }
        }

        var runtimeNodes = new JsonArray();
        var runtimeEdges = new JsonArray();
        foreach (var fiber in Runtime.Snapshot().Fibers)
        {
            runtimeNodes.Add(new JsonObject
            {
                ["fiberId"] = fiber.Id.ToString(),
                ["name"] = fiber.Name,
                ["state"] = fiber.State.ToString(),
                ["activationId"] = fiber.Generation,
                ["provides"] = new JsonArray(fiber.Provides.Select(p => (JsonNode?)p).ToArray()),
                ["requires"] = new JsonArray(fiber.Requires.Select(r => (JsonNode?)r).ToArray()),
            });
            foreach (var binding in fiber.BoundProviders)
            {
                var separator = binding.IndexOf("->", StringComparison.Ordinal);
                var key = separator > 0 ? binding[..separator] : binding;
                var provider = separator > 0 ? binding[(separator + 2)..] : string.Empty;
                runtimeEdges.Add(new JsonObject
                {
                    ["consumerFiberId"] = fiber.Id.ToString(),
                    ["providerFiberId"] = provider,
                    ["key"] = key,
                    ["status"] = "Satisfied",
                });
            }
        }

        return new JsonObject
        {
            ["eventSequence"] = Runtime.Events.Sequence,
            ["declared"] = new JsonObject { ["nodes"] = declaredNodes, ["edges"] = declaredEdges },
            ["runtime"] = new JsonObject { ["nodes"] = runtimeNodes, ["edges"] = runtimeEdges },
        };
    }

    /// <summary>The registry is supplied by the host after construction; when
    /// absent the declared view is empty (runtime view still works).</summary>
    private Plugins.PluginRegistry? _registry;
    public void AttachRegistry(Plugins.PluginRegistry registry) => _registry = registry;
    private Plugins.PluginRegistry RegistryForInsight() =>
        _registry ?? new Plugins.PluginRegistry(Runtime);

    /// <summary>
    /// Canonical config/schema wire shape (Go insight/dto.go SchemaList):
    /// {components:[{type, schema}]} where schema is the generated JSON Schema
    /// document. The console's ConfigEditor consumes it to drive typed forms;
    /// an empty list keeps the legacy value-typed editor.
    /// </summary>
    private JsonObject ConfigSchema()
    {
        var components = new JsonArray();
        if (_registry is not null)
        {
            foreach (var (type, schema) in _registry.SchemasFor())
            {
                components.Add(new JsonObject
                {
                    ["type"] = type,
                    ["schema"] = JsonNode.Parse(schema.ToJsonSchema()),
                });
            }
        }

        return new JsonObject { ["components"] = components };
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
