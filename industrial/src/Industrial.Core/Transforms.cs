using System.Globalization;
using System.Text.Json;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Schema-driven field mapping. The mapping dictionary is
/// targetField -> sourceField; the technical source identity is untouched.</summary>
public sealed class MappingTransform
{
    private readonly IReadOnlyDictionary<string, string> _mapping;

    public MappingTransform(IReadOnlyDictionary<string, object?> config)
    {
        if (config.TryGetValue("mapping", out var value) && value is JsonElement element)
        {
            _mapping = element.Deserialize<Dictionary<string, string>>() ?? [];
        }
        else if (value is IReadOnlyDictionary<string, string> direct)
        {
            _mapping = direct;
        }
        else
        {
            _mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    public RecordEnvelope Apply(RecordEnvelope record)
    {
        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (target, source) in _mapping)
        {
            record.Values.TryGetValue(source, out var value);
            mapped[target] = value;
        }

        mapped["record_id"] = record.Values.GetValueOrDefault("record_id");
        mapped["record_time"] = record.Values.GetValueOrDefault("record_time");
        return record with { Values = mapped };
    }
}

/// <summary>Required-field and type validation. Invalid records are dropped
/// with a reason; the caller can surface the count through execution status.</summary>
public sealed class ValidationTransform
{
    private readonly string[] _required;
    private readonly IReadOnlyDictionary<string, string> _types;

    public ValidationTransform(IReadOnlyDictionary<string, object?> config)
    {
        _required = config.TryGetValue("required", out var value) && value is JsonElement array
            ? array.Deserialize<string[]>() ?? []
            : [];
        _types = config.TryGetValue("types", out var types) && types is JsonElement typeElement
            ? typeElement.Deserialize<Dictionary<string, string>>() ?? []
            : [];
    }

    public bool TryApply(RecordEnvelope record, out RecordEnvelope? validated, out string? reason)
    {
        validated = null;
        reason = null;
        foreach (var field in _required)
        {
            if (!record.Values.TryGetValue(field, out var value) || value is null ||
                (value is string text && string.IsNullOrWhiteSpace(text)))
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
