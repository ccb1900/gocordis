using System.Text.Json.Serialization;

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

    /// <summary>Type of the fiber failure, kept beside its message so observers
    /// can distinguish two failures with identical text.</summary>
    public string? FailureType { get; init; }

    /// <summary>True when at least one inverse failed during this fiber's unwind.</summary>
    public bool CleanupIncomplete { get; init; }

    /// <summary>Messages of inverses that failed during unwind.</summary>
    public IReadOnlyList<string> CleanupFailures { get; init; } = [];

    /// <summary>Install/inverse failures attributed to an effect slot.</summary>
    public IReadOnlyList<EffectFailureRecord> EffectFailures { get; init; } = [];

    /// <summary>
    /// Reference identities of the declared inject keys, mirrored to
    /// <see cref="Requires"/> by index. A capability is not identified by its
    /// name alone: its Key instance and value type are part of the contract.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ICapabilityKey> RequiredKeys { get; init; } = [];

    /// <summary>Reference identities of the declared provide keys, mirrored to <see cref="Provides"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<ICapabilityKey> ProvidedKeys { get; init; } = [];

    /// <summary>
    /// The fiber's home realm plus per-key routes. Capability visibility is a
    /// paper-observable dimension (Def 24/ADR-0001): no ancestor fallback.
    /// </summary>
    [JsonIgnore]
    public Realm HomeRealm { get; init; } = global::Gocordis.Core.Realm.Default;

    [JsonIgnore]
    public IReadOnlyList<KeyRoute> Routes { get; init; } = [];

    /// <summary>
    /// Committed bindings projected with the provider's full semantic identity
    /// instead of a name-only substitute for its generative fiber id.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<CapabilityBindingSnapshot> SemanticBindings { get; init; } = [];
}

/// <summary>One per-key realm route: reference identity of both ends matters.</summary>
public sealed record KeyRoute(ICapabilityKey Key, Realm Realm);

/// <summary>
/// Semantic identity of a binding source. Generative FiberId/ActivationId are
/// excluded; the provider's name, current state, home realm and declared key
/// identities are retained.
/// </summary>
public sealed record ProviderIdentitySnapshot(
    string Name,
    FiberState State,
    Realm Realm,
    IReadOnlyList<ICapabilityKey> Requires,
    IReadOnlyList<ICapabilityKey> Provides);

/// <summary>A committed capability binding: which key resolved to which provider.</summary>
public sealed record CapabilityBindingSnapshot(ICapabilityKey Key, ProviderIdentitySnapshot Provider);

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
