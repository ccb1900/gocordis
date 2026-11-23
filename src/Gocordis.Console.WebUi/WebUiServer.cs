using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Extensions;
using Gocordis.Packaging;
using Gocordis.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Gocordis.Console.WebUi;

/// <summary>The console capability a request wants to exercise. A policy sees
/// the scope, never the raw request, so read-only and narrow write policies
/// stay simple.</summary>
public enum ConsoleAccessScope
{
    Query,
    Command,
    Lifecycle,
    Config,
    Package,
    /// <summary>Reads of host/plugin state (list, config, dependencies, store).</summary>
    Read,
    /// <summary>Continuous observation of host state (SSE stream).</summary>
    Observe,
}

/// <summary>Returns true when the caller may exercise the given scope.</summary>
public delegate bool ConsoleAccessPolicy(ConsoleAccessScope scope);

/// <summary>
/// Transport-level console API. The host and UI contribution contracts live in
/// Gocordis.Plugins; this project is only an ASP.NET Core adapter.
/// </summary>
public sealed class WebUiServer : IAsyncDisposable
{
    private const long MaxBodyBytes = 1L << 20;
    private const string RedactedValue = "***";
    private static readonly TimeSpan StreamInterval = TimeSpan.FromMilliseconds(500);

    // Declaration values are echoed to the UI; credential-shaped names are
    // projected to a fixed marker before they leave the transport boundary.
    private static readonly string[] SensitiveFieldMarkers =
    [
        "password", "passwd", "pwd", "secret", "token", "credential",
        "connectionstring", "authorization",
    ];

    private readonly WebApplication _app;
    private readonly ConsoleHost _host;
    private readonly bool _allowRemote;
    private readonly IPluginControlPlane? _control;
    private readonly ConsoleAccessPolicy? _accessPolicy;
    private readonly Func<string, string?> _clientModuleRoot;
    private readonly string? _reactDist;

    public WebUiServer(
        ConsoleHost host,
        string address = "http://127.0.0.1:0",
        bool allowRemote = false,
        Plugins.PluginRegistry? registry = null,
        Func<string, string?>? clientModuleRootResolver = null,
        string? webDistPath = null,
        IPluginControlPlane? control = null,
        ConsoleAccessPolicy? accessPolicy = null,
        ServerRouteGuard? routeGuard = null)
    {
        _host = host;
        _allowRemote = allowRemote;
        _control = control;
        _accessPolicy = accessPolicy;
        _clientModuleRoot = clientModuleRootResolver ?? DefaultClientModuleRoot;
        _reactDist = FindReactDist(webDistPath);

        ListenGuard.Validate(address, allowRemote);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        // Package uploads are streamed to the control service; both the multipart
        // reader and Kestrel must admit the same explicit package size limit.
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = PluginPackageLimits.MaxPackageBytes;
            options.MemoryBufferThreshold = 1 << 20;
        });
        builder.WebHost.ConfigureKestrel(options =>
            options.Limits.MaxRequestBodySize = PluginPackageLimits.MaxPackageBytes);
        _app = builder.Build();

        // Optional inbound route guard (e.g. HostAcl via ServerRouteGuard).
        // It is never installed by default, so existing listeners keep their
        // behavior; denials are the guard's own 404, ahead of any route mapping.
        if (routeGuard is not null)
        {
            _app.Use(routeGuard.Middleware);
        }

        // The built React console is the default UI when its publish output is
        // present. The embedded bootstrap remains the zero-build fallback.
        _app.MapGet("/", ServeIndex);
        _app.MapGet("/assets/{**path}", (string path, HttpContext context) => ServeReactAsset(path, context));

        // Legacy bootstrap endpoints stay available (embedded, always present).
        _app.MapGet("/app.js", () => Results.Content(WebResources.Read("app.js"), "text/javascript"));
        _app.MapGet("/app.css", () => Results.Content(WebResources.Read("app.css"), "text/css"));

        _app.MapGet("/api/plugins", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Read);
            if (denial is not null) return denial;

            if (_control is not null)
            {
                var entries = await _control.ListAsync(context.RequestAborted);
                return ApiResults.Data(new JsonArray(entries.Select(entry => (JsonNode?)ToJson(entry)).ToArray()));
            }

            var plugins = new JsonArray();
            foreach (var (id, entry) in registry?.Entries ?? new Dictionary<string, Plugins.PluginEntry>())
            {
                plugins.Add(new JsonObject
                {
                    ["id"] = id,
                    ["type"] = entry.Manifest.Type,
                    ["enabled"] = entry.Manifest.Enabled,
                    ["state"] = entry.Fiber?.State.ToString() ?? "disabled",
                });
            }

            return ApiResults.Data(plugins);
        });

        _app.MapGet("/api/plugins/store", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Read);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            var installed = await _control.StoreAsync(context.RequestAborted);
            return ApiResults.Data(new JsonArray(installed.Select(item => (JsonNode?)ToJson(item)).ToArray()));
        });

        _app.MapGet("/api/plugins/{id}/dependencies", async (string id, HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Read);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            var view = await _control.DependenciesAsync(id, context.RequestAborted);
            return view is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown plugin '{id}'")
                : ApiResults.Data(ToJson(view));
        });

        _app.MapGet("/api/fleet", () => ApiResults.Data(new JsonObject
        {
            ["frameworkVersion"] = "1",
            ["self"] = true,
            ["plugins"] = registry?.Entries.Count ?? 0,
        }));

        _app.MapGet("/api/meta", () => ApiResults.Data(new JsonObject
        {
            ["frameworkVersion"] = "1",
            ["kind"] = "gocordis-console",
        }));

        _app.MapGet("/api/ui/pages", () =>
            ApiResults.Data(new JsonObject { ["pages"] = PageNodes(host.Ui) }));

        _app.MapGet("/api/ui/panels", () =>
            ApiResults.Data(new JsonObject { ["panels"] = PanelNodes(host.Ui) }));

        _app.MapGet("/api/ui/client-modules", () =>
            ApiResults.Data(ModuleData(host.Ui)));

        // One read model for the console's composition snapshot. The existing
        // narrow endpoints keep their exact wire shape; this endpoint only
        // composes them so an installed plugin's whole UI declaration is
        // observable without polling three routes separately.
        _app.MapGet("/api/ui/manifest", () =>
        {
            var data = ModuleData(host.Ui);
            data["pages"] = PageNodes(host.Ui);
            data["panels"] = PanelNodes(host.Ui);
            return ApiResults.Data(data);
        });

        _app.MapGet("/api/query/{**name}", (string name) =>
        {
            // Reads stay open in the remote-without-policy mode; an explicit
            // policy may still restrict query access.
            IResult? denial = _accessPolicy is null ? null : Deny(ConsoleAccessScope.Query);
            if (denial is not null) return denial;

            var result = host.Api.Query(name);
            return result is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown query '{name}'")
                : ApiResults.Data(result);
        });

        _app.MapPost("/api/command/{**name}", async (string name, HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Command);
            if (denial is not null) return denial;

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            var ok = await host.Api.ExecuteAsync(name, payload, context.RequestAborted);
            return ok
                ? ApiResults.Data(null, StatusCodes.Status202Accepted)
                : ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown command '{name}'");
        });

        // Multipart package upload. IFormFile is backed by a buffered stream, so
        // the control service still receives a forward-only stream and enforces
        // the package limit itself; FormOptions/Kestrel admit the full size.
        _app.MapPost("/api/plugins/packages", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Package);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            if (context.Request.ContentLength > PluginPackageLimits.MaxPackageBytes + (1 << 20))
            {
                return ApiResults.Error(StatusCodes.Status413RequestEntityTooLarge, "invalid_request",
                    "package exceeds the size limit");
            }

            IFormFile? file;
            try
            {
                file = (await context.Request.ReadFormAsync(context.RequestAborted))
                    .Files["package"];
            }
            catch (InvalidDataException ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", ex.Message);
            }

            if (file is null || file.Length == 0)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", "package file is required");
            }

            if (file.Length > PluginPackageLimits.MaxPackageBytes)
            {
                return ApiResults.Error(StatusCodes.Status413RequestEntityTooLarge, "invalid_request",
                    "package exceeds the size limit");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var upload = await _control.UploadAsync(stream, file.FileName, context.RequestAborted);
                return ApiResults.Data(ToJson(upload));
            }
            catch (PluginUploadTooLargeException ex)
            {
                return ApiResults.Error(StatusCodes.Status413RequestEntityTooLarge, "invalid_request", ex.Message);
            }
            catch (InvalidPluginPackageException ex)
            {
                return ApiResults.Error(StatusCodes.Status422UnprocessableEntity, "invalid_package", ex.Message);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", ex.Message);
            }
        });

        _app.MapPost("/api/plugins/install", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Package);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            var body = payload as JsonObject;
            var uploadKey = body?["uploadKey"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uploadKey))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", "uploadKey is required");
            }

            var enable = true;
            if (body?["enable"] is JsonValue enableValue && !enableValue.TryGetValue(out enable))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", "enable must be a boolean");
            }

            return MutationResult(await _control.InstallAsync(uploadKey, enable, context.RequestAborted));
        });

        _app.MapPost("/api/plugins/upgrade", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Package);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            var body = payload as JsonObject;
            var id = body?["id"]?.GetValue<string>();
            var uploadKey = body?["uploadKey"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(uploadKey))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request",
                    "id and uploadKey are required");
            }

            var versionRange = body?["versionRange"]?.GetValue<string>();
            return MutationResult(await _control.UpgradeAsync(id, uploadKey, versionRange, context.RequestAborted));
        });

        _app.MapPost("/api/plugins/uninstall", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Package);
            if (denial is not null) return denial;

            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            var body = payload as JsonObject;
            var id = body?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", "id is required");
            }

            var version = body?["version"]?.GetValue<string>();
            return MutationResult(await _control.UninstallAsync(id, version, context.RequestAborted));
        });

        _app.MapPost("/api/plugins/control", async (HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Lifecycle);
            if (denial is not null) return denial;

            // The registry is a read model. Without the declaration control
            // service a write could only change a fiber, never its manifest.
            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            var body = payload as JsonObject;
            var pluginId = body?["pluginId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(pluginId) ||
                body?["enable"] is not JsonValue enableValue ||
                !enableValue.TryGetValue<bool>(out var enabled))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request",
                    "pluginId and boolean enable are required");
            }

            return MutationResult(await _control.SetEnabledAsync(pluginId, enabled, context.RequestAborted));
        });

        _app.MapGet("/api/plugins/{id}/config", (string id) =>
        {
            var denial = Deny(ConsoleAccessScope.Read);
            if (denial is not null) return denial;

            if (registry is null)
            {
                return Unavailable("plugin registry is not configured");
            }

            if (!registry.Entries.TryGetValue(id, out var entry))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown plugin '{id}'");
            }

            // Both config read paths share the redacted projection; the raw
            // declaration stays in the registry for the control service.
            return ApiResults.Data(ConfigNode(entry.Manifest.Config));
        });

        _app.MapPost("/api/plugins/{id}/config", async (string id, HttpContext context) =>
        {
            var denial = Deny(ConsoleAccessScope.Config);
            if (denial is not null) return denial;

            // Configuration is a declaration: only the control service can
            // persist it. A registry-only fallback would leave a fiber and its
            // declaration out of sync after a restart.
            if (_control is null)
            {
                return Unavailable("plugin control service is not configured");
            }

            JsonNode? payload;
            try
            {
                payload = await ReadBodyAsync(context);
            }
            catch (ApiRequestException ex)
            {
                return ApiResults.Error(ex.Status, "invalid_request", ex.Message);
            }

            if (payload is not JsonObject body || body["config"] is not JsonObject config)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", "config object required");
            }

            var values = new Dictionary<string, object?>();
            foreach (var (key, value) in config)
            {
                values[key] = ToClr(value);
            }

            return MutationResult(await _control.SetConfigAsync(id, values, context.RequestAborted));
        });

        // Plugin client modules: only registered names are served, and every
        // path segment is checked before it touches the filesystem.
        _app.MapGet("/client-modules/{name}", (string name, HttpContext context) =>
            ServeClientModule(name, null, context));
        _app.MapGet("/client-modules/{name}/{**path}", (string name, string path, HttpContext context) =>
            ServeClientModule(name, path, context));

        // SSE subscription: one observation per tick, plus a ui-manifest frame
        // whenever the composition registry's revision moves and a plugin-state
        // frame whenever the declaration control service commits a mutation.
        // Existing observation frames keep their exact wire shape.
        _app.MapGet("/api/stream", async context =>
        {
            // Authorize before the first header or body byte: a rejected
            // observer must not receive an SSE envelope or any host state.
            var denial = Deny(ConsoleAccessScope.Observe);
            if (denial is not null)
            {
                await denial.ExecuteAsync(context);
                return;
            }

            var token = context.RequestAborted;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            // Acquisition comment shares the first event block; no blank line here,
            // so the immediately following observation is the first SSE event.
            await context.Response.WriteAsync(": connected\n", token);
            await context.Response.Body.FlushAsync(token);

            var lastRevision = host.Ui.Revision;
            var lastPluginRevision = _control?.Revision ?? 0;
            using var timer = new PeriodicTimer(StreamInterval);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var revision = host.Ui.Revision;
                    if (revision != lastRevision)
                    {
                        var manifest = new JsonObject { ["revision"] = revision };
                        await context.Response.WriteAsync(
                            $"event: ui-manifest\ndata: {manifest.ToJsonString()}\n\n", token);
                        await context.Response.Body.FlushAsync(token);
                        lastRevision = revision;
                    }

                    if (_control is not null)
                    {
                        var pluginRevision = _control.Revision;
                        if (pluginRevision != lastPluginRevision)
                        {
                            var mutation = new JsonObject
                            {
                                ["revision"] = pluginRevision,
                                ["action"] = "mutation",
                            };
                            await context.Response.WriteAsync(
                                $"event: plugin-state\ndata: {mutation.ToJsonString()}\n\n", token);
                            await context.Response.Body.FlushAsync(token);
                            lastPluginRevision = pluginRevision;
                        }
                    }

                    var observation = host.RuntimeSnapshot();
                    observation["type"] = "runtime.snapshot";
                    await context.Response.WriteAsync(
                        $"event: observation\ndata: {observation.ToJsonString()}\n\n", token);
                    await context.Response.Body.FlushAsync(token);
                    await timer.WaitForNextTickAsync(token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Normal end of a browser subscription.
            }
        });

        _app.MapFallback("/api/{**path}", () =>
            ApiResults.Error(StatusCodes.Status404NotFound, "not_found", "unknown api route"));
        _app.MapFallback(ServeIndex);
    }

    public string? Address => _app.Urls.FirstOrDefault();

    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);

    public Task StopAsync(CancellationToken token = default) => _app.StopAsync(token);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private IResult ServeIndex()
    {
        if (_reactDist is not null)
        {
            var index = Path.Combine(_reactDist, "index.html");
            if (File.Exists(index))
            {
                return Results.File(index, "text/html; charset=utf-8");
            }
        }

        return Results.Content(WebResources.Read("index.html"), "text/html");
    }

    private static string RawTarget(HttpContext context) =>
        context.Features.Get<IHttpRequestFeature>()?.RawTarget ??
        context.Request.Path + context.Request.QueryString;

    private IResult ServeReactAsset(string path, HttpContext context)
    {
        if (_reactDist is null || WebAssetPaths.ContainsEncodedTraversal(RawTarget(context)))
        {
            return Results.NotFound();
        }

        var segments = WebAssetPaths.SplitSegments(path);
        if (!WebAssetPaths.TryResolve(Path.Combine(_reactDist, "assets"), segments, out var file))
        {
            return Results.NotFound();
        }

        return Results.File(file, WebAssetPaths.ContentTypeOf(file), enableRangeProcessing: true);
    }

    private IResult ServeClientModule(string name, string? path, HttpContext context)
    {
        if (IsUnsafeName(name) || WebAssetPaths.ContainsEncodedTraversal(RawTarget(context)))
        {
            return Results.NotFound();
        }

        var module = _host.Ui.ClientModules().FirstOrDefault(item => item.Name == name);
        if (module is null)
        {
            return Results.NotFound();
        }

        var segments = WebAssetPaths.SplitSegments(path);
        if (segments.Any(segment => segment is "." or ".."))
        {
            return Results.NotFound();
        }
        if (segments.Length == 0)
        {
            segments = WebAssetPaths.SplitSegments(module.Entry);
        }

        // A packaged plugin carries its own install directory in the module
        // contribution. That version directory is the authority; the resolver
        // is only the legacy/host-provided fallback for hand-registered modules.
        var root = module.Source is { } source && Directory.Exists(source)
            ? source
            : _clientModuleRoot(name);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Results.NotFound();
        }

        var mount = Path.GetFullPath(root);
        var publicRoot = Path.Combine(mount, "public");
        if (Directory.Exists(publicRoot))
        {
            mount = publicRoot;
        }

        if (!WebAssetPaths.TryResolve(mount, segments, out var file))
        {
            return Results.NotFound();
        }

        var isEntry = segments.SequenceEqual(WebAssetPaths.SplitSegments(module.Entry));
        return Results.File(
            file,
            WebAssetPaths.ContentTypeOf(file, forceJavaScript: isEntry),
            enableRangeProcessing: true);
    }

    private static JsonArray PageNodes(UiContributionRegistry ui)
    {
        var pages = new JsonArray();
        foreach (var page in ui.Pages())
        {
            var item = new JsonObject
            {
                ["id"] = page.Id,
                ["title"] = page.Title,
                ["route"] = page.Route,
                ["renderer"] = page.Renderer,
                ["order"] = page.Order,
            };
            if (page.Icon is not null)
            {
                item["icon"] = page.Icon;
            }
            if (page.Group is not null)
            {
                item["group"] = page.Group;
            }
            if (page.Description is not null)
            {
                item["description"] = page.Description;
            }
            if (page.Views is not null)
            {
                item["views"] = page.Views.DeepClone();
            }
            if (page.Actions.Count > 0)
            {
                var actions = new JsonArray();
                foreach (var action in page.Actions)
                {
                    var entry = new JsonObject
                    {
                        ["label"] = action.Label,
                        ["command"] = action.Command,
                    };
                    if (action.DatePicker)
                    {
                        entry["datePicker"] = true;
                    }
                    actions.Add(entry);
                }
                item["actions"] = actions;
            }
            pages.Add(item);
        }

        return pages;
    }

    private static JsonArray PanelNodes(UiContributionRegistry ui)
    {
        var panels = new JsonArray();
        foreach (var panel in ui.Panels())
        {
            var item = new JsonObject
            {
                ["id"] = panel.Id,
                ["title"] = panel.Title,
                ["position"] = panel.Position,
                ["renderer"] = panel.Renderer,
            };
            if (panel.Pages.Count > 0)
            {
                item["pages"] = new JsonArray(
                    panel.Pages.Select(page => (JsonNode?)JsonValue.Create(page)).ToArray());
            }
            if (panel.Views is not null)
            {
                item["views"] = panel.Views.DeepClone();
            }
            panels.Add(item);
        }

        return panels;
    }

    private static JsonObject ModuleData(UiContributionRegistry ui)
    {
        var revision = ui.Revision;
        var modules = new JsonArray();
        foreach (var module in ui.ClientModules())
        {
            modules.Add(new JsonObject
            {
                ["name"] = module.Name,
                ["url"] = ClientModuleUrl(module, revision),
                ["packageId"] = JsonValue.Create(module.PackageId),
                ["version"] = JsonValue.Create(module.PackageVersion),
                ["contentHash"] = JsonValue.Create(module.ContentHash),
            });
        }

        return new JsonObject
        {
            ["modules"] = modules,
            ["revision"] = revision,
        };
    }

    private static string ClientModuleUrl(UiClientModule module, long revision)
    {
        var segments = WebAssetPaths.SplitSegments(module.Entry)
            .Where(segment => segment is not ("." or "..") && !segment.StartsWith('.'))
            .ToArray();
        if (segments.Length == 0)
        {
            segments = ["ui.js"];
        }

        var entry = string.Join('/', segments.Select(Uri.EscapeDataString));
        // A composition revision is part of the URL so a re-registered module
        // always gets a fresh ESM identity even when its file bytes are unchanged.
        // Source/version/content-hash are part of the cache key: an upgrade
        // always produces a fresh ESM identity even when the file name is the
        // same, so the browser cannot keep a stale module activation.
        var payload = string.Join('\u001f',
            revision, module.Name, module.Revision, entry,
            module.PackageId ?? string.Empty, module.PackageVersion ?? string.Empty,
            module.ContentHash ?? string.Empty,
            module.Source ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var version = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
        return $"/client-modules/{Uri.EscapeDataString(module.Name)}/{entry}?v={version}";
    }

    private static bool IsUnsafeName(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value is "." or ".." ||
        value.StartsWith('.') ||
        value.Contains('/') || value.Contains('\\') || value.Contains(':');

    private static string DefaultClientModuleRoot(string name) =>
        Path.Combine(AppContext.BaseDirectory, "client-modules", name);

    private static string? FindReactDist(string? explicitPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        var environmentPath = Environment.GetEnvironmentVariable("GOCORDIS_WEB_DIST");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            candidates.Add(environmentPath);
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "web-dist"));

        return candidates.FirstOrDefault(path =>
        {
            try
            {
                return File.Exists(Path.Combine(path, "index.html"));
            }
            catch (IOException)
            {
                return false;
            }
        });
    }

    // ---- plugin control wire shapes (lowercase fields are the fixed contract) --

    private static JsonObject ToJson(PluginControlEntry entry) => new()
    {
        ["id"] = entry.Id,
        ["name"] = entry.Name,
        ["type"] = entry.Type,
        ["enabled"] = entry.Enabled,
        ["state"] = entry.State,
        ["error"] = JsonValue.Create(entry.Error),
        ["version"] = entry.Version,
        ["installedVersion"] = JsonValue.Create(entry.InstalledVersion),
        ["protected"] = entry.Protected,
        ["controllable"] = entry.Controllable,
        ["declared"] = entry.Declared,
        ["config"] = ConfigNode(entry.Config),
        ["components"] = StringArray(entry.Components),
        ["capabilities"] = StringArray(entry.Capabilities),
        ["requires"] = StringArray(entry.Requires),
        ["provides"] = StringArray(entry.Provides),
        ["declaredCapabilityRequires"] = StringArray(entry.DeclaredCapabilityRequires),
        ["declaredCapabilityProvides"] = StringArray(entry.DeclaredCapabilityProvides),
    };

    private static JsonObject ToJson(StorePluginEntry entry) => new()
    {
        ["id"] = entry.Id,
        ["version"] = entry.Version,
        ["displayName"] = entry.DisplayName,
        ["hostVersionRange"] = entry.HostVersionRange,
        ["installPath"] = InstallPathName(entry.InstallPath),
        ["dependencies"] = DependencyArray(entry.Dependencies),
        ["provides"] = StringArray(entry.Provides),
        ["checksum"] = entry.Checksum,
        ["declared"] = entry.Declared,
        ["running"] = entry.Running,
    };

    private static JsonObject ToJson(PluginDependencyView view) => new()
    {
        ["id"] = view.Id,
        ["version"] = view.Version,
        ["packageDependencies"] = new JsonArray(view.PackageDependencies.Select(dependency =>
            (JsonNode?)new JsonObject
            {
                ["id"] = dependency.Id,
                ["versionRange"] = dependency.VersionRange,
                ["installedVersion"] = JsonValue.Create(dependency.InstalledVersion),
                ["satisfied"] = dependency.Satisfied,
            }).ToArray()),
        ["dependents"] = StringArray(view.Dependents),
        ["capabilityRequires"] = StringArray(view.CapabilityRequires),
        ["capabilityProvides"] = StringArray(view.CapabilityProvides),
        ["declaredCapabilityRequires"] = StringArray(view.DeclaredCapabilityRequires),
        ["declaredCapabilityProvides"] = StringArray(view.DeclaredCapabilityProvides),
        ["diagnostics"] = StringArray(view.Diagnostics),
    };

    private static JsonObject ToJson(PluginUploadResult result) => new()
    {
        ["uploadKey"] = result.UploadKey,
        ["id"] = result.Id,
        ["version"] = result.Version,
        ["type"] = result.Type,
        ["hostVersionRange"] = result.HostVersionRange,
        ["checksum"] = result.Checksum,
        ["dependencies"] = DependencyArray(result.Dependencies),
    };

    private static JsonObject ToJson(PluginMutationResult result) => new()
    {
        ["accepted"] = result.Accepted,
        ["code"] = result.Code,
        ["error"] = JsonValue.Create(result.Error),
        ["id"] = result.Id,
        ["state"] = JsonValue.Create(result.State),
        ["persisted"] = result.Persisted,
        ["changes"] = new JsonArray(result.Changes.Select(change =>
            (JsonNode?)new JsonObject
            {
                ["kind"] = change.Kind.ToString(),
                ["id"] = change.Id,
                ["version"] = change.Version,
            }).ToArray()),
    };

    private static IResult MutationResult(PluginMutationResult result)
    {
        if (result.Accepted)
        {
            return ApiResults.Data(ToJson(result));
        }

        var status = result.Code switch
        {
            "invalid_request" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "invalid_package" or "invalid_config" => StatusCodes.Status422UnprocessableEntity,
            "forbidden" => StatusCodes.Status403Forbidden,
            "unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        // Keep the machine-readable code at the root for the unified transport
        // error shape and the full mutation result inside `data` for clients that
        // inspect `accepted`/`changes` even on a rejected command.
        return Results.Json(new JsonObject
        {
            ["data"] = ToJson(result),
            ["code"] = result.Code,
            ["message"] = result.Error ?? "mutation rejected",
        }, statusCode: status);
    }

    /// <summary>Projects a declaration config for the wire: credential-shaped
    /// fields are replaced at every depth, while ordinary values keep their
    /// JSON shape. The stored declaration is never changed.</summary>
    private static JsonObject ConfigNode(IReadOnlyDictionary<string, object?> config)
    {
        var node = new JsonObject();
        foreach (var (key, value) in config)
        {
            node[key] = IsSensitiveField(key)
                ? JsonValue.Create(RedactedValue)
                : MaskNode(JsonSerializer.SerializeToNode(value));
        }

        return node;
    }

    private static JsonNode? MaskNode(JsonNode? node) => node switch
    {
        JsonObject obj =>
            new JsonObject(obj.Select(pair =>
                new KeyValuePair<string, JsonNode?>(
                    pair.Key,
                    IsSensitiveField(pair.Key) ? JsonValue.Create(RedactedValue) : MaskNode(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(MaskNode).ToArray()),
        _ => node?.DeepClone(),
    };

    private static bool IsSensitiveField(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (SensitiveFieldMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            return true;
        }

        // "key" is a secret only as a whole field component: apiKey,
        // signing_key, key — not incidental text such as "monkey".
        return FieldWords(name).Contains("key");
    }

    private static IEnumerable<string> FieldWords(string name)
    {
        var word = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString().ToLowerInvariant();
                    word.Clear();
                }
                continue;
            }

            if (word.Length > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
            {
                yield return word.ToString().ToLowerInvariant();
                word.Clear();
            }

            word.Append(c);
        }

        if (word.Length > 0)
        {
            yield return word.ToString().ToLowerInvariant();
        }
    }

    /// <summary>Store rows are an inventory display: a package file name is
    /// useful, its host directory is not part of the remote contract.</summary>
    private static string InstallPathName(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return RedactedValue;
        }

        var name = Path.GetFileName(installPath.Replace('\\', '/'));
        return string.IsNullOrEmpty(name) ? RedactedValue : name;
    }

    private static JsonArray StringArray(IReadOnlyList<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonArray DependencyArray(IReadOnlyList<PluginDependency> dependencies) =>
        new(dependencies.Select(dependency => (JsonNode?)new JsonObject
        {
            ["id"] = dependency.Id,
            ["versionRange"] = dependency.VersionRange,
        }).ToArray());

    private static IResult Unavailable(string message) =>
        ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "unavailable", message);

    // A remote listener grants NO guarded scope until the host supplies an
    // explicit policy; a supplied policy is authoritative even on loopback.
    private bool CanAccess(ConsoleAccessScope scope) =>
        _accessPolicy?.Invoke(scope) ?? !_allowRemote;

    private IResult? Deny(ConsoleAccessScope scope) =>
        CanAccess(scope)
            ? null
            : ApiResults.Error(StatusCodes.Status403Forbidden, "forbidden",
                $"{scope} is not permitted on this console");

    private static async Task<JsonNode?> ReadBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is > MaxBodyBytes)
        {
            throw new ApiRequestException("request body exceeds 1 MiB", StatusCodes.Status413RequestEntityTooLarge);
        }

        using var collected = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxBodyBytes)
            {
                throw new ApiRequestException("request body exceeds 1 MiB", StatusCodes.Status413RequestEntityTooLarge);
            }

            collected.Write(chunk, 0, read);
        }

        if (total == 0)
        {
            return null;
        }

        collected.Position = 0;
        try
        {
            return await JsonNode.ParseAsync(collected, cancellationToken: context.RequestAborted);
        }
        catch (JsonException ex)
        {
            throw new ApiRequestException("invalid JSON body", StatusCodes.Status400BadRequest, ex);
        }
    }

    private static object? ToClr(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => ToClr(pair.Value)),
        JsonArray array => array.Select(ToClr).ToArray(),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
        JsonValue value when value.TryGetValue<long>(out var number) => number,
        JsonValue value when value.TryGetValue<double>(out var real) => real,
        JsonValue value => value.ToJsonString(),
        _ => node?.ToJsonString(),
    };
}

internal static class ApiResults
{
    public static IResult Data(JsonNode? data, int status = StatusCodes.Status200OK) =>
        Results.Json(new JsonObject { ["data"] = data }, statusCode: status);

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new JsonObject { ["code"] = code, ["message"] = message }, statusCode: status);
}

internal sealed class ApiRequestException : Exception
{
    public ApiRequestException(string message, int status = StatusCodes.Status400BadRequest, Exception? inner = null)
        : base(message, inner) => Status = status;

    public int Status { get; }
}

internal static class WebResources
{
    public static string Read(string name)
    {
        using var stream = typeof(WebUiServer).Assembly
            .GetManifestResourceStream($"Gocordis.Console.WebUi.{name}") ??
            throw new InvalidOperationException($"embedded resource '{name}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public static class ListenGuard
{
    /// <summary>Loopback by default; wildcard or remote binding requires explicit opt-in.</summary>
    public static void Validate(string address, bool allowRemote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var host = ExtractHost(address);
        if (allowRemote)
        {
            return;
        }

        if (host is "*" or "+" or "")
        {
            throw new InvalidOperationException("wildcard listen requires explicit opt-in");
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip) &&
            System.Net.IPAddress.IsLoopback(ip))
        {
            return;
        }

        throw new InvalidOperationException($"remote listen '{host}' requires explicit opt-in");
    }

    private static string ExtractHost(string address)
    {
        var rest = address.Contains("://", StringComparison.Ordinal)
            ? address[(address.IndexOf("://", StringComparison.Ordinal) + 3)..]
            : address;
        var authority = rest.Split('/', 2)[0];
        if (authority.StartsWith('['))
        {
            var closing = authority.IndexOf(']');
            return closing < 0 ? authority : authority[..(closing + 1)];
        }

        var colon = authority.LastIndexOf(':');
        return colon < 0 ? authority : authority[..colon];
    }
}
