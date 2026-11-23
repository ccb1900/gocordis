using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Gocordis.Runtime.Managed;

/// <summary>
/// Full identity of a shared contract assembly. Simple name + version alone is
/// not enough: two different builds can share both. We compare culture, public
/// key token, module MVID and the payload's SHA-256 so a same-named, same-
/// versioned but different contract cannot silently change the Key&lt;T&gt;
/// identity that every plugin resolves through the default load context.
/// </summary>
public sealed record SharedAssemblyIdentity(
    string Name,
    Version? Version,
    string? Culture,
    string PublicKeyToken,
    Guid Mvid,
    string Sha256)
{
    /// <summary>Reads identity without loading the assembly into any ALC.</summary>
    public static SharedAssemblyIdentity FromFile(string path)
    {
        var full = Path.GetFullPath(path);
        var name = AssemblyName.GetAssemblyName(full);
        var token = name.GetPublicKeyToken();
        var tokenText = token is { Length: > 0 }
            ? Convert.ToHexString(token).ToLowerInvariant()
            : string.Empty;

        Guid mvid;
        using (var stream = File.OpenRead(full))
        using (var pe = new PEReader(stream))
        {
            mvid = pe.GetMetadataReader().GetGuid(pe.GetMetadataReader().GetModuleDefinition().Mvid);
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        return new SharedAssemblyIdentity(
            name.Name ?? string.Empty, name.Version, name.CultureName, tokenText, mvid, hash);
    }

    /// <summary>Identity of an already-loaded assembly (from its file location).</summary>
    public static SharedAssemblyIdentity FromLoaded(Assembly assembly)
    {
        if (string.IsNullOrWhiteSpace(assembly.Location) || !File.Exists(assembly.Location))
        {
            // Dynamic/in-memory assemblies cannot be verified against a file;
            // report an empty hash so the comparison treats it as unverifiable.
            var n = assembly.GetName();
            var token = n.GetPublicKeyToken();
            return new SharedAssemblyIdentity(
                n.Name ?? string.Empty, n.Version, n.CultureName,
                token is { Length: > 0 } ? Convert.ToHexString(token).ToLowerInvariant() : string.Empty,
                assembly.ManifestModule.ModuleVersionId, string.Empty);
        }

        return FromFile(assembly.Location) with { Mvid = assembly.ManifestModule.ModuleVersionId };
    }

    /// <summary>
    /// Strict equality on every verifiable field. An empty hash (unverifiable
    /// in-memory assembly) is skipped; all other fields must match exactly.
    /// </summary>
    public bool Matches(SharedAssemblyIdentity other) =>
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        Equals(Version, other.Version) &&
        string.Equals(Culture, other.Culture, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(PublicKeyToken, other.PublicKeyToken, StringComparison.OrdinalIgnoreCase) &&
        Mvid == other.Mvid &&
        (Sha256.Length == 0 || other.Sha256.Length == 0 ||
         string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase));

    public string Describe() =>
        $"{Name} v{(Version is null ? "?" : Version.ToString())} culture={Culture ?? "neutral"} pkt={PublicKeyToken} mvid={Mvid} sha256={Sha256}";
}

/// <summary>
/// Promotes plugin-declared shared contract assemblies into the default load
/// context. <see cref="PluginLoadContext"/> returns <c>null</c> for a shared
/// name, which makes the runtime fall back to the default context; that only
/// yields one <c>Key&lt;T&gt;</c>/type identity across separate plugins when the
/// assembly actually lives in the default context. Without this, two packaged
/// plugins each carrying their own Contracts.dll would resolve two distinct
/// static Key instances and cross-plugin dependency gating would silently fail.
///
/// TRUST BOUNDARY: managed plugins are **trusted code**, not a sandbox. The
/// collectible plugin ALC is not an isolation boundary; use Process/WASM for
/// untrusted code. This registry is defense against *accidental* identity
/// collisions, not a security boundary: only host-approved names may be
/// promoted (<see cref="Allow(string)"/>), and every payload must match the
/// already-loaded identity on all fields.
///
/// LIFETIME: a shared assembly promoted here lives in
/// <see cref="AssemblyLoadContext.Default"/> for the **process lifetime**. It
/// is never unloaded with the plugin ALC. Replacing a shared contract (new
/// bytes/MVID) therefore requires a controlled **host restart**; there is no
/// in-process hot swap for CLR contract assemblies. Browser-side frontend
/// modules have their own `?v=` revision/HMR path — that is a different
/// mechanism and does not apply here.
/// </summary>
public static class SharedAssemblyRegistry
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // Framework contracts are always shared by PluginLoadContext.
        "Gocordis.Core", "Gocordis.Plugins",
    };
    // name → identity the HOST sealed into Default ALC before any plugin loaded.
    private static readonly Dictionary<string, SharedAssemblyIdentity> Sealed =
        new(StringComparer.Ordinal);

    /// <summary>Approve a simple assembly name for process-level sharing. The
    /// host calls this for names declared by installed (and thus verified)
    /// packages; arbitrary plugin code cannot approve itself.</summary>
    public static void Allow(string simpleName)
    {
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            lock (Sync)
            {
                Allowed.Add(simpleName);
            }
        }
    }

    public static void Allow(IEnumerable<string>? simpleNames)
    {
        if (simpleNames is null) return;
        foreach (var name in simpleNames) Allow(name);
    }

    public static bool IsAllowed(string simpleName)
    {
        lock (Sync) return Allowed.Contains(simpleName);
    }

    /// <summary>
    /// HOST-ONLY contract sealing. Called once during application assembly,
    /// BEFORE any plugin fiber is activated. For every shared contract name the
    /// host supplies ALL candidate copies (one per declaring package). The
    /// registry verifies every candidate has the same full identity (name,
    /// version, culture, public key token, MVID and SHA-256), then loads ONE
    /// canonical copy into the Default ALC and records that identity as sealed.
    ///
    /// After sealing, plugin loads can only REUSE the sealed identity — no
    /// "first plugin wins" promotion remains. A package whose contract differs
    /// from the sealed identity is rejected at activation with a clear conflict.
    /// This is the product rule for process-level contract version governance.
    /// </summary>
    /// <param name="candidates">simple name → all candidate DLL paths supplied by installed packages</param>
    /// <returns>the sealed identities, keyed by simple name</returns>
    /// <exception cref="SharedAssemblyConflictException">
    /// Candidates for one name do not share one identity, or a name is already
    /// sealed with a different identity (contract upgrade needs a host restart).
    /// </exception>
    public static IReadOnlyDictionary<string, SharedAssemblyIdentity> Seal(
        IReadOnlyDictionary<string, IReadOnlyList<string>> candidates)
    {
        lock (Sync)
        {
            foreach (var (name, paths) in candidates)
            {
                var copies = (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToArray();
                if (copies.Length == 0)
                {
                    throw new FileNotFoundException($"shared contract '{name}' has no candidate DLL supplied by the host");
                }

                var identities = new List<SharedAssemblyIdentity>(copies.Length);
                foreach (var path in copies)
                {
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException($"shared contract '{name}' candidate not found: '{path}'");
                    }

                    var identity = SharedAssemblyIdentity.FromFile(path);
                    if (!string.Equals(identity.Name, name, StringComparison.Ordinal))
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared contract '{name}' candidate '{path}' declares assembly name '{identity.Name}'");
                    }

                    identities.Add(identity);
                }

                var canonical = identities[0];
                for (var i = 1; i < identities.Count; i++)
                {
                    if (!canonical.Matches(identities[i]))
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared contract '{name}' has more than one identity across installed packages: " +
                            $"[{canonical.Describe()}] vs [{identities[i].Describe()}]. " +
                            "The host must resolve one contract identity before activation.");
                    }
                }

                if (Sealed.TryGetValue(name, out var already))
                {
                    if (!already.Matches(canonical))
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared contract '{name}' is already sealed as [{already.Describe()}] and cannot be " +
                            "replaced in-process; upgrading a contract requires a host restart.");
                    }

                    continue;
                }

                var loaded = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));
                if (loaded is null)
                {
                    // The HOST chooses the canonical copy — not the first plugin
                    // that happens to load.
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(copies[0]));
                }
                else if (!canonical.Matches(SharedAssemblyIdentity.FromLoaded(loaded)))
                {
                    throw new SharedAssemblyConflictException(
                        $"shared contract '{name}' conflicts with an assembly already loaded in the default context; " +
                        "a contract upgrade requires a host restart.");
                }

                Allowed.Add(name);
                Sealed[name] = canonical;
            }

            return new Dictionary<string, SharedAssemblyIdentity>(Sealed, StringComparer.Ordinal);
        }
    }

    /// <summary>The identity the host sealed for a contract, or null if unsealed.</summary>
    public static SharedAssemblyIdentity? SealedIdentity(string name)
    {
        lock (Sync) return Sealed.TryGetValue(name, out var identity) ? identity : null;
    }

    /// <summary>
    /// Ensures every named shared assembly is present in the default context,
    /// loading it from <paramref name="pluginDirectory"/>&lt;name&gt;.dll when
    /// absent. Returns the (possibly empty) set of names for the plugin ALC to
    /// treat as shared.
    /// </summary>
    /// <exception cref="SharedAssemblyNotAllowedException">
    /// The name was not approved by the host.
    /// </exception>
    /// <exception cref="SharedAssemblyConflictException">
    /// A same-named assembly is already loaded with a different full identity.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// A declared shared assembly is missing from the plugin payload.
    /// </exception>
    public static IReadOnlyList<string> EnsureLoaded(
        IEnumerable<string>? names, string pluginDirectory)
    {
        if (names is null) return [];

        var requested = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0) return [];

        lock (Sync)
        {
            foreach (var name in requested)
            {
                // 1) Host approval gate — no arbitrary promotion into Default ALC.
                if (!Allowed.Contains(name))
                {
                    throw new SharedAssemblyNotAllowedException(
                        $"shared assembly '{name}' is not host-approved; only approved contract " +
                        "assemblies may be promoted into the default load context");
                }

                var payload = Path.Combine(pluginDirectory, name + ".dll");
                if (!File.Exists(payload))
                {
                    throw new FileNotFoundException(
                        $"shared assembly '{name}' was declared but '{payload}' was not found in the plugin payload");
                }

                var declared = SharedAssemblyIdentity.FromFile(payload);
                if (!string.Equals(declared.Name, name, StringComparison.Ordinal))
                {
                    throw new SharedAssemblyConflictException(
                        $"shared assembly name '{name}' does not match payload identity '{declared.Name}'");
                }

                // A sealed contract is host-owned: verify against the sealed
                // identity and NEVER load the plugin's own copy. This removes
                // the "first plugin wins" behaviour entirely.
                if (Sealed.TryGetValue(name, out var sealedIdentity))
                {
                    if (!declared.Matches(sealedIdentity))
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared contract '{name}' in this plugin [{declared.Describe()}] differs from the " +
                            $"host-sealed identity [{sealedIdentity.Describe()}]. " +
                            "Contract upgrades require a host restart.");
                    }

                    continue; // reuse the host-sealed Default ALC instance
                }

                var loaded = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));
                if (loaded is not null)
                {
                    var existing = SharedAssemblyIdentity.FromLoaded(loaded);
                    if (!declared.Matches(existing))
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared assembly '{name}' identity conflict: default context has " +
                            $"[{existing.Describe()}], plugin payload requires [{declared.Describe()}]. " +
                            "All plugins and the host must agree on one contract identity; " +
                            "upgrading a shared contract requires a host restart.");
                    }

                    continue; // identical identity — one shared instance already present
                }

                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(payload));
            }
        }

        return requested;
    }
}

/// <summary>A shared contract name was not approved by the host.</summary>
public sealed class SharedAssemblyNotAllowedException : InvalidOperationException
{
    public SharedAssemblyNotAllowedException(string message) : base(message) { }
}

/// <summary>A shared contract name resolves to a different full identity across plugins.</summary>
public sealed class SharedAssemblyConflictException : InvalidOperationException
{
    public SharedAssemblyConflictException(string message) : base(message) { }
}
