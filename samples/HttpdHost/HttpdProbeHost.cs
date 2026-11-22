using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HttpdHost;

/// <summary>
/// Dedicated minimal HTTP host sample (the C# counterpart of Go's cmd/httpd).
/// One endpoint, one owner: StartAsync returns only when the listener is serving
/// (Active == ready), and StopAsync is the inverse that leaves no listener behind.
/// This is an implementation-layer resource binding sample, not a second
/// lifecycle system - no runtime state is persisted here.
/// </summary>
public sealed class HttpdProbeHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _configuredAddress;
    private string? _boundAddress;
    private bool _started;
    private bool _disposed;

    /// <param name="address">Kestrel bind URL, e.g. "http://127.0.0.1:8080" or ":0" for a free port.</param>
    public HttpdProbeHost(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        _configuredAddress = address;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.MapGet("/hello", () => Results.Json(new { message = "hello" }));
    }

    /// <summary>The bound URL; before start this is the configured URL, afterwards the real one.</summary>
    public string Address => _boundAddress ?? _configuredAddress;

    public async Task StartAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("HttpdProbeHost is already started");
        }

        await _app.StartAsync(token);
        _boundAddress = _app.Urls.FirstOrDefault() ?? _configuredAddress;
        _started = true;
    }

    public async Task StopAsync(CancellationToken token = default)
    {
        if (_disposed || !_started)
        {
            return;
        }

        await _app.StopAsync(token);
        _started = false;
    }

    /// <summary>
    /// Probes GET /hello through an injected client. Returns the JSON "message"
    /// field; throws on any non-success status or unexpected payload.
    /// </summary>
    public async Task<string> ProbeAsync(HttpClient client, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException("HttpdProbeHost is not started");
        }

        using var response = await client.GetAsync($"{Address}/hello", token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.GetProperty("message").GetString()
               ?? throw new InvalidDataException("probe response has no message");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        await _app.DisposeAsync();
        _disposed = true;
    }
}
