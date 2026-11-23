namespace Gocordis.Packaging;

/// <summary>
/// Local trust policy for package installation. Signatures prove provenance,
/// not sandboxing: Managed is trusted code, Process/WASM are isolation
/// boundaries but carry no OS-level privilege restriction here.
/// </summary>
/// <param name="RequireSignature">Reject unsigned packages when true.</param>
/// <param name="TrustedPublicKeys">
/// PEM public keys accepted as signers. Empty means "any valid signature" when
/// RequireSignature is true; callers that need publisher pinning supply keys.
/// </param>
public sealed record PluginTrustPolicy(
    bool RequireSignature = false,
    IReadOnlyList<string>? TrustedPublicKeys = null)
{
    /// <summary>Local development: unsigned packages are accepted.</summary>
    public static PluginTrustPolicy Development { get; } = new(false, null);

    /// <summary>Trusted-only mode: a valid signature from a pinned key is required.</summary>
    public static PluginTrustPolicy TrustedOnly(params string[] publicKeys) =>
        new(true, publicKeys);
}
