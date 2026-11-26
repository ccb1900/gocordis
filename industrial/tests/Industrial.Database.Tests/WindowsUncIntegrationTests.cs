using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using Microsoft.Data.Sqlite;

namespace Industrial.Database.Tests;

/// <summary>Runs only on Windows with a real SMB share. On other hosts the
/// skip reason is explicit; this is an environment blocker, not a hidden gap.</summary>
public sealed class WindowsUncFactAttribute : FactAttribute
{
    public WindowsUncFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows UNC/SMB session lease requires Windows (net use)";
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOCORDIS_UNC_PATH")))
        {
            Skip = "Set GOCORDIS_UNC_PATH, GOCORDIS_UNC_USER, GOCORDIS_UNC_PASSWORD " +
                   "and optionally GOCORDIS_UNC_DOMAIN to a real SMB share";
        }
    }
}

public sealed class WindowsUncIntegrationTests
{
    [WindowsUncFact]
    public async Task UncSourceDiscoversFilesThroughARealSessionLease()
    {
        var root = Environment.GetEnvironmentVariable("GOCORDIS_UNC_PATH")!;
        var user = Environment.GetEnvironmentVariable("GOCORDIS_UNC_USER") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("GOCORDIS_UNC_PASSWORD") ?? string.Empty;
        var domain = Environment.GetEnvironmentVariable("GOCORDIS_UNC_DOMAIN");
        var dbRoot = Path.Combine(Path.GetTempPath(), "industrial-unc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbRoot);
        try
        {
            Func<IndustrialDbContext> db = () =>
                IndustrialStore.Create(Path.Combine(dbRoot, "unc-test.db"));
            var vault = new CredentialVault(db, allowDevelopmentKey: true);
            var secret = System.Text.Json.JsonSerializer.Serialize(new
            {
                user, password, domain,
            });
            await vault.PutAsync("unc-cred", "SMB", "username-password", secret);
            var resources = new ResourceRegistry(db);
            await resources.RegisterAsync(new ResourceDefinition(
                "unc-source", "filesystem.unc", "SMB share", "local",
                new Dictionary<string, object?> { ["root"] = root },
                "unc-cred", 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

            var source = new UncFileSource(resources, vault);
            var files = await source.DiscoverAsync("unc-source",
                new SourceDateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddDays(1)));
            Assert.NotEmpty(files);
            Assert.All(files, file => Assert.Equal("unc-source", file.ResourceId));
        }
        finally
        {
            // Windows keeps pooled SQLite handles alive briefly; release them
            // before removing the temporary database directory.
            SqliteConnection.ClearAllPools();
            Directory.Delete(dbRoot, recursive: true);
        }
    }
}
