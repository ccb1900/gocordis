using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Gocordis.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gocordis.Extensions;

/// <summary>Raised when typed CLI arguments do not satisfy a command schema.</summary>
public sealed class CliSchemaException : ArgumentException
{
    public CliSchemaException(string message) : base(message) { }
}

/// <summary>One typed argument of a CLI command.</summary>
public sealed record CliOption(
    string Name,
    string Description,
    bool Required = false,
    object? Default = null);

/// <summary>Declarative schema for a CLI command, used for validation and help.</summary>
public sealed record CliCommandSchema(
    string Name,
    string Description,
    IReadOnlyList<CliOption> Options,
    IReadOnlyList<string>? Aliases = null);

/// <summary>
/// Adds typed schema registration and help generation to the existing
/// quote-aware CliDispatcher. Schemas are attached per dispatcher instance,
/// so two dispatchers never share command metadata; registration and reads
/// are serialized to keep concurrent hosts consistent.
/// </summary>
public static class CliDispatcherSchemaExtensions
{
    private static readonly ConditionalWeakTable<CliDispatcher, CliSchemaRegistry> Schemas = new();

    public static void RegisterTyped(
        this CliDispatcher dispatcher,
        CliCommandSchema schema,
        Func<IReadOnlyDictionary<string, string>, string> handler)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema.Name);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in schema.Options)
        {
            if (!names.Add(option.Name))
            {
                throw new CliSchemaException(
                    $"duplicate option '--{option.Name}' for command '{schema.Name}'");
            }
        }

        var aliases = schema.Aliases?.ToArray() ?? [];
        var registry = Schemas.GetValue(dispatcher, static _ => new CliSchemaRegistry());
        registry.Add(schema, aliases);
        dispatcher.Register(schema.Name, args => handler(ParseArguments(schema, args)), aliases);
    }

    /// <summary>Parses and validates arguments for a registered typed command.</summary>
    public static IReadOnlyDictionary<string, string> ParseArgs(
        this CliDispatcher dispatcher,
        string commandName,
        params string[] args)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(args);
        if (!Schemas.TryGetValue(dispatcher, out var registry) ||
            registry.Find(commandName) is not { } schema)
        {
            throw new CliSchemaException($"unknown command: {commandName}");
        }

        return ParseArguments(schema, args);
    }

    /// <summary>One-line summary of every typed command.</summary>
    public static string Help(this CliDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (!Schemas.TryGetValue(dispatcher, out var registry))
        {
            return string.Empty;
        }

        var lines = new List<string> { "Commands:" };
        foreach (var schema in registry.Commands())
        {
            lines.Add(Describe(schema));
            AppendOptions(lines, schema);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Full help for one command, including every option and requirement.</summary>
    public static string Help(this CliDispatcher dispatcher, string commandName)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        if (!Schemas.TryGetValue(dispatcher, out var registry) ||
            registry.Find(commandName) is not { } schema)
        {
            return $"unknown command: {commandName}";
        }

        var lines = new List<string> { Describe(schema) };
        AppendOptions(lines, schema);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendOptions(List<string> lines, CliCommandSchema schema)
    {
        foreach (var option in schema.Options)
        {
            var required = option.Required ? " (required)" : string.Empty;
            var fallback = option.Default is null ? string.Empty : $" (default: {option.Default})";
            lines.Add($"  --{option.Name}{required}{fallback}  {option.Description}");
        }
    }

    private static string Describe(CliCommandSchema schema)
    {
        var aliases = schema.Aliases is { Count: > 0 }
            ? $" (aliases: {string.Join(", ", schema.Aliases)})"
            : string.Empty;
        return $"{schema.Name}{aliases} - {schema.Description}";
    }

    private static IReadOnlyDictionary<string, string> ParseArguments(
        CliCommandSchema schema,
        string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byName = schema.Options.ToDictionary(option => option.Name, StringComparer.OrdinalIgnoreCase);
        var positional = 0;
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            string name;
            string? value = null;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token[2..];
                var separator = body.IndexOf('=');
                if (separator >= 0)
                {
                    name = body[..separator];
                    value = body[(separator + 1)..];
                }
                else
                {
                    name = body;
                }

                if (!byName.TryGetValue(name, out var option) || option is null)
                {
                    throw new CliSchemaException($"unknown option '--{name}' for command '{schema.Name}'");
                }

                if (value is null)
                {
                    if (index + 1 >= args.Length)
                    {
                        throw new CliSchemaException($"option '--{option.Name}' requires a value");
                    }

                    value = args[++index];
                }

                values[option.Name] = value;
            }
            else
            {
                if (positional >= schema.Options.Count)
                {
                    throw new CliSchemaException($"unexpected argument '{token}' for command '{schema.Name}'");
                }

                values[schema.Options[positional++].Name] = token;
            }
        }

        foreach (var option in schema.Options)
        {
            if (values.ContainsKey(option.Name))
            {
                continue;
            }

            if (option.Required)
            {
                throw new CliSchemaException(
                    $"option '--{option.Name}' is required for command '{schema.Name}'");
            }

            values[option.Name] = option.Default?.ToString() ?? string.Empty;
        }

        return values;
    }

    private sealed class CliSchemaRegistry
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, CliCommandSchema> _byName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CliCommandSchema> _commands = [];

        public void Add(CliCommandSchema schema, string[] aliases)
        {
            lock (_sync)
            {
                _commands.RemoveAll(existing => existing.Name == schema.Name);
                _byName[schema.Name] = schema;
                foreach (var alias in aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        _byName[alias] = schema;
                    }
                }

                _commands.Add(schema);
            }
        }

        public CliCommandSchema? Find(string name)
        {
            lock (_sync)
            {
                return _byName.GetValueOrDefault(name);
            }
        }

        public IReadOnlyList<CliCommandSchema> Commands()
        {
            lock (_sync)
            {
                return _commands.OrderBy(schema => schema.Name, StringComparer.Ordinal).ToArray();
            }
        }
    }
}

/// <summary>Raised by Broker selection/Call when the routing set is empty.</summary>
public sealed class BrokerNoProviderException : InvalidOperationException
{
    public BrokerNoProviderException() : base("broker: no backing provider registered") { }
}

/// <summary>
/// Multi-implementation routing with named, reversible registration (paper §6.2).
/// A provider registers through the returned handle; disposing that handle
/// withdraws the route, so a retiring owner leaves no residue in the broker.
/// </summary>
public sealed class Broker<T>
{
    private readonly object _sync = new();
    private readonly List<Route> _routes = [];
    private readonly Func<IReadOnlyList<T>, T>? _policy;
    private long _ticket;

    /// <summary>Null policy means round-robin in registration order.</summary>
    public Broker(Func<IReadOnlyList<T>, T>? policy = null) => _policy = policy;

    public IDisposable Register(string name, T provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_sync)
        {
            if (_routes.Any(route => route.Name == name))
            {
                throw new InvalidOperationException($"broker: duplicate provider name {name}");
            }
            _routes.Add(new Route(name, provider));
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                var index = _routes.FindIndex(route => route.Name == name);
                if (index >= 0)
                {
                    _routes.RemoveAt(index);
                }
            }
        });
    }

    public IDisposable Register(T provider) => Register(Guid.NewGuid().ToString("N"), provider);

    public IReadOnlyList<T> Providers
    {
        get
        {
            lock (_sync)
            {
                return _routes.Select(route => route.Provider).ToArray();
            }
        }
    }

    /// <summary>Current routing set names in registration order (oldest first).</summary>
    public IReadOnlyList<string> Names()
    {
        lock (_sync)
        {
            return _routes.Select(route => route.Name).ToArray();
        }
    }

    public T First()
    {
        lock (_sync)
        {
            if (_routes.Count == 0)
            {
                throw new BrokerNoProviderException();
            }
            return _routes[0].Provider;
        }
    }

    /// <summary>Stateful round-robin; the first call selects the oldest provider.</summary>
    public T RoundRobin()
    {
        lock (_sync)
        {
            if (_routes.Count == 0)
            {
                throw new BrokerNoProviderException();
            }
            var ticket = Interlocked.Increment(ref _ticket) - 1;
            var index = (int)((ticket % _routes.Count + _routes.Count) % _routes.Count);
            return _routes[index].Provider;
        }
    }

    /// <summary>
    /// Legacy stateless round-robin driven by an explicit ticket; throws when
    /// the routing set is empty, like every other selection entry point.
    /// </summary>
    public T? RoundRobin(long ticket)
    {
        lock (_sync)
        {
            if (_routes.Count == 0)
            {
                throw new BrokerNoProviderException();
            }
            var index = (int)((ticket % _routes.Count + _routes.Count) % _routes.Count);
            return _routes[index].Provider;
        }
    }

    public void Call(Action<T> action) => action(Select());

    public TResult Call<TResult>(Func<T, TResult> selector) => selector(Select());

    private T Select()
    {
        var snapshot = Providers;
        if (snapshot.Count == 0)
        {
            throw new BrokerNoProviderException();
        }
        if (_policy is not null)
        {
            return _policy(snapshot);
        }
        var ticket = Interlocked.Increment(ref _ticket) - 1;
        return snapshot[(int)((ticket % snapshot.Count + snapshot.Count) % snapshot.Count)];
    }

    private sealed record Route(string Name, T Provider);

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>
/// Stable membership with atomic replace and immutable snapshots.
/// Subscription contract: after <see cref="IDisposable.Dispose"/> returns, no
/// notification STARTED afterwards will call the watcher. A notification whose
/// membership snapshot was taken before Dispose may still run once for a
/// watcher that was already disposed concurrently — check your own state in
/// the callback if you need stronger linearization.
/// </summary>
public sealed class StableRegistry<T>
{
    private readonly Dictionary<string, T> _members = new(StringComparer.Ordinal);
    private readonly List<Subscription> _watchers = [];
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
            var subscription = new Subscription(onChange);
            _watchers.Add(subscription);
            return subscription;
        }
    }

    private void Notify()
    {
        Subscription[] snapshot;
        lock (_sync)
        {
            snapshot = [.. _watchers];
        }

        foreach (var watcher in snapshot)
        {
            // A watcher disposed before its turn is skipped here, not invoked.
            if (watcher.IsDisposed)
            {
                continue;
            }

            watcher.Invoke();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public bool IsDisposed => Volatile.Read(ref _action) is null;

        public void Invoke() => _action?.Invoke();

        /// <summary>Release only: a subscription is NOT a notification source,
        /// so disposal must not synthesize one final callback.</summary>
        public void Dispose() => Interlocked.Exchange(ref _action, null);
    }
}

/// <summary>
/// SINGLE-consumer work queue over one shared channel (not the fan-out
/// ObservationLog): each subscriber competes for the next item. Use
/// <see cref="ObservationLog{T}"/> when every subscriber must see the full
/// event stream.
/// </summary>
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
    // One lock for BOTH maps: watcher registration, revision reads and
    // disposal must be serialized (FileSystemService pattern).
    private readonly object _sync = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _revisions = new(StringComparer.Ordinal);
    private bool _disposed;

    public long Revision(string path)
    {
        lock (_sync)
        {
            return _revisions.GetValueOrDefault(path);
        }
    }

    public IDisposable Watch(string path, Action<string, long> onChange)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

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
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _watchers[full] = watcher;
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                _watchers.Remove(full);
            }

            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        });
    }

    public async ValueTask DisposeAsync()
    {
        FileSystemWatcher[] watchers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watchers = [.. _watchers.Values];
            _watchers.Clear();
        }

        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        await ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>One package recorded in a declaration lockfile.</summary>
public sealed record LockEntry(string Id, string Version, string? Checksum = null);

/// <summary>Minimal JSON lockfile: a deterministic snapshot of declared versions.</summary>
public static class PackageLockfile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(IEnumerable<PluginManifest> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var packages = declarations
            .OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
            .Select(manifest => new LockEntry(manifest.Id, manifest.Version, manifest.Checksum))
            .ToArray();
        return JsonSerializer.Serialize(new { version = 1, packages }, Options);
    }

    public static IReadOnlyList<LockEntry> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("packages", out var packages) ||
            packages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LockEntry>();
        foreach (var item in packages.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("lockfile: package is missing a string 'id'");
            }

            var version = item.TryGetProperty("version", out var versionElement) &&
                          versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()!
                : "1.0.0";
            string? checksum = null;
            if (item.TryGetProperty("checksum", out var checksumElement) &&
                checksumElement.ValueKind == JsonValueKind.String)
            {
                checksum = checksumElement.GetString();
            }

            result.Add(new LockEntry(idElement.GetString()!, version, checksum));
        }

        return result;
    }

    public static void Write(string path, IEnumerable<PluginManifest> declarations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Serialize(declarations));
    }

    public static IReadOnlyList<LockEntry> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path));
    }
}

/// <summary>Before/after version of one changed declaration.</summary>
public sealed record ChangeDetail(string Id, string Before, string After);

/// <summary>Structured, ordered diff between two declaration snapshots.</summary>
public sealed record DeclarationDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Updated,
    IReadOnlyList<ChangeDetail> Details);

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

        return PluginManifestChecksum.Matches(entry.Manifest with { Checksum = entry.Checksum },
            File.ReadAllBytes(path));
    }

    /// <summary>Adds a verified catalog entry; rejects duplicates without editing input.</summary>
    public IReadOnlyList<PluginManifest> Install(IReadOnlyList<PluginManifest> current, CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(entry);
        if (!Verify(entry, entry.Path))
        {
            throw new InvalidOperationException(
                $"market: checksum mismatch for '{entry.Manifest.Id}'");
        }

        if (current.Any(manifest => manifest.Id == entry.Manifest.Id))
        {
            throw new InvalidOperationException(
                $"market: '{entry.Manifest.Id}' is already installed");
        }

        return current.Append(entry.Manifest with { Enabled = true }).ToArray();
    }

    /// <summary>Replaces an installed declaration with a strictly newer version.</summary>
    public IReadOnlyList<PluginManifest> Upgrade(IReadOnlyList<PluginManifest> current, CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(entry);
        if (!Verify(entry, entry.Path))
        {
            throw new InvalidOperationException(
                $"market: checksum mismatch for '{entry.Manifest.Id}'");
        }

        var staged = current.ToList();
        var index = staged.FindIndex(manifest => manifest.Id == entry.Manifest.Id);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"market: '{entry.Manifest.Id}' is not installed");
        }

        if (!Version.TryParse(staged[index].Version, out var installed) ||
            !Version.TryParse(entry.Manifest.Version, out var available) ||
            available <= installed)
        {
            throw new InvalidOperationException(
                $"market: '{entry.Manifest.Id}' has no newer version than {staged[index].Version}");
        }

        staged[index] = entry.Manifest with { Enabled = true };
        return staged;
    }

    /// <summary>Removes an installed declaration; missing ids are an error.</summary>
    public IReadOnlyList<PluginManifest> Uninstall(IReadOnlyList<PluginManifest> current, string id)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var staged = current.Where(manifest => manifest.Id != id).ToArray();
        if (staged.Length == current.Count)
        {
            throw new InvalidOperationException($"market: '{id}' is not installed");
        }

        return staged;
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

                    staged.Add(Clone(change.Manifest));
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

                    staged[index] = Clone(change.Manifest);
                    break;
                default:
                    Rollback(current);
                    break;
            }
        }

        return staged;
    }

    /// <summary>Structured diff between two declaration snapshots.</summary>
    public DeclarationDiff Diff(IReadOnlyList<PluginManifest> current, IReadOnlyList<PluginManifest> desired)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(desired);
        var before = current.ToDictionary(manifest => manifest.Id, StringComparer.Ordinal);
        var after = desired.ToDictionary(manifest => manifest.Id, StringComparer.Ordinal);

        var added = after.Keys.Except(before.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var removed = before.Keys.Except(after.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var updated = new List<string>();
        var details = new List<ChangeDetail>();
        foreach (var id in before.Keys.Intersect(after.Keys, StringComparer.Ordinal)
                     .OrderBy(id => id, StringComparer.Ordinal))
        {
            var oldVersion = before[id].Version;
            var newVersion = after[id].Version;
            if (string.Equals(oldVersion, newVersion, StringComparison.Ordinal))
            {
                continue;
            }

            updated.Add(id);
            details.Add(new ChangeDetail(id, oldVersion, newVersion));
        }

        return new DeclarationDiff(added, removed, updated, details);
    }

    /// <summary>Begins a reversible transaction over a snapshot of declarations.</summary>
    public PackageTransaction Begin(IReadOnlyList<PluginManifest> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new PackageTransaction(this, current.Select(Clone).ToArray());
    }

    internal static PluginManifest Clone(PluginManifest manifest) => manifest with
    {
        Config = new Dictionary<string, object?>(manifest.Config, StringComparer.Ordinal),
        Requires = manifest.Requires.ToArray(),
        Provides = manifest.Provides.ToArray(),
    };

    private static void Rollback(IReadOnlyList<PluginManifest> current) =>
        throw new InvalidOperationException("package change rejected; declarations unchanged");
}

/// <summary>
/// One reversible package edit session. Apply stages on a private snapshot;
/// a rejected batch throws before the snapshot advances, and Rollback always
/// restores the exact baseline. Access is serialized for concurrent hosts.
/// </summary>
public sealed class PackageTransaction
{
    private readonly PackageManager _manager;
    private readonly IReadOnlyList<PluginManifest> _baseline;
    private readonly object _sync = new();
    private IReadOnlyList<PluginManifest> _current;

    internal PackageTransaction(PackageManager manager, IReadOnlyList<PluginManifest> baseline)
    {
        _manager = manager;
        _baseline = baseline;
        _current = baseline;
    }

    public IReadOnlyList<PluginManifest> Current
    {
        get { lock (_sync) { return _current; } }
    }

    public DeclarationDiff Diff
    {
        get { lock (_sync) { return _manager.Diff(_baseline, _current); } }
    }

    public IReadOnlyList<PluginManifest> Apply(IReadOnlyList<PackageManager.Change> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_sync)
        {
            var staged = _manager.Apply(_current, changes);
            _current = staged;
            return staged;
        }
    }

    public IReadOnlyList<PluginManifest> Rollback()
    {
        lock (_sync)
        {
            _current = _baseline;
            return _current;
        }
    }
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
