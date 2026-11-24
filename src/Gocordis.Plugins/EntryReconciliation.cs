using Gocordis.Core;

namespace Gocordis.Plugins;

/// <summary>Paper §5.2.1: interception is read-time metadata, so a stable
/// MetaKey carries the entry's intercept annotation without a reload.</summary>
public static class EntryMetaKeys
{
    public static readonly MetaKey<string, string> Intercept = new(
        "gocordis.entry.intercept", string.Empty, static (current, incoming) =>
            string.IsNullOrEmpty(incoming) ? current : incoming);
}

/// <summary>Components opt into in-place config updates; otherwise the loader
/// uses the declaration-led retire/reinsert path (paper §5.2.1 config field).</summary>
public interface IConfigurableComponent
{
    ValueTask<bool> TryApplyConfigAsync(
        IReadOnlyDictionary<string, object?> config, CancellationToken cancellationToken = default);
}

public enum EntryChangeKind
{
    None,
    Rebuild,
    Rehome,
    Intercept,
    Config,
    Disabled,
}

/// <summary>Field-level dispatch for the paper's Entry fields. Fields that are
/// not part of an Entry never influence the decision; runtime state is not a
/// declaration.</summary>
public static class EntryDiff
{
    public static EntryChangeKind Classify(PluginManifest current, PluginManifest next)
    {
        if (current.Id != next.Id || current.Type != next.Type ||
            current.Url != next.Url || current.Version != next.Version ||
            current.Source != next.Source || current.Checksum != next.Checksum ||
            current.Backend != next.Backend || current.EntryAssembly != next.EntryAssembly ||
            current.EntryType != next.EntryType ||
            !SequenceEqual(current.Requires, next.Requires) ||
            !SequenceEqual(current.Provides, next.Provides) ||
            !SequenceEqual(current.CapabilitiesOrEmpty, next.CapabilitiesOrEmpty) ||
            !SequenceEqual(current.SharedAssemblies ?? [], next.SharedAssemblies ?? []))
        {
            return EntryChangeKind.Rebuild;
        }

        if (!string.Equals(current.Isolate, next.Isolate, StringComparison.Ordinal))
        {
            return EntryChangeKind.Rehome;
        }

        if (!string.Equals(current.Intercept, next.Intercept, StringComparison.Ordinal) ||
            !string.Equals(current.ParentId, next.ParentId, StringComparison.Ordinal))
        {
            return EntryChangeKind.Intercept;
        }

        if (!ConfigEqual(current.Config, next.Config))
        {
            return EntryChangeKind.Config;
        }

        return current.Enabled == next.Enabled
            ? EntryChangeKind.None
            : EntryChangeKind.Disabled;
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
