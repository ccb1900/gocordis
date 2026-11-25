using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Industrial.Contracts;

namespace Industrial.Core;

/// <summary>
/// Windows SMB session for a UNC resource. The lease owns the session so
/// multiple workflows share one connection per resource, avoiding error 1219
/// (same server, different credentials) in one logon session. It deliberately
/// uses the Windows SMB client rather than reimplementing SMB.
/// </summary>
public sealed class UncConnectionLease : IAsyncDisposable
{
    private readonly string _uncPath;
    private bool _disposed;

    public UncConnectionLease(string uncPath, string userName, string password, string? domain = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("UNC session leases require the Windows SMB client");
        }

        if (!uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("UNC path must start with \\\\server\\share", nameof(uncPath));
        }

        _uncPath = uncPath;
        var account = string.IsNullOrWhiteSpace(domain) ? userName : $"{domain}\\{userName}";
        RunNetUse($"\"{uncPath}\"", "/user:", $"\"{account}\"", $"\"{password}\"");
    }

    public static string LocalPath(string uncPath, string relativePath) =>
        Path.Combine(uncPath, relativePath);

    private static void RunNetUse(params string[] arguments)
    {
        var start = new ProcessStartInfo("net")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Unicode,
        };
        start.ArgumentList.Add("use");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("cannot start net use");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"net use failed: {output}");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        RunNetUse($"\"{_uncPath}\"", "/delete", "/y");
        return ValueTask.CompletedTask;
    }
}

/// <summary>Caches one SMB session per UNC share and credential, so several
/// workflows never establish conflicting sessions with the same server.</summary>
public sealed class UncFileSource : IFileSource
{
    private static readonly ConcurrentDictionary<string, UncConnectionLease> Leases = new(StringComparer.Ordinal);
    private readonly IResourceRegistry _resources;
    private readonly ICredentialVault? _vault;

    public UncFileSource(IResourceRegistry resources, ICredentialVault? vault = null)
    {
        _resources = resources;
        _vault = vault;
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
        if (!string.IsNullOrWhiteSpace(user))
        {
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{root}|{user}|{domain}|{password}")));
            Leases.GetOrAdd(key, _ => new UncConnectionLease(root, user, password!, domain));
        }

        var result = new List<DiscoveredFile>();
        var files = Directory.EnumerateFiles(root, "*.csv", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.xlsx", SearchOption.AllDirectories));
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var identity = FileIdentityFactory.Create(path, root);
            var sourceDate = FileIdentityFactory.ParseSourceDate(Path.GetFileName(path)) ?? identity.LastWriteUtc;
            if (sourceDate < range.FromInclusive || sourceDate >= range.ToExclusive)
            {
                continue;
            }

            result.Add(new DiscoveredFile(identity, path, sourceDate, resource.Id));
        }

        return result.OrderBy(file => file.SourceDate).ToArray();
    }

    public static void ReleaseAll()
    {
        foreach (var lease in Leases.Values)
        {
            lease.DisposeAsync().GetAwaiter().GetResult();
        }

        Leases.Clear();
    }

    public ValueTask<bool> IsStableAsync(
        DiscoveredFile file, TimeSpan quietPeriod, CancellationToken ct = default) =>
        ValueTask.FromResult(File.Exists(file.FullPath));

    private static (string? User, string? Password, string? Domain) ParseCredential(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return (null, null, null);
        }

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
