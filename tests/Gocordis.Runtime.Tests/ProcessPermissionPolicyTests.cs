using System.Diagnostics;
using Gocordis.Runtime.Process;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Paper §6.3: the Process boundary has an explicit OS launch policy. It is
/// deliberately NOT a credential-drop sandbox — that boundary is documented,
/// not claimed. These tests prove the policy is actually applied at launch,
/// including the minimal (non-inherited) child environment.
/// </summary>
public sealed class ProcessPermissionPolicyTests
{
    private const string SecretName = "GOCORDIS_POLICY_SECRET_CANARY";
    private const string AllowedName = "GOCORDIS_POLICY_ALLOWED_CANARY";
    private const string ExplicitName = "GOCORDIS_POLICY_EXPLICIT_CANARY";

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
    public void DefaultPolicyClearsTheInheritedEnvironmentAndSetsWorkingDirectory()
    {
        WithParentVariables(("parent-value", "do-not-leak", "parent-value"), () =>
        {
            var start = new ProcessStartInfo
            {
                FileName = "/bin/true",
                UseShellExecute = false,
            };
            // ProcessStartInfo inherits the parent environment; a pre-seeded
            // value must be removed, not merely ignored by a consumer.
            start.Environment[SecretName] = "do-not-leak";

            var policy = new ProcessPermissionPolicy(WorkingDirectory: System.IO.Path.GetTempPath());
            policy.ApplyTo(start, "/bin/true");

            Assert.DoesNotContain(SecretName, start.Environment.Keys);
            Assert.DoesNotContain("PATH", start.Environment.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(System.IO.Path.GetTempPath(), start.WorkingDirectory);
            Assert.False(start.UseShellExecute);
        });
    }

    [Fact]
    public void ExplicitlyAllowedParentVariablesArePassedThroughButSecretsAreNot()
    {
        WithParentVariables(("parent-value", "do-not-leak", "parent-value"), () =>
        {
            var start = new ProcessStartInfo { FileName = "/bin/true", UseShellExecute = false };
            var policy = new ProcessPermissionPolicy(
                AllowedEnvironmentVariables: new HashSet<string>(StringComparer.Ordinal) { AllowedName });
            policy.ApplyTo(start, "/bin/true");

            Assert.Equal("parent-value", start.Environment[AllowedName]);
            Assert.DoesNotContain(SecretName, start.Environment.Keys);
            Assert.DoesNotContain("PATH", start.Environment.Keys, StringComparer.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ExplicitEnvironmentValuesOverrideTheParentWithoutInheritingSecrets()
    {
        WithParentVariables(("parent-value", "do-not-leak", "parent-value"), () =>
        {
            var start = new ProcessStartInfo { FileName = "/bin/true", UseShellExecute = false };
            var policy = ProcessPermissionPolicy.Default
                .WithEnvironmentVariable(ExplicitName, "explicit-value");
            policy.ApplyTo(start, "/bin/true");

            Assert.Equal("explicit-value", start.Environment[ExplicitName]);
            Assert.DoesNotContain(SecretName, start.Environment.Keys);
            Assert.DoesNotContain("PATH", start.Environment.Keys, StringComparer.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void HostCanAuthorizeAdditionalVariablesWithAnExplicitDictionary()
    {
        WithParentVariables(("parent-value", "do-not-leak", "parent-value"), () =>
        {
            var start = new ProcessStartInfo { FileName = "/bin/true", UseShellExecute = false };
            var policy = new ProcessPermissionPolicy(
                EnvironmentVariables: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ExplicitName] = "dictionary-value",
                });
            policy.ApplyTo(start, "/bin/true");

            Assert.Equal("dictionary-value", start.Environment[ExplicitName]);
            Assert.DoesNotContain(SecretName, start.Environment.Keys);
        });
    }

    [Fact]
    public async Task SpawnedChildDoesNotInheritParentSecrets()
    {
        if (OperatingSystem.IsWindows())
        {
            // The /bin/sh probe is Unix-only. Windows reparse/junction setup is
            // covered by the HMR tests; the ApplyTo assertions above are
            // platform-neutral and cover the environment contract.
            return;
        }

        await WithParentVariablesAsync(("parent-value", "do-not-leak", "parent-value"), async () =>
        {
            var secret = await ReadChildValueAsync(ProcessPermissionPolicy.Default, SecretName);
            Assert.Equal(string.Empty, secret);
        });
    }

    [Fact]
    public async Task SpawnedChildReceivesOnlyHostAuthorizedVariables()
    {
        if (OperatingSystem.IsWindows())
        {
            // Unix shell fixture; see the platform note in the preceding test.
            return;
        }

        await WithParentVariablesAsync(("parent-value", "do-not-leak", "parent-value"), async () =>
        {
            var allowed = new ProcessPermissionPolicy(
                AllowedEnvironmentVariables: new HashSet<string>(StringComparer.Ordinal) { AllowedName });
            Assert.Equal("parent-value", await ReadChildValueAsync(allowed, AllowedName));

            var injected = ProcessPermissionPolicy.Default.WithEnvironmentVariable(ExplicitName, "injected");
            Assert.Equal("injected", await ReadChildValueAsync(injected, ExplicitName));
            Assert.Equal(string.Empty, await ReadChildValueAsync(injected, SecretName));
        });
    }

    private static async Task<string> ReadChildValueAsync(ProcessPermissionPolicy policy, string name)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"printf '%s' \"${{{name}}}\"");
        policy.ApplyTo(start, "/bin/sh");

        using var child = System.Diagnostics.Process.Start(start)
                          ?? throw new InvalidOperationException("failed to start the environment probe");
        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync();
        Assert.Equal(0, child.ExitCode);
        return output;
    }

    private static void WithParentVariables(
        (string allowed, string secret, string explicitValue) values, Action body)
    {
        var previousAllowed = System.Environment.GetEnvironmentVariable(AllowedName);
        var previousSecret = System.Environment.GetEnvironmentVariable(SecretName);
        var previousExplicit = System.Environment.GetEnvironmentVariable(ExplicitName);
        System.Environment.SetEnvironmentVariable(AllowedName, values.allowed);
        System.Environment.SetEnvironmentVariable(SecretName, values.secret);
        System.Environment.SetEnvironmentVariable(ExplicitName, values.explicitValue);
        try
        {
            body();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(AllowedName, previousAllowed);
            System.Environment.SetEnvironmentVariable(SecretName, previousSecret);
            System.Environment.SetEnvironmentVariable(ExplicitName, previousExplicit);
        }
    }

    private static async Task WithParentVariablesAsync(
        (string allowed, string secret, string explicitValue) values, Func<Task> body)
    {
        var previousAllowed = System.Environment.GetEnvironmentVariable(AllowedName);
        var previousSecret = System.Environment.GetEnvironmentVariable(SecretName);
        var previousExplicit = System.Environment.GetEnvironmentVariable(ExplicitName);
        System.Environment.SetEnvironmentVariable(AllowedName, values.allowed);
        System.Environment.SetEnvironmentVariable(SecretName, values.secret);
        System.Environment.SetEnvironmentVariable(ExplicitName, values.explicitValue);
        try
        {
            await body();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(AllowedName, previousAllowed);
            System.Environment.SetEnvironmentVariable(SecretName, previousSecret);
            System.Environment.SetEnvironmentVariable(ExplicitName, previousExplicit);
        }
    }
}
