using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                else if (child.Default is not null && map is IDictionary<string, object?> mutable)
                {
                    // Defaults are declarations too: materialize them for
                    // missing optional properties so the factory sees the same
                    // config the schema describes. Clone object defaults so
                    // nested filling never mutates the registered schema.
                    var applied = child.Default is IReadOnlyDictionary<string, object?> defaultMap
                        ? new Dictionary<string, object?>(defaultMap, StringComparer.Ordinal)
                        : child.Default;
                    mutable[name] = applied;
                    child.Validate(applied, $"{path}.{name}");
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

        if (Enum is not null && !Enum.Any(candidate => ValuesEqual(candidate, value)))
        {
            throw new ConfigSchemaException($"{path} must be one of the declared enum values");
        }
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (Equals(left, right))
        {
            return true;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return false;
        }

        // JSON/TOML readers may produce int, long or double for the same value;
        // compare numeric enums by value, not by CLR box type.
        try
        {
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture) ==
                   Convert.ToDecimal(right, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return Convert.ToDouble(left, CultureInfo.InvariantCulture) ==
                   Convert.ToDouble(right, CultureInfo.InvariantCulture);
        }
    }

    private static bool IsNumeric(object? value) => value is int or long or short or byte or sbyte
        or uint or ushort or ulong or float or double or decimal;

    /// <summary>Self-contained draft 2020-12 document for console/help consumers.</summary>
    public string ToJsonSchema()
    {
        var document = ToObject(this);
        document["$schema"] = "https://json-schema.org/draft/2020-12/schema";
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

        if (schema.Enum is not null)
        {
            document["enum"] = schema.Enum.ToArray();
        }

        if (schema.Default is not null)
        {
            document["default"] = schema.Default;
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

    /// <summary>
    /// Registers a JSON Schema DOCUMENT for a plugin type without a validated
    /// factory. Used by the host for package-declared schemas so the console's
    /// ConfigEditor can render typed fields for managed plugins whose factory
    /// is compiled and cannot carry an in-process ConfigSchema object.
    /// The schema travels WITH the plugin package, not in console code.
    /// </summary>
    public static void RegisterSchema(this PluginRegistry registry, string type, JsonNode schema)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(schema);
        var table = Tables.GetValue(registry, static _ => new RegistrySchemas());
        table.AddDocument(type, schema.DeepClone());
    }

    private sealed class RegistrySchemas
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, ConfigSchema> _schemas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonNode> _documents = new(StringComparer.Ordinal);

        public void Add(string type, ConfigSchema schema)
        {
            lock (_sync)
            {
                _schemas[type] = schema;
            }
        }

        public void AddDocument(string type, JsonNode document)
        {
            lock (_sync)
            {
                if (_documents.TryGetValue(type, out var existing) &&
                    !string.Equals(Canonical(existing), Canonical(document), StringComparison.Ordinal))
                {
                    throw new ConfigSchemaException(
                        $"plugin type '{type}' declares a conflicting config schema; " +
                        "schema upgrades require distinct plugin types");
                }

                _documents[type] = document;
            }
        }

        /// <summary>Order-independent projection so declaration order cannot
        /// turn an identical schema into a conflict.</summary>
        private static string Canonical(JsonNode? node) => node switch
        {
            null => "null",
            JsonObject obj => "{" + string.Join(",", obj
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Canonical(pair.Value))) + "}",
            JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
            _ => node.ToJsonString(),
        };

        public bool TryGet(string type, out ConfigSchema? schema)
        {
            lock (_sync)
            {
                return _schemas.TryGetValue(type, out schema);
            }
        }

        /// <summary>Stable snapshot of all type→JSON-Schema documents, ordered
        /// by type so the config/schema query is deterministic. A validated
        /// factory's ConfigSchema is rendered via ToJsonSchema(); a package-
        /// declared document is used as-is. Documents win on a type collision.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonNode>> Snapshot()
        {
            lock (_sync)
            {
                return _documents
                    .Select(pair => new KeyValuePair<string, JsonNode>(pair.Key, pair.Value.DeepClone()))
                    .Concat(_schemas.Where(pair => !_documents.ContainsKey(pair.Key))
                        .Select(pair => new KeyValuePair<string, JsonNode>(
                            pair.Key, JsonNode.Parse(pair.Value.ToJsonSchema())!)))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Read model for the console's <c>config/schema</c> query: the JSON Schema
    /// document registered for each plugin type. Returns an empty list when no
    /// schema is registered (the console then keeps its legacy value-typed form).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, JsonNode>> SchemasFor(this PluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return Tables.TryGetValue(registry, out var table)
            ? table.Snapshot()
            : Array.Empty<KeyValuePair<string, JsonNode>>();
    }
}
