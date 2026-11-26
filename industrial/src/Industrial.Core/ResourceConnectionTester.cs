using System.Net.Sockets;
using Industrial.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Industrial.Core;

/// <summary>Opens or inspects a configured resource without writing business
/// data. The result is user-facing: technical exceptions are classified into
/// an actionable message while the UI stays free of driver details.</summary>
public sealed class ResourceConnectionTester(
    ICredentialVault vault, INativeNetworkConnection? native = null) : IResourceConnectionTester
{
    public async ValueTask<ResourceTestResult> TestAsync(
        ResourceDefinition resource, CancellationToken ct = default)
    {
        try
        {
            switch (resource.Type.Trim().ToLowerInvariant())
            {
                case "filesystem.local":
                {
                    return await TestDirectoryAsync(
                        ResourceConnection.Require(resource, "root", "path"));
                }
                case "filesystem.unc":
                {
                    var root = ResourceConnection.Require(resource, "root", "path");
                    // A UNC resource must be probed with the same credentialed
                    // WNet session that real collection uses, not the process identity.
                    if (resource.CredentialId is null)
                    {
                        return await TestDirectoryAsync(root);
                    }
                    var secret = await vault.RevealAsync(resource.CredentialId, ct);
                    var (user, password, domain) = UncFileSource.ParseCredential(secret);
                    if (string.IsNullOrWhiteSpace(user))
                    {
                        return new ResourceTestResult(false, "凭证中没有用户名称，无法建立 SMB 会话。");
                    }
                    var lease = UncConnectionLease.Acquire(root, user, password ?? string.Empty, domain, native);
                    try
                    {
                        return await TestDirectoryAsync(root);
                    }
                    finally
                    {
                        await lease.DisposeAsync();
                    }
                }
                case "sqlserver":
                {
                    var connectionString = await SqlServerSinkFactory.BuildConnectionStringAsync(resource, vault, ct);
                    await using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync(ct);
                    return new ResourceTestResult(true, "SQL Server 连接成功");
                }
                case "postgresql":
                {
                    var connectionString = await PostgreSqlSinkFactory.BuildConnectionStringAsync(resource, vault, ct);
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(ct);
                    return new ResourceTestResult(true, "PostgreSQL 连接成功");
                }
                case "mysql":
                {
                    var connectionString = await MySqlSinkFactory.BuildConnectionStringAsync(resource, vault, ct);
                    await using var connection = new MySqlConnection(connectionString);
                    await connection.OpenAsync(ct);
                    return new ResourceTestResult(true, "MySQL 连接成功");
                }
                case "sqlite":
                {
                    var database = ResourceConnection.Require(resource, "database", "path");
                    await using var connection = new SqliteConnection($"Data Source={database}");
                    await connection.OpenAsync(ct);
                    return new ResourceTestResult(true, "SQLite 数据库可打开");
                }
                default:
                    return new ResourceTestResult(false, $"暂不支持连接测试的资源类型：{resource.Type}");
            }
        }
        catch (Exception ex)
        {
            return new ResourceTestResult(false, Explain(ex));
        }
    }

    private static ValueTask<ResourceTestResult> TestDirectoryAsync(string root)
    {
        if (!Directory.Exists(root))
        {
            return ValueTask.FromResult(new ResourceTestResult(false, $"找不到目录：{root}"));
        }
        var files = Directory.EnumerateFiles(root).Take(10).ToArray();
        return ValueTask.FromResult(new ResourceTestResult(true,
            files.Length == 0 ? "目录存在，但尚未发现文件" : $"目录可访问，发现至少 {files.Length} 个文件"));
    }

    private static string Explain(Exception ex) => ex switch
    {
        SocketException => "无法连接目标地址，请检查地址、端口和网络。",
        SqlException => "数据库拒绝连接，请检查地址、数据库名、账号和密码。",
        NpgsqlException => "数据库拒绝连接，请检查地址、数据库名、账号和密码。",
        MySqlException => "数据库拒绝连接，请检查地址、数据库名、账号和密码。",
        DirectoryNotFoundException or IOException => "目录不可访问，请检查路径和权限。",
        UnauthorizedAccessException => "没有访问权限，请检查账号和共享权限。",
        OperationCanceledException => "连接测试已取消。",
        _ => ex.Message,
    };
}
