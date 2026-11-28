using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>Windows SMB session backed by WNetAddConnection2 rather than a
/// password-bearing command line. The native call is isolated behind a seam so
/// lease/refcount logic is testable on non-Windows hosts.</summary>
public interface INativeNetworkConnection
{
    int Add(string remote, string userName, string password);
    int Cancel(string remote);
}

/// <summary>One shared SMB session per UNC share and credential. Session
/// identity is derived from a normalized share root plus the credential, never
/// from a caller-supplied cache key, so acquire/remove always use the same key.</summary>
public sealed class UncConnectionLease : IAsyncDisposable
{
    /// <summary>Active = usable/reacquirable; Releasing = native Cancel is in
    /// flight and the session MUST NOT be replaced; Released = final state,
    /// removed from the cache under the same lock that flips the state.</summary>
    private enum SessionState { Active, Releasing, Released }

    /// <summary>Upper bound for a concurrent Acquire that observes a session in
    /// Releasing. Native Cancel is synchronous and short; a bound keeps a stuck
    /// native call from blocking a workflow forever. No Task.Delay: bounded
    /// SpinWait/Yield polling of the state under Gate is sufficient.</summary>
    private static readonly TimeSpan ReacquireTimeout = TimeSpan.FromSeconds(5);

    private static readonly Dictionary<string, UncConnectionLease> Sessions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private readonly string _key;
    private readonly string _remote;
    private readonly INativeNetworkConnection _native;
    private int _references;
    private SessionState _state = SessionState.Active;

    private UncConnectionLease(string key, string remote,
        string account, string password, INativeNetworkConnection native)
    {
        _key = key;
        _remote = remote;
        _native = native;
        var result = native.Add(remote, account, password);
        // 2250 means another handle in this logon session already mapped it.
        if (result is not (0 or 2250))
        {
            throw new InvalidOperationException(
                result == 1219
                    ? "SMB credential conflict for the same server (error 1219)"
                    : $"WNetAddConnection2 failed with {result}");
        }
    }

    public static string LocalPath(string uncPath, string relativePath) =>
        Path.Combine(uncPath, relativePath);

    /// <summary>Acquire a shared session for the UNC share root. Equivalent
    /// spellings of the same share and account share one native connection;
    /// a different credential gets its own session (or fails at WNet with 1219).
    /// While the last owner is releasing a session, Acquire waits (bounded) for
    /// that Cancel to finish instead of creating a session it would immediately
    /// disconnect.</summary>
    public static UncConnectionLease Acquire(string uncPath, string userName, string password,
        string? domain = null, INativeNetworkConnection? native = null)
    {
        var remote = NormalizeShareRoot(uncPath);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("UNC credential requires a user name", nameof(userName));
        }
        password ??= string.Empty;

        var account = string.IsNullOrWhiteSpace(domain) ? userName.Trim() : $"{domain.Trim()}\\{userName.Trim()}";
        // The cache key is the only session identity. It binds the normalized
        // share root, case-insensitive account and password without retaining
        // the secret on the lease, so a different credential can never reuse
        // (or remove) another credential's session.
        var identity = $"{remote}|{account.ToUpperInvariant()}|{password}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        var started = Stopwatch.GetTimestamp();
        var spin = new SpinWait();
        while (true)
        {
            var waitForCancel = false;
            lock (Gate)
            {
                if (Sessions.TryGetValue(key, out var existing))
                {
                    switch (existing._state)
                    {
                        case SessionState.Active:
                            existing._references++;
                            return existing;
                        case SessionState.Releasing:
                            // The lease intentionally stays in the dictionary
                            // until Cancel completes; a new Add right now would
                            // be disconnected by the previous owner's Cancel.
                            waitForCancel = true;
                            break;
                        default:
                            // Defensive: a released lease must never be reused.
                            Sessions.Remove(key);
                            break;
                    }
                }

                if (!waitForCancel)
                {
                    var created = new UncConnectionLease(
                        key, remote, account, password, native ?? new WindowsNetworkConnection());
                    created._references = 1;
                    Sessions[key] = created;
                    return created;
                }
            }

            if (Stopwatch.GetElapsedTime(started) >= ReacquireTimeout)
            {
                throw new InvalidOperationException(
                    $"UNC session for {remote} is still being released; timed out waiting to reacquire");
            }
            spin.SpinOnce();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (Gate)
        {
            // A second dispose (or ReleaseAll already releasing this session)
            // must never issue a second native Cancel.
            if (_state != SessionState.Active) return ValueTask.CompletedTask;
            if (_references > 1)
            {
                _references--;
                return ValueTask.CompletedTask;
            }
            _references = 0;
            _state = SessionState.Releasing;
            // Do not remove from Sessions yet: Acquire must see Releasing while
            // the native Cancel is in flight.
        }

        try
        {
            _native.Cancel(_remote);
        }
        finally
        {
            lock (Gate)
            {
                if (Sessions.TryGetValue(_key, out var current) && ReferenceEquals(current, this))
                    Sessions.Remove(_key);
                _state = SessionState.Released;
            }
        }
        return ValueTask.CompletedTask;
    }

    public static void ReleaseAll()
    {
        List<UncConnectionLease> releasing;
        lock (Gate)
        {
            releasing = [];
            foreach (var lease in Sessions.Values)
            {
                // A lease already being released by its owner will be finalized
                // (and removed) by that owner; skip it here to avoid a second Cancel.
                if (lease._state != SessionState.Active) continue;
                lease._references = 0;
                lease._state = SessionState.Releasing;
                releasing.Add(lease);
            }
        }

        foreach (var lease in releasing)
        {
            try
            {
                lease._native.Cancel(lease._remote);
            }
            finally
            {
                lock (Gate)
                {
                    if (Sessions.TryGetValue(lease._key, out var current) && ReferenceEquals(current, lease))
                        Sessions.Remove(lease._key);
                    lease._state = SessionState.Released;
                }
            }
        }
    }

    private static string NormalizeShareRoot(string uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("UNC path must start with \\\\server\\share", nameof(uncPath));
        }

        var parts = uncPath.Trim().TrimEnd('\\', '/')[2..]
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException("UNC path must include server and share", nameof(uncPath));
        }
        return $@"\\{parts[0].ToLowerInvariant()}\{parts[1].ToLowerInvariant()}";
    }
}

public sealed class WindowsNetworkConnection : INativeNetworkConnection
{
    public int Add(string remote, string userName, string password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("UNC session leases require the Windows SMB client");
        }

        var resource = new NETRESOURCEW
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = remote,
        };
        return WNetAddConnection2W(ref resource, password, userName, CONNECT_TEMPORARY);
    }

    public int Cancel(string remote)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        return WNetCancelConnection2W(remote, 0, true);
    }

    private const int RESOURCETYPE_DISK = 1;
    private const uint CONNECT_TEMPORARY = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCEW
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpLocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpRemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpComment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2W(
        ref NETRESOURCEW netResource,
        string password,
        string username,
        uint flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2W(string name, uint flags, bool force);
}

/// <summary>Caches one SMB session per UNC share and credential, so several
/// workflows never establish conflicting sessions with the same server.</summary>
public sealed class UncFileSource : IFileSource
{
    private readonly IResourceRegistry _resources;
    private readonly ICredentialVault? _vault;
    private readonly Func<IReadOnlyList<string>>? _extensions;
    private readonly TimeSpan _quietPeriod;

    public UncFileSource(
        IResourceRegistry resources,
        ICredentialVault? vault = null,
        Func<IReadOnlyList<string>>? extensions = null,
        TimeSpan? quietPeriod = null)
    {
        _resources = resources;
        _vault = vault;
        _extensions = extensions;
        _quietPeriod = quietPeriod ?? ProfileFileDiscovery.DefaultQuietPeriod;
    }

    public ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default) =>
        DiscoverAsync(resourceId, range, null, null, ct);

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, FileProfileDefinition? profile,
        IReadOnlySet<DateOnly>? businessDates, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value) ? value?.ToString() : null;
        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("resource is not a UNC path", nameof(resourceId));
        }

        var secret = resource.CredentialId is null
            ? null
            : await _vault!.RevealAsync(resource.CredentialId, ct);
        var (user, password, domain) = ParseCredential(secret);
        // Flat sources point at the file itself; the SMB session still belongs
        // to its share root, never to a file or subfolder path.
        var connectionRoot = Directory.Exists(root) ? root! : ShareRoot(root!);
        UncConnectionLease? lease = null;
        if (!string.IsNullOrWhiteSpace(user))
        {
            lease = UncConnectionLease.Acquire(connectionRoot, user, password!, domain);
        }

        try
        {
            return await ProfileFileDiscovery.FindAsync(
                root!, resource.Id, _extensions, range, profile, businessDates,
                _quietPeriod, null, ct);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    internal static string ShareRoot(string path)
    {
        var parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : path;
    }

    public static void ReleaseAll() => UncConnectionLease.ReleaseAll();

    /// <summary>Hold the credentialed SMB session for the whole source stage.
    /// DiscoverAsync still acquires its own reference-counted lease; this outer
    /// scope keeps the session alive while stability checks, parsing and
    /// archiving touch the same share.</summary>
    public async ValueTask<IAsyncDisposable?> LeaseScopeAsync(
        string resourceId, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(resourceId, ct)
                       ?? throw new InvalidOperationException($"resource '{resourceId}' not found");
        var root = resource.Configuration.TryGetValue("root", out var value) ? value?.ToString() : null;
        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith(@"\\", StringComparison.Ordinal))
            return NoopLease.Instance;
        var secret = resource.CredentialId is null
            ? null
            : await _vault!.RevealAsync(resource.CredentialId, ct);
        var (user, password, domain) = ParseCredential(secret);
        if (string.IsNullOrWhiteSpace(user)) return NoopLease.Instance;
        var connectionRoot = Directory.Exists(root) ? root! : ShareRoot(root!);
        return UncConnectionLease.Acquire(connectionRoot, user, password!, domain);
    }

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        FileStability.IsStableAsync(file.FullPath, quietPeriod, ct);

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public static (string? User, string? Password, string? Domain) ParseCredential(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return (null, null, null);
        try
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            string? Pick(params string[] names) => names
                .Select(name => root.TryGetProperty(name, out var value) ? value.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return (
                Pick("user", "username", "uid"),
                Pick("password", "pwd"),
                Pick("domain"));
        }
        catch (JsonException)
        {
            var split = secret.Split(':', 2);
            return (split[0], split.Length > 1 ? split[1] : null, null);
        }
    }
}
