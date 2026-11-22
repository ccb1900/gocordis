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
    private Exception? _lastError;
    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Last reconcile error observed by the watcher, if any.</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

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

    /// <summary>Returns true when declarations were successfully reconciled.</summary>
    private async Task<bool> ReconcileCoreAsync()
    {
        try
        {
            var manifests = File.Exists(_path)
                ? PluginManifest.ParseToml(await File.ReadAllTextAsync(_path))
                : [];
            await _reconcile(manifests);
            return true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastError, ex);
            return false;
        }
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
        if (IsDisposed)
        {
            return;
        }

        await _reconcileGate.WaitAsync();
        try
        {
            if (IsDisposed)
            {
                return;
            }

            if (await ReconcileCoreAsync())
            {
                Interlocked.Increment(ref _revision);
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
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
