using System.Text.Json;
using Industrial.Contracts;
using Industrial.Persistence;

namespace Industrial.Core;

/// <summary>Reads resource configuration and the vault once, at sink creation.
/// Node configuration only carries a resource id; the secret is never copied
/// into workflow JSON, UI payloads or the ledger.</summary>
internal static class ResourceConnection
{
    public static string? Value(ResourceDefinition resource, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var pair in resource.Configuration)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value?.ToString()))
                {
                    return pair.Value!.ToString();
                }
            }
        }
        return null;
    }

    public static async ValueTask<(string User, string Password)> CredentialsAsync(
        ResourceDefinition resource, ICredentialVault vault, CancellationToken ct)
    {
        if (resource.CredentialId is null)
        {
            return (string.Empty, string.Empty);
        }
        var secret = await vault.RevealAsync(resource.CredentialId, ct);
        if (string.IsNullOrWhiteSpace(secret)) return (string.Empty, string.Empty);
        if (secret.TrimStart().StartsWith('{'))
        {
            using var document = JsonDocument.Parse(secret);
            var root = document.RootElement;
            string Pick(params string[] keys) => keys
                .Select(key => root.TryGetProperty(key, out var value) ? value.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            return (Pick("username", "user", "uid"), Pick("password", "pwd"));
        }
        var split = secret.IndexOf(':');
        return split < 0
            ? (string.Empty, secret)
            : (secret[..split], secret[(split + 1)..]);
    }

    public static string Require(ResourceDefinition resource, params string[] keys) =>
        Value(resource, keys) ?? throw new InvalidOperationException(
            $"resource '{resource.Id}' is missing required configuration '{string.Join("/", keys)}'");
}

public sealed class SqlServerSinkFactory : IRecordSinkFactory
{
    public string ResourceType => "sqlserver";

    /// <summary>Pure resolution of a SQL Server Resource and its credential.
    /// Exposed so a test can prove the secret comes from the vault, not from
    /// workflow JSON, without needing a live SQL Server.</summary>
    public static async ValueTask<string> BuildConnectionStringAsync(
        ResourceDefinition resource, ICredentialVault vault, CancellationToken ct = default)
    {
        var (user, password) = await ResourceConnection.CredentialsAsync(resource, vault, ct);
        var server = ResourceConnection.Require(resource, "server", "host", "dataSource", "data_source");
        var database = ResourceConnection.Require(resource, "database", "initialCatalog", "initial_catalog");
        return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            UserID = user,
            Password = password,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public async ValueTask<IRecordSink> CreateAsync(
        ResourceDefinition resource, ICredentialVault vault,
        IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default)
    {
        var table = ResourceConnection.Value(resource, "table") ?? "IndustrialRecords";
        var config = new NodeConfigReader(nodeConfig);
        var connectionString = await BuildConnectionStringAsync(resource, vault, ct);
        return new SqlServerRecordSink(connectionString, table, resource.Id,
            config.GetString("writeMode", "append"), config.GetString("businessKey"));
    }
}

public sealed class PostgreSqlSinkFactory : IRecordSinkFactory
{
    public string ResourceType => "postgresql";

    public static async ValueTask<string> BuildConnectionStringAsync(
        ResourceDefinition resource, ICredentialVault vault, CancellationToken ct = default)
    {
        var (user, password) = await ResourceConnection.CredentialsAsync(resource, vault, ct);
        var host = ResourceConnection.Require(resource, "host", "server");
        var database = ResourceConnection.Require(resource, "database");
        var port = ResourceConnection.Value(resource, "port") ?? "5432";
        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }

    public async ValueTask<IRecordSink> CreateAsync(
        ResourceDefinition resource, ICredentialVault vault,
        IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default) =>
        new PostgreSqlRecordSink(
            await BuildConnectionStringAsync(resource, vault, ct), resource.Id);
}

public sealed class MySqlSinkFactory : IRecordSinkFactory
{
    public string ResourceType => "mysql";

    public static async ValueTask<string> BuildConnectionStringAsync(
        ResourceDefinition resource, ICredentialVault vault, CancellationToken ct = default)
    {
        var (user, password) = await ResourceConnection.CredentialsAsync(resource, vault, ct);
        var host = ResourceConnection.Require(resource, "host", "server");
        var database = ResourceConnection.Require(resource, "database");
        var port = ResourceConnection.Value(resource, "port") ?? "3306";
        return $"Server={host};Port={port};Database={database};Uid={user};Pwd={password}";
    }

    public async ValueTask<IRecordSink> CreateAsync(
        ResourceDefinition resource, ICredentialVault vault,
        IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default) =>
        new MySqlRecordSink(
            await BuildConnectionStringAsync(resource, vault, ct), resource.Id);
}

public sealed class SqliteSinkFactory(Func<IndustrialTargetDbContext> db) : IRecordSinkFactory
{
    public string ResourceType => "sqlite";

    public ValueTask<IRecordSink> CreateAsync(
        ResourceDefinition resource, ICredentialVault vault,
        IReadOnlyDictionary<string, object?>? nodeConfig = null, CancellationToken ct = default) =>
        ValueTask.FromResult<IRecordSink>(new SqliteRecordSink(db, resource.Id));
}

/// <summary>Archive is a separate capability (IArchiveSink): the coordinator
/// archives the DiscoveredFile itself, so no record-to-file adapter is needed.</summary>
