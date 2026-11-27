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
    private static readonly Dictionary<string, UncConnectionLease> Sessions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private readonly string _key;
    private readonly string _remote;
    private readonly INativeNetworkConnection _native;
    private int _references;
    private bool _disposed;

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
    /// a different credential gets its own session (or fails at WNet with 1219).</summary>
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

        lock (Gate)
        {
            if (Sessions.TryGetValue(key, out var existing))
            {
                existing._references++;
                return existing;
            }

            var created = new UncConnectionLease(
                key, remote, account, password, native ?? new WindowsNetworkConnection());
            created._references = 1;
            Sessions[key] = created;
            return created;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (Gate)
        {
            if (_references > 1)
            {
                _references--;
                return ValueTask.CompletedTask;
            }
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _references = 0;
            if (Sessions.TryGetValue(_key, out var current) && ReferenceEquals(current, this))
            {
                Sessions.Remove(_key);
            }
        }

        _native.Cancel(_remote);
        return ValueTask.CompletedTask;
    }

    public static void ReleaseAll()
    {
        lock (Gate)
        {
            foreach (var lease in Sessions.Values)
            {
                lease._references = 0;
                lease._disposed = true;
                lease._native.Cancel(lease._remote);
            }
            Sessions.Clear();
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

    public UncFileSource(
        IResourceRegistry resources,
        ICredentialVault? vault = null,
        Func<IReadOnlyList<string>>? extensions = null)
    {
        _resources = resources;
        _vault = vault;
        _extensions = extensions;
    }

    public async ValueTask<IReadOnlyList<DiscoveredFile>> DiscoverAsync(
        string resourceId, SourceDateRange range, CancellationToken ct = default)
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
            if (File.Exists(root))
            {
                var parent = Path.GetDirectoryName(root!) ?? root!;
                var identity = FileIdentityFactory.Create(root!, parent, resource.Id);
                var flatDate = FileIdentityFactory.ParseSourceDate(Path.GetFileName(root))
                               ?? identity.LastWriteUtc;
                return flatDate < range.FromInclusive || flatDate >= range.ToExclusive
                    ? []
                    : [new DiscoveredFile(identity, root!, flatDate, resource.Id)];
            }

            var result = new List<DiscoveredFile>();
            var files = DiscoveredFilePatterns.Enumerate(root!, _extensions);
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                var identity = FileIdentityFactory.Create(path, root!, resource.Id);
                var sourceDate = FileIdentityFactory.ParseSourceDate(Path.GetFileName(path)) ?? identity.LastWriteUtc;
                if (sourceDate < range.FromInclusive || sourceDate >= range.ToExclusive) continue;
                result.Add(new DiscoveredFile(identity, path, sourceDate, resource.Id));
            }
            return result.OrderBy(file => file.SourceDate).ToArray();
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

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        ValueTask.FromResult(File.Exists(file.FullPath));

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
