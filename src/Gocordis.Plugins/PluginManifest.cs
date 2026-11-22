using Tomlyn;
using Tomlyn.Model;

namespace Gocordis.Plugins;

/// <summary>
/// Persistent declaration. Field names mirror the Go manifest: id/type/enabled/config,
/// plus descriptive requires/provides/version/source/checksum used by discovery.
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
            var id = ReadString(component, "id") ??
                     throw new ManifestParseException("manifest component is missing 'id'");
            var type = ReadString(component, "type") ?? id;
            var enabled = ReadBool(component, "enabled", id);
            var config = component.TryGetValue("config", out var configValue) && configValue is TomlTable table
                ? table.ToDictionary(pair => pair.Key, pair => ConvertValue(pair.Value))
                : new Dictionary<string, object?>();
            var requires = ReadStringArray(component, "requires");
            var provides = ReadStringArray(component, "provides");

            result.Add(new PluginManifest(
                id,
                type,
                enabled,
                config,
                requires,
                provides,
                ReadString(component, "version") ?? "1.0.0",
                ReadString(component, "source"),
                ReadString(component, "checksum")));
        }

        return result;
    }

    private static string? ReadString(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string text ? text : null;

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

    private static IReadOnlyList<string> ReadStringArray(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string text => [text],
            TomlArray array => array.Select(ConvertValue).Select(item => item?.ToString() ?? string.Empty).ToArray(),
            _ => [],
        };
    }

    private static object? ConvertValue(object? value) => value switch
    {
        TomlArray array => array.Select(ConvertValue).ToArray(),
        TomlTable table => table.ToDictionary(pair => pair.Key, pair => ConvertValue(pair.Value)),
        DateTimeOffset date => date.UtcDateTime,
        _ => value,
    };
}
