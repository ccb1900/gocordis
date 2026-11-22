namespace Gocordis.Core;

/// <summary>Identity of a capability or event key. Equality is reference identity.</summary>
public interface ICapabilityKey
{
    string Name { get; }
    Type ValueType { get; }
}

public abstract class CapabilityKey : ICapabilityKey
{
    protected CapabilityKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public abstract Type ValueType { get; }

    public override string ToString() => $"{GetType().Name}({Name})";
}

public sealed class Key<T> : CapabilityKey
{
    public Key(string name) : base(name) { }

    public override Type ValueType => typeof(T);
}

public sealed class EventKey<T> : CapabilityKey
{
    public EventKey(string name) : base(name) { }

    public override Type ValueType => typeof(T);
}

public enum FiberState
{
    Pending,
    Loading,
    Active,
    Unloading,
    Failed,
    Gone,
}

public enum LifecycleIntent
{
    Load,
    Retired,
}

/// <summary>Inverse of an installed effect. Inverses run in strict LIFO order.</summary>
public delegate ValueTask Cleanup();

/// <summary>
/// One step of an effect iterator: install an effect and return its inverse.
/// A single <see cref="Component.ApplyAsync"/> is adapted to exactly one step.
/// </summary>
public sealed record EffectRegistration(Func<ValueTask<Func<ValueTask>?>> Install)
{
    public static EffectRegistration From(Func<ValueTask> install, Func<ValueTask> inverse)
    {
        return new EffectRegistration(async () =>
        {
            await install();
            return inverse;
        });
    }
}

public sealed class DependencyMissingException : Exception
{
    public DependencyMissingException(string message) : base(message) { }
}

public sealed class UndeclaredCapabilityException : Exception
{
    public UndeclaredCapabilityException(string message) : base(message) { }
}

public sealed class DuplicateProviderException : Exception
{
    public DuplicateProviderException(string message) : base(message) { }
}

public sealed class DependencyCycleException : Exception
{
    public DependencyCycleException(string message) : base(message) { }
}

/// <summary>
/// A target changed while an activation was in flight. This is not a failure:
/// the runtime unwinds what was installed and returns the fiber to Pending.
/// </summary>
public sealed class DivertedException : Exception
{
    public DivertedException() : base("activation target changed") { }
}
