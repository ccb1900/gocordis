using System.Text.Json;
using System.Text.Json.Serialization;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

/// <summary>Paper §5.2.1 Entry: stable id, module URL, isolation/interception
/// annotations, config and administrative disabled state. Group and Include
/// are ordinary configuration nodes; they are expanded into child entries by
/// the loader and never become a second Runtime.</summary>
public sealed record EntryNode(
    string Id,
    string? Url = null,
    bool Disabled = false,
    IReadOnlyDictionary<string, object?>? Config = null,
    string? Isolate = null,
    string? Intercept = null,
    IReadOnlyList<EntryNode>? Children = null,
    string? Include = null);

public sealed record ApplicationPlugin(
    string Id,
    string VersionRange,
    bool Enabled,
    IReadOnlyDictionary<string, object?>? Config = null,
    // Declaration constraint vs. lock result vs. package entity are distinct:
    // VersionRange says what is allowed, LockedVersion what actually runs,
    // PackageHash which bytes were resolved.
    string? LockedVersion = null,
    string? PackageHash = null,
    string? Source = null,
    // Paper Entry annotations carried into PluginManifest.
    string? Url = null,
    string? Isolate = null,
    string? Intercept = null,
    string? ParentId = null,
    string? PackageId = null);

public sealed record ApplicationManifest(
    string Id,
    string Version,
    string HostVersion,
    IReadOnlyList<ApplicationPlugin> Plugins,
    IReadOnlyList<EntryNode>? Entries = null)
{
    /// <summary>Path the manifest was loaded from, used to resolve Include files.</summary>
    [JsonIgnore]
    public string? SourcePath { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<ApplicationManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ApplicationManifest>(bytes, JsonOptions)
               ?? throw new InvalidDataException($"application manifest '{path}' is empty");
        return manifest with { SourcePath = Path.GetFullPath(path) };
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>An Include file is (transitively) including itself.</summary>
public sealed class IncludeCycleException : InvalidOperationException
{
    public IncludeCycleException(IReadOnlyList<string> cycle)
        : base($"application Include cycle: {string.Join(" -> ", cycle)}")
    {
        Cycle = cycle;
    }

    /// <summary>Normalized full paths from the repeated edge back to itself.</summary>
    public IReadOnlyList<string> Cycle { get; }
}

/// <summary>Include nesting exceeded the loader's safety limit.</summary>
public sealed class IncludeDepthLimitException : InvalidOperationException
{
    public IncludeDepthLimitException(int maxDepth, string path)
        : base($"application Include depth exceeded {maxDepth} while loading '{path}'")
    {
        MaxDepth = maxDepth;
        Path = path;
    }

    public int MaxDepth { get; }

    public string Path { get; }
}

/// <summary>
/// Turns an application declaration into PluginRegistry declarations. It does
/// not own fibers and never writes runtime state directly.
/// </summary>
public sealed class ApplicationLoader
{
    /// <summary>Maximum nested Include depth; deeper trees fail explicitly
    /// instead of recursing until the process stack overflows.</summary>
    public const int MaxIncludeDepth = 32;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly PluginStore _store;
    private readonly PluginRegistry _registry;

    /// <summary>
    /// The capability declaration plan is part of the SAME transaction as the
    /// package/registry apply: the plan runs before the registry sees any
    /// component, and a failed apply restores the previous plan so a rolled
    /// back install cannot leave declarations behind.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<IDeclarationTransaction>>? DeclarationPlan { get; init; }

    public ApplicationLoader(PluginStore store, PluginRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    private static readonly JsonSerializerOptions IncludeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Expands the paper §5.2.1 configuration tree into the flat declaration
    /// set Core consumes. Group nodes are structural (children carry the
    /// parent id); Include nodes graft an external JSON entry list. No component
    /// is instantiated here and no Runtime command is issued.
    ///
    /// Include paths are resolved against <see cref="ApplicationManifest.SourcePath"/>
    /// and normalized with <see cref="Path.GetFullPath(string)"/> before use. A
    /// path already on the current recursion stack is a cycle and fails with
    /// <see cref="IncludeCycleException"/>; nesting past
    /// <see cref="MaxIncludeDepth"/> fails with <see cref="IncludeDepthLimitException"/>.
    /// A repeated Include outside the current stack is ignored: the first
    /// expansion owns its declarations and parent linkage. Explicit flat
    /// declarations still override entry-derived ones for the same id.
    /// </summary>
    public static async Task<IReadOnlyList<ApplicationPlugin>> EffectivePluginsAsync(
        ApplicationManifest application, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ApplicationPlugin>(StringComparer.Ordinal);
        var includeStack = new List<string>();
        var expandedIncludes = new HashSet<string>(PathComparer);
        var basePath = Path.GetDirectoryName(application.SourcePath ?? string.Empty) ?? string.Empty;

        async Task AddNode(EntryNode node, string? parentId)
        {
            if (node.Include is { } include)
            {
                var includePath = Path.GetFullPath(Path.IsPathRooted(include)
                    ? include
                    : Path.Combine(basePath, include));
                var stackIndex = includeStack.FindIndex(path => PathComparer.Equals(path, includePath));
                if (stackIndex >= 0)
                {
                    // Report the concrete cycle chain, not a generic recursion
                    // failure; callers can fix the offending manifest edge.
                    var cycle = includeStack.Skip(stackIndex).Append(includePath).ToArray();
                    throw new IncludeCycleException(cycle);
                }

                if (expandedIncludes.Add(includePath))
                {
                    includeStack.Add(includePath);
                    try
                    {
                        if (includeStack.Count > MaxIncludeDepth)
                        {
                            throw new IncludeDepthLimitException(MaxIncludeDepth, includePath);
                        }

                        var json = await File.ReadAllTextAsync(includePath, cancellationToken);
                        var included = JsonSerializer.Deserialize<List<EntryNode>>(json, IncludeOptions) ?? [];
                        foreach (var child in included)
                        {
                            await AddNode(child, parentId);
                        }
                    }
                    finally
                    {
                        includeStack.RemoveAt(includeStack.Count - 1);
                    }
                }
                // Repeated include (already expanded on another branch): skip
                // it; declaration priority follows the first occurrence.
            }

            foreach (var child in node.Children ?? [])
            {
                await AddNode(child, node.Id);
            }

            if (node.Url is not null || node.Config is not null || node.Isolate is not null ||
                node.Intercept is not null || node.Disabled)
            {
                var (packageId, range) = ParseUrl(node.Url ?? node.Id);
                result[node.Id] = new ApplicationPlugin(
                    node.Id, range, !node.Disabled, node.Config,
                    Url: node.Url, Isolate: node.Isolate, Intercept: node.Intercept,
                    ParentId: parentId, PackageId: packageId);
            }
        }

        foreach (var entry in application.Entries ?? [])
        {
            await AddNode(entry, null);
        }

        // Explicit flat declarations remain authoritative for the same id.
        foreach (var plugin in application.Plugins)
        {
            result[plugin.Id] = plugin;
        }

        return [.. result.Values];
    }

    private static (string Id, string Range) ParseUrl(string url)
    {
        var separator = url.IndexOf('@');
        return separator < 0
            ? (url, "[1.0,)")
            : (url[..separator], url[(separator + 1)..]);
    }

    public async Task ApplyAsync(ApplicationManifest application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!string.Equals(application.HostVersion, _store.HostVersion, StringComparison.Ordinal))
        {
            throw new IncompatiblePluginException(
                $"application host version '{application.HostVersion}' does not match store host '{_store.HostVersion}'");
        }

        var declarations = new List<PluginManifest>(application.Plugins.Count);
        foreach (var plugin in await EffectivePluginsAsync(application, cancellationToken))
        {
            var selection = plugin.LockedVersion ?? plugin.VersionRange;
            var packageId = plugin.PackageId ?? plugin.Id;
            var installed = await _store.ResolveAsync(packageId, selection, cancellationToken)
                            ?? throw new MissingPluginDependencyException(
                                $"application '{application.Id}' declares '{plugin.Id}' {selection}, which is not installed");
            if (plugin.PackageHash is { } expectedHash &&
                !string.Equals(expectedHash, installed.Manifest.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new ChecksumMismatchException(
                    $"application '{application.Id}' locked '{plugin.Id}' to package hash " +
                    $"'{expectedHash}' but store resolved '{installed.Manifest.Checksum}'");
            }

            declarations.Add(new PluginManifest(
                plugin.Id,
                installed.Manifest.Type,
                plugin.Enabled,
                plugin.Config ?? new Dictionary<string, object?>(),
                installed.Manifest.DependenciesOrEmpty.Select(d => d.Id).ToArray(),
                installed.Manifest.ProvidesOrEmpty,
                installed.Version,
                installed.InstallPath,
                installed.Manifest.Checksum,
                installed.Manifest.Backend,
                installed.Manifest.EntryAssembly,
                installed.Manifest.EntryType,
                installed.Manifest.SharedAssemblies,
                installed.Manifest.Capabilities,
                plugin.Url, plugin.Isolate, plugin.Intercept, plugin.ParentId));
        }

        var planWire = declarations
            .SelectMany(manifest => manifest.CapabilitiesOrEmpty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IDeclarationTransaction? transaction = null;
        try
        {
            // The plan itself is inside the try: a rejected candidate must be
            // rolled back by its own transaction, not left half-applied.
            if (DeclarationPlan is { } plan)
            {
                transaction = await plan(planWire);
            }

            await _registry.ApplyAsync(declarations);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }
}
