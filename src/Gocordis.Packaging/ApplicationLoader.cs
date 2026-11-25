using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// <summary>Sidecar holding the most recent declaration set that was written
    /// successfully. It is declaration state only; no fiber/runtime state is
    /// ever persisted here.</summary>
    public const string LastKnownGoodSuffix = ".last-known-good.json";

    /// <summary>Path the manifest was loaded from, used to resolve Include files.</summary>
    [JsonIgnore]
    public string? SourcePath { get; init; }

    /// <summary>Set only when the primary manifest was corrupt and this instance
    /// was recovered from the last-known-good sidecar.</summary>
    [JsonIgnore]
    public string? RecoveredFrom { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<ApplicationManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            var manifest = Deserialize(bytes, path);
            return manifest with { SourcePath = Path.GetFullPath(path) };
        }
        catch (JsonException primary)
        {
            // A partially written or hand-corrupted primary file must not make
            // the declaration set unrecoverable. The sidecar is authoritative
            // only for JSON corruption; a missing file still reports FileNotFound.
            var backupPath = path + LastKnownGoodSuffix;
            if (!File.Exists(backupPath))
            {
                throw new InvalidDataException(
                    $"application manifest '{path}' is corrupt and no last-known-good backup exists at '{backupPath}'",
                    primary);
            }

            try
            {
                var recovered = Deserialize(
                    await File.ReadAllBytesAsync(backupPath, cancellationToken), backupPath);
                return recovered with
                {
                    SourcePath = Path.GetFullPath(path),
                    RecoveredFrom = Path.GetFullPath(backupPath),
                };
            }
            catch (JsonException backup)
            {
                throw new InvalidDataException(
                    $"application manifest '{path}' and its last-known-good backup '{backupPath}' are both corrupt",
                    new AggregateException(primary, backup));
            }
        }
    }

    /// <summary>Restores the last-known-good declaration set over a corrupt (or
    /// simply undesired) primary file. The sidecar is left untouched, so a
    /// failed apply can still be retried. This writes declarations only.</summary>
    public static async Task<ApplicationManifest> RestoreLastKnownGoodAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var backupPath = path + LastKnownGoodSuffix;
        var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        var manifest = Deserialize(bytes, backupPath);
        await AtomicWriteAsync(path + ".recover.tmp", path, bytes, cancellationToken);
        return manifest with { SourcePath = Path.GetFullPath(path) };
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        try
        {
            // Backup the CURRENT declaration before replacing it. A corrupt
            // primary file is never promoted to last-known-good; recovery must
            // still find the previous valid declaration set. The first write
            // seeds the sidecar with the declaration being persisted.
            if (!File.Exists(path))
            {
                await AtomicWriteAsync(path + ".backup.tmp", path + LastKnownGoodSuffix, bytes, cancellationToken);
            }
            else
            {
                var current = await File.ReadAllBytesAsync(path, cancellationToken);
                if (CanDeserialize(current))
                {
                    await AtomicWriteAsync(path + ".backup.tmp", path + LastKnownGoodSuffix, current, cancellationToken);
                }
            }
        }
        catch (JsonException)
        {
            // The primary is corrupt: keep the older sidecar. The subsequent
            // atomic write of the new declaration is still valid.
        }

        await AtomicWriteAsync(path + ".tmp", path, bytes, cancellationToken);
    }

    private static ApplicationManifest Deserialize(byte[] bytes, string path) =>
        JsonSerializer.Deserialize<ApplicationManifest>(bytes, JsonOptions)
        ?? throw new InvalidDataException($"application manifest '{path}' is empty");

    private static bool CanDeserialize(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ApplicationManifest>(bytes, JsonOptions) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task AtomicWriteAsync(
        string temp, string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        File.Move(temp, destination, overwrite: true);
    }
}

/// <summary>Where an effective declaration was written. Flat Plugins are a
/// later authority over the Entry tree; Entry declarations name the file and
/// the index path of the node that produced them so a control mutation can edit
/// the same declaration instead of inventing a second override layer.</summary>
public enum DeclarationOrigin { FlatPlugin, Entry }

public sealed record DeclarationSource(
    DeclarationOrigin Origin,
    string FilePath,
    IReadOnlyList<int> EntryPath);

/// <summary>
/// Pure data: one declaration after Entry/Group/Include expansion, flat Plugins
/// override, and package resolution. This is the single shape the composition
/// root and the control plane read; it carries declaration AND package metadata
/// so neither side resolves a package id or schema differently.
/// </summary>
public sealed record EffectiveDeclaration(
    ApplicationPlugin Declaration,
    DeclarationSource Source,
    InstalledPlugin? Installed)
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyConfig =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<string> EmptyStrings = Array.Empty<string>();

    public string Id => Declaration.Id;

    /// <summary>Identical to ApplicationLoader's package resolution rule.</summary>
    public string PackageId => Declaration.PackageId ?? Declaration.Id;

    /// <summary>Identical to ApplicationLoader's version selection rule.</summary>
    public string VersionSelection => Declaration.LockedVersion ?? Declaration.VersionRange;

    public string VersionRange => Declaration.VersionRange;

    public bool Enabled => Declaration.Enabled;

    public IReadOnlyDictionary<string, object?> Config => Declaration.Config ?? EmptyConfig;

    public string? Url => Declaration.Url;

    public string? Isolate => Declaration.Isolate;

    public string? Intercept => Declaration.Intercept;

    public string? ParentId => Declaration.ParentId;

    public string? Type => Installed?.Manifest.Type;

    public string? InstalledVersion => Installed?.Version;

    public string? InstallPath => Installed?.InstallPath;

    public string? Checksum => Installed?.Manifest.Checksum;

    public string? DisplayName => Installed?.Manifest.DisplayName;

    public string? Backend => Installed?.Manifest.Backend;

    public string? EntryAssembly => Installed?.Manifest.EntryAssembly;

    public string? EntryType => Installed?.Manifest.EntryType;

    public IReadOnlyList<string> SharedAssemblies => Installed?.Manifest.SharedAssembliesOrEmpty ?? EmptyStrings;

    public IReadOnlyList<string> Capabilities => Installed?.Manifest.CapabilitiesOrEmpty ?? EmptyStrings;

    public IReadOnlyList<string> Provides => Installed?.Manifest.ProvidesOrEmpty ?? EmptyStrings;

    public IReadOnlyList<PluginDependency> Dependencies => Installed?.Manifest.DependenciesOrEmpty ?? [];

    public JsonNode? ConfigSchema => Installed?.Manifest.ConfigSchema;
}

/// <summary>The one resolved declaration set used by host pre-scan, loader and
/// control plane. Order follows the expansion (Entries first, Plugins override
/// in place), which is also the order the loader reconciles.</summary>
public sealed record ResolvedDeclarationPlan(IReadOnlyList<EffectiveDeclaration> Declarations)
{
    public int Count => Declarations.Count;

    public EffectiveDeclaration? Find(string id) =>
        Declarations.FirstOrDefault(declaration =>
            string.Equals(declaration.Id, id, StringComparison.Ordinal));

    public IReadOnlyList<string> DeclaredIds =>
        Declarations.Select(declaration => declaration.Id).ToArray();

    public IReadOnlyList<string> CapabilityWire =>
        Declarations.SelectMany(declaration => declaration.Capabilities)
            .Distinct(StringComparer.Ordinal).ToArray();
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
/// Two declaration nodes in one Include expansion tree carry the same Id.
/// Failing explicitly replaces a silent dictionary overwrite: a duplicated id
/// has two different sources/parents, and neither can be inferred as the winner.
/// Explicit flat Plugins are a separate, later authority and may override an
/// entry-derived id without triggering this error.
/// </summary>
public sealed class DuplicateEntryException : InvalidOperationException
{
    public DuplicateEntryException(string id, string path)
        : base($"duplicate entry id '{id}' in application Include tree at '{path}'")
    {
        Id = id;
        Path = path;
    }

    public string Id { get; }

    /// <summary>Normalized path of the file declaring the duplicate node.</summary>
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
    /// expansion owns its declarations and parent linkage. Two declaration
    /// nodes with the same Id anywhere in the expansion tree fail with
    /// <see cref="DuplicateEntryException"/> instead of overwriting each other.
    /// Explicit flat declarations still override entry-derived ones for the
    /// same id; they are a later, distinct authority.
    /// </summary>
    public static async Task<IReadOnlyList<ApplicationPlugin>> EffectivePluginsAsync(
        ApplicationManifest application, CancellationToken cancellationToken = default) =>
        (await ExpandAsync(application, cancellationToken)).Plugins;

    /// <summary>
    /// The single resolution entry point. Expansion and package resolution live
    /// here so the host pre-scan, the loader and the control plane can never
    /// disagree about package ids, versions, schemas or capabilities.
    /// </summary>
    public static async Task<ResolvedDeclarationPlan> ResolveAsync(
        ApplicationManifest application, PluginStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(store);
        var (plugins, sources) = await ExpandAsync(application, cancellationToken);
        var declarations = new List<EffectiveDeclaration>(plugins.Count);
        foreach (var plugin in plugins)
        {
            // Same id/range rule as ApplyAsync: an Entry's Url carries the
            // package id and range; a flat declaration may name a different one.
            var installed = await store.ResolveAsync(
                plugin.PackageId ?? plugin.Id,
                plugin.LockedVersion ?? plugin.VersionRange,
                cancellationToken);
            declarations.Add(new EffectiveDeclaration(plugin, sources[plugin.Id], installed));
        }

        return new ResolvedDeclarationPlan(declarations);
    }

    /// <summary>All files that participate in this declaration tree, including
    /// Include files. Used to snapshot declaration sources for rollback.</summary>
    public static async Task<IReadOnlyList<string>> DeclarationFilesAsync(
        ApplicationManifest application, CancellationToken cancellationToken = default)
    {
        var (_, sources) = await ExpandAsync(application, cancellationToken);
        return sources.Values.Select(source => source.FilePath)
            .Distinct(PathComparer).ToArray();
    }

    private static async Task<(IReadOnlyList<ApplicationPlugin> Plugins,
        IReadOnlyDictionary<string, DeclarationSource> Sources)> ExpandAsync(
        ApplicationManifest application, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ApplicationPlugin>(StringComparer.Ordinal);
        var sources = new Dictionary<string, DeclarationSource>(StringComparer.Ordinal);
        var includeStack = new List<string>();
        var expandedIncludes = new HashSet<string>(PathComparer);
        var basePath = Path.GetDirectoryName(application.SourcePath ?? string.Empty) ?? string.Empty;

        async Task AddNode(EntryNode node, string? parentId, string sourcePath, IReadOnlyList<int> path)
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
                        for (var index = 0; index < included.Count; index++)
                        {
                            await AddNode(included[index], parentId, includePath, [index]);
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

            var children = node.Children ?? [];
            for (var index = 0; index < children.Count; index++)
            {
                await AddNode(children[index], node.Id, sourcePath, [.. path, index]);
            }

            if (node.Url is not null || node.Config is not null || node.Isolate is not null ||
                node.Intercept is not null || node.Disabled)
            {
                if (result.ContainsKey(node.Id))
                {
                    // Structural group nodes do not declare a plugin, so only
                    // declaration-producing nodes can collide here. Report the
                    // offending node's source path and id rather than letting
                    // a later node silently replace an earlier declaration.
                    throw new DuplicateEntryException(node.Id, sourcePath);
                }

                var (packageId, range) = ParseUrl(node.Url ?? node.Id);
                result[node.Id] = new ApplicationPlugin(
                    node.Id, range, !node.Disabled, node.Config,
                    Url: node.Url, Isolate: node.Isolate, Intercept: node.Intercept,
                    ParentId: parentId, PackageId: packageId);
                sources[node.Id] = new DeclarationSource(
                    DeclarationOrigin.Entry, sourcePath, path);
            }
        }

        var manifestPath = application.SourcePath ?? string.Empty;
        var roots = application.Entries ?? [];
        for (var index = 0; index < roots.Count; index++)
        {
            await AddNode(roots[index], null, manifestPath, [index]);
        }

        // Explicit flat declarations remain authoritative for the same id.
        foreach (var plugin in application.Plugins)
        {
            result[plugin.Id] = plugin;
            sources[plugin.Id] = new DeclarationSource(DeclarationOrigin.FlatPlugin, manifestPath, []);
        }

        return ([.. result.Values], sources);
    }

    /// <summary>
    /// Writes a declaration mutation back to the authority that produced it.
    /// Flat declarations edit Plugins; entry declarations edit the producing
    /// EntryNode (in the manifest tree or in the Include file that grafted it).
    /// No manifest schema field and no new override protocol is introduced.
    /// </summary>
    public static async Task<ApplicationManifest> EditDeclarationAsync(
        ApplicationManifest application,
        EffectiveDeclaration declaration,
        Func<ApplicationPlugin, ApplicationPlugin> editFlat,
        Func<EntryNode, EntryNode> editEntry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(editFlat);
        ArgumentNullException.ThrowIfNull(editEntry);

        var source = declaration.Source;
        if (source.Origin == DeclarationOrigin.FlatPlugin)
        {
            var replaced = application.Plugins
                .Select(plugin => string.Equals(plugin.Id, declaration.Id, StringComparison.Ordinal)
                    ? editFlat(plugin)
                    : plugin)
                .ToArray();
            return application with { Plugins = replaced };
        }

        var manifestPath = application.SourcePath ?? string.Empty;
        if (PathComparer.Equals(Path.GetFullPath(source.FilePath), Path.GetFullPath(manifestPath)))
        {
            var entries = ReplaceAtPath(application.Entries ?? [], source.EntryPath, editEntry);
            return application with { Entries = entries };
        }

        var json = await File.ReadAllTextAsync(source.FilePath, cancellationToken);
        var included = JsonSerializer.Deserialize<List<EntryNode>>(json, IncludeOptions) ?? [];
        var updated = ReplaceAtPath(included, source.EntryPath, editEntry);
        await File.WriteAllTextAsync(
            source.FilePath, JsonSerializer.Serialize(updated, IncludeOptions), cancellationToken);
        return application;
    }

    private static IReadOnlyList<EntryNode> ReplaceAtPath(
        IReadOnlyList<EntryNode> nodes, IReadOnlyList<int> path, Func<EntryNode, EntryNode> edit)
    {
        if (path.Count == 0)
        {
            throw new InvalidOperationException("entry declaration has no source location");
        }

        var index = path[0];
        if (index < 0 || index >= nodes.Count)
        {
            throw new InvalidOperationException($"entry declaration source index '{index}' is stale");
        }

        var node = nodes[index];
        var replaced = path.Count == 1
            ? edit(node)
            : node with { Children = ReplaceAtPath(node.Children ?? [], path.Skip(1).ToArray(), edit) };
        var result = nodes.ToArray();
        result[index] = replaced;
        return result;
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

        var resolved = await ResolveAsync(application, _store, cancellationToken);
        var declarations = new List<PluginManifest>(resolved.Count);
        foreach (var effective in resolved.Declarations)
        {
            var plugin = effective.Declaration;
            var selection = effective.VersionSelection;
            var installed = effective.Installed
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

        var planWire = resolved.CapabilityWire;
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
