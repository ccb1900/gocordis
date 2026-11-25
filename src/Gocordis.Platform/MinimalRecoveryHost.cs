using System.IO;
using System.Text.Json.Nodes;
using Gocordis.Builtin.WebConsole;
using Gocordis.Packaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Gocordis.Platform;

/// <summary>
/// Host-owned degraded maintenance surface. It is deliberately NOT the Console
/// plugin: only the two recovery endpoints exist. The full Console owns its
/// transport during normal operation; this host exists so a failed or disabled
/// Web Console never removes the operator's last-known-good restore path.
/// </summary>
public sealed class MinimalRecoveryHost : IAsyncDisposable
{
    private const string RecoveryPage = """
        <!doctype html><html lang="en"><meta charset="utf-8"><title>Gocordis recovery</title>
        <body><h1>Gocordis recovery</h1><p>The Web Console plugin is unavailable.</p>
        <p>POST /api/host/recover with {"source":"cli"} to restore the last-known-good manifest.</p></body></html>
        """;

    private readonly WebApplication _app;

    public MinimalRecoveryHost(string address, bool allowRemote, HostRecovery recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ListenGuard.Validate(address, allowRemote);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        _app = builder.Build();
        _app.MapGet("/recover", () => Results.Content(RecoveryPage, "text/html"));
        _app.MapPost("/api/host/recover", async (HttpContext context) =>
        {
            JsonNode? payload = null;
            try
            {
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    payload = JsonNode.Parse(body);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                payload = null;
            }

            var source = payload is JsonObject obj && obj["source"] is JsonValue value &&
                         value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                ? text
                : "recovery-host";
            var mutation = await recovery.RecoverAsync(source, context.RequestAborted);
            return Results.Json(new
            {
                data = mutation,
                code = mutation.Code,
                message = mutation.Error,
            });
        });
    }

    public string? Address => _app.Urls.FirstOrDefault();

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _app.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _app.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
