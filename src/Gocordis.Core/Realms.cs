namespace Gocordis.Core;

/// <summary>Paper Def 24: a namespace in which a key resolves to one provider.</summary>
public sealed class Realm
{
    public static readonly Realm Default = new("default");

    public Realm(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Paper Def 26/27: a per-key monoid used to intercept a capability's value
/// without changing its resolution. Identity and combine are supplied by the
/// capability owner; the context override is always applied last (right-biased).
/// </summary>
public interface IMetaKey : ICapabilityKey;

public sealed class MetaKey<T, M> : CapabilityKey, IMetaKey
{
    public MetaKey(string name, M identity, Func<M, M, M> combine) : base(name)
    {
        Identity = identity;
        Combine = combine ?? throw new ArgumentNullException(nameof(combine));
    }

    public M Identity { get; }

    public Func<M, M, M> Combine { get; }

    public override Type ValueType => typeof(T);
}
