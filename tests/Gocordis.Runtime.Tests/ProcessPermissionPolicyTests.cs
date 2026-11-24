using System.Diagnostics;
using Gocordis.Runtime.Process;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Paper §6.3: the Process boundary has an explicit OS launch policy. It is
/// deliberately NOT a credential-drop sandbox — that boundary is documented,
/// not claimed. These tests prove the policy is actually applied at launch.
/// </summary>
public sealed class ProcessPermissionPolicyTests
{
    [Fact]
    public async Task StartAsyncAppliesThePolicyBeforeLaunchingAnyChild()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessPluginClient.StartAsync("relative-plugin-not-on-path"));
    }

    [Fact]
    public void DefaultPolicyRejectsRelativeExecutableAndShellExecution()
    {
        var policy = ProcessPermissionPolicy.Default;
        var relative = new ProcessStartInfo { FileName = "plugin", UseShellExecute = false };
        Assert.Throws<InvalidOperationException>(() => policy.ApplyTo(relative, "plugin"));

        var shell = new ProcessStartInfo { FileName = "/bin/sh", UseShellExecute = true };
        Assert.Throws<InvalidOperationException>(() => policy.ApplyTo(shell, "/bin/sh"));
    }

    [Fact]
    public void PolicyScrubsDeniedEnvironmentVariablesAndSetsWorkingDirectory()
    {
        var start = new ProcessStartInfo
        {
            FileName = "/bin/true",
            UseShellExecute = false,
        };
        start.Environment["GOCORDIS_SECRET"] = "do-not-leak";

        var policy = new ProcessPermissionPolicy(
            DeniedEnvironmentVariables: new HashSet<string>(StringComparer.Ordinal) { "GOCORDIS_SECRET" },
            WorkingDirectory: System.IO.Path.GetTempPath());
        policy.ApplyTo(start, "/bin/true");

        Assert.False(start.Environment.ContainsKey("GOCORDIS_SECRET"));
        Assert.Equal(System.IO.Path.GetTempPath(), start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
    }
}
