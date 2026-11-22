using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Raised when a declaration config does not satisfy its registered schema.</summary>
public sealed class ConfigSchemaException : ArgumentException
{
    public ConfigSchemaException(string message) : base(message) { }
}

/// <summary>
/// Synchronous JSON-Schema subset for a plugin type's config map. This is a
/// declarative annotation: validation runs before a Component is constructed,
/// so an invalid declaration is rejected in the registry's Prepare phase with
/// no fiber, effect or entry side effect. No async work and no external package.
/// </summary>
public sealed class ConfigSchema
{
    public string Type { get; init; } = "object";

    public IReadOnlyDictionary<string, ConfigSchema> Properties { get; init; } =
        new Dictionary<string, ConfigSchema>(StringComparer.Ordinal);

    public IReadOnlyList<string> Required { get; init; } = [];

    public ConfigSchema? Items { get; init; }

    public IReadOnlyList<object?>? Enum { get; init; }

    public object? Default { get; init; }

    public void Validate(object? value, string path = "$")
    {
        CheckType(value, path);

        if (Type == "object" && value is IReadOnlyDictionary<string, object?> map)
        {
            foreach (var name in Required)
            {
                if (!map.ContainsKey(name))
                {
                    throw new ConfigSchemaException($"{path}.{name} is required");
                }
            }

            foreach (var (name, child) in Properties)
            {
                if (map.TryGetValue(name, out var childValue))
                {
                    child.Validate(childValue, $"{path}.{name}");
                }
            }
        }
        else if (Type == "array" && value is IEnumerable sequence)
        {
            if (Items is not null)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Items.Validate(item, $"{path}[{index}]");
                    index++;
                }
            }
        }

        if (Enum is not null && !Enum.Any(candidate => Equals(candidate, value)))
        {
            throw new ConfigSchemaException($"{path} must be one of the declared enum values");
        }
    }

    /// <summary>Self-contained draft 2020-12 document for console/help consumers.</summary>
    public string ToJsonSchema()
    {
        var document = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = Type,
        };
        if (Properties.Count > 0)
        {
            document["properties"] = Properties.ToDictionary(
                pair => pair.Key, pair => ToObject(pair.Value), StringComparer.Ordinal);
        }

        if (Required.Count > 0)
        {
            document["required"] = Required.ToArray();
        }

        if (Items is not null)
        {
            document["items"] = ToObject(Items);
        }

        if (Enum is not null)
        {
            document["enum"] = Enum.ToArray();
        }

        if (Default is not null)
        {
            document["default"] = Default;
        }

        return JsonSerializer.Serialize(document);
    }

    private static Dictionary<string, object?> ToObject(ConfigSchema schema)
    {
        var document = new Dictionary<string, object?> { ["type"] = schema.Type };
        if (schema.Properties.Count > 0)
        {
            document["properties"] = schema.Properties.ToDictionary(
                pair => pair.Key, pair => ToObject(pair.Value), StringComparer.Ordinal);
        }

        if (schema.Required.Count > 0)
        {
            document["required"] = schema.Required.ToArray();
        }

        if (schema.Items is not null)
        {
            document["items"] = ToObject(schema.Items);
        }

        return document;
    }

    private void CheckType(object? value, string path)
    {
        var matches = Type switch
        {
            "object" => value is IReadOnlyDictionary<string, object?>,
            "string" => value is string,
            "integer" => value is int or long or short or byte,
            "number" => value is int or long or short or byte or float or double or decimal,
            "boolean" => value is bool,
            "array" => value is IEnumerable and not string,
            "null" => value is null,
            _ => throw new ConfigSchemaException($"unsupported schema type '{Type}' at {path}"),
        };
        if (!matches)
        {
            throw new ConfigSchemaException($"{path} must be {Type}");
        }
    }
}

/// <summary>
/// Attaches a synchronous schema to a registry factory. The wrapper runs inside
/// PluginRegistry.Prepare, before any fiber is loaded, so rejecting a config
/// leaves the registry snapshot byte-for-byte unchanged.
/// </summary>
public static class PluginRegistrySchemaExtensions
{
    private static readonly ConditionalWeakTable<PluginRegistry, RegistrySchemas> Tables = new();

    public static void RegisterValidatedFactory(
        this PluginRegistry registry,
        string type,
        ConfigSchema schema,
        Func<IReadOnlyDictionary<string, object?>, Component> factory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(factory);

        var table = Tables.GetValue(registry, static _ => new RegistrySchemas());
        table.Add(type, schema);
        registry.RegisterFactory(type, config =>
        {
            schema.Validate(config);
            return factory(config);
        });
    }

    /// <summary>Explicit sync validation for hosts that stage declarations themselves.</summary>
    public static void ValidateConfigs(
        this PluginRegistry registry,
        IEnumerable<PluginManifest> declarations)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(declarations);
        if (!Tables.TryGetValue(registry, out var table))
        {
            return;
        }

        foreach (var manifest in declarations)
        {
            if (table.TryGet(manifest.Type, out var schema) && schema is not null)
            {
                schema.Validate(manifest.Config, $"$.{manifest.Id}");
            }
        }
    }

    private sealed class RegistrySchemas
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, ConfigSchema> _schemas = new(StringComparer.Ordinal);

        public void Add(string type, ConfigSchema schema)
        {
            lock (_sync)
            {
                _schemas[type] = schema;
            }
        }

        public bool TryGet(string type, out ConfigSchema? schema)
        {
            lock (_sync)
            {
                return _schemas.TryGetValue(type, out schema);
            }
        }
    }
}
