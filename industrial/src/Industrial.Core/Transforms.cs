using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Schema-driven field mapping. The mapping dictionary is
/// targetField -> sourceField; the technical source identity is untouched.</summary>
public sealed class MappingTransform
{
    private readonly IReadOnlyDictionary<string, string> _mapping;
    private readonly bool _dropUnmapped;
    private readonly IReadOnlyList<MetadataRule> _metadataRules;

    public MappingTransform(IReadOnlyDictionary<string, object?> config)
    {
        var reader = new NodeConfigReader(config);
        _mapping = reader.GetStringMap("mapping");
        _dropUnmapped = reader.GetBool("dropUnmapped", true);
        _metadataRules = config.TryGetValue("metadataRules", out var rules) && rules is not null
            ? JsonSerializer.Deserialize<List<MetadataRule>>(JsonSerializer.Serialize(rules),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []
            : [];
    }

    public RecordEnvelope Apply(RecordEnvelope record)
    {
        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in _mapping)
        {
            record.Values.TryGetValue(source, out var value);
            mapped[target] = value;
        }

        foreach (var rule in _metadataRules)
        {
            var relative = record.Source.Key.Split('|').Skip(1).FirstOrDefault() ?? string.Empty;
            var text = string.Equals(rule.From, "path", StringComparison.OrdinalIgnoreCase)
                ? relative : Path.GetFileName(relative);
            var match = Regex.Match(text, rule.Pattern, RegexOptions.IgnoreCase);
            var value = match.Success
                ? (match.Groups["value"].Success ? match.Groups["value"].Value : match.Value)
                : null;
            if (value is not null || rule.Required) mapped[rule.Key] = value;
        }

        mapped["record_id"] = record.Values.GetValueOrDefault("record_id");
        mapped["record_time"] = record.Values.GetValueOrDefault("record_time");
        if (!_dropUnmapped)
        {
            foreach (var (key, value) in record.Values)
            {
                if (!mapped.ContainsKey(key)) mapped[key] = value;
            }
        }
        return record with { Values = mapped };
    }
}

/// <summary>Required-field and type validation. Invalid records are dropped
/// with a reason; the caller can surface the count through execution status.</summary>
public sealed class ValidationTransform
{
    private readonly string[] _required;
    private readonly IReadOnlyDictionary<string, string> _types;

    private readonly NodeConfigReader _config;
    private readonly string _onInvalid;

    public ValidationTransform(IReadOnlyDictionary<string, object?> config)
    {
        _config = new NodeConfigReader(config);
        _required = _config.GetStringList("required").ToArray();
        _types = _config.GetStringMap("types");
        _onInvalid = _config.GetString("onInvalid", "drop");
    }

    public string OnInvalid => _onInvalid;

    private static bool IsBlank(object value) => value switch
    {
        string text => string.IsNullOrWhiteSpace(text),
        // Stage spool round-trips turn plain strings into JsonElement values.
        JsonElement element => element.ValueKind == JsonValueKind.Null ||
                               element.ValueKind == JsonValueKind.Undefined ||
                               (element.ValueKind == JsonValueKind.String &&
                                string.IsNullOrWhiteSpace(element.GetString())),
        _ => false,
    };

    public bool TryApply(RecordEnvelope record, out RecordEnvelope? validated, out string? reason)
    {
        validated = null;
        reason = null;
        foreach (var field in _required)
        {
            if (!record.Values.TryGetValue(field, out var value) || value is null || IsBlank(value))
            {
                reason = $"missing required field '{field}'";
                return false;
            }
        }

        foreach (var (field, expected) in _types)
        {
            if (!record.Values.TryGetValue(field, out var value) || value is null)
            {
                continue;
            }

            var ok = expected switch
            {
                "int" or "integer" => long.TryParse(value.ToString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _),
                "decimal" or "double" => double.TryParse(value.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _),
                "datetime" => DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _),
                _ => true,
            };
            if (!ok)
            {
                reason = $"field '{field}' is not a valid {expected}";
                return false;
            }
        }

        validated = record;
        return true;
    }
}
