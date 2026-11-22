using NuGet.Versioning;
using Tomlyn;
using Tomlyn.Model;

namespace Gocordis.Plugins;

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
    string? Checksum = null)
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "id", "type", "enabled", "config", "requires", "provides", "version", "source", "checksum",
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
            var id = ReadString(component, "id", "<unknown>");
            if (id is null)
            {
                throw new ManifestParseException("manifest component is missing 'id'");
            }

            RejectUnknownFields(component, id);

            var type = ReadString(component, "type", id) ?? id;
            var enabled = ReadBool(component, "enabled", id);
            var config = ReadConfig(component, id);
            var requires = ReadStringArray(component, "requires", id);
            var provides = ReadStringArray(component, "provides", id);
            var version = ReadVersion(component, id);
            var source = ReadString(component, "source", id);
            var checksum = ReadString(component, "checksum", id);

            result.Add(new PluginManifest(
                id, type, enabled, config, requires, provides,
                version, source, checksum));
        }

        return result;
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
}
