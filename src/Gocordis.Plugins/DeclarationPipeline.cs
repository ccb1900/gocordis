using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace Gocordis.Plugins;

public sealed record DeclarationPatch(string Action, string Id, PluginManifest? Manifest = null);

/// <summary>Layered composition and atomic row patches (Go bundle/patch/compose semantics).</summary>
public sealed class DeclarationComposer
{
    public IReadOnlyList<PluginManifest> Compose(params IEnumerable<PluginManifest>[] layers)
    {
        var merged = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            foreach (var manifest in layer)
            {
                merged[manifest.Id] = manifest;
            }
        }

        return merged.Values.OrderBy(manifest => manifest.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<PluginManifest> ApplyPatches(
        IEnumerable<PluginManifest> declarations,
        IEnumerable<DeclarationPatch> patches)
    {
        var result = declarations.ToDictionary(manifest => manifest.Id, StringComparer.Ordinal);
        foreach (var patch in patches)
        {
            switch (patch.Action)
            {
                case "add":
                    if (patch.Manifest is null || !result.TryAdd(patch.Manifest.Id, patch.Manifest))
                    {
                        throw new InvalidOperationException($"cannot add '{patch.Id}'");
                    }

                    break;
                case "replace":
                    if (patch.Manifest is null || !result.ContainsKey(patch.Id))
                    {
                        throw new InvalidOperationException($"cannot replace '{patch.Id}'");
                    }

                    result[patch.Id] = patch.Manifest;
                    break;
                case "remove":
                    if (!result.Remove(patch.Id))
                    {
                        throw new InvalidOperationException($"cannot remove '{patch.Id}'");
                    }

                    break;
                default:
                    throw new InvalidOperationException($"unknown patch action '{patch.Action}'");
            }
        }

        return result.Values.OrderBy(manifest => manifest.Id, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>
/// File-backed named preset library (Go extensions/bundle). A bundle document
/// maps a name to component rows; expansion emits the named presets in order,
/// with a later row replacing an earlier row of the same id in place. An
/// unknown name is rejected and reports what is actually registered, so a typo
/// can never silently drop a declaration.
/// </summary>
public sealed class BundleLibrary
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IReadOnlyList<PluginManifest>> _bundles =
        new(StringComparer.Ordinal);

    /// <summary>Registered preset names, sorted for diagnostics.</summary>
    public IReadOnlyList<string> Names()
    {
        lock (_sync)
        {
            return _bundles.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>Registers a preset; re-registering a name replaces its rows.</summary>
    public void Register(string name, IEnumerable<PluginManifest> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rows);
        var copy = rows.ToArray();
        lock (_sync)
        {
            _bundles[name] = copy;
        }
    }

    /// <summary>Loads a .toml or .json bundle document and registers its presets.</summary>
    public IReadOnlyList<string> LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var text = File.ReadAllText(full);
        var extension = Path.GetExtension(full);
        var documents = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonBundles(text)
            : extension.Equals(".toml", StringComparison.OrdinalIgnoreCase)
                ? ParseTomlBundles(text)
                : throw new ManifestParseException(
                    $"unsupported bundle file format '{extension}' (expected .toml or .json)");

        foreach (var (name, rows) in documents)
        {
            Register(name, rows);
        }

        return documents.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Expands presets in order; unknown names are an error.</summary>
    public IReadOnlyList<PluginManifest> Expand(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Dictionary<string, IReadOnlyList<PluginManifest>> snapshot;
        lock (_sync)
        {
            snapshot = new Dictionary<string, IReadOnlyList<PluginManifest>>(_bundles, StringComparer.Ordinal);
        }

        var missing = names.Where(name => !snapshot.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"unknown bundle(s) {string.Join(", ", missing)}; registered: " +
                string.Join(", ", snapshot.Keys.OrderBy(name => name, StringComparer.Ordinal)));
        }

        var rows = new List<PluginManifest>();
        foreach (var name in names)
        {
            foreach (var row in snapshot[name])
            {
                var index = rows.FindIndex(candidate => candidate.Id == row.Id);
                if (index >= 0)
                {
                    rows[index] = row;
                }
                else
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    private static Dictionary<string, IReadOnlyList<PluginManifest>> ParseTomlBundles(string text)
    {
        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(text)
                    ?? throw new ManifestParseException("bundle file is empty");
        }
        catch (TomlException ex)
        {
            throw new ManifestParseException("bundle file is not valid TOML", ex);
        }

        if (!model.TryGetValue("bundles", out var value) || value is not TomlTable bundles)
        {
            throw new ManifestParseException("bundle file must contain a 'bundles' table");
        }

        var result = new Dictionary<string, IReadOnlyList<PluginManifest>>(StringComparer.Ordinal);
        foreach (var (name, bundleValue) in bundles)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ManifestParseException("bundle name must not be empty");
            }

            if (bundleValue is not TomlTable bundleTable ||
                !bundleTable.TryGetValue("components", out var componentValue) ||
                componentValue is not TomlTableArray components)
            {
                throw new ManifestParseException($"bundle '{name}' must have a 'components' array of tables");
            }

            result[name] = components.Select(PluginManifest.FromTomlTable).ToArray();
        }

        return result;
    }

    private static Dictionary<string, IReadOnlyList<PluginManifest>> ParseJsonBundles(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("bundle file is not valid JSON", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("bundles", out var bundles) ||
                bundles.ValueKind != JsonValueKind.Object)
            {
                throw new ManifestParseException("bundle file must contain a 'bundles' object");
            }

            var result = new Dictionary<string, IReadOnlyList<PluginManifest>>(StringComparer.Ordinal);
            foreach (var property in bundles.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    throw new ManifestParseException("bundle name must not be empty");
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new ManifestParseException($"bundle '{property.Name}' must be an array of components");
                }

                result[property.Name] = property.Value.EnumerateArray()
                    .Select(PluginManifest.FromJsonElement).ToArray();
            }

            return result;
        }
    }
}

/// <summary>One ordered, file-loaded composition edit (remove/replace/insert).</summary>
public sealed record DeclarationFilePatch(string Op, string Id, PluginManifest? Component = null);

/// <summary>
/// File-backed patch loading with atomic in-memory application. Every patch is
/// validated and staged against a deep copy; the caller's declarations are
/// returned unchanged when any operation fails, so no half-applied state can
/// escape. The existing in-memory DeclarationComposer.ApplyPatches is kept.
/// </summary>
public static class PatchFile
{
    public const int Version = 1;

    public static IReadOnlyList<DeclarationFilePatch> Parse(string text, string? format = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var extension = format ?? ".json";
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ParseJson(text)
            : extension.Equals(".toml", StringComparison.OrdinalIgnoreCase)
                ? ParseToml(text)
                : throw new ManifestParseException(
                    $"unsupported patch file format '{format}' (expected .json or .toml)");
    }

    public static IReadOnlyList<DeclarationFilePatch> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return Parse(File.ReadAllText(full), Path.GetExtension(full));
    }

    public static IReadOnlyList<PluginManifest> Apply(
        IReadOnlyList<PluginManifest> baseRows,
        IReadOnlyList<DeclarationFilePatch> patches)
    {
        ArgumentNullException.ThrowIfNull(baseRows);
        ArgumentNullException.ThrowIfNull(patches);

        var staged = baseRows.Select(Clone).ToList();
        foreach (var patch in patches)
        {
            if (string.IsNullOrWhiteSpace(patch.Id))
            {
                throw new InvalidOperationException($"patch '{patch.Op}' is missing a component id");
            }

            switch (patch.Op)
            {
                case "remove":
                    staged.RemoveAll(row => row.Id == patch.Id);
                    break;
                case "replace":
                    if (patch.Component is null || patch.Component.Id != patch.Id)
                    {
                        throw new InvalidOperationException(
                            $"patch replace '{patch.Id}': component definition missing or has mismatched id");
                    }

                    var replaceIndex = staged.FindIndex(row => row.Id == patch.Id);
                    if (replaceIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"patch replace '{patch.Id}': component not in base composition");
                    }

                    staged[replaceIndex] = Clone(patch.Component);
                    break;
                case "insert":
                    if (patch.Component is null)
                    {
                        throw new InvalidOperationException(
                            $"patch insert '{patch.Id}': missing component definition");
                    }

                    if (staged.Any(row => row.Id == patch.Component.Id))
                    {
                        throw new InvalidOperationException(
                            $"patch insert '{patch.Id}': component already in composition");
                    }

                    staged.Add(Clone(patch.Component));
                    break;
                default:
                    throw new InvalidOperationException($"patch '{patch.Id}': unknown op '{patch.Op}'");
            }
        }

        return staged;
    }

    private static PluginManifest Clone(PluginManifest manifest) => manifest with
    {
        Config = new Dictionary<string, object?>(manifest.Config, StringComparer.Ordinal),
        Requires = manifest.Requires.ToArray(),
        Provides = manifest.Provides.ToArray(),
    };

    private static IReadOnlyList<DeclarationFilePatch> Validate(
        IEnumerable<DeclarationFilePatch> patches)
    {
        var result = patches.ToArray();
        foreach (var patch in result)
        {
            if (string.IsNullOrWhiteSpace(patch.Id))
            {
                throw new ManifestParseException($"patch '{patch.Op}' is missing a component id");
            }

            if (patch.Op is "replace" or "insert" &&
                (patch.Component is null || patch.Component.Id != patch.Id))
            {
                throw new ManifestParseException(
                    $"patch {patch.Op} '{patch.Id}': component definition missing or has mismatched id");
            }
        }

        return result;
    }

    private static IReadOnlyList<DeclarationFilePatch> ParseJson(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ManifestParseException("patch file is not valid JSON", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ManifestParseException("patch file must be a JSON object");
            }

            if (root.TryGetProperty("version", out var versionElement) &&
                (!versionElement.TryGetInt32(out var version) || version != Version))
            {
                throw new ManifestParseException(
                    $"patch file: unsupported format version (supported: {Version})");
            }

            var result = new List<DeclarationFilePatch>();
            if (!root.TryGetProperty("patches", out var patches))
            {
                // Legacy console overlay: modified rows first, then removed rows.
                if (root.TryGetProperty("modified", out var modified) &&
                    modified.ValueKind == JsonValueKind.Array)
                {
                    foreach (var component in modified.EnumerateArray())
                    {
                        var manifest = PluginManifest.FromJsonElement(component);
                        result.Add(new DeclarationFilePatch("replace", manifest.Id, manifest));
                    }
                }

                if (root.TryGetProperty("removed", out var removed) &&
                    removed.ValueKind == JsonValueKind.Array)
                {
                    foreach (var component in removed.EnumerateArray())
                    {
                        var manifest = PluginManifest.FromJsonElement(component);
                        result.Add(new DeclarationFilePatch("remove", manifest.Id, manifest));
                    }
                }

                return Validate(result);
            }

            if (patches.ValueKind != JsonValueKind.Array)
            {
                throw new ManifestParseException("patch file 'patches' must be an array");
            }

            foreach (var item in patches.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new ManifestParseException("patch entry must be an object");
                }

                var op = ReadJsonString(item, "op")
                         ?? throw new ManifestParseException("patch entry is missing 'op'");
                var id = ReadJsonString(item, "id")
                         ?? throw new ManifestParseException($"patch '{op}' is missing 'id'");
                PluginManifest? component = null;
                if (item.TryGetProperty("component", out var componentElement) &&
                    componentElement.ValueKind != JsonValueKind.Null)
                {
                    component = PluginManifest.FromJsonElement(componentElement);
                }

                result.Add(new DeclarationFilePatch(op, id, component));
            }

            return Validate(result);
        }
    }

    private static IReadOnlyList<DeclarationFilePatch> ParseToml(string text)
    {
        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(text)
                    ?? throw new ManifestParseException("patch file is empty");
        }
        catch (TomlException ex)
        {
            throw new ManifestParseException("patch file is not valid TOML", ex);
        }

        if (model.TryGetValue("version", out var versionValue) &&
            versionValue is long version && version != Version)
        {
            throw new ManifestParseException(
                $"patch file: unsupported format version {version} (supported: {Version})");
        }

        if (!model.TryGetValue("patches", out var value) || value is not TomlTableArray patches)
        {
            return [];
        }

        var result = new List<DeclarationFilePatch>();
        foreach (var patch in patches)
        {
            if (!patch.TryGetValue("op", out var opValue) || opValue is not string op)
            {
                throw new ManifestParseException("patch entry has non-string 'op'");
            }

            if (!patch.TryGetValue("id", out var idValue) || idValue is not string id)
            {
                throw new ManifestParseException($"patch '{op}' has non-string 'id'");
            }

            PluginManifest? component = null;
            if (patch.TryGetValue("component", out var componentValue))
            {
                if (componentValue is not TomlTable componentTable)
                {
                    throw new ManifestParseException($"patch '{op}' '{id}' has invalid 'component'");
                }

                component = PluginManifest.FromTomlTable(componentTable);
            }

            result.Add(new DeclarationFilePatch(op, id, component));
        }

        return Validate(result);
    }

    private static string? ReadJsonString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new ManifestParseException($"patch file has non-string '{key}'");
    }
}

/// <summary>Wraps one whole parse → reconcile transaction (Go WithTxGuard).</summary>
public delegate Task TxGuard(Func<Task> operation);

/// <summary>Watches a declaration file and coalesces rapid revisions into one reconcile.</summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly string _path;
    private readonly Func<IReadOnlyList<PluginManifest>, Task> _reconcile;
    private readonly TxGuard? _txGuard;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private long _revision;
    private Exception? _lastError;
    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Last reconcile error observed by the watcher, if any.</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    public ConfigWatcher(
        string path,
        Func<IReadOnlyList<PluginManifest>, Task> reconcile,
        TxGuard? txGuard = null)
    {
        _path = Path.GetFullPath(path);
        _reconcile = reconcile;
        _txGuard = txGuard;
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _debounce = new Timer(_ => _ = FireAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public long Revision => Interlocked.Read(ref _revision);

    public async Task SyncAsync()
    {
        await _reconcileGate.WaitAsync();
        try
        {
            if (IsDisposed)
            {
                return;
            }

            await ReconcileCoreAsync();
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    /// <summary>
    /// Runs one guarded reconcile. Revision advances only when the whole
    /// transaction (guard + parse + reconcile) completes successfully.
    /// </summary>
    public async Task<bool> ReconcileOnceAsync()
    {
        await _reconcileGate.WaitAsync();
        try
        {
            if (IsDisposed || !await ReconcileCoreAsync())
            {
                return false;
            }

            Interlocked.Increment(ref _revision);
            return true;
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    /// <summary>Returns true when declarations were successfully reconciled.</summary>
    private async Task<bool> ReconcileCoreAsync()
    {
        var completed = false;
        try
        {
            if (_txGuard is null)
            {
                completed = await ReconcileUnlockedAsync();
            }
            else
            {
                await _txGuard(async () => { completed = await ReconcileUnlockedAsync(); });
            }

            return completed;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex);
            return false;
        }
    }

    private async Task<bool> ReconcileUnlockedAsync()
    {
        var manifests = File.Exists(_path)
            ? PluginManifest.ParseToml(await File.ReadAllTextAsync(_path))
            : [];
        await _reconcile(manifests);
        return true;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            _debounce.Change(TimeSpan.FromMilliseconds(80), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Dispose raced with a queued file event.
        }
    }

    private async Task FireAsync()
    {
        await ReconcileOnceAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
        // The gate deliberately stays alive: a queued FireAsync may still be
        // waiting on it and must observe _disposed rather than fault.
    }
}
