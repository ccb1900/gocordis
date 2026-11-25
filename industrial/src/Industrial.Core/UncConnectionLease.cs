using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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

/// <summary>One shared SMB session per UNC share and credential.</summary>
public sealed class UncConnectionLease : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, UncConnectionLease> Sessions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private readonly string _remote;
    private readonly INativeNetworkConnection _native;
    private int _references;

    public UncConnectionLease(string uncPath, string userName, string password,
        string? domain = null, INativeNetworkConnection? native = null)
    {
        if (!uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("UNC path must start with \\\\server\\share", nameof(uncPath));
        }

        _remote = uncPath;
        _native = native ?? new WindowsNetworkConnection();
        var account = string.IsNullOrWhiteSpace(domain) ? userName : $"{domain}\\{userName}";
        var result = _native.Add(uncPath, account, password);
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

    public static UncConnectionLease Acquire(string key, string uncPath, string userName,
        string password, string? domain = null, INativeNetworkConnection? native = null)
    {
        lock (Gate)
        {
            if (Sessions.TryGetValue(key, out var existing))
            {
                existing._references++;
                return existing;
            }

            var created = new UncConnectionLease(uncPath, userName, password, domain, native);
            created._references = 1;
            Sessions[key] = created;
            return created;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (Gate)
        {
            if (--_references > 0) return ValueTask.CompletedTask;
            Sessions.TryRemove(KeyOf(_remote), out _);
        }
        _native.Cancel(_remote);
        return ValueTask.CompletedTask;
    }

    public static void ReleaseAll()
    {
        lock (Gate)
        {
            foreach (var lease in Sessions.Values) lease._native.Cancel(lease._remote);
            Sessions.Clear();
        }
    }

    private static string KeyOf(string remote) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(remote)));
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
        UncConnectionLease? lease = null;
        if (!string.IsNullOrWhiteSpace(user))
        {
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{root}|{user}|{domain}|{password}")));
            lease = UncConnectionLease.Acquire(key, root!, user, password!, domain);
        }

        try
        {
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

    public static void ReleaseAll() => UncConnectionLease.ReleaseAll();

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        ValueTask.FromResult(File.Exists(file.FullPath));

    private static (string? User, string? Password, string? Domain) ParseCredential(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return (null, null, null);
        try
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            return (
                root.TryGetProperty("user", out var user) ? user.GetString() : null,
                root.TryGetProperty("password", out var password) ? password.GetString() : null,
                root.TryGetProperty("domain", out var domain) ? domain.GetString() : null);
        }
        catch (JsonException)
        {
            var split = secret.Split(':', 2);
            return (split[0], split.Length > 1 ? split[1] : null, null);
        }
    }
}
