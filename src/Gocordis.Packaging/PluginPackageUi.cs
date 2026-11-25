using System.Text.Json;
using System.Text.Json.Nodes;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

/// <summary>
/// UI resource declarations shared by the package writer/reader and the host
/// binder. A package may declare entries inline (manifest.ui) or ship the
/// plugin-kit's <c>gocordis-ui.json</c> payload file. Both forms are validated
/// against the payload file list and covered by the package integrity checksum
/// (inline entries via the ui= checksum lines, a payload declaration via its
/// file= checksum line). The store materializes one derived declaration file in
/// the install directory so activation needs no package archive and no host
/// special-casing per plugin.
/// </summary>
internal static class PluginPackageUi
{
    internal const string DeclarationEntryName = "gocordis-ui.json";
    internal const string InstalledDeclarationName = ".gocordis-ui.json";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string CanonicalJson(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + CanonicalJson(pair.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(CanonicalJson)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    /// <summary>Validates declared UI entries at write and at read time.</summary>
    internal static void Validate(
        PluginPackageManifest manifest,
        IReadOnlyCollection<string> payloadPaths,
        Func<string, byte[]>? readFile = null)
    {
        var files = new HashSet<string>(payloadPaths, StringComparer.Ordinal);
        var resources = new List<PackageUiResource>(manifest.UiOrEmpty);
        if (files.Contains(DeclarationEntryName) && readFile is not null)
        {
            resources.AddRange(ParseDeclaration(readFile(DeclarationEntryName)));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var kind = NormalizeKind(resource.Kind);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource.Id);
            PluginPackageWriter.EnsureSafeSegment(resource.Id, "ui resource id");
            PluginPackageWriter.EnsureSafeRelativePath(resource.Path);
            if (!files.Contains(resource.Path))
            {
                throw new InvalidPluginPackageException(
                    $"UI resource '{kind}:{resource.Id}' path '{resource.Path}' is not in the package payload");
            }

            if (!ids.Add($"{kind}:{resource.Id}"))
            {
                throw new InvalidPluginPackageException($"duplicate UI resource '{kind}:{resource.Id}'");
            }

            if (!paths.Add(resource.Path))
            {
                throw new InvalidPluginPackageException($"duplicate UI resource path '{resource.Path}'");
            }

            if (kind is "page" or "panel")
            {
                var definition = resource.Definition;
                if (definition is null && readFile is not null)
                {
                    definition = ParseDefinition(readFile(resource.Path));
                }

                if (definition is not JsonObject obj)
                {
                    // Without file bytes (writer pre-scan) the payload bytes are
                    // checked later by the checksum; only inline data is checked.
                    if (resource.Definition is not null || readFile is not null)
                    {
                        throw new InvalidPluginPackageException(
                            $"UI resource '{kind}:{resource.Id}' definition must be a JSON object");
                    }

                    continue;
                }

                if (obj["id"] is JsonValue idValue &&
                    idValue.TryGetValue<string>(out var declaredId) &&
                    !string.Equals(declaredId, resource.Id, StringComparison.Ordinal))
                {
                    throw new InvalidPluginPackageException(
                        $"UI resource '{kind}:{resource.Id}' definition declares id '{declaredId}'");
                }

                RequireText(obj, "title", kind, resource.Id);
                RequireText(obj, kind == "page" ? "route" : "position", kind, resource.Id);
                RequireText(obj, "renderer", kind, resource.Id);
            }
            else if (kind == "client-module")
            {
                var extension = Path.GetExtension(resource.Path);
                if (!string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".mjs", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginPackageException(
                        $"client module '{resource.Id}' entry '{resource.Path}' is not an ESM file");
                }
            }
            else
            {
                var definition = resource.Definition;
                if (definition is null && readFile is not null)
                {
                    definition = ParseDefinition(readFile(resource.Path));
                }

                if (definition is not JsonObject obj)
                {
                    if (resource.Definition is not null || readFile is not null)
                    {
                        throw new InvalidPluginPackageException(
                            $"UI resource '{kind}:{resource.Id}' definition must be a JSON object");
                    }

                    continue;
                }

                if (obj["id"] is JsonValue idValue &&
                    idValue.TryGetValue<string>(out var declaredId) &&
                    !string.Equals(declaredId, resource.Id, StringComparison.Ordinal))
                {
                    throw new InvalidPluginPackageException(
                        $"UI resource '{kind}:{resource.Id}' definition declares id '{declaredId}'");
                }

                var required = kind switch
                {
                    "route" => new[] { "path", "pageId" },
                    "navigation" => new[] { "label", "route" },
                    "extension" => new[] { "slot", "renderer" },
                    "resource" => new[] { "kind", "path" },
                    _ => Array.Empty<string>(),
                };
                foreach (var field in required)
                {
                    RequireText(obj, field, kind, resource.Id);
                }
            }
        }
    }

    internal static string NormalizeKind(string kind) => kind.ToLowerInvariant() switch
    {
        "page" => "page",
        "panel" => "panel",
        "client-module" => "client-module",
        "route" => "route",
        "navigation" => "navigation",
        "extension" => "extension",
        "resource" => "resource",
        _ => throw new InvalidPluginPackageException($"unsupported UI resource kind '{kind}'"),
    };

    private static void RequireText(JsonObject obj, string field, string kind, string id)
    {
        if (obj[field] is not JsonValue value || !value.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidPluginPackageException(
                $"UI resource '{kind}:{id}' definition is missing '{field}'");
        }
    }

    private static JsonNode? ParseDefinition(byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidPluginPackageException("UI resource definition is not valid JSON", ex);
        }
    }

    internal static IReadOnlyList<PackageUiResource> ParseDeclaration(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidPluginPackageException("UI declaration must contain a 'resources' array");
            }

            var result = new List<PackageUiResource>();
            foreach (var item in resources.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidPluginPackageException("UI declaration resource must be an object");
                }

                var definition = item.TryGetProperty("definition", out var inline) &&
                                 inline.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(inline.GetRawText())
                    : null;
                result.Add(new PackageUiResource(
                    ReadString(item, "kind"),
                    ReadString(item, "id"),
                    ReadString(item, "path"),
                    definition));
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidPluginPackageException("UI declaration is not valid JSON", ex);
        }
    }

    private static string ReadString(JsonElement obj, string field)
    {
        if (!obj.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidPluginPackageException($"UI declaration resource is missing '{field}'");
        }

        return value.GetString()!;
    }

    /// <summary>
    /// Materializes the validated declaration in the install directory so the
    /// host binds contributions from the Store path, never from a package file
    /// or AppContext.BaseDirectory.
    /// </summary>
    internal static void WriteInstalled(
        string installPath, PluginPackageManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var resources = new List<PackageUiResource>(manifest.UiOrEmpty);
        if (files.TryGetValue(DeclarationEntryName, out var declarationBytes))
        {
            resources.AddRange(ParseDeclaration(declarationBytes));
        }

        if (resources.Count == 0)
        {
            return;
        }

        var json = new JsonObject
        {
            ["resources"] = new JsonArray(resources.Select(resource =>
            {
                var item = new JsonObject
                {
                    ["kind"] = resource.Kind,
                    ["id"] = resource.Id,
                    ["path"] = resource.Path.Replace('\\', '/'),
                };
                if (resource.Definition is not null)
                {
                    item["definition"] = resource.Definition.DeepClone();
                }

                return (JsonNode?)item;
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(installPath, InstalledDeclarationName), json.ToJsonString(JsonOptions));
    }

    internal static IReadOnlyList<PackageUiResource> LoadInstalled(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return [];
        }

        var derived = Path.Combine(installPath, InstalledDeclarationName);
        var path = File.Exists(derived) ? derived : Path.Combine(installPath, DeclarationEntryName);
        if (!File.Exists(path))
        {
            return [];
        }

        var resources = ParseDeclaration(File.ReadAllBytes(path));
        return resources;
    }

    internal static JsonObject DefinitionOf(PackageUiResource resource, string installPath)
    {
        if (resource.Definition is JsonObject inline)
        {
            return (JsonObject)inline.DeepClone();
        }

        var full = SafePath(installPath, resource.Path);
        if (File.Exists(full))
        {
            try
            {
                return JsonNode.Parse(File.ReadAllBytes(full)) as JsonObject
                       ?? throw new InvalidPluginPackageException(
                           $"UI resource '{resource.Kind}:{resource.Id}' definition is not a JSON object");
            }
            catch (JsonException ex)
            {
                throw new InvalidPluginPackageException(
                    $"UI resource '{resource.Kind}:{resource.Id}' definition is not valid JSON", ex);
            }
        }

        throw new FileNotFoundException(
            $"UI resource '{resource.Kind}:{resource.Id}' definition is missing", full);
    }

    internal static string SafePath(string root, string relative)
    {
        PluginPackageWriter.EnsureSafeRelativePath(relative);
        var full = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        if (!full.StartsWith(normalizedRoot, PathComparison))
        {
            throw new InvalidPluginPackageException($"UI resource path '{relative}' escapes the install directory");
        }

        var link = File.ResolveLinkTarget(full, returnFinalTarget: true);
        if (link is not null)
        {
            var resolved = Path.GetFullPath(link.FullName);
            if (!resolved.StartsWith(normalizedRoot, PathComparison))
            {
                throw new InvalidPluginPackageException(
                    $"UI resource path '{relative}' links outside the install directory");
            }
        }

        return full;
    }
}
