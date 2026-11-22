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
    private readonly WebApplication _app;

    public WebUiServer(ConsoleHost host, string address = "http://127.0.0.1:0", bool allowRemote = false)
    {
        ListenGuard.Validate(address, allowRemote);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        _app = builder.Build();

        _app.MapGet("/api/meta", () => new JsonObject
        {
            ["frameworkVersion"] = "1",
            ["kind"] = "gocordis-console",
        });

        _app.MapGet("/api/ui/pages", () => host.Ui.Snapshot()
            .Where(entry => entry.Kind == "page")
            .Select(entry => (JsonNode?)new JsonObject { ["name"] = entry.Name })
            .ToArray());

        _app.MapGet("/api/query/{**name}", (string name) =>
        {
            var result = host.Api.Query(name);
            return Results.Json(result is null ? new JsonObject { ["error"] = "unknown query" } : result);
        });

        _app.MapPost("/api/command/{**name}", async (string name, HttpContext context) =>
        {
            JsonNode? payload = null;
            if (context.Request.ContentLength is > 0)
            {
                payload = await JsonNode.ParseAsync(context.Request.Body);
            }

            var ok = await host.Api.ExecuteAsync(name, payload, context.RequestAborted);
            return ok ? Results.Ok() : Results.NotFound();
        });

        _app.MapGet("/api/stream", async context =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync($"data: {host.RuntimeSnapshot().ToJsonString()}\n\n");
            await context.Response.Body.FlushAsync();
        });
    }

    public string? Address => _app.Urls.FirstOrDefault();

    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);

    public Task StopAsync(CancellationToken token = default) => _app.StopAsync(token);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
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
