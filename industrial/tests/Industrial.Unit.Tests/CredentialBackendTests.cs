using System.Text.Json;
using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;
using MySqlConnector;
using Npgsql;

namespace Industrial.Unit.Tests;

/// <summary>Per-backend credential shape verification. These tests cover the
/// parsing/authentication path without requiring live database servers.</summary>
public sealed class CredentialBackendTests
{
    [Fact]
    public async Task SqlServerConnectionStringUsesRevealedCredential()
    {
        var dbPath = TempDb();
        var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
        await vault.PutAsync("sql", "SQL", CredentialKinds.DomainAccount,
            "{\"username\":\"operator\",\"password\":\"secret\",\"domain\":\"factory\"}");
        var resource = new ResourceDefinition("sql-target", "sqlserver", "SQL", "fixture",
            new Dictionary<string, object?> { ["server"] = "sql.local", ["database"] = "industrial" },
            "sql", 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var connection = await SqlServerSinkFactory.BuildConnectionStringAsync(resource, vault);
        Assert.Contains("Data Source=sql.local", connection, StringComparison.Ordinal);
        Assert.Contains("User ID=operator", connection, StringComparison.Ordinal);
        Assert.Contains("Password=secret", connection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSqlConnectionStringUsesRevealedCredential()
    {
        var dbPath = TempDb();
        var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
        const string password = "pg;se=cret 'with spaces'";
        await vault.PutAsync("pg", "PG", CredentialKinds.UsernamePassword,
            JsonSerializer.Serialize(new { username = "reader", password }));
        var resource = new ResourceDefinition("pg-target", "postgresql", "PG", "fixture",
            new Dictionary<string, object?> { ["host"] = "pg.local", ["database"] = "industrial" },
            "pg", 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var parsed = new NpgsqlConnectionStringBuilder(
            await PostgreSqlSinkFactory.BuildConnectionStringAsync(resource, vault));
        Assert.Equal("pg.local", parsed.Host);
        Assert.Equal("reader", parsed.Username);
        Assert.Equal(password, parsed.Password);
        Assert.Equal(5432, parsed.Port);
    }

    [Fact]
    public async Task MySqlConnectionStringUsesRevealedCredential()
    {
        var dbPath = TempDb();
        var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
        const string password = "my;sql=pa ss'word";
        await vault.PutAsync("mysql", "MySQL", CredentialKinds.UsernamePassword,
            JsonSerializer.Serialize(new { username = "writer", password }));
        var resource = new ResourceDefinition("mysql-target", "mysql", "MySQL", "fixture",
            new Dictionary<string, object?> { ["host"] = "mysql.local", ["database"] = "industrial" },
            "mysql", 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var parsed = new MySqlConnectionStringBuilder(
            await MySqlSinkFactory.BuildConnectionStringAsync(resource, vault));
        Assert.Equal("mysql.local", parsed.Server);
        Assert.Equal("writer", parsed.UserID);
        Assert.Equal(password, parsed.Password);
        Assert.Equal(3306u, parsed.Port);
    }

    [Fact]
    public void UncCredentialParserUnderstandsEveryStructuredKind()
    {
        var (user, password, domain) = UncFileSource.ParseCredential(
            "{\"username\":\"operator\",\"password\":\"secret\",\"domain\":\"factory\"}");
        Assert.Equal("operator", user);
        Assert.Equal("secret", password);
        Assert.Equal("factory", domain);
        var (tokenUser, tokenPassword, _) = UncFileSource.ParseCredential("token-user:token-secret");
        Assert.Equal("token-user", tokenUser);
        Assert.Equal("token-secret", tokenPassword);
    }

    [Fact]
    public async Task SqliteNeedsNoCredentialAndTestReportsConfigurationError()
    {
        var resource = new ResourceDefinition("sqlite-target", "sqlite", "SQLite", "fixture",
            new Dictionary<string, object?>(), null, 0, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var tester = new ResourceConnectionTester(
            new CredentialVault(() => IndustrialStore.Create(TempDb()), allowDevelopmentKey: true));
        var result = await tester.TestAsync(resource);
        Assert.False(result.Ok);
        Assert.Contains("database", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqliteResourceOpensWithNoCredentialAndReportsRealFile()
    {
        var dbPath = TempDb();
        await using (var store = IndustrialStore.Create(dbPath)) { }
        var resource = new ResourceDefinition("sqlite-real", "sqlite", "SQLite", "fixture",
            new Dictionary<string, object?> { ["database"] = dbPath }, null, 0, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var tester = new ResourceConnectionTester(
            new CredentialVault(() => IndustrialStore.Create(TempDb()), allowDevelopmentKey: true));
        var result = await tester.TestAsync(resource);
        Assert.True(result.Ok, result.Detail);
        Assert.Contains("SQLite", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), $"industrial-cred-{Guid.NewGuid():N}.db");
}
