using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gocordis.Client;

public sealed record ConsoleMeta(string FrameworkVersion, string Kind);
public sealed record ConsolePage(string Id, string Title, string Route, string Renderer);
public sealed record ConsolePanel(string Id, string Title, string Position, string Renderer);
public sealed record ConsoleClientModule(string Name, string Url);
public sealed record ConsoleClientModuleManifest(long Revision, IReadOnlyList<ConsoleClientModule> Modules);

// ---- typed control contracts (same wire shapes as WebUiServer) -------------

public sealed record ConsolePluginEntry(
    string Id, string Name, string Type, bool Enabled, string State, string? Error,
    string Version, string? InstalledVersion, bool Protected, bool Controllable, bool Declared);

public sealed record ConsoleStoreEntry(
    string Id, string Version, string DisplayName, string HostVersionRange,
    string InstallPath, string Checksum, bool Declared, bool Running);

public sealed record ConsoleUploadResult(
    string UploadKey, string Id, string Version, string Type, string HostVersionRange, string Checksum);

public sealed record ConsoleChange(string Kind, string Id, string Version);

public sealed record ConsoleMutationResult(
    bool Accepted, string Code, string? Error, string Id, string? State,
    bool Persisted, IReadOnlyList<ConsoleChange> Changes);

public sealed record ConsoleAuditEntry(
    long Revision, string Timestamp, string Source, string Action,
    string Id, string Outcome, string Code, string? Error);

public sealed record ConsoleUiRoute(string Id, string Path, string PageId, string? Layout, int Order, string? OwnerPluginId);
public sealed record ConsoleUiNavigation(string Id, string Label, string Route, string? Group, string? Icon, int Order, string? OwnerPluginId);
public sealed record ConsoleUiExtension(string Id, string Slot, string Renderer, string? PageId, int Order, string? OwnerPluginId);
public sealed record ConsoleUiResource(string Id, string Kind, string Path, string? MediaType, string? OwnerPluginId);
public sealed record ConsoleUiContributions(
    IReadOnlyList<ConsoleUiRoute> Routes,
    IReadOnlyList<ConsoleUiNavigation> Navigation,
    IReadOnlyList<ConsoleUiExtension> Extensions,
    IReadOnlyList<ConsoleUiResource> Resources);

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

    // ---- IPluginControlPlane over HTTP -------------------------------------
    // Every method below targets the single control plane exposed by the host
    // transport. The CLI uses these instead of touching store/manifest files.

    public async Task<IReadOnlyList<ConsolePluginEntry>> GetPluginsAsync(
        CancellationToken cancellationToken = default) =>
        await GetDataAsync<List<ConsolePluginEntry>>("/api/plugins", cancellationToken);

    public async Task<IReadOnlyList<ConsoleStoreEntry>> GetStoreAsync(
        CancellationToken cancellationToken = default) =>
        await GetDataAsync<List<ConsoleStoreEntry>>("/api/plugins/store", cancellationToken);

    public async Task<ConsoleUploadResult> UploadPackageAsync(
        Stream packageStream, string fileName, string source = "cli",
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(packageStream);
        form.Add(file, "package", fileName);
        form.Add(new StringContent(source), "source");
        using var response = await _http.PostAsync("/api/plugins/packages", form, cancellationToken);
        var data = await ReadDataNodeAsync(response, cancellationToken);
        return data.Deserialize<ConsoleUploadResult>(Json)
               ?? throw new InvalidDataException("upload response has no data envelope");
    }

    public Task<ConsoleMutationResult> InstallPluginAsync(
        string uploadKey, bool enable = true, string source = "cli",
        CancellationToken cancellationToken = default) =>
        PostMutationAsync("/api/plugins/install", new JsonObject
        {
            ["uploadKey"] = uploadKey,
            ["enable"] = enable,
            ["source"] = source,
        }, cancellationToken);

    public Task<ConsoleMutationResult> UpgradePluginAsync(
        string id, string uploadKey, string? versionRange = null, string source = "cli",
        CancellationToken cancellationToken = default) =>
        PostMutationAsync("/api/plugins/upgrade", new JsonObject
        {
            ["id"] = id,
            ["uploadKey"] = uploadKey,
            ["versionRange"] = versionRange,
            ["source"] = source,
        }, cancellationToken);

    public Task<ConsoleMutationResult> UninstallPluginAsync(
        string id, string? version = null, string source = "cli",
        CancellationToken cancellationToken = default) =>
        PostMutationAsync("/api/plugins/uninstall", new JsonObject
        {
            ["id"] = id,
            ["version"] = version,
            ["source"] = source,
        }, cancellationToken);

    public Task<ConsoleMutationResult> SetPluginEnabledAsync(
        string id, bool enabled, string source = "cli",
        CancellationToken cancellationToken = default) =>
        PostMutationAsync("/api/plugins/control", new JsonObject
        {
            ["pluginId"] = id,
            ["enable"] = enabled,
            ["source"] = source,
        }, cancellationToken);

    public Task<ConsoleMutationResult> RecoverHostAsync(
        string source = "cli", CancellationToken cancellationToken = default) =>
        PostMutationAsync("/api/host/recover", new JsonObject { ["source"] = source }, cancellationToken);

    public async Task<IReadOnlyList<ConsoleAuditEntry>> GetAuditAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync("/api/plugins/audit", cancellationToken);
        return envelope["data"]?["audit"]?.Deserialize<List<ConsoleAuditEntry>>(Json) ?? [];
    }

    public async Task<ConsoleUiContributions> GetUiContributionsAsync(
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync("/api/ui/contributions", cancellationToken);
        var data = envelope["data"] ?? new JsonObject();
        return new ConsoleUiContributions(
            data["routes"]?.Deserialize<List<ConsoleUiRoute>>(Json) ?? [],
            data["navigation"]?.Deserialize<List<ConsoleUiNavigation>>(Json) ?? [],
            data["extensions"]?.Deserialize<List<ConsoleUiExtension>>(Json) ?? [],
            data["resources"]?.Deserialize<List<ConsoleUiResource>>(Json) ?? []);
    }

    private async Task<ConsoleMutationResult> PostMutationAsync(
        string path, JsonObject payload, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, cancellationToken);
        var data = await ReadDataNodeAsync(response, cancellationToken);
        return data.Deserialize<ConsoleMutationResult>(Json)
               ?? throw new InvalidDataException($"mutation response from '{path}' has no data envelope");
    }

    private static async Task<JsonNode> ReadDataNodeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(text) ?? throw new InvalidDataException("empty mutation response");
        return node["data"]?.DeepClone() ?? new JsonObject();
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
