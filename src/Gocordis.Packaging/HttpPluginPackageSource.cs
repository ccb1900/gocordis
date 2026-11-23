using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gocordis.Extensions;
using NuGet.Versioning;

namespace Gocordis.Packaging;

/// <summary>
/// Read-only plugin package source backed by a static HTTP registry index.
/// The index is a JSON array of {id, version, url, sha256} objects; resolution
/// picks the highest version that satisfies the requested range, downloads it
/// to a temporary file and validates the bytes before returning the path.
/// No package code is executed and no runtime/store state is written.
/// </summary>
public sealed class HttpPluginPackageSource : IPluginPackageSource
{
    private static readonly HttpClient SharedClient = new();
    private static readonly JsonSerializerOptions IndexJson = new() { PropertyNameCaseInsensitive = true };

    private readonly Uri _baseUri;
    private readonly string _baseUrl;
    private readonly HttpClient _client;
    private readonly HttpService? _httpService;

    public HttpPluginPackageSource(string baseUrl, HttpClient? client = null, HttpService? httpService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var normalized = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) ||
            !(string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"invalid registry base url '{baseUrl}' (want an absolute http/https url)", nameof(baseUrl));
        }

        if (client is not null && httpService is not null)
        {
            throw new ArgumentException(
                "specify either an HttpClient or an HttpService, not both", nameof(httpService));
        }

        _baseUri = parsed;
        _baseUrl = normalized;
        _client = client ?? SharedClient;
        _httpService = httpService;
    }

    /// <summary>
    /// Routes registry requests through a caller-owned HttpService, so its
    /// reversible interceptor chain (including SOCKS transport links) applies to
    /// remote package resolution. The service stays owned by the caller.
    /// </summary>
    public HttpPluginPackageSource(string baseUrl, HttpService httpService)
        : this(baseUrl, client: null, httpService: httpService) { }

    public async Task<string> ResolveAsync(
        string id, string versionRange, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var range = ParseRange(versionRange);
        var entries = await LoadIndexAsync(cancellationToken);

        var matches = new List<(NuGetVersion Version, RegistryEntry Entry)>();
        foreach (var entry in entries)
        {
            if (!string.Equals(entry.Id, id, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(entry.Url) ||
                !NuGetVersion.TryParse(entry.Version, out var version) ||
                !range.Satisfies(version))
            {
                continue;
            }

            matches.Add((version, entry));
        }

        if (matches.Count == 0)
        {
            throw new MissingPluginDependencyException($"no remote package matches '{id}' {versionRange}");
        }

        var chosen = matches.OrderByDescending(match => match.Version).First();
        Uri uri;
        try
        {
            uri = ResolvePackageUri(chosen.Entry.Url);
        }
        catch (InvalidPluginPackageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            throw new InvalidPluginPackageException($"invalid package url '{chosen.Entry.Url}'", ex);
        }

        string? temp = null;
        try
        {
            temp = await DownloadAsync(uri, chosen.Entry.Sha256, cancellationToken);
            // IPluginPackageSource carries no hostVersion: structural and payload
            // integrity are checked here, host compatibility is left to
            // `plugin doctor` / install (which read the same file with a real host).
            _ = await PackageSourceReader.ReadIgnoringHostAsync(temp, cancellationToken);
            return temp;
        }
        catch (InvalidPluginPackageException)
        {
            DeleteQuietly(temp);
            throw;
        }
        catch (Exception ex)
        {
            DeleteQuietly(temp);
            throw new InvalidPluginPackageException(
                $"cannot resolve remote package '{id}' {versionRange}: {ex.Message}", ex);
        }
    }

    private static VersionRange ParseRange(string versionRange)
    {
        try
        {
            return VersionRange.Parse(versionRange);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidPluginPackageException($"invalid version range '{versionRange}'", ex);
        }
    }

    private async Task<RegistryEntry[]> LoadIndexAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            if (_httpService is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/index.json");
                using var response = await _httpService.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidPluginPackageException(
                        $"registry index request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                json = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            else
            {
                using var response = await _client.GetAsync(
                    _baseUrl + "/index.json", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidPluginPackageException(
                        $"registry index request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                json = await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (InvalidPluginPackageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw new InvalidPluginPackageException($"cannot read registry index '{_baseUrl}/index.json': {ex.Message}", ex);
        }

        try
        {
            return JsonSerializer.Deserialize<RegistryEntry[]>(json, IndexJson) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidPluginPackageException("registry index is not a valid JSON array", ex);
        }
    }

    private Uri ResolvePackageUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        // A root-relative path can parse as an absolute file: URI on some platforms.
        // Treat it as relative to the registry base before trying absolute parsing.
        if (!value.StartsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            if (!string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginPackageException(
                    $"registry entry has a non-http package url '{value}'");
            }

            return absolute;
        }

        var decoded = Uri.UnescapeDataString(value);
        if (value.StartsWith("//", StringComparison.Ordinal) ||
            decoded.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new InvalidPluginPackageException($"unsafe relative package url '{value}'");
        }

        // A root-relative entry is relative to the base host when the registry
        // is mounted at the root, and to the base path when it is mounted below.
        var relative = value.StartsWith('/') && _baseUri.AbsolutePath.TrimEnd('/').Length > 0
            ? value.TrimStart('/')
            : value;
        if (!Uri.TryCreate(_baseUri, relative, out var combined) ||
            !(string.Equals(combined.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(combined.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidPluginPackageException($"invalid relative package url '{value}'");
        }

        return combined;
    }

    private async Task<string> DownloadAsync(Uri uri, string? sha256, CancellationToken cancellationToken)
    {
        // Guid + GetTempPath keeps the write inside the private temp area; the
        // file is removed on every failure path, so no half package is returned.
        var temp = Path.Combine(Path.GetTempPath(), $"gocordis-remote-{Guid.NewGuid():N}.gocordis-plugin");
        try
        {
            if (_httpService is not null)
            {
                // Stream bodies stay service-owned until closed: the await-using
                // releases the lease on success and on every failure path.
                var streamResponse = await _httpService.GetStreamAsync(uri.AbsoluteUri, cancellationToken);
                await using (streamResponse)
                {
                    if (streamResponse.StatusCode is < 200 or >= 300)
                    {
                        throw new InvalidPluginPackageException(
                            $"package download failed: {streamResponse.StatusCode}");
                    }

                    await WriteAndVerifyAsync(streamResponse.Body, temp, sha256, cancellationToken);
                }
            }
            else
            {
                using var response = await _client.GetAsync(
                    uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidPluginPackageException(
                        $"package download failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                if (response.Content.Headers.ContentLength > PluginPackageLimits.MaxPackageBytes)
                {
                    throw new InvalidPluginPackageException("remote package exceeds the size limit");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await WriteAndVerifyAsync(source, temp, sha256, cancellationToken);
            }

            return temp;
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    private static async Task WriteAndVerifyAsync(
        Stream source, string temp, string? sha256, CancellationToken cancellationToken)
    {
        await using var target = new FileStream(
            temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > PluginPackageLimits.MaxPackageBytes)
            {
                throw new InvalidPluginPackageException("remote package exceeds the size limit");
            }

            hasher.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            {
                throw new PackageIntegrityException("registry returned an invalid sha256 value");
            }

            var actual = Convert.ToHexString(hasher.GetHashAndReset());
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageIntegrityException(
                    $"sha256 mismatch for remote package: expected {sha256}, got {actual.ToLowerInvariant()}");
            }
        }
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal sealed record RegistryEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("sha256")] string? Sha256);
}

/// <summary>
/// Shared package validation for read-only sources. IPluginPackageSource has no
/// hostVersion argument, so we derive a probe version inside the package's own
/// declared host range: PluginPackageReader still performs identity, payload,
/// file-hash and manifest-checksum verification, while real host compatibility
/// remains a doctor/install concern. A package with an unsatisfiable host range
/// is rejected here as invalid rather than silently accepted.
/// </summary>
internal static class PackageSourceReader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<PluginPackageContent> ReadIgnoringHostAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var hostVersion = await ProbeHostVersionAsync(path, cancellationToken);
        return await PluginPackageReader.ReadAsync(path, hostVersion, cancellationToken);
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<string> ProbeHostVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry("manifest.json");
            if (entry is null || entry.Length > PluginPackageLimits.MaxManifestBytes)
            {
                return "0.0.0"; // let the authoritative reader report the real problem
            }

            using var reader = new StreamReader(entry.Open());
            var json = await reader.ReadToEndAsync(cancellationToken);
            var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json, JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.HostVersionRange))
            {
                return "0.0.0";
            }

            var range = VersionRange.Parse(manifest.HostVersionRange);
            var candidates = new List<NuGetVersion>();
            if (range.MinVersion is { } min)
            {
                if (range.Satisfies(min))
                {
                    candidates.Add(min);
                }

                if (NuGetVersion.TryParse(min.ToNormalizedString() + ".1", out var bumped))
                {
                    candidates.Add(bumped);
                }
            }

            candidates.Add(NuGetVersion.Parse("0.0.0"));
            candidates.Add(NuGetVersion.Parse("1.0.0"));
            candidates.Add(NuGetVersion.Parse("9999.0.0"));
            foreach (var candidate in candidates)
            {
                if (range.Satisfies(candidate))
                {
                    return candidate.ToNormalizedString();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or
                                      ArgumentException or FormatException or InvalidPluginPackageException)
        {
            // The authoritative read below produces the precise error message.
        }

        return "0.0.0";
    }
}
