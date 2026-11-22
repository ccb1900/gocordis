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

/// <summary>Watches a declaration file and coalesces rapid revisions into one reconcile.</summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly string _path;
    private readonly Func<IReadOnlyList<PluginManifest>, Task> _reconcile;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private long _revision;
    private volatile bool _disposed;

    /// <summary>Last reconcile error observed by the watcher, if any.</summary>
    public Exception? LastError { get; private set; }

    public ConfigWatcher(string path, Func<IReadOnlyList<PluginManifest>, Task> reconcile)
    {
        _path = Path.GetFullPath(path);
        _reconcile = reconcile;
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
            await SyncCoreAsync();
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task SyncCoreAsync()
    {
        var text = File.Exists(_path) ? await File.ReadAllTextAsync(_path) : string.Empty;
        await _reconcile(PluginManifest.ParseToml(text));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_disposed)
        {
            _debounce.Change(TimeSpan.FromMilliseconds(80), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FireAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _reconcileGate.WaitAsync();
        try
        {
            await SyncCoreAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
        }
        finally
        {
            try
            {
                _reconcileGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Dispose raced with a queued file event.
            }

            Interlocked.Increment(ref _revision);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounce.Dispose();
        _reconcileGate.Dispose();
    }
}
