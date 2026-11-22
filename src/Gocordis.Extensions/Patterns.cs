using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Gocordis.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gocordis.Extensions;

/// <summary>Multi-implementation routing with reversible registration (paper §6.2).</summary>
public sealed class Broker<T>
{
    private readonly List<T> _providers = [];
    private readonly object _sync = new();

    public IDisposable Register(T provider)
    {
        lock (_sync)
        {
            _providers.Add(provider);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _providers.Remove(provider);
            }
        });
    }

    public IReadOnlyList<T> Providers
    {
        get
        {
            lock (_sync)
            {
                return _providers.ToArray();
            }
        }
    }

    public T? RoundRobin(long ticket)
    {
        lock (_sync)
        {
            if (_providers.Count == 0)
            {
                return default;
            }

            var index = (int)((ticket % _providers.Count + _providers.Count) % _providers.Count);
            return _providers[index];
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>Stable membership with atomic replace and immutable snapshots.</summary>
public sealed class StableRegistry<T>
{
    private readonly Dictionary<string, T> _members = new(StringComparer.Ordinal);
    private readonly List<Action> _watchers = [];
    private readonly object _sync = new();

    public IReadOnlyDictionary<string, T> Snapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, T>(_members, StringComparer.Ordinal);
        }
    }

    public bool TryAdd(string id, T value)
    {
        lock (_sync)
        {
            if (!_members.TryAdd(id, value))
            {
                return false;
            }
        }

        Notify();
        return true;
    }

    public bool TryReplace(string id, T value)
    {
        lock (_sync)
        {
            if (!_members.ContainsKey(id))
            {
                return false;
            }

            _members[id] = value;
        }

        Notify();
        return true;
    }

    public bool TryRemove(string id)
    {
        lock (_sync)
        {
            if (!_members.Remove(id))
            {
                return false;
            }
        }

        Notify();
        return true;
    }

    public IDisposable Watch(Action onChange)
    {
        lock (_sync)
        {
            _watchers.Add(onChange);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _watchers.Remove(onChange);
            }
        });
    }

    private void Notify()
    {
        Action[] snapshot;
        lock (_sync)
        {
            snapshot = _watchers.ToArray();
        }

        foreach (var watcher in snapshot)
        {
            watcher();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

public sealed class ObserveStream<T> : IAsyncDisposable
{
    private readonly Channel<(long Sequence, T Item)> _channel =
        Channel.CreateBounded<(long, T)>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private long _sequence;

    public long Publish(T item)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        _channel.Writer.TryWrite((sequence, item));
        return sequence;
    }

    public IAsyncEnumerable<T> Subscribe(long fromSequence, CancellationToken token = default) =>
        Project(SubscribeWithSequence(fromSequence, token));

    /// <summary>
    /// Exposes the sequence number so a slow consumer can detect gaps caused by
    /// the bounded DropOldest policy instead of silently missing events.
    /// </summary>
    public IAsyncEnumerable<(long Sequence, T Item)> SubscribeWithSequence(
        long fromSequence,
        CancellationToken token = default) => ReadAll(fromSequence, token);

    private static async IAsyncEnumerable<T> Project(
        IAsyncEnumerable<(long Sequence, T Item)> source)
    {
        await foreach (var (_, item) in source)
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<(long Sequence, T Item)> ReadAll(long fromSequence,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var (sequence, item) in _channel.Reader.ReadAllAsync(token))
        {
            if (sequence >= fromSequence)
            {
                yield return (sequence, item);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await ValueTask.CompletedTask;
    }
}

/// <summary>File watch with revision coalescing, built on FileSystemWatcher.</summary>
public sealed class WatchService : IAsyncDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _revisions = new(StringComparer.Ordinal);

    public long Revision(string path) => _revisions.GetValueOrDefault(path);

    public IDisposable Watch(string path, Action<string, long> onChange)
    {
        var full = Path.GetFullPath(path);
        var directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        var filter = Directory.Exists(full) ? "*" : Path.GetFileName(full);
        var watcher = new FileSystemWatcher(directory, filter);
        FileSystemEventHandler handler = (_, args) =>
        {
            long revision;
            lock (_revisions)
            {
                _revisions.TryGetValue(full, out var current);
                revision = current + 1;
                _revisions[full] = revision;
            }

            onChange(args.FullPath, revision);
        };
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.EnableRaisingEvents = true;
        _watchers[full] = watcher;
        return new Subscription(() =>
        {
            watcher.EnableRaisingEvents = false;
            _watchers.Remove(full);
            watcher.Dispose();
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        await ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>Local plugin catalog with checksum and version gates.</summary>
public sealed class MarketService
{
    public sealed record CatalogEntry(PluginManifest Manifest, string Checksum, string Path);

    public IReadOnlyList<CatalogEntry> Scan(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<CatalogEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.toml", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            var checksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            foreach (var manifest in PluginManifest.ParseToml(text))
            {
                entries.Add(new CatalogEntry(manifest, manifest.Checksum ?? checksum, file));
            }
        }

        return entries;
    }

    public bool IsCompatible(string required, string provided) =>
        Version.TryParse(required, out var minimum) &&
        Version.TryParse(provided, out var actual) &&
        actual >= minimum;

    public bool Verify(CatalogEntry entry, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        return string.Equals(checksum, entry.Checksum, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Declaration report/apply with atomic rollback (no plugin market server).</summary>
public sealed class PackageManager
{
    public sealed record Change(string Action, PluginManifest Manifest);

    public IReadOnlyList<PluginManifest> Apply(
        IReadOnlyList<PluginManifest> current,
        IReadOnlyList<Change> changes)
    {
        var staged = current.ToList();
        foreach (var change in changes)
        {
            switch (change.Action)
            {
                case "add":
                    if (staged.Any(entry => entry.Id == change.Manifest.Id))
                    {
                        Rollback(current);
                    }

                    staged.Add(change.Manifest);
                    break;
                case "remove":
                    if (staged.RemoveAll(entry => entry.Id == change.Manifest.Id) == 0)
                    {
                        Rollback(current);
                    }

                    break;
                case "update":
                    var index = staged.FindIndex(entry => entry.Id == change.Manifest.Id);
                    if (index < 0)
                    {
                        Rollback(current);
                    }

                    staged[index] = change.Manifest;
                    break;
                default:
                    Rollback(current);
                    break;
            }
        }

        return staged;
    }

    private static void Rollback(IReadOnlyList<PluginManifest> current) =>
        throw new InvalidOperationException("package change rejected; declarations unchanged");
}

/// <summary>Minimal ASP.NET Core server host with reversible route registration.</summary>
public sealed class ServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public ServerHost(string address = "http://127.0.0.1:0")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(address);
        _app = builder.Build();
    }

    public string? Address => _app.Urls.FirstOrDefault();

    public void MapGet(string path, Func<JsonObject> handler) =>
        _app.MapGet(path, handler);

    public void MapPost(string path, Func<JsonObject, JsonObject> handler) =>
        _app.MapPost(path, handler);

    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);

    public Task StopAsync(CancellationToken token = default) => _app.StopAsync(token);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
