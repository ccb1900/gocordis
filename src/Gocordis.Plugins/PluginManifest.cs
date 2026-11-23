using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Versioning;
using Tomlyn;
using Tomlyn.Model;

namespace Gocordis.Plugins;

/// <summary>Rejected when a declared checksum does not match the artifact content.</summary>
public sealed class ChecksumMismatchException : InvalidOperationException
{
    public ChecksumMismatchException(string message) : base(message) { }
}

/// <summary>Content checksum verification for a plugin declaration and its artifact.</summary>
public static class PluginManifestChecksum
{
    public static string Compute(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(SHA256.HashData(content));
    }

    public static string Compute(string text) => Compute(Encoding.UTF8.GetBytes(text));

    public static string ComputeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Compute(File.ReadAllBytes(path));
    }

    public static bool Matches(PluginManifest manifest, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Checksum is null)
        {
            return false;
        }

        var expected = manifest.Checksum.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? manifest.Checksum[7..]
            : manifest.Checksum;
        return string.Equals(expected, Compute(content), StringComparison.OrdinalIgnoreCase);
    }

    public static void Verify(PluginManifest manifest, byte[] content)
    {
        if (!Matches(manifest, content))
        {
            throw new ChecksumMismatchException($"checksum mismatch for plugin '{manifest.Id}'");
        }
    }

    public static void VerifyFile(PluginManifest manifest, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new ChecksumMismatchException(
                $"checksum artifact for plugin '{manifest.Id}' not found: '{path}'");
        }

        if (!Matches(manifest, File.ReadAllBytes(path)))
        {
            throw new ChecksumMismatchException(
                $"checksum mismatch for plugin '{manifest.Id}' at '{path}'");
        }
    }
}

/// <summary>
/// Persistent declaration. Field names mirror the Go manifest: id/type/enabled/config,
/// plus descriptive requires/provides/version/source/checksum used by discovery.
/// All fields are validated here, before a Component is constructed, so every
/// backend consumes the same declaration contract through PluginRegistry.
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Type,
    bool Enabled,
    IReadOnlyDictionary<string, object?> Config,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    string Version = "1.0.0",
    string? Source = null,
    string? Checksum = null,
    string? Backend = null,
    string? EntryAssembly = null,
    string? EntryType = null)
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "id", "type", "enabled", "config", "requires", "provides", "version", "source", "checksum",
        "backend", "entry_assembly", "entry_type",
    };

    public static IReadOnlyList<PluginManifest> ParseToml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(text)
                    ?? throw new ManifestParseException("manifest is empty");
        }
        catch (TomlException ex)
        {
            throw new ManifestParseException("manifest is not valid TOML", ex);
        }

        if (!model.TryGetValue("components", out var value))
        {
            return [];
        }

        if (value is not TomlTableArray components)
        {
            throw new ManifestParseException("manifest 'components' must be an array of tables");
        }

        var result = new List<PluginManifest>();
        foreach (var component in components)
        {
            result.Add(FromTomlTable(component));
        }

        return result;
    }

    /// <summary>Parses the same declaration shape from a JSON document.</summary>
    public static IReadOnlyList<PluginManifest> ParseJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("manifest is not valid JSON", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("components", out var components))
            {
                return [];
            }

            if (components.ValueKind != JsonValueKind.Array)
            {
                throw new ManifestParseException("manifest 'components' must be an array");
            }

            var result = new List<PluginManifest>();
            foreach (var component in components.EnumerateArray())
            {
                result.Add(FromJsonElement(component));
            }

            return result;
        }
    }

    internal static PluginManifest FromTomlTable(TomlTable component)
    {
        var id = ReadString(component, "id", "<unknown>")
                 ?? throw new ManifestParseException("manifest component is missing 'id'");
        RejectUnknownFields(component, id);

        var type = ReadString(component, "type", id) ?? id;
        var enabled = ReadBool(component, "enabled", id);
        var config = ReadConfig(component, id);
        var requires = ReadStringArray(component, "requires", id);
        var provides = ReadStringArray(component, "provides", id);
        var version = ReadVersion(component, id);
        var source = ReadString(component, "source", id);
        var checksum = ReadString(component, "checksum", id);
        var backend = ReadString(component, "backend", id);
        var entryAssembly = ReadString(component, "entry_assembly", id);
        var entryType = ReadString(component, "entry_type", id);

        return new PluginManifest(
            id, type, enabled, config, requires, provides,
            version, source, checksum, backend, entryAssembly, entryType);
    }

    internal static PluginManifest FromJsonElement(JsonElement component)
    {
        if (component.ValueKind != JsonValueKind.Object)
        {
            throw new ManifestParseException("manifest component must be a JSON object");
        }

        foreach (var field in component.EnumerateObject())
        {
            if (!KnownFields.Contains(field.Name))
            {
                throw new ManifestParseException(
                    $"manifest component has unknown field '{field.Name}'");
            }
        }

        var id = ReadJsonString(component, "id")
                 ?? throw new ManifestParseException("manifest component is missing 'id'");
        var type = ReadJsonString(component, "type", id) ?? id;
        var enabled = ReadJsonBool(component, "enabled", id);
        var config = ReadJsonConfig(component, id);
        var requires = ReadJsonStringArray(component, "requires", id);
        var provides = ReadJsonStringArray(component, "provides", id);
        var version = ReadJsonVersion(component, id);
        var source = ReadJsonString(component, "source", id);
        var checksum = ReadJsonString(component, "checksum", id);
        var backend = ReadJsonString(component, "backend", id);
        var entryAssembly = ReadJsonString(component, "entry_assembly", id);
        var entryType = ReadJsonString(component, "entry_type", id);

        return new PluginManifest(
            id, type, enabled, config, requires, provides,
            version, source, checksum, backend, entryAssembly, entryType);
    }

    private static void RejectUnknownFields(TomlTable component, string id)
    {
        foreach (var field in component.Keys)
        {
            if (!KnownFields.Contains(field))
            {
                throw new ManifestParseException(
                    $"manifest component '{id}' has unknown field '{field}'");
            }
        }
    }

    private static string? ReadString(TomlTable table, string key, string id)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        return value is string text
            ? text
            : throw new ManifestParseException($"manifest component '{id}' has non-string '{key}'");
    }

    private static bool ReadBool(TomlTable table, string key, string id)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return false;
        }

        return value is bool flag
            ? flag
            : throw new ManifestParseException($"manifest component '{id}' has non-boolean '{key}'");
    }

    private static IReadOnlyDictionary<string, object?> ReadConfig(TomlTable component, string id)
    {
        if (!component.TryGetValue("config", out var value))
        {
            return new Dictionary<string, object?>();
        }

        if (value is not TomlTable table)
        {
            throw new ManifestParseException($"manifest component '{id}' has non-table 'config'");
        }

        return table.ToDictionary(pair => pair.Key, pair => ConvertValue(pair.Value));
    }

    private static IReadOnlyList<string> ReadStringArray(TomlTable table, string key, string id)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string text => [text],
            TomlArray array => array.Select(item => item switch
            {
                string text => text,
                _ => throw new ManifestParseException(
                    $"manifest component '{id}' has non-string element in '{key}'"),
            }).ToArray(),
            _ => throw new ManifestParseException(
                $"manifest component '{id}' has invalid type for '{key}'"),
        };
    }

    private static string ReadVersion(TomlTable component, string id)
    {
        var text = ReadString(component, "version", id) ?? "1.0.0";
        if (!NuGetVersion.TryParse(text, out var version))
        {
            throw new ManifestParseException(
                $"manifest component '{id}' has invalid version '{text}'");
        }

        return version.ToNormalizedString();
    }

    private static object? ConvertValue(object? value) => value switch
    {
        TomlArray array => array.Select(ConvertValue).ToArray(),
        TomlTable table => table.ToDictionary(pair => pair.Key, pair => ConvertValue(pair.Value)),
        DateTimeOffset date => date.UtcDateTime,
        _ => value,
    };

    private static string? ReadJsonString(JsonElement element, string key, string id = "<unknown>")
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new ManifestParseException($"manifest component '{id}' has non-string '{key}'");
    }

    private static bool ReadJsonBool(JsonElement element, string key, string id)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ManifestParseException($"manifest component '{id}' has non-boolean '{key}'"),
        };
    }

    private static IReadOnlyDictionary<string, object?> ReadJsonConfig(JsonElement element, string id)
    {
        if (!element.TryGetProperty("config", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, object?>();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ManifestParseException($"manifest component '{id}' has non-table 'config'");
        }

        return value.EnumerateObject()
            .ToDictionary(property => property.Name, property => ConvertJsonValue(property.Value),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ReadJsonStringArray(JsonElement element, string key, string id)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()!],
            JsonValueKind.Array => value.EnumerateArray().Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString()!,
                _ => throw new ManifestParseException(
                    $"manifest component '{id}' has non-string element in '{key}'"),
            }).ToArray(),
            _ => throw new ManifestParseException(
                $"manifest component '{id}' has invalid type for '{key}'"),
        };
    }

    private static string ReadJsonVersion(JsonElement element, string id)
    {
        var text = ReadJsonString(element, "version", id) ?? "1.0.0";
        if (!NuGetVersion.TryParse(text, out var version))
        {
            throw new ManifestParseException(
                $"manifest component '{id}' has invalid version '{text}'");
        }

        return version.ToNormalizedString();
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
        JsonValueKind.Object => value.EnumerateObject()
            .ToDictionary(property => property.Name, property => ConvertJsonValue(property.Value),
                StringComparer.Ordinal),
        _ => throw new ManifestParseException($"unsupported JSON value kind '{value.ValueKind}'"),
    };
}
