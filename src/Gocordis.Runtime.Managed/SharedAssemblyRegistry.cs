using System.Reflection;
using System.Runtime.Loader;

namespace Gocordis.Runtime.Managed;

/// <summary>
/// Promotes plugin-declared shared contract assemblies into the default load
/// context. <see cref="PluginLoadContext"/> returns <c>null</c> for a shared
/// name, which makes the runtime fall back to the default context; that only
/// yields one <c>Key&lt;T&gt;</c>/type identity across separate plugins when the
/// assembly actually lives in the default context. This registry loads the
/// payload copy there exactly once and rejects conflicting versions.
///
/// Without this, two packaged plugins each carrying their own Contracts.dll
/// would resolve two distinct static Key instances, so a consumer's
/// <c>Inject()</c> could never be satisfied by the provider's <c>Provide()</c>
/// — cross-plugin dependency gating would silently fail in the packaged form.
/// </summary>
public static class SharedAssemblyRegistry
{
    private static readonly object Sync = new();

    /// <summary>
    /// Ensures every named shared assembly is present in the default context,
    /// loading it from <paramref name="pluginDirectory"/>&lt;name&gt;.dll when
    /// absent. Returns the (possibly empty) set of names for the plugin ALC to
    /// treat as shared.
    /// </summary>
    /// <exception cref="SharedAssemblyConflictException">
    /// The same simple name is already loaded at a different assembly version.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// A declared shared assembly is missing from the plugin payload.
    /// </exception>
    public static IReadOnlyList<string> EnsureLoaded(
        IEnumerable<string>? names, string pluginDirectory)
    {
        if (names is null)
        {
            return [];
        }

        var requested = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        lock (Sync)
        {
            foreach (var name in requested)
            {
                var payload = Path.Combine(pluginDirectory, name + ".dll");
                if (!File.Exists(payload))
                {
                    throw new FileNotFoundException(
                        $"shared assembly '{name}' was declared but '{payload}' was not found in the plugin payload");
                }

                var declaredVersion = AssemblyName.GetAssemblyName(payload).Version;
                var loaded = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.Ordinal));

                if (loaded is not null)
                {
                    // A framework contract (e.g. Gocordis.Core) may already be
                    // in the default context at the host's version. The plugin
                    // copy must agree or typed keys would be ambiguous.
                    if (declaredVersion is not null && loaded.GetName().Version != declaredVersion)
                    {
                        throw new SharedAssemblyConflictException(
                            $"shared assembly '{name}' version conflict: default context has " +
                            $"{loaded.GetName().Version}, plugin payload requires {declaredVersion}. " +
                            "All plugins and the host must agree on one contract version.");
                    }

                    continue;
                }

                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(payload));
            }
        }

        return requested;
    }
}

/// <summary>A shared contract name resolves to incompatible versions across plugins.</summary>
public sealed class SharedAssemblyConflictException : InvalidOperationException
{
    public SharedAssemblyConflictException(string message) : base(message) { }
}
