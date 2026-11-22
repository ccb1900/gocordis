using System.Collections.Concurrent;

namespace Gocordis.Core;

/// <summary>Runtime-owned instance of one component. Nobody outside Core writes its state.</summary>
public sealed class Fiber
{
    private readonly List<Func<ValueTask>> _inverses = [];
    private readonly Dictionary<ICapabilityKey, object?> _provided =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<ICapabilityKey, Fiber> _committed =
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

    public int Generation { get; internal set; }

    public Exception? Failure { get; internal set; }

    internal bool IsActivating { get; set; }

    public IReadOnlyList<Fiber> Children => _children;

    internal List<Fiber> ChildList => _children;

    internal List<Func<ValueTask>> Inverses => _inverses;

    internal IDictionary<ICapabilityKey, object?> Provided => _provided;

    internal IDictionary<ICapabilityKey, Fiber> Committed => _committed;

    internal CancellationTokenSource? ActivationCancellation { get; set; }

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

    internal void AddInverse(Func<ValueTask> inverse) => _inverses.Add(inverse);

    internal void SignalGone() => _gone.TrySetResult();

    public Task GoneAsync() => _gone.Task;
}
