using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gocordis.Console.WebUi;

/// <summary>
/// Transport-level console API. The host and UI contribution contracts live in
/// Gocordis.Plugins; this project is only an ASP.NET Core adapter.
/// </summary>
public sealed class WebUiServer : IAsyncDisposable
{
    private const long MaxBodyBytes = 1L << 20;
    private static readonly TimeSpan StreamInterval = TimeSpan.FromMilliseconds(500);

    private readonly WebApplication _app;

    public WebUiServer(
        ConsoleHost host,
        string address = "http://127.0.0.1:0",
        bool allowRemote = false,
        Plugins.PluginRegistry? registry = null)
    {
        ListenGuard.Validate(address, allowRemote);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        _app = builder.Build();

        _app.MapGet("/", () => Results.Content(WebResources.Read("index.html"), "text/html"));
        _app.MapGet("/app.js", () => Results.Content(WebResources.Read("app.js"), "text/javascript"));
        _app.MapGet("/app.css", () => Results.Content(WebResources.Read("app.css"), "text/css"));

        _app.MapGet("/api/plugins", () =>
        {
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
        {
            var pages = new JsonArray();
            foreach (var entry in host.Ui.Snapshot().Where(entry => entry.Kind == "page"))
            {
                pages.Add(new JsonObject { ["name"] = entry.Name });
            }

            return ApiResults.Data(pages);
        });

        _app.MapGet("/api/query/{**name}", (string name) =>
        {
            var result = host.Api.Query(name);
            return result is null
                ? ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown query '{name}'")
                : ApiResults.Data(result);
        });

        _app.MapPost("/api/command/{**name}", async (string name, HttpContext context) =>
        {
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

        _app.MapPost("/api/plugins/control", async (HttpContext context) =>
        {
            if (registry is null)
            {
                return Unavailable("plugin registry is not configured");
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

            var id = (payload as JsonObject)?["pluginId"]?.GetValue<string>();
            var enableNode = (payload as JsonObject)?["enable"];
            if (string.IsNullOrWhiteSpace(id) || enableNode is not JsonValue enableValue ||
                !enableValue.TryGetValue<bool>(out var enable))
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request",
                    "pluginId and boolean enable are required");
            }

            if (!registry.Entries.ContainsKey(id))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown plugin '{id}'");
            }

            var declarations = registry.Entries.Values
                .Select(entry => entry.Manifest.Id == id ? entry.Manifest with { Enabled = enable } : entry.Manifest)
                .ToArray();
            try
            {
                await registry.ApplyAsync(declarations);
            }
            catch (Exception ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", ex.Message);
            }

            return ApiResults.Data(new JsonObject { ["id"] = id, ["enabled"] = enable });
        });

        _app.MapGet("/api/plugins/{id}/config", (string id) =>
        {
            if (registry is null)
            {
                return Unavailable("plugin registry is not configured");
            }

            if (!registry.Entries.TryGetValue(id, out var entry))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown plugin '{id}'");
            }

            var config = JsonSerializer.SerializeToNode(entry.Manifest.Config) as JsonObject ?? new JsonObject();
            return ApiResults.Data(config);
        });

        _app.MapPost("/api/plugins/{id}/config", async (string id, HttpContext context) =>
        {
            if (registry is null)
            {
                return Unavailable("plugin registry is not configured");
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

            if (!registry.Entries.TryGetValue(id, out var entry))
            {
                return ApiResults.Error(StatusCodes.Status404NotFound, "not_found", $"unknown plugin '{id}'");
            }

            var values = new Dictionary<string, object?>();
            foreach (var (key, value) in config)
            {
                values[key] = ToClr(value);
            }

            var declarations = registry.Entries.Values
                .Select(item => item.Manifest.Id == id ? item.Manifest with { Config = values } : item.Manifest)
                .ToArray();
            try
            {
                await registry.ApplyAsync(declarations);
            }
            catch (Exception ex)
            {
                return ApiResults.Error(StatusCodes.Status400BadRequest, "invalid_request", ex.Message);
            }

            return ApiResults.Data(null, StatusCodes.Status202Accepted);
        });

        // SSE subscription: one observation per tick, and exit when the peer disconnects.
        _app.MapGet("/api/stream", async context =>
        {
            var token = context.RequestAborted;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            // Acquisition comment shares the first event block; no blank line here,
            // so the immediately following observation is the first SSE event.
            await context.Response.WriteAsync(": connected\n", token);
            await context.Response.Body.FlushAsync(token);

            using var timer = new PeriodicTimer(StreamInterval);
            try
            {
                while (!token.IsCancellationRequested)
                {
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
    }

    public string? Address => _app.Urls.FirstOrDefault();

    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);

    public Task StopAsync(CancellationToken token = default) => _app.StopAsync(token);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private static IResult Unavailable(string message) =>
        ApiResults.Error(StatusCodes.Status503ServiceUnavailable, "unavailable", message);

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
