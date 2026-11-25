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

/// <summary>Stable identity of the optional console UI plugin. It is an
/// ordinary declaration: neither the host nor the control plane treats this id
/// as protected, so the console can be disabled, uninstalled, upgraded or
/// replaced through the same IPluginControlPlane as any other plugin.
/// </summary>
public static class ConsolePlugin
{
    public const string Id = "gocordis-console";
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
    private readonly Dictionary<string, UiRoute> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiNavigation> _navigations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiExtension> _extensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiResource> _resources = new(StringComparer.Ordinal);
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
                NormalizeGroup(definition.Group), definition.Home);
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

    public UiContributionHandle RegisterRoute(ContributionOwner owner, UiRouteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.PageId);
        lock (_sync)
        {
            EnsureFree("route", definition.Id, owner);
            _routes[definition.Id] = new UiRoute(
                definition.Id, definition.Path, definition.PageId, definition.Layout,
                definition.Order, owner,
                definition.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, object?>(StringComparer.Ordinal));
            return Track("route", definition.Id, owner);
        }
    }

    public UiContributionHandle RegisterNavigation(ContributionOwner owner, UiNavigationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Route);
        lock (_sync)
        {
            EnsureFree("navigation", definition.Id, owner);
            _navigations[definition.Id] = new UiNavigation(
                definition.Id, definition.Label, definition.Route, definition.Icon,
                NormalizeGroup(definition.Group), definition.PageId, definition.Order, owner);
            return Track("navigation", definition.Id, owner);
        }
    }

    public UiContributionHandle RegisterExtension(ContributionOwner owner, UiExtensionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Renderer);
        lock (_sync)
        {
            EnsureFree("extension", definition.Id, owner);
            _extensions[definition.Id] = new UiExtension(
                definition.Id, definition.Slot, definition.Renderer, definition.PageId,
                definition.Props?.DeepClone(), definition.Order, owner);
            return Track("extension", definition.Id, owner);
        }
    }

    public UiContributionHandle RegisterResource(ContributionOwner owner, UiResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Path);
        lock (_sync)
        {
            EnsureFree("resource", definition.Id, owner);
            _resources[definition.Id] = new UiResource(
                definition.Id, definition.Kind, definition.Path, definition.MediaType,
                definition.Metadata?.DeepClone(), owner);
            return Track("resource", definition.Id, owner);
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

    public IReadOnlyList<UiRoute> Routes()
    {
        lock (_sync)
        {
            return _routes.Values
                .OrderBy(route => route.Order)
                .ThenBy(route => route.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<UiNavigation> Navigations()
    {
        lock (_sync)
        {
            return _navigations.Values
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<UiExtension> Extensions()
    {
        lock (_sync)
        {
            return _extensions.Values
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<UiResource> Resources()
    {
        lock (_sync)
        {
            return _resources.Values
                .OrderBy(item => item.Id, StringComparer.Ordinal)
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
                case "route":
                    _routes.Remove(handle.Id);
                    break;
                case "navigation":
                    _navigations.Remove(handle.Id);
                    break;
                case "extension":
                    _extensions.Remove(handle.Id);
                    break;
                case "resource":
                    _resources.Remove(handle.Id);
                    break;
            }

            _revision++;
        }
    }

    public void UnregisterPage(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterPanel(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterClientModule(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterRoute(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterNavigation(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterExtension(UiContributionHandle handle) => Unregister(handle);

    public void UnregisterResource(UiContributionHandle handle) => Unregister(handle);

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
                    case "route": _routes.Remove(id); break;
                    case "navigation": _navigations.Remove(id); break;
                    case "extension": _extensions.Remove(id); break;
                    case "resource": _resources.Remove(id); break;
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
    /// <summary>Framework capability key. The Web Console plugin consumes this
    /// read model instead of receiving a host-constructed instance.</summary>
    public static readonly Key<ConsoleHost> Key = new("console-host");

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
                    ["schema"] = schema.DeepClone(),
                });
            }
        }

        return new JsonObject { ["components"] = components };
    }

    public JsonObject RuntimeSnapshot()
    {
        var snapshot = Runtime.Snapshot();
        var fibers = new JsonArray();
        var providers = new JsonArray();
        var dependencies = new JsonArray();
        var providerKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fiber in snapshot.Fibers)
        {
            var children = snapshot.Fibers
                .Where(candidate => candidate.ParentId == fiber.Id)
                .Select(candidate => (JsonNode?)JsonValue.Create(candidate.Id.ToString()))
                .ToArray();
            JsonNode[] parents = fiber.ParentId is { } parent
                ? [JsonValue.Create(parent.ToString())]
                : [];

            fibers.Add(new JsonObject
            {
                ["id"] = fiber.Id.ToString(),
                ["name"] = fiber.Name,
                ["component"] = fiber.Name,
                ["state"] = fiber.State.ToString(),
                // Intent/scope are not distinct FiberSnapshot fields; state-based
                // values keep the inspector honest instead of showing dashes.
                ["intent"] = fiber.State switch
                {
                    FiberState.Active => "Mounted",
                    FiberState.Pending => "Awaiting",
                    FiberState.Failed => "Failed",
                    _ => fiber.State.ToString(),
                },
                ["err"] = fiber.Failure,
                ["activationID"] = fiber.Generation,
                ["scopeID"] = 0,
                ["parentFiberIDs"] = new JsonArray(parents),
                ["childFiberIDs"] = new JsonArray(children),
                ["generation"] = fiber.Generation,
                ["requires"] = new JsonArray(fiber.Requires.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
                ["provides"] = new JsonArray(fiber.Provides.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            });

            foreach (var key in fiber.Provides)
            {
                providerKeys.Add(key);
                providers.Add(new JsonObject
                {
                    ["key"] = key,
                    ["state"] = fiber.State.ToString(),
                    ["ownerFiberID"] = fiber.Id.ToString(),
                    ["ownerActivationID"] = fiber.Generation,
                    ["scopeID"] = 0,
                });
            }

            var bindings = fiber.BoundProviders
                .Select(binding => binding.Split("->", 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            foreach (var requirement in fiber.Requires)
            {
                bindings.TryGetValue(requirement, out var providerFiber);
                dependencies.Add(new JsonObject
                {
                    ["key"] = requirement,
                    ["consumerFiberID"] = fiber.Id.ToString(),
                    ["consumerActivationID"] = fiber.Generation,
                    ["providerFiberID"] = providerFiber,
                    ["status"] = providerFiber is null ? "Pending" : "Bound",
                });
            }
        }

        var effectFailures = new JsonArray();
        foreach (var failure in snapshot.EffectFailures)
        {
            effectFailures.Add(new JsonObject
            {
                ["fiberId"] = failure.FiberId.ToString(),
                ["activationId"] = failure.ActivationId,
                ["slotIndex"] = failure.SlotIndex,
                ["label"] = failure.Label,
                ["isInstall"] = failure.IsInstall,
                ["exceptionType"] = failure.ExceptionType,
                ["message"] = failure.Message,
            });
        }

        // No scope/effect-stack read model exists in Core yet: emit the one
        // default scope and empty effects rather than fabricating rows.
        var scopes = new JsonArray
        {
            new JsonObject
            {
                ["id"] = 0,
                ["providerKeys"] = new JsonArray(providerKeys
                    .Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
                ["fiberIDs"] = new JsonArray(snapshot.Fibers
                    .Select(fiber => (JsonNode?)JsonValue.Create(fiber.Id.ToString())).ToArray()),
            },
        };

        return new JsonObject
        {
            ["sequence"] = Runtime.Events.Snapshot().Sequence,
            ["runtimeID"] = "runtime:1",
            ["state"] = "Running",
            ["fibers"] = fibers,
            ["scopes"] = scopes,
            ["providers"] = providers,
            ["dependencies"] = dependencies,
            ["effects"] = new JsonArray(),
            ["effectFailures"] = effectFailures,
        };
    }
}
