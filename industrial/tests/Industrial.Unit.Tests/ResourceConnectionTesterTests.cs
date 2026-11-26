using Industrial.Contracts;
using Industrial.Core;
using Industrial.Persistence;

namespace Industrial.Unit.Tests;

public sealed class ResourceConnectionTesterTests
{
    [Fact]
    public async Task LocalDirectoryIsTestedWithoutWritingBusinessData()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-resource-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "recent.csv"), "record_id,reading\n1,42\n");
            var dbPath = Path.Combine(root, "vault.db");
            var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
            var tester = new ResourceConnectionTester(vault);
            var resource = new ResourceDefinition(
                "source", "filesystem.local", "Source", "local",
                new Dictionary<string, object?> { ["root"] = root }, null, 0, true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            var result = await tester.TestAsync(resource);

            Assert.True(result.Ok);
            Assert.Contains("目录可访问", result.Detail, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "IndustrialRecords")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingDirectoryReturnsActionableMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "industrial-resource-test-" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(Path.GetTempPath(), "industrial-vault-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
            var tester = new ResourceConnectionTester(vault);
            var resource = new ResourceDefinition(
                "missing", "filesystem.local", "Missing", "local",
                new Dictionary<string, object?> { ["root"] = root }, null, 0, true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            var result = await tester.TestAsync(resource);

            Assert.False(result.Ok);
            Assert.Contains("找不到目录", result.Detail, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task UncTestEstablishesCredentialedSessionBeforeProbing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "industrial-vault-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var vault = new CredentialVault(() => IndustrialStore.Create(dbPath), allowDevelopmentKey: true);
            await vault.PutAsync("unc-cred", "SMB", "username-password",
                "{\"user\":\"operator\",\"password\":\"secret\",\"domain\":\"factory\"}");
            var native = new FakeNetworkConnection();
            var tester = new ResourceConnectionTester(vault, native);
            var resource = new ResourceDefinition(
                "unc", "filesystem.unc", "UNC", "local",
                new Dictionary<string, object?> { ["root"] = @"\\factory\share" },
                "unc-cred", 0, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            await tester.TestAsync(resource);

            Assert.Equal(1, native.Adds);
            Assert.Equal(1, native.Cancels);
            Assert.Equal("factory\\operator", native.LastUser);
            Assert.Equal("secret", native.LastPassword);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private sealed class FakeNetworkConnection : INativeNetworkConnection
    {
        public int Adds { get; private set; }
        public int Cancels { get; private set; }
        public string? LastUser { get; private set; }
        public string? LastPassword { get; private set; }

        public int Add(string remote, string userName, string password)
        {
            Adds++;
            LastUser = userName;
            LastPassword = password;
            return 0;
        }

        public int Cancel(string remote)
        {
            Cancels++;
            return 0;
        }
    }
}
