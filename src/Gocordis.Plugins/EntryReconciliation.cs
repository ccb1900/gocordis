using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Paper §5.2.1: interception is read-time metadata, so a stable
/// MetaKey carries the entry's intercept annotation without a reload.</summary>
public static class EntryMetaKeys
{
    public static readonly MetaKey<string, string> Intercept = new(
        "gocordis.entry.intercept", string.Empty, static (current, incoming) =>
            string.IsNullOrEmpty(incoming) ? current : incoming);

    /// <summary>The configuration-tree parent id is also read-time metadata;
    /// it never changes a fiber's capability set or realm.</summary>
    public static readonly MetaKey<string, string> Parent = new(
        "gocordis.entry.parent", string.Empty, static (current, incoming) =>
            string.IsNullOrEmpty(incoming) ? current : incoming);
}

/// <summary>Components opt into in-place config updates; otherwise the loader
/// uses the declaration-led retire/reinsert path (paper §5.2.1 config field).</summary>
public interface IConfigurableComponent
{
    ValueTask<bool> TryApplyConfigAsync(
        IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Every Entry field that differs between two declarations. A declaration
/// batch can change several fields at once, so this is a set of independent
/// flags, NOT a single priority-ordered label. The coordinator decides one
/// plan that honours all of them; a low-priority field can never swallow
/// <see cref="Disabled"/>.
/// </summary>
public readonly record struct EntryDelta(
    bool Rebuild,
    bool Rehome,
    bool InterceptChanged,
    bool ParentChanged,
    bool ConfigChanged,
    bool EnabledChanged,
    bool Disabled)
{
    public bool IsEmpty => !Rebuild && !Rehome && !InterceptChanged &&
                           !ParentChanged && !ConfigChanged && !EnabledChanged;

    public bool HasMetaChange => InterceptChanged || ParentChanged;

    /// <summary>True when the current fiber cannot survive this declaration.</summary>
    public bool RetiresFiber => Rebuild || Rehome || Disabled;
}

/// <summary>Field-level dispatch for the paper's Entry fields. Fields that are
/// not part of an Entry never influence the decision; runtime state is not a
/// declaration.</summary>
public static class EntryDiff
{
    /// <summary>Computes all field differences independently. Callers must plan
    /// from the whole delta: <c>Disabled</c> is the target state and wins over
    /// config/meta/rehome edits, while rebuild wins over rehome.</summary>
    public static EntryDelta Classify(PluginManifest current, PluginManifest next)
    {
        var rebuild = current.Id != next.Id || current.Type != next.Type ||
            current.Url != next.Url || current.Version != next.Version ||
            current.Source != next.Source || current.Checksum != next.Checksum ||
            current.Backend != next.Backend || current.EntryAssembly != next.EntryAssembly ||
            current.EntryType != next.EntryType ||
            !SequenceEqual(current.Requires, next.Requires) ||
            !SequenceEqual(current.Provides, next.Provides) ||
            !SequenceEqual(current.CapabilitiesOrEmpty, next.CapabilitiesOrEmpty) ||
            !SequenceEqual(current.SharedAssemblies ?? [], next.SharedAssemblies ?? []);

        return new EntryDelta(
            Rebuild: rebuild,
            Rehome: !string.Equals(current.Isolate, next.Isolate, StringComparison.Ordinal),
            InterceptChanged: !string.Equals(current.Intercept, next.Intercept, StringComparison.Ordinal),
            ParentChanged: !string.Equals(current.ParentId, next.ParentId, StringComparison.Ordinal),
            ConfigChanged: !ConfigEqual(current.Config, next.Config),
            EnabledChanged: current.Enabled != next.Enabled,
            Disabled: !next.Enabled);
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConfigEqual(
        IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }
}
