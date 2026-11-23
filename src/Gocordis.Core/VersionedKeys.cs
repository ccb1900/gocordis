using NuGet.Versioning;

namespace Gocordis.Core;

/// <summary>
/// Paper §6.6: a key carrying an interface version and (for consumers) a
/// peer-dependency range. Instead of nominally matching key identity only,
/// resolution also checks a semantic-version constraint using NuGet.Versioning.
/// </summary>
public interface IVersionedKey : ICapabilityKey
{
    NuGetVersion Version { get; }

    VersionRange? Constraint { get; }

    bool Accepts(NuGetVersion candidate);
}

public sealed class VersionedKey<T> : CapabilityKey, IVersionedKey
{
    public VersionedKey(string name, string version, string? constraint = null) : base(name)
    {
        Version = NuGetVersion.Parse(version);
        Constraint = constraint is null ? null : VersionRange.Parse(constraint);
    }

    public NuGetVersion Version { get; }

    /// <summary>Null on a provider key; a peer range on a consumer key.</summary>
    public VersionRange? Constraint { get; }

    public override Type ValueType => typeof(T);

    public bool Accepts(NuGetVersion candidate) =>
        Constraint is null ? Version == candidate : Constraint.Satisfies(candidate);
}
