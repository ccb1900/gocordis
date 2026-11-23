namespace Gocordis.Core;

/// <summary>
/// A declared component. Dependencies and capabilities are declared up front and
/// enforced at runtime; an undeclared Require or Provide is rejected.
/// </summary>
public abstract class Component
{
    private static readonly ICapabilityKey[] NoKeys = [];

    public abstract string Name { get; }

    public virtual IReadOnlyList<ICapabilityKey> Inject() => NoKeys;

    public virtual IReadOnlyList<ICapabilityKey> Provide() => NoKeys;

    /// <summary>
    /// Single-step activation. The returned cleanup is installed as one effect.
    /// Components needing per-effect control should implement
    /// <see cref="IIterativeComponent"/> instead.
    /// </summary>
    public abstract ValueTask<Cleanup?> ApplyAsync(Context context);
}

/// <summary>Paper §3 effect iterator: activation is a sequence of reversible steps.</summary>
public interface IIterativeComponent
{
    IAsyncEnumerable<EffectRegistration> IterateAsync(Context context);
}

public sealed record FiberSnapshot(
    Guid Id,
    string Name,
    FiberState State,
    int Generation,
    Guid? ParentId,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> BoundProviders,
    string? Failure)
{
    /// <summary>Revision counter: a realm reassignment creates a new fiber at a higher revision.</summary>
    public long Revision { get; init; }

    /// <summary>True when at least one inverse failed during this fiber's unwind.</summary>
    public bool CleanupIncomplete { get; init; }

    /// <summary>Messages of inverses that failed during unwind.</summary>
    public IReadOnlyList<string> CleanupFailures { get; init; } = [];

    /// <summary>Install/inverse failures attributed to an effect slot.</summary>
    public IReadOnlyList<EffectFailureRecord> EffectFailures { get; init; } = [];
}

public sealed record RuntimeSnapshot(IReadOnlyList<FiberSnapshot> Fibers)
{
    /// <summary>Cleanup failures are retained even after the offending fiber goes Gone.</summary>
    public IReadOnlyList<CleanupFailureRecord> CleanupFailures { get; init; } = [];

    /// <summary>Retained effect-level failures across the whole runtime.</summary>
    public IReadOnlyList<EffectFailureRecord> EffectFailures { get; init; } = [];
}

/// <summary>
/// Which effect slot failed, and with which exception. The original exception
/// object is retained for in-process diagnostics (never serialized to the UI);
/// its identity is unchanged so C33 failure semantics survive.
/// </summary>
public sealed record EffectFailureRecord(
    Guid FiberId,
    long ActivationId,
    int SlotIndex,
    string Label,
    bool IsInstall,
    string ExceptionType,
    string Message,
    Exception Exception,
    string? StackTrace);
