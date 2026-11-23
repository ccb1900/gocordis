using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>The three observations a watcher can report.</summary>
public enum WatchEventKind
{
    Change,
    Ready,
    Error,
}

/// <summary>One immutable watcher observation.</summary>
public sealed record WatchEvent(
    WatchEventKind Kind,
    string Path,
    long Revision = 0,
    string? Error = null);

/// <summary>
/// Watcher options. Recursive watches observe subdirectories; Filter is a
/// semicolon-separated set of globs (supports * and ** directory wildcards).
/// </summary>
public sealed record WatchOptions(bool Recursive = false, string? Filter = null);

/// <summary>
/// File watcher whose subscriptions are reversible effects of their owner.
/// Ready is emitted after native notifications are installed so consumers can
/// wait before producing changes; Error is emitted for invalid sources and
/// native watcher faults. Every state change is serialized by one lock and
/// disposal is idempotent.
/// </summary>
public sealed class ReversibleWatchService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Subscription> _subscriptions = [];
    private readonly Dictionary<string, long> _revisions = new(StringComparer.Ordinal);
    private bool _disposed;

    public long Revision(string path)
    {
        lock (_sync)
        {
            return _revisions.GetValueOrDefault(path);
        }
    }

    /// <summary>
    /// Starts observing <paramref name="path"/> and returns a handle whose
    /// single Dispose stops and releases the underlying provider.
    /// </summary>
    public IDisposable Watch(string path, Action<WatchEvent> onEvent, WatchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onEvent);
        options ??= new WatchOptions();

        var full = Path.GetFullPath(path);
        var directoryTarget = Directory.Exists(full);
        var directory = directoryTarget ? full : Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            // A missing source is reported as an Error observation, not a
            // surprise exception for a watcher that can be retried later.
            onEvent(new WatchEvent(WatchEventKind.Error, path,
                Error: $"watch source does not exist: {path}"));
            return new InertSubscription();
        }

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = directoryTarget ? "*" : Path.GetFileName(full),
            IncludeSubdirectories = directoryTarget && options.Recursive,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                           NotifyFilters.DirectoryName | NotifyFilters.Size,
        };
        var id = Guid.NewGuid();
        var subscription = new Subscription(this, id, watcher, full, options.Filter, onEvent);

        FileSystemEventHandler handler = (_, args) =>
        {
            if (!subscription.Matches(args.FullPath))
            {
                return;
            }

            long revision;
            lock (_sync)
            {
                if (_disposed || subscription.IsDisposed)
                {
                    return;
                }

                _revisions.TryGetValue(full, out var current);
                revision = current + 1;
                _revisions[full] = revision;
            }

            subscription.Deliver(new WatchEvent(WatchEventKind.Change, args.FullPath, revision));
        };
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (sender, args) => handler(sender, args);
        watcher.Error += (_, args) => subscription.Deliver(new WatchEvent(WatchEventKind.Error, full,
            Error: args.GetException()?.Message ?? "file watcher error"));

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscriptions[id] = subscription;
            watcher.EnableRaisingEvents = true;
        }

        // Ready is delivered outside the service lock: user callbacks must
        // never run while the service is serializing watcher state. Native
        // observations raised before this returns are buffered per
        // subscription and flushed afterwards, so Ready is always first.
        onEvent(new WatchEvent(WatchEventKind.Ready, full));
        subscription.MarkReady();
        return subscription;
    }

    /// <summary>
    /// Registers the watcher as an effect of <paramref name="owner"/>: when that
    /// activation unwinds, the provider is stopped and released.
    /// </summary>
    public IDisposable WatchOwned(Context owner, string path, Action<WatchEvent> onEvent,
        WatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        IDisposable? subscription = null;
        try
        {
            owner.EffectAsync(
                () =>
                {
                    subscription = Watch(path, onEvent, options);
                    return ValueTask.CompletedTask;
                },
                () =>
                {
                    subscription?.Dispose();
                    return ValueTask.CompletedTask;
                }).GetAwaiter().GetResult();
        }
        catch
        {
            subscription?.Dispose(); // refused effect: compensate immediately
            throw;
        }

        return subscription ?? throw new InvalidOperationException("watch effect did not register");
    }

    public ValueTask DisposeAsync()
    {
        FileSystemWatcher[] watchers;
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            watchers = [.. _subscriptions.Values.Select(subscription => subscription.Watcher)];
            _subscriptions.Clear();
        }

        foreach (var watcher in watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
            }
            catch (ObjectDisposedException)
            {
                // already stopped by its own subscription
            }

            watcher.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void Remove(Guid id, FileSystemWatcher watcher)
    {
        lock (_sync)
        {
            _subscriptions.Remove(id);
        }

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
            // service disposal may have released it first
        }

        watcher.Dispose();
    }

    private sealed class Subscription : IDisposable
    {
        private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);
        private readonly ReversibleWatchService _owner;
        private readonly Guid _id;
        private readonly string _root;
        private readonly string? _filter;
        private readonly Action<WatchEvent> _onEvent;
        private readonly object _deliverySync = new();
        private Queue<WatchEvent>? _buffered;
        private bool _readyDelivered;
        private int _disposed;

        internal Subscription(ReversibleWatchService owner, Guid id, FileSystemWatcher watcher,
            string root, string? filter, Action<WatchEvent> onEvent)
        {
            _owner = owner;
            _id = id;
            Watcher = watcher;
            _root = root;
            _filter = filter;
            _onEvent = onEvent;
        }

        internal FileSystemWatcher Watcher { get; }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>
        /// Holds any observation raised before Ready was handed to the consumer.
        /// The per-subscription lock also serializes the flush so a later
        /// observation cannot overtake a buffered one.
        /// </summary>
        internal void Deliver(WatchEvent evt)
        {
            lock (_deliverySync)
            {
                if (!_readyDelivered)
                {
                    (_buffered ??= new Queue<WatchEvent>()).Enqueue(evt);
                    return;
                }

                _onEvent(evt);
            }
        }

        internal void MarkReady()
        {
            lock (_deliverySync)
            {
                _readyDelivered = true;
                if (_buffered is null)
                {
                    return;
                }

                while (_buffered.Count > 0)
                {
                    _onEvent(_buffered.Dequeue());
                }

                _buffered = null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Remove(_id, Watcher);
            }
        }

        internal bool Matches(string path)
        {
            if (string.IsNullOrWhiteSpace(_filter) || _filter == "*")
            {
                return true;
            }

            var relative = Path.GetRelativePath(_root, path).Replace('\\', '/');
            var name = Path.GetFileName(path);
            foreach (var pattern in _filter.Split(';', StringSplitOptions.RemoveEmptyEntries |
                                                      StringSplitOptions.TrimEntries))
            {
                var regex = Patterns.GetOrAdd(pattern, Compile);
                if (regex.IsMatch(relative) || regex.IsMatch(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static Regex Compile(string pattern)
        {
            var source = pattern.Replace('\\', '/');
            var expression = new StringBuilder("^");
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                if (character == '*' && index + 1 < source.Length && source[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < source.Length && source[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else if (character == '*')
                {
                    expression.Append("[^/]*");
                }
                else if (character == '?')
                {
                    expression.Append("[^/]");
                }
                else
                {
                    expression.Append(Regex.Escape(character.ToString()));
                }
            }

            expression.Append('$');
            var options = OperatingSystem.IsWindows()
                ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                : RegexOptions.CultureInvariant;
            return new Regex(expression.ToString(), options);
        }
    }

    private sealed class InertSubscription : IDisposable
    {
        public void Dispose() { }
    }
}
