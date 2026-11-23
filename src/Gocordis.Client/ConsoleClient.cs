using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gocordis.Client;

public sealed record ConsoleMeta(string FrameworkVersion, string Kind);
public sealed record ConsolePage(string Id, string Title, string Route, string Renderer);
public sealed record ConsolePanel(string Id, string Title, string Position, string Renderer);
public sealed record ConsoleClientModule(string Name, string Url);
public sealed record ConsoleClientModuleManifest(long Revision, IReadOnlyList<ConsoleClientModule> Modules);

/// <summary>
/// .NET-native replacement for the JS browser client SDK. It consumes the same
/// data/error envelope as the Web UI and never mutates plugin lifecycle state.
/// </summary>
public sealed class ConsoleClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public ConsoleClient(string baseAddress, HttpClient? httpClient = null)
        : this(new Uri(baseAddress, UriKind.Absolute), httpClient)
    {
    }

    public ConsoleClient(Uri baseAddress, HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = baseAddress;
    }

    public async Task<ConsoleMeta> GetMetaAsync(CancellationToken cancellationToken = default) =>
        await GetDataAsync<ConsoleMeta>("/api/meta", cancellationToken);

    public async Task<IReadOnlyList<ConsolePage>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync("/api/ui/pages", cancellationToken);
        return envelope["data"]?["pages"]?.Deserialize<IReadOnlyList<ConsolePage>>(Json) ?? [];
    }

    public async Task<IReadOnlyList<ConsolePanel>> GetPanelsAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync("/api/ui/panels", cancellationToken);
        return envelope["data"]?["panels"]?.Deserialize<IReadOnlyList<ConsolePanel>>(Json) ?? [];
    }

    public async Task<ConsoleClientModuleManifest> GetClientModulesAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync("/api/ui/client-modules", cancellationToken);
        var data = envelope["data"];
        return new ConsoleClientModuleManifest(
            data?["revision"]?.GetValue<long>() ?? 0,
            data?["modules"]?.Deserialize<IReadOnlyList<ConsoleClientModule>>(Json) ?? []);
    }

    public Task<JsonNode> QueryAsync(string name, CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync($"/api/query/{name}", cancellationToken);

    public async Task<bool> ExecuteAsync(
        string name, JsonNode? payload = null, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"/api/command/{name}", payload ?? new JsonObject(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("data", out _);
    }

    public async IAsyncEnumerable<string> StreamLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            "/api/stream", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }

    private async Task<T> GetDataAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var envelope = await GetEnvelopeAsync(path, cancellationToken);
        var value = envelope["data"]?.Deserialize<T>(Json);
        if (value is null)
        {
            throw new InvalidDataException($"response from '{path}' has no data envelope");
        }

        return value;
    }

    private async Task<JsonNode> GetEnvelopeAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(text) ?? throw new InvalidDataException($"empty response from '{path}'");
        if (!response.IsSuccessStatusCode || node["code"] is not null)
        {
            var code = node["code"]?.GetValue<string>() ?? ((int)response.StatusCode).ToString();
            var message = node["message"]?.GetValue<string>() ?? response.ReasonPhrase ?? "request failed";
            throw new InvalidOperationException($"{code}: {message}");
        }

        return node;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
