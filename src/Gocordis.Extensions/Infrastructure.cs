using System.Net.Http;
using System.Text;

namespace Gocordis.Extensions;

/// <summary>Mount-routed filesystem with path-escape guard and reversible watchers.</summary>
public sealed class FileSystemService : IAsyncDisposable
{
    private readonly Dictionary<string, string> _mounts = new(StringComparer.Ordinal);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _watchersSync = new();

    public void Mount(string prefix, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _mounts[prefix] = Path.GetFullPath(root);
    }

    public async Task WriteAsync(string path, byte[] content)
    {
        var resolved = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        await File.WriteAllBytesAsync(resolved, content);
    }

    public Task<byte[]> ReadAsync(string path) => File.ReadAllBytesAsync(Resolve(path));

    public IDisposable Watch(string path, Action<string> onChange)
    {
        var resolved = Resolve(path);
        var directory = Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved)!;
        var filter = Directory.Exists(resolved) ? "*" : Path.GetFileName(resolved);
        var watcher = new FileSystemWatcher(directory, filter);
        watcher.Changed += (_, args) => onChange(args.FullPath);
        watcher.Created += (_, args) => onChange(args.FullPath);
        watcher.EnableRaisingEvents = true;
        lock (_watchersSync)
        {
            _watchers.Add(watcher);
        }

        return new Subscription(() =>
        {
            watcher.EnableRaisingEvents = false;
            lock (_watchersSync)
            {
                _watchers.Remove(watcher);
            }

            watcher.Dispose();
        });
    }

    private static void EnsureWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("path escapes the mount root");
        }
    }

    private string Resolve(string path)
    {
        var match = _mounts
            .Where(pair => path.StartsWith(pair.Key, StringComparison.Ordinal))
            .OrderByDescending(pair => pair.Key.Length)
            .FirstOrDefault();
        if (match.Key is null)
        {
            throw new InvalidOperationException($"no mount matches '{path}'");
        }

        var relative = path[match.Key.Length..].TrimStart('/', '\\');
        var resolved = Path.GetFullPath(Path.Combine(match.Value, relative));
        EnsureWithin(match.Value, resolved);

        // Path.GetFullPath does not resolve symlinks; check the final target too.
        var info = new FileInfo(resolved);
        if (info.Exists)
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                EnsureWithin(match.Value, target.FullName);
            }
        }

        return resolved;
    }

    public async ValueTask DisposeAsync()
    {
        FileSystemWatcher[] watchers;
        lock (_watchersSync)
        {
            watchers = _watchers.ToArray();
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

/// <summary>HttpClient with ordered, reversible interceptors.</summary>
public sealed class HttpService : IAsyncDisposable
{
    private readonly HttpClient _client = new();
    private readonly List<Func<HttpRequestMessage, ValueTask>> _interceptors = [];

    public void SetBaseAddress(Uri uri) => _client.BaseAddress = uri;

    public IDisposable AddInterceptor(Func<HttpRequestMessage, ValueTask> interceptor)
    {
        _interceptors.Add(interceptor);
        return new Subscription(() => _interceptors.Remove(interceptor));
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        foreach (var interceptor in _interceptors.ToArray())
        {
            await interceptor(request);
        }

        return await _client.SendAsync(request);
    }

    public async Task<string> GetStringAsync(string uri)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, uri));
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;

        public Subscription(Action action) => _action = action;

        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>dotenv-style configuration with process-env precedence and rollback.</summary>
public sealed class EnvService : IAsyncDisposable
{
    private readonly List<(string Key, string? Previous)> _changes = [];

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            result[key] = Environment.ExpandEnvironmentVariables(value);
        }

        return result;
    }

    public void Apply(IReadOnlyDictionary<string, string> values, bool processWins = true)
    {
        foreach (var (key, value) in values)
        {
            if (processWins && Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            _changes.Add((key, Environment.GetEnvironmentVariable(key)));
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _changes.Count - 1; index >= 0; index--)
        {
            var (key, previous) = _changes[index];
            Environment.SetEnvironmentVariable(key, previous);
        }

        _changes.Clear();
        await ValueTask.CompletedTask;
    }
}

/// <summary>Command dispatcher with quote-aware tokenization and aliases.</summary>
public sealed class CliDispatcher
{
    private readonly Dictionary<string, (string[] Aliases, Func<string[], string> Handler)> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in input)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    public void Register(string name, Func<string[], string> handler, params string[] aliases) =>
        _commands[name] = (aliases, handler);

    public string Dispatch(string input)
    {
        var tokens = Tokenize(input);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var command = _commands.FirstOrDefault(pair =>
            pair.Key.Equals(tokens[0], StringComparison.OrdinalIgnoreCase) ||
            pair.Value.Aliases.Contains(tokens[0], StringComparer.OrdinalIgnoreCase));
        return command.Value.Handler is null
            ? $"unknown command: {tokens[0]}"
            : command.Value.Handler(tokens.Skip(1).ToArray());
    }
}
