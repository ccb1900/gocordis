namespace Gocordis.Core;

/// <summary>
/// One activation = one run cycle of one fiber (paper Def 58 episode).
/// Fiber is the long-lived identity; Activation is the short-lived one.
/// All mutable activation state is owned by the Runtime decision domain.
/// </summary>
internal sealed class Activation
{
    internal Activation(long id, Fiber fiber, Runtime runtime, CancellationTokenSource cancellation)
    {
        Id = id;
        Fiber = fiber;
        Runtime = runtime;
        Context = new Context(this);
        Cancellation = cancellation;
    }

    internal long Id { get; }
    internal Fiber Fiber { get; }
    internal Runtime Runtime { get; }
    internal Context Context { get; }
    internal CancellationTokenSource Cancellation { get; }
    internal ActivationPhase Phase { get; set; } = ActivationPhase.Installing;

    /// <summary>Unwind requested while Apply was in flight (inertial divert).</summary>
    internal bool UnwindRequested { get; set; }

    /// <summary>Apply finished (success or failure); no longer in flight.</summary>
    internal bool ApplyLanded { get; set; }

    /// <summary>Effects installed by this activation, in install order; inverses run LIFO.</summary>
    internal List<EffectSlot> Effects { get; } = [];

    internal List<ProviderBinding> Dependencies { get; set; } = [];

    internal bool IsCurrent => Fiber.CurrentActivation == this;
}

internal enum ActivationPhase
{
    Installing,
    Committed,
    Unwinding,
    Unwound,
}

internal sealed class EffectSlot
{
    internal EffectSlot(Func<ValueTask>? inverse)
    {
        Inverse = inverse;
        State = inverse is null ? EffectState.Committed : EffectState.Installing;
    }

    internal Func<ValueTask>? Inverse { get; }
    internal EffectState State { get; set; }
}

/// <summary>
/// Identity-resolved dependency binding (paper: provider = Fiber + Activation).
/// </summary>
internal sealed record ProviderBinding(
    ICapabilityKey Key,
    Guid FiberId,
    long ActivationId,
    Fiber Fiber,
    object? Value);
