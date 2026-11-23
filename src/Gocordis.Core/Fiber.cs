using System.Collections.Concurrent;

namespace Gocordis.Core;

/// <summary>Runtime-owned instance of one component. Nobody outside Core writes its state.</summary>
public sealed class Fiber
{
    private readonly ConcurrentDictionary<ICapabilityKey, ProviderBinding> _committed =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<Fiber> _children = [];
    private readonly Dictionary<ICapabilityKey, Realm> _realmOverrides =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ICapabilityKey, MetaState> _metadata =
        new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource _gone =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Fiber(Guid id, Component component, Fiber? parent, int generation)
    {
        Id = id;
        Component = component;
        Parent = parent;
        Generation = generation;
    }

    public Guid Id { get; }
    public Component Component { get; }
    public Fiber? Parent { get; }
    public FiberState State { get; internal set; } = FiberState.Pending;
    public LifecycleIntent Intent { get; internal set; } = LifecycleIntent.Load;

    /// <summary>Legacy counter; ActivationId is the authoritative identity.</summary>
    public int Generation { get; internal set; }

    public Exception? Failure { get; internal set; }
    internal bool IsActivating { get; set; }

    internal Activation? CurrentActivation { get; set; }
    internal long ActivationId => CurrentActivation?.Id ?? 0;

    public IReadOnlyList<Fiber> Children => _children;
    internal List<Fiber> ChildList => _children;
    internal IDictionary<ICapabilityKey, object?> Provided { get; } =
        new Dictionary<ICapabilityKey, object?>(ReferenceEqualityComparer.Instance);
    internal ConcurrentDictionary<ICapabilityKey, ProviderBinding> Committed => _committed;

    internal Realm Realm { get; set; } = Realm.Default;
    internal IDictionary<ICapabilityKey, Realm> RealmOverrides => _realmOverrides;
    internal IDictionary<ICapabilityKey, MetaState> Metadata => _metadata;

    internal sealed class MetaState
    {
        public object? Declared { get; set; }
        public object? Override { get; set; }
    }

    internal void AddChild(Fiber child) => _children.Add(child);
    internal void RemoveChild(Fiber child) => _children.Remove(child);
    internal void SignalGone() => _gone.TrySetResult();
    public Task GoneAsync() => _gone.Task;
}
