namespace Gocordis.Packaging;

/// <summary>
/// Local trust policy for package installation. Signatures prove provenance,
/// not sandboxing: Managed is trusted code, Process/WASM are isolation
/// boundaries but carry no OS-level privilege restriction here.
/// </summary>
/// <param name="RequireSignature">Reject unsigned packages when true.</param>
/// <param name="TrustedPublicKeys">
/// PEM public keys accepted as signers. REQUIRED for <see cref="TrustedOnly"/>:
/// a policy that merely checks for a signature field is not a trust policy.
/// </param>
public sealed record PluginTrustPolicy(
    bool RequireSignature = false,
    IReadOnlyList<string>? TrustedPublicKeys = null)
{
    /// <summary>Local development: unsigned packages are accepted.</summary>
    public static PluginTrustPolicy Development { get; } = new(false, null);

    /// <summary>Trusted-only mode: a valid signature from a pinned key is required.</summary>
    public static PluginTrustPolicy TrustedOnly(params string[] publicKeys)
    {
        if (publicKeys.Length == 0 || publicKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "TrustedOnly requires at least one pinned public key", nameof(publicKeys));
        }

        return new PluginTrustPolicy(true, publicKeys);
    }

    /// <summary>
    /// Single trust-configuration decision for the CLI, the product runner and
    /// any embedding host. PEM files are read here so every entry point parses
    /// --trust/--public-key identically; no second trust engine is introduced.
    /// </summary>
    public static PluginTrustPolicy FromPemFiles(string? mode, IEnumerable<string>? pemFiles)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode == "development")
        {
            return Development;
        }

        if (mode != "trusted-only")
        {
            throw new ArgumentException(
                $"unknown trust mode '{mode}'; use development or trusted-only", nameof(mode));
        }

        var keys = (pemFiles ?? [])
            .Select(path => File.ReadAllText(path))
            .ToArray();
        if (keys.Length == 0)
        {
            throw new ArgumentException(
                "trusted-only requires at least one --public-key <pem> file", nameof(pemFiles));
        }

        return TrustedOnly(keys);
    }
}
