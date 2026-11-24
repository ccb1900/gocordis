using System.Text;

namespace Gocordis.Extensions;

/// <summary>Local provider: physical directory with symlink-escape guard.</summary>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    private readonly string _root;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _sync = new();

    public LocalFileSystemProvider(string root) => _root = Path.GetFullPath(root);

    public string Scheme => "file";

    private string Resolve(string relative)
    {
        var resolved = Path.GetFullPath(Path.Combine(_root, relative.TrimStart('/', '\\')));
        EnsureWithin(_root, resolved);
        var cursor = _root;
        foreach (var segment in relative.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.GetFullPath(Path.Combine(cursor, segment));
            EnsureWithin(_root, cursor);
            FileSystemInfo? info = Directory.Exists(cursor)
                ? new DirectoryInfo(cursor)
                : File.Exists(cursor) ? new FileInfo(cursor) : null;
            if (info is null)
            {
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                EnsureWithin(_root, target.FullName);
                cursor = target.FullName;
            }
        }

        return resolved;
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

    public Task<byte[]> ReadAsync(string path) => File.ReadAllBytesAsync(Resolve(path));

    public async Task<byte[]> ReadRangeAsync(string path, long offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        await using var file = new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        if (offset > 0)
        {
            file.Seek(offset, SeekOrigin.Begin);
        }

        using var collected = new MemoryStream();
        var buffer = new byte[8192];
        var remaining = count < 0 ? long.MaxValue : (long)count;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0)
            {
                break;
            }

            await collected.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }

        return collected.ToArray();
    }

    public async Task WriteAsync(string path, byte[] content, FileWriteOptions options)
    {
        var resolved = Resolve(path);
        var directory = Path.GetDirectoryName(resolved)!;
        Directory.CreateDirectory(directory);
        if (!options.Atomic)
        {
            var mode = options.Exclusive ? FileMode.CreateNew : FileMode.Create;
            await using var file = new FileStream(resolved, mode, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous);
            await file.WriteAsync(content);
            return;
        }

        var temp = Path.Combine(directory,
            $".{Path.GetFileName(resolved)}.gocordis-tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var staged = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await staged.WriteAsync(content);
            }

            File.Move(temp, resolved, overwrite: !options.Exclusive);
            temp = null!;
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task<bool> ExistsAsync(string path)
    {
        var resolved = Resolve(path);
        return Task.FromResult(File.Exists(resolved) || Directory.Exists(resolved));
    }

    public Task<IReadOnlyList<string>> ListAsync(string path)
    {
        var resolved = Resolve(path);
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"no such directory: '{path}'");
        }

        IReadOnlyList<string> names = Directory.GetFileSystemEntries(resolved)
            .Select(entry => Path.GetFileName(entry)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(names);
    }

    public Task MkdirAsync(string path, bool recursive)
    {
        var resolved = Resolve(path);
        if (!recursive)
        {
            var parent = Path.GetDirectoryName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent is not null && !Directory.Exists(parent))
            {
                throw new DirectoryNotFoundException($"parent directory does not exist: '{path}'");
            }
        }

        Directory.CreateDirectory(resolved);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string path, bool recursive)
    {
        var resolved = Resolve(path);
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive);
        }
        else if (File.Exists(resolved))
        {
            File.Delete(resolved);
        }
        else
        {
            throw new FileNotFoundException($"no such file or directory: '{path}'");
        }

        return Task.CompletedTask;
    }

    public async Task CopyAsync(string source, string destination, FileCopyOptions options)
    {
        var from = Resolve(source);
        var to = Resolve(destination);
        var directory = Path.GetDirectoryName(to)!;
        Directory.CreateDirectory(directory);
        if (!options.Atomic)
        {
            var mode = options.Exclusive || !options.Overwrite ? FileMode.CreateNew : FileMode.Create;
            await using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            await using var output = new FileStream(to, mode, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous);
            await input.CopyToAsync(output);
            return;
        }

        var temp = Path.Combine(directory, $".{Path.GetFileName(to)}.gocordis-tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read,
                             4096, FileOptions.Asynchronous))
            await using (var staged = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(staged);
            }

            File.Move(temp, to, overwrite: options.Overwrite && !options.Exclusive);
            temp = null!;
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task MoveAsync(string source, string destination)
    {
        var from = Resolve(source);
        var to = Resolve(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
        }

        return Task.CompletedTask;
    }

    public IDisposable Watch(string path, Action<string> onChange)
    {
        var resolved = Resolve(path);
        var directory = Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved)!;
        var filter = Directory.Exists(resolved) ? "*" : Path.GetFileName(resolved);
        var watcher = new FileSystemWatcher(directory, filter);
        watcher.Changed += (_, args) => onChange(args.FullPath);
        watcher.Created += (_, args) => onChange(args.FullPath);
        watcher.EnableRaisingEvents = true;
        lock (_sync)
        {
            _watchers.Add(watcher);
        }

        return new Subscription(() =>
        {
            watcher.EnableRaisingEvents = false;
            lock (_sync)
            {
                _watchers.Remove(watcher);
            }

            watcher.Dispose();
        });
    }

    public ValueTask DisposeAsync()
    {
        FileSystemWatcher[] watchers;
        lock (_sync)
        {
            watchers = _watchers.ToArray();
            _watchers.Clear();
        }

        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;
        public Subscription(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

/// <summary>In-memory provider: no physical I/O, watch notifications on write.</summary>
public sealed class MemoryFileSystemProvider : IFileSystemProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "" };
    private readonly Dictionary<string, List<Action<string>>> _watches = new(StringComparer.Ordinal);

    public MemoryFileSystemProvider(IEnumerable<KeyValuePair<string, byte[]>>? seeds = null)
    {
        foreach (var pair in seeds ?? [])
        {
            var path = Normalize(pair.Key);
            EnsureParents(path);
            _files[path] = pair.Value.ToArray();
        }
    }

    public string Scheme => "memory";

    private static string Normalize(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join('/', stack);
    }

    private void EnsureParents(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < segments.Length; i++)
        {
            _directories.Add(string.Join('/', segments[..i]));
        }
    }

    private void Notify(string path)
    {
        foreach (var (prefix, callbacks) in _watches)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal) || prefix.Length == 0)
            {
                foreach (var callback in callbacks.ToArray())
                {
                    callback(path);
                }
            }
        }
    }

    public Task<byte[]> ReadAsync(string path)
    {
        lock (_sync)
        {
            return Task.FromResult(_files.TryGetValue(Normalize(path), out var bytes)
                ? bytes.ToArray()
                : throw new FileNotFoundException($"no such file: '{path}'"));
        }
    }

    public Task<byte[]> ReadRangeAsync(string path, long offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var bytes = ReadAsync(path).Result;
        var start = (int)Math.Min(bytes.Length, offset);
        var length = count < 0 ? bytes.Length - start : Math.Min(count, bytes.Length - start);
        return Task.FromResult(bytes.AsSpan(start, Math.Max(0, length)).ToArray());
    }

    public Task WriteAsync(string path, byte[] content, FileWriteOptions options)
    {
        var normalized = Normalize(path);
        lock (_sync)
        {
            EnsureParents(normalized);
            if (options.Exclusive && _files.ContainsKey(normalized))
            {
                throw new IOException($"file already exists: '{path}'");
            }

            _files[normalized] = content.ToArray();
        }

        Notify(normalized);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path)
    {
        var normalized = Normalize(path);
        lock (_sync)
        {
            return Task.FromResult(_files.ContainsKey(normalized) || _directories.Contains(normalized));
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(string path)
    {
        var prefix = Normalize(path);
        lock (_sync)
        {
            if (!_directories.Contains(prefix))
            {
                throw new DirectoryNotFoundException($"no such directory: '{path}'");
            }

            var head = prefix.Length == 0 ? string.Empty : prefix + "/";
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _files.Keys.Concat(_directories))
            {
                if (!entry.StartsWith(head, StringComparison.Ordinal))
                {
                    continue;
                }

                var tail = entry[head.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (tail.Length > 0)
                {
                    names.Add(tail[0]);
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(names.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
    }

    public Task MkdirAsync(string path, bool recursive)
    {
        var normalized = Normalize(path);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        lock (_sync)
        {
            if (!recursive && segments.Length > 1)
            {
                var parent = string.Join('/', segments[..^1]);
                if (!_directories.Contains(parent))
                {
                    throw new DirectoryNotFoundException($"parent directory does not exist: '{path}'");
                }
            }

            _directories.Add(normalized);
            for (var i = 1; i < segments.Length; i++)
            {
                _directories.Add(string.Join('/', segments[..i]));
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string path, bool recursive)
    {
        var normalized = Normalize(path);
        lock (_sync)
        {
            if (_files.Remove(normalized))
            {
                return Task.CompletedTask;
            }

            if (!_directories.Contains(normalized))
            {
                throw new FileNotFoundException($"no such file or directory: '{path}'");
            }

            var head = normalized.Length == 0 ? string.Empty : normalized + "/";
            if (!recursive && _files.Keys.Concat(_directories).Any(entry => entry.StartsWith(head, StringComparison.Ordinal)))
            {
                throw new IOException($"directory not empty: '{path}'");
            }

            foreach (var entry in _files.Keys.Where(key => key.StartsWith(head, StringComparison.Ordinal)).ToArray())
            {
                _files.Remove(entry);
            }

            foreach (var entry in _directories.Where(key => key.StartsWith(head, StringComparison.Ordinal)).ToArray())
            {
                _directories.Remove(entry);
            }

            _directories.Remove(normalized);
        }

        return Task.CompletedTask;
    }

    public Task CopyAsync(string source, string destination, FileCopyOptions options)
    {
        var bytes = ReadAsync(source).Result;
        return WriteAsync(destination, bytes, new FileWriteOptions(Exclusive: options.Exclusive || !options.Overwrite));
    }

    public Task MoveAsync(string source, string destination)
    {
        var bytes = ReadAsync(source).Result;
        WriteAsync(destination, bytes, new FileWriteOptions()).GetAwaiter().GetResult();
        RemoveAsync(source, recursive: false).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public IDisposable Watch(string path, Action<string> onChange)
    {
        var prefix = Normalize(path);
        lock (_sync)
        {
            if (!_watches.TryGetValue(prefix, out var callbacks))
            {
                callbacks = [];
                _watches[prefix] = callbacks;
            }

            callbacks.Add(onChange);
        }

        return new Subscription(() =>
        {
            lock (_sync)
            {
                if (_watches.TryGetValue(prefix, out var callbacks))
                {
                    callbacks.Remove(onChange);
                    if (callbacks.Count == 0)
                    {
                        _watches.Remove(prefix);
                    }
                }
            }
        });
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _watches.Clear();
            _files.Clear();
            _directories.Clear();
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _action;
        public Subscription(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
