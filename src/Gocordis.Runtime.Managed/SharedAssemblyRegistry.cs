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
