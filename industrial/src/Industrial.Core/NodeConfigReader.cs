using System.Text.Json;
using System.Text.Json.Nodes;

namespace Industrial.Core;

/// <summary>One reader for node configuration regardless of whether it came
/// from JSON wire payloads, a JsonObject envelope or an in-memory dictionary.
/// The designer and the runtime therefore see exactly the same values.</summary>
public sealed class NodeConfigReader
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public NodeConfigReader(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null) return;
        foreach (var (key, value) in config) _values[key] = Normalize(value);
    }

    public NodeConfigReader(JsonObject? config)
    {
        if (config is null) return;
        foreach (var (key, value) in config) _values[key] = Normalize(value?.ToJsonString());
    }

    public bool Contains(string key) => _values.ContainsKey(key);

    public string GetString(string key, string fallback = "") =>
        TryGet(key, out var value) && value is not null ? value.ToString() ?? fallback : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        TryGet(key, out var value) && value is not null && bool.TryParse(value.ToString(), out var parsed)
            ? parsed
            : fallback;

    public int GetInt(string key, int fallback = 0) =>
        TryGet(key, out var value) && value is not null && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : fallback;

    public IReadOnlyDictionary<string, string> GetStringMap(string key)
    {
        if (!TryGet(key, out var value)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value is Dictionary<string, string> direct)
        {
            foreach (var (k, v) in direct) result[k] = v;
            return result;
        }
        if (value is IReadOnlyDictionary<string, object?> objects)
        {
            foreach (var (k, v) in objects) result[k] = v?.ToString() ?? string.Empty;
            return result;
        }
        if (value is not JsonElement element) return result;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }
        else if (element.ValueKind == JsonValueKind.String &&
                 element.GetString() is { } json && json.TrimStart().StartsWith("{"))
        {
            using var parsed = JsonDocument.Parse(json);
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }
        return result;
    }

    public IReadOnlyList<string> GetStringList(string key)
    {
        if (!TryGet(key, out var value)) return [];
        if (value is IReadOnlyList<string> direct) return direct;
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray().Select(item => item.ToString()).ToArray();
            }
            if (element.ValueKind == JsonValueKind.String &&
                element.GetString() is { } json && json.TrimStart().StartsWith("["))
            {
                using var parsed = JsonDocument.Parse(json);
                return parsed.RootElement.EnumerateArray().Select(item => item.ToString()).ToArray();
            }
        }
        return [];
    }

    private bool TryGet(string key, out object? value) => _values.TryGetValue(key, out value);

    private static object? Normalize(object? value)
    {
        if (value is JsonElement or JsonObject or Dictionary<string, object?> or
            IReadOnlyDictionary<string, object?> or IReadOnlyList<string> or string or bool or int or long or double)
        {
            return value;
        }
        return value;
    }
}
