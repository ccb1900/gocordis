using System.Threading.Channels;
using NuGet.Versioning;

namespace Gocordis.Core;

/// <summary>
/// The single lifecycle authority. A Channel-backed decision loop owns lifecycle
/// state; component Apply/Unwind code runs in independent Tasks and reports back
/// with commands carrying an ActivationId. Stale completions are ignored.
/// </summary>
public sealed class Runtime : IAsyncDisposable
{
    // ---- internal command protocol -----------------------------------------

    private abstract record RuntimeCommand;

    private sealed record MutateCommand(Action Mutation, TaskCompletionSource Reply) : RuntimeCommand;

    private sealed record LoadCommand(
        Component Component, Fiber? Parent, Realm? Realm,
        TaskCompletionSource<Fiber> Reply, bool AwaitActivation) : RuntimeCommand
    {
        internal Fiber? Created { get; set; }
    }

    private sealed record DisposeCommand(Fiber Fiber, TaskCompletionSource Reply) : RuntimeCommand;
    private sealed record CloseCommand(TaskCompletionSource Reply) : RuntimeCommand;

    /// <summary>Per-key route declaration, applied only inside the decision domain.</summary>
    private sealed record IsolateCommand(
        Fiber Fiber, ICapabilityKey Key, Realm Realm, TaskCompletionSource Reply) : RuntimeCommand;

    /// <summary>
    /// Paper C34 revision composite: retire the old fiber, deactivate it,
    /// O-Remove its records and reinsert the same-name component at a fresh
    /// realm/identity. All steps run on this command loop.
    /// </summary>
    private sealed record RehomeCommand(
        Fiber Fiber,
        Realm Realm,
        Component? Replacement,
        TaskCompletionSource<Fiber> Reply) : RuntimeCommand
    {
        internal Dictionary<ICapabilityKey, Realm> CapturedOverrides { get; set; } = [];
        internal Dictionary<ICapabilityKey, Fiber.MetaState> CapturedMetadata { get; set; } = [];
    }

    private sealed record StepRequestCommand(
        Activation Activation, TaskCompletionSource<bool> Reply) : RuntimeCommand;

    private sealed record StepInstalledCommand(
        Activation Activation, Func<ValueTask>? Inverse, TaskCompletionSource Reply) : RuntimeCommand;

    private sealed record ApplyCompletedCommand(Activation Activation, Exception? Error) : RuntimeCommand;
    private sealed record UnwindCompletedCommand(Activation Activation, Exception? Error) : RuntimeCommand;

    private sealed record VersionedBinding(
        Realm Realm, string Name, Type ValueType, NuGetVersion Version,
        Fiber Provider, long ActivationId, object Value);

    private sealed record Handler(Fiber Owner, long ActivationId, Func<object?, ValueTask> Invoke);

    // ---- state (decision domain; short lock shared with user-side writes) ---

    private readonly Channel<RuntimeCommand> _commands =
        Channel.CreateUnbounded<RuntimeCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private Task? _loop;
    private readonly object _loopGate = new();

    private readonly object _stateLock = new();

    private readonly Dictionary<Guid, Fiber> _fibers = [];
    private readonly Dictionary<ICapabilityKey, Fiber> _providers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Realm, Dictionary<ICapabilityKey, Fiber>> _realmProviders = [];
    private readonly List<VersionedBinding> _versionedProviders = [];
    private readonly Dictionary<ICapabilityKey, List<Handler>> _handlers =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<long, Activation> _activations = [];
    private readonly Dictionary<Guid, List<TaskCompletionSource>> _disposeWaiters = [];
    private readonly List<LoadCommand> _awaitingLoads = [];
    private readonly Dictionary<Guid, RehomeCommand> _pendingRehomes = [];
    private readonly List<CleanupFailureRecord> _cleanupFailures = [];
    private readonly Dictionary<long, int> _unwindWaiters = [];
    private readonly HashSet<long> _unwinding = [];
    private readonly HashSet<long> _unwindRunning = [];
    private readonly HashSet<long> _applying = [];
    private readonly HashSet<long> _justCommitted = [];

    /// <summary>
    /// Public replies are completed only after Reconcile/PublishSnapshot, so an
    /// awaited Dispose/Load/Close never observes a stale snapshot.
    /// </summary>
    private readonly List<Action> _pendingReplies = [];

    private long _nextActivationId;
    private bool _bindingsChanged;
    private bool _closing;
    private bool _closed;
    private readonly List<TaskCompletionSource> _closeWaiters = [];
    private volatile RuntimeSnapshot _snapshot = new([]);

    /// <summary>
    /// Bounded observation ring of canonical lifecycle transitions. The kernel
    /// assigns the monotonic Sequence; consumers read <see cref="RuntimeEventRing.Snapshot"/>
    /// through the console's <c>runtime/events</c> query. Retention lives here,
    /// not in the decision loop's hot path beyond one append.
    /// </summary>
    public RuntimeEventRing Events { get; } = new();

    public Runtime() { }

    private void EnsureLoop()
    {
        if (_loop is not null)
        {
            return;
        }

        lock (_loopGate)
        {
            _loop ??= Task.Run(RunAsync);
        }
    }

    private bool TryWrite(RuntimeCommand command)
    {
        EnsureLoop();
        return _commands.Writer.TryWrite(command);
    }

    // ---- public API ---------------------------------------------------------

    public Task<Fiber> LoadAsync(Component component, Fiber? parent = null, Realm? realm = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        var reply = new TaskCompletionSource<Fiber>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new LoadCommand(component, parent, realm, reply, AwaitActivation: true)))
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        return reply.Task;
    }

    /// <summary>
    /// Paper C34: reassign a fiber to a realm as a revision composite. The old
    /// fiber is retired and deactivated, its records are removed, then a NEW
    /// fiber with the same name/component is reinserted at the new realm. The
    /// returned task completes only when the replacement is Active.
    /// </summary>
    public Task<Fiber> RehomeAsync(Fiber fiber, Realm realm, Component? replacement = null)
    {
        ArgumentNullException.ThrowIfNull(fiber);
        ArgumentNullException.ThrowIfNull(realm);
        if (replacement is not null &&
            !string.Equals(replacement.Name, fiber.Component.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("a rehome must reinsert the same component name", nameof(replacement));
        }

        var reply = new TaskCompletionSource<Fiber>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new RehomeCommand(fiber, realm, replacement, reply)))
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        return reply.Task;
    }

    internal ValueTask IsolateAsync(Fiber fiber, ICapabilityKey key, Realm realm)
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new IsolateCommand(fiber, key, realm, reply)))
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        return new ValueTask(reply.Task);
    }

    internal Task<Fiber> LoadChildAsync(Component component, Fiber parent)
    {
        var reply = new TaskCompletionSource<Fiber>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new LoadCommand(component, parent, null, reply, AwaitActivation: false)))
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        return reply.Task;
    }

    public Task DisposeAsync(Fiber fiber)
    {
        ArgumentNullException.ThrowIfNull(fiber);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new DisposeCommand(fiber, reply)))
        {
            return Task.CompletedTask;
        }

        return reply.Task;
    }

    public Task CloseAsync()
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new CloseCommand(reply)))
        {
            return Task.CompletedTask;
        }

        return reply.Task;
    }

    public RuntimeSnapshot Snapshot() => _snapshot;

    public Fiber? GetFiber(Guid id)
    {
        lock (_stateLock)
        {
            return _fibers.TryGetValue(id, out var fiber) ? fiber : null;
        }
    }

    /// <summary>
    /// Read-only capability lookup for composition roots. Resolves the default
    /// realm's committed provider under the state lock and returns its provided
    /// value. It records no binding, installs no effect and changes no lifecycle
    /// state: dependency declarations and reconciliation remain the only way to
    /// govern activation.
    /// </summary>
    public T RequireCapability<T>(Key<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_stateLock)
        {
            var provider = FindProvider(Realm.Default, key);
            if (provider is null ||
                provider.State != FiberState.Active ||
                provider.CurrentActivation?.Phase != ActivationPhase.Committed ||
                !provider.Provided.TryGetValue(key, out var value))
            {
                throw new DependencyMissingException(
                    $"no active provider for capability '{key.Name}'");
            }

            return (T)value!;
        }
    }

    // ---- decision loop ------------------------------------------------------

    private async Task RunAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync())
            {
                try
                {
                    _pendingReplies.Clear();
                    lock (_stateLock)
                    {
                        _justCommitted.Clear();
                        switch (command)
                        {
                            case MutateCommand mutate:
                                mutate.Mutation();
                                _pendingReplies.Add(() => mutate.Reply.TrySetResult());
                                break;
                            case LoadCommand load:
                                HandleLoad(load);
                                break;
                            case DisposeCommand dispose:
                                HandleDispose(dispose);
                                break;
                            case CloseCommand close:
                                HandleClose(close);
                                break;
                            case IsolateCommand isolate:
                                HandleIsolate(isolate);
                                break;
                            case RehomeCommand rehome:
                                HandleRehome(rehome);
                                break;
                            case StepRequestCommand request:
                                HandleStepRequest(request);
                                break;
                            case StepInstalledCommand installed:
                                HandleStepInstalled(installed);
                                break;
                            case ApplyCompletedCommand applied:
                                HandleApplyCompleted(applied);
                                break;
                            case UnwindCompletedCommand unwound:
                                HandleUnwindCompleted(unwound);
                                break;
                        }

                        Reconcile();
                        PublishSnapshot();
                        FlushAwaitingLoads();
                    }

                    CompletePendingReplies();
                }
                catch (Exception ex)
                {
                    Fail(command, ex);
                    CompletePendingReplies();
                }
            }
        }
        finally
        {
            _closed = true;
        }
    }

    private void Fail(RuntimeCommand command, Exception ex)
    {
        switch (command)
        {
            case LoadCommand load:
                load.Reply.TrySetException(ex);
                break;
            case DisposeCommand dispose:
                dispose.Reply.TrySetException(ex);
                break;
            case CloseCommand close:
                close.Reply.TrySetException(ex);
                break;
            case IsolateCommand isolate:
                isolate.Reply.TrySetException(ex);
                break;
            case RehomeCommand rehome:
                rehome.Reply.TrySetException(ex);
                _pendingRehomes.Remove(rehome.Fiber.Id);
                break;
            case StepRequestCommand request:
                request.Reply.TrySetResult(false);
                break;
            case StepInstalledCommand installed:
                installed.Reply.TrySetException(ex);
                break;
        }
    }

    private Fiber HandleLoad(LoadCommand command)
    {
        if (_closing)
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        if (command.Parent is not null && !_fibers.ContainsKey(command.Parent.Id))
        {
            throw new InvalidOperationException("parent fiber does not belong to this runtime");
        }

        // Child inherits the PARENT realm and per-key routes (paper §4.4, Go baseline).
        var realm = command.Realm ?? command.Parent?.Realm ?? Realm.Default;
        ValidateDeclarations(command.Component, realm);

        var fiber = new Fiber(Guid.NewGuid(), command.Component, command.Parent, generation: 0)
        {
            Realm = realm,
            CapturedInjectKeys = [.. command.Component.Inject()],
            CapturedProvideKeys = [.. command.Component.Provide()],
            CapturedRequires = [.. command.Component.Inject().Select(k => k.Name)],
            CapturedProvides = [.. command.Component.Provide().Select(k => k.Name)],
        };
        if (command.Parent is not null)
        {
            foreach (var pair in command.Parent.RealmOverrides)
            {
                fiber.RealmOverrides[pair.Key] = pair.Value;
            }

            foreach (var pair in command.Parent.Metadata)
            {
                fiber.Metadata[pair.Key] = new Fiber.MetaState
                {
                    Declared = pair.Value.Declared,
                    Override = pair.Value.Override,
                };
            }
        }

        _fibers[fiber.Id] = fiber;
        command.Parent?.AddChild(fiber);
        Events.Append(nameof(RuntimeEventType.FiberCreated), fiber.Id, 0);

        var cycle = FindCycle();
        if (cycle is not null)
        {
            _fibers.Remove(fiber.Id);
            command.Parent?.RemoveChild(fiber);
            throw new DependencyCycleException($"dependency cycle: {cycle}");
        }

        command.Created = fiber;
        if (command.AwaitActivation)
        {
            _awaitingLoads.Add(command);
        }
        else
        {
            _pendingReplies.Add(() => command.Reply.TrySetResult(fiber));
        }

        _bindingsChanged = true;
        return fiber;
    }

    private void HandleDispose(DisposeCommand command)
    {
        var fiber = command.Fiber;
        if (!_fibers.TryGetValue(fiber.Id, out var current) || !ReferenceEquals(current, fiber))
        {
            _pendingReplies.Add(() => command.Reply.TrySetResult());
            return;
        }

        fiber.Intent = LifecycleIntent.Retired;
        var activation = fiber.CurrentActivation;
        if (activation is null || activation.Phase == ActivationPhase.Unwound ||
            fiber.State is FiberState.Gone or FiberState.Failed or FiberState.Pending)
        {
            fiber.State = FiberState.Gone;
            _fibers.Remove(fiber.Id);
            fiber.Parent?.RemoveChild(fiber);
            fiber.SignalGone();
            _pendingReplies.Add(() => command.Reply.TrySetResult());
            _bindingsChanged = true;
            return;
        }

        if (_disposeWaiters.TryGetValue(fiber.Id, out var waiters))
        {
            // A second Dispose joins the in-flight teardown instead of
            // replacing the first caller's completion.
            waiters.Add(command.Reply);
            return;
        }

        StartUnwind(fiber, dependencyLoss: false);
        _disposeWaiters[fiber.Id] = [command.Reply];
    }

    private void HandleClose(CloseCommand command)
    {
        if (_closed)
        {
            _pendingReplies.Add(() => command.Reply.TrySetResult());
            return;
        }

        _closing = true;
        _closeWaiters.Add(command.Reply);
        foreach (var fiber in _fibers.Values.ToArray())
        {
            fiber.Intent = LifecycleIntent.Retired;
            var activation = fiber.CurrentActivation;
            if (activation is null || activation.Phase == ActivationPhase.Unwound)
            {
                fiber.State = FiberState.Gone;
                _fibers.Remove(fiber.Id);
                fiber.Parent?.RemoveChild(fiber);
                fiber.SignalGone();
                CompleteDispose(fiber);
            }
            else
            {
                StartUnwind(fiber, dependencyLoss: false);
            }
        }

        CheckCloseComplete();
    }

    private void HandleIsolate(IsolateCommand command)
    {
        if (_closing)
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        if (!_fibers.TryGetValue(command.Fiber.Id, out var current) ||
            !ReferenceEquals(current, command.Fiber))
        {
            command.Reply.TrySetException(new DivertedException());
            return;
        }

        // The decision domain owns fiber state; Context only declares the route.
        command.Fiber.RealmOverrides[command.Key] = command.Realm;
        _bindingsChanged = true;
        _pendingReplies.Add(() => command.Reply.TrySetResult());
    }

    private void HandleRehome(RehomeCommand command)
    {
        var fiber = command.Fiber;
        if (_closing)
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        if (!_fibers.TryGetValue(fiber.Id, out var current) || !ReferenceEquals(current, fiber) ||
            fiber.State != FiberState.Active || fiber.CurrentActivation?.Phase != ActivationPhase.Committed)
        {
            command.Reply.TrySetException(new InvalidOperationException("rehome requires an Active fiber"));
            return;
        }

        if (_pendingRehomes.ContainsKey(fiber.Id))
        {
            command.Reply.TrySetException(new InvalidOperationException("rehome already in progress"));
            return;
        }

        // Snapshot the ownership position before the old fiber is torn down:
        // per-key routes and metadata are inherited by the reinserted fiber,
        // except for declared keys, which follow the fresh realm.
        command.CapturedOverrides = new Dictionary<ICapabilityKey, Realm>(
            fiber.RealmOverrides, ReferenceEqualityComparer.Instance);
        command.CapturedMetadata = new Dictionary<ICapabilityKey, Fiber.MetaState>(
            fiber.Metadata, ReferenceEqualityComparer.Instance);
        _pendingRehomes[fiber.Id] = command;

        // Paper C34: retire -> deactivate (LIFO inverses, children before
        // parent) -> O-Remove. The reinsert step runs when the old fiber's
        // unwind reports completion; see CompleteRehome.
        fiber.Intent = LifecycleIntent.Retired;
        StartUnwind(fiber, dependencyLoss: false);
    }

    private void CompleteRehome(Fiber old, RehomeCommand command)
    {
        var component = command.Replacement ?? old.Component;
        var replacement = new Fiber(Guid.NewGuid(), component, old.Parent, old.Generation + 1)
        {
            Realm = command.Realm,
            Revision = old.Revision + 1,
            CapturedInjectKeys = [.. component.Inject()],
            CapturedProvideKeys = [.. component.Provide()],
            CapturedRequires = [.. component.Inject().Select(k => k.Name)],
            CapturedProvides = [.. component.Provide().Select(k => k.Name)],
        };

        var declared = new HashSet<ICapabilityKey>(
            [.. component.Inject(), .. component.Provide()], ReferenceEqualityComparer.Instance);
        foreach (var pair in command.CapturedOverrides)
        {
            if (!declared.Contains(pair.Key))
            {
                replacement.RealmOverrides[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in command.CapturedMetadata)
        {
            replacement.Metadata[pair.Key] = new Fiber.MetaState
            {
                Declared = pair.Value.Declared,
                Override = pair.Value.Override,
            };
        }

        foreach (var key in component.Provide())
        {
            var existing = FindProvider(EffectiveRealm(replacement, key), key);
            if (existing is not null && !ReferenceEquals(existing, old))
            {
                command.Reply.TrySetException(new DuplicateProviderException(
                    $"key '{key.Name}' is already provided after rehome"));
                return;
            }
        }

        _fibers[replacement.Id] = replacement;
        old.Parent?.AddChild(replacement);
        var cycle = FindCycle();
        if (cycle is not null)
        {
            _fibers.Remove(replacement.Id);
            old.Parent?.RemoveChild(replacement);
            command.Reply.TrySetException(new DependencyCycleException($"dependency cycle: {cycle}"));
            return;
        }

        // Same-name reinsert at the same ownership position. Reply is held until
        // the replacement is Active, just like a regular Load.
        _awaitingLoads.Add(new LoadCommand(
            component, old.Parent, command.Realm, command.Reply, AwaitActivation: true)
        {
            Created = replacement,
        });
        _bindingsChanged = true;
    }

    private void RecordCleanupFailures(Fiber fiber, CleanupIncompleteException error)
    {
        fiber.CleanupFailures = error.InnerExceptions;
        foreach (var failure in error.InnerExceptions)
        {
            _cleanupFailures.Add(new CleanupFailureRecord(fiber.Id, fiber.Component.Name, failure.Message));
        }
    }

    private void HandleStepRequest(StepRequestCommand command)
    {
        var activation = command.Activation;
        var fiber = activation.Fiber;
        var allowed = fiber.CurrentActivation == activation &&
                      activation.Phase == ActivationPhase.Installing &&
                      fiber.Intent == LifecycleIntent.Load &&
                      !activation.Cancellation.IsCancellationRequested &&
                      Satisfied(activation);
        command.Reply.TrySetResult(allowed);
    }

    private void HandleStepInstalled(StepInstalledCommand command)
    {
        var activation = command.Activation;
        if (!activation.IsCurrent || activation.Phase != ActivationPhase.Installing)
        {
            if (command.Inverse is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await command.Inverse();
                    }
                    catch
                    {
                        // best-effort compensation for obsolete step
                    }
                });
            }

            command.Reply.TrySetResult();
            return;
        }

        if (command.Inverse is not null)
        {
            activation.Effects.Add(new EffectSlot(command.Inverse) { State = EffectState.Committed });
        }

        if (activation.Cancellation.IsCancellationRequested)
        {
            StartUnwind(activation.Fiber, dependencyLoss: true);
        }

        command.Reply.TrySetResult();
    }

    private void HandleApplyCompleted(ApplyCompletedCommand command)
    {
        var activation = command.Activation;
        if (!activation.IsCurrent || activation.Phase != ActivationPhase.Installing)
        {
            return;
        }

        _applying.Remove(activation.Id);
        activation.ApplyLanded = true;
        if (activation.UnwindRequested || command.Error is DivertedException ||
            activation.Cancellation.IsCancellationRequested)
        {
            BeginUnwindExecution(activation);
            return;
        }

        if (command.Error is not null)
        {
            // Apply has LANDED: this failure must not take the in-flight divert
            // deferral path in StartUnwind, or no unwind would ever run.
            activation.Fiber.Failure = command.Error;
            StartUnwind(activation.Fiber, dependencyLoss: false);
            return;
        }

        foreach (var key in activation.Fiber.CapturedProvideKeys)
        {
            if (!activation.Fiber.Provided.TryGetValue(key, out var value))
            {
                continue;
            }

            var realm = EffectiveRealm(activation.Fiber, key);
            var existing = FindProvider(realm, key);
            if (existing is not null && !ReferenceEquals(existing, activation.Fiber))
            {
                activation.Fiber.Failure = new DuplicateProviderException(
                    $"key '{key.Name}' is already provided by '{existing.Component.Name}'");
                StartUnwind(activation.Fiber, dependencyLoss: false);
                return;
            }

            SetProvider(realm, key, activation.Fiber);
        }

        _justCommitted.Add(activation.Id);
        activation.Dependencies = [.. activation.Fiber.Committed.Values];
        activation.Phase = ActivationPhase.Committed;
        activation.Fiber.State = FiberState.Active;
        _bindingsChanged = true;
        Events.Append(nameof(RuntimeEventType.ActivationActive), activation.Fiber.Id, activation.Id);
    }

    private void HandleUnwindCompleted(UnwindCompletedCommand command)
    {
        var activation = command.Activation;
        _unwinding.Remove(activation.Id);
        _applying.Remove(activation.Id);
        _unwindRunning.Remove(activation.Id);
        if (!activation.IsCurrent || activation.Phase != ActivationPhase.Unwinding)
        {
            if (command.Error is CleanupIncompleteException staleError)
            {
                RecordCleanupFailures(activation.Fiber, staleError);
            }

            ForgetActivation(activation);
            CheckCloseComplete();
            return;
        }

        var fiber = activation.Fiber;
        var cleanupError = command.Error as CleanupIncompleteException;
        if (cleanupError is not null)
        {
            RecordCleanupFailures(fiber, cleanupError);
        }
        var providerActivations = fiber.Committed.Values.Select(b => b.ActivationId).Distinct().ToArray();
        var providedRealms = new Dictionary<ICapabilityKey, Realm>(ReferenceEqualityComparer.Instance);
        foreach (var key in fiber.CapturedProvideKeys)
        {
            providedRealms[key] = EffectiveRealm(fiber, key);
        }

        fiber.Provided.Clear();
        fiber.Committed.Clear();
        fiber.Metadata.Clear();
        _versionedProviders.RemoveAll(b => b.ActivationId == activation.Id && ReferenceEquals(b.Provider, fiber));
        foreach (var key in fiber.CapturedProvideKeys)
        {
            var realm = providedRealms[key];
            if (ReferenceEquals(FindProvider(realm, key), fiber))
            {
                RemoveProvider(realm, key);
            }
        }

        foreach (var list in _handlers.Values)
        {
            list.RemoveAll(h => h.ActivationId == activation.Id);
        }

        activation.Phase = ActivationPhase.Unwound;
        Events.Append(nameof(RuntimeEventType.ActivationEnded), fiber.Id, activation.Id,
            new { State = fiber.State.ToString() });
        if (fiber.Intent == LifecycleIntent.Retired || _closing)
        {
            fiber.State = FiberState.Gone;
            _fibers.Remove(fiber.Id);
            fiber.Parent?.RemoveChild(fiber);
            fiber.SignalGone();
            CompleteDispose(fiber);
        }
        else if (cleanupError is not null)
        {
            // A failed inverse is a terminal outcome, not a clean Pending that
            // the reconciler would silently reactivate.
            fiber.State = FiberState.Failed;
            fiber.Failure = cleanupError;
        }
        else if (fiber.Failure is not null)
        {
            fiber.State = FiberState.Failed;
        }
        else
        {
            fiber.State = FiberState.Pending;
        }

        if (_pendingRehomes.Remove(fiber.Id, out var rehome))
        {
            CompleteRehome(fiber, rehome);
        }

        foreach (var providerId in providerActivations)
        {
            ReleaseWaiters(providerId);
        }

        if (fiber.Parent?.CurrentActivation is { } parentActivation)
        {
            ReleaseWaiters(parentActivation.Id);
        }

        ForgetActivation(activation);
        _bindingsChanged = true;
        CheckCloseComplete();
    }

    /// <summary>
    /// Drop strong references from retired bookkeeping so an unloaded plugin's
    /// AssemblyLoadContext is not pinned by a still-live Runtime or a Pending
    /// consumer's old dependency snapshot.
    /// </summary>
    private void ForgetActivation(Activation activation)
    {
        _activations.Remove(activation.Id);
        activation.Dependencies.Clear();
        activation.Effects.Clear();
        if (activation.Fiber.CurrentActivation == activation)
        {
            activation.Fiber.CurrentActivation = null;
        }
    }

    private void ReleaseWaiters(long providerActivationId)
    {
        if (!_unwindWaiters.TryGetValue(providerActivationId, out var count))
        {
            return;
        }

        if (count > 1)
        {
            _unwindWaiters[providerActivationId] = count - 1;
            return;
        }

        _unwindWaiters.Remove(providerActivationId);
        if (_activations.TryGetValue(providerActivationId, out var provider))
        {
            BeginUnwindExecution(provider);
        }
    }

    private void CompleteDispose(Fiber fiber)
    {
        if (_disposeWaiters.Remove(fiber.Id, out var waiters))
        {
            foreach (var reply in waiters)
            {
                _pendingReplies.Add(() => reply.TrySetResult());
            }
        }
    }

    private void FlushAwaitingLoads()
    {
        if (_applying.Count > 0 || _unwinding.Count > 0 || _awaitingLoads.Count == 0)
        {
            return;
        }

        foreach (var load in _awaitingLoads)
        {
            if (load.Created is not null)
            {
                _pendingReplies.Add(() => load.Reply.TrySetResult(load.Created!));
            }
        }

        _awaitingLoads.Clear();
    }

    private void CheckCloseComplete()
    {
        if (!_closing || _closeWaiters.Count == 0 || _applying.Count > 0 ||
            _unwinding.Count > 0 || _fibers.Count > 0)
        {
            return;
        }

        _closed = true;
        foreach (var reply in _closeWaiters)
        {
            _pendingReplies.Add(() => reply.TrySetResult());
        }

        _closeWaiters.Clear();

        // Release plugin assemblies as soon as the last fiber is gone. The
        // Runtime may still be alive for a short teardown window, but it must
        // not pin an unloaded ALC through activation/bookkeeping state.
        _activations.Clear();
        _versionedProviders.Clear();
        _handlers.Clear();
        _providers.Clear();
        _realmProviders.Clear();
        _disposeWaiters.Clear();
        _unwindWaiters.Clear();
        _pendingRehomes.Clear();
    }

    private void CompletePendingReplies()
    {
        foreach (var complete in _pendingReplies)
        {
            complete();
        }

        _pendingReplies.Clear();
    }

    // ---- reconciliation -----------------------------------------------------

    private void Reconcile()
    {
        var guard = 0;
        do
        {
            _bindingsChanged = false;
            if (++guard > 64)
            {
                throw new InvalidOperationException("reconciliation did not converge");
            }

            foreach (var fiber in _fibers.Values.ToArray())
            {
                var activation = fiber.CurrentActivation;
                if (activation is null || !activation.IsCurrent ||
                    activation.Phase != ActivationPhase.Committed ||
                    _justCommitted.Contains(activation.Id) ||
                    _unwinding.Contains(activation.Id))
                {
                    continue;
                }

                if (fiber.Intent == LifecycleIntent.Retired || !Satisfied(activation))
                {
                    StartUnwind(fiber, dependencyLoss: fiber.Intent != LifecycleIntent.Retired);
                }
            }

            foreach (var fiber in _fibers.Values.ToArray())
            {
                if (fiber.State == FiberState.Pending && fiber.Failure is null &&
                    fiber.Intent == LifecycleIntent.Load &&
                    fiber.CurrentActivation?.Phase != ActivationPhase.Installing &&
                    Satisfied(new PendingProbe(fiber)))
                {
                    StartActivation(fiber);
                }
            }
        }
        while (_bindingsChanged);
    }

    private readonly record struct PendingProbe(Fiber Fiber);

    private bool Satisfied(Activation activation)
    {
        foreach (var key in activation.Fiber.CapturedInjectKeys.Where(k => k is not IMetaKey))
        {
            if (!SatisfiedKey(activation.Fiber, key, activation))
            {
                return false;
            }
        }

        return true;
    }

    private bool Satisfied(PendingProbe probe)
    {
        foreach (var key in probe.Fiber.CapturedInjectKeys.Where(k => k is not IMetaKey))
        {
            if (!SatisfiedKey(probe.Fiber, key, null))
            {
                return false;
            }
        }

        return true;
    }

    private bool SatisfiedKey(Fiber fiber, ICapabilityKey key, Activation? consumer)
    {
        if (key is IVersionedKey versioned)
        {
            var target = TargetVersioned(versioned, fiber);
            if (target is null)
            {
                return false;
            }

            return consumer is null ||
                   !fiber.Committed.TryGetValue(key, out var bound) ||
                   (bound.FiberId == target.Provider.Id && bound.ActivationId == target.ActivationId);
        }

        var provider = FindProvider(EffectiveRealm(fiber, key), key);
        if (provider is null || provider.State != FiberState.Active ||
            provider.CurrentActivation?.Phase != ActivationPhase.Committed ||
            ReferenceEquals(provider, fiber))
        {
            return false;
        }

        return consumer is null ||
               !fiber.Committed.TryGetValue(key, out var current) ||
               (current.FiberId == provider.Id && current.ActivationId == provider.ActivationId);
    }

    private void StartActivation(Fiber fiber)
    {
        var id = ++_nextActivationId;
        var cts = new CancellationTokenSource();
        var activation = new Activation(id, fiber, this, cts);
        fiber.Generation++;
        fiber.CurrentActivation = activation;
        fiber.Failure = null;
        fiber.State = FiberState.Loading;
        activation.Phase = ActivationPhase.Installing;
        _activations[id] = activation;
        _applying.Add(id);
        _bindingsChanged = true;
        Events.Append(nameof(RuntimeEventType.ActivationLoading), fiber.Id, id);
        _ = Task.Run(() => RunApplyAsync(activation));
    }

    private async Task RunApplyAsync(Activation activation)
    {
        Exception? error = null;
        try
        {
            await foreach (var step in StepsAsync(activation))
            {
                var proceed = await RequestStepAsync(activation);
                if (!proceed)
                {
                    error = new DivertedException();
                    break;
                }

                var inverse = await step.Install();
                var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!TryWrite(new StepInstalledCommand(activation, inverse, reply)))
                {
                    throw new ObjectDisposedException(nameof(Runtime));
                }

                await reply.Task;
                if (!activation.IsCurrent || activation.Phase != ActivationPhase.Installing)
                {
                    error = new DivertedException();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        TryWrite(new ApplyCompletedCommand(activation, error));
    }

    private async Task<bool> RequestStepAsync(Activation activation)
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryWrite(new StepRequestCommand(activation, reply)))
        {
            throw new ObjectDisposedException(nameof(Runtime));
        }

        return await reply.Task;
    }

    private static async IAsyncEnumerable<EffectRegistration> StepsAsync(Activation activation)
    {
        if (activation.Fiber.Component is IIterativeComponent iterative)
        {
            await foreach (var step in iterative.IterateAsync(activation.Context))
            {
                yield return step;
            }

            yield break;
        }

        // Single-step component: Apply itself is the install — the decision
        // point happens BEFORE user code runs (paper Iterator / Divert).
        yield return new EffectRegistration(async () =>
        {
            var cleanup = await activation.Fiber.Component.ApplyAsync(activation.Context);
            if (cleanup is not null)
            {
                activation.Effects.Add(new EffectSlot(() => cleanup())
                {
                    State = EffectState.Committed,
                });
            }

            return null;
        });
    }

    private void StartUnwind(Fiber fiber, bool dependencyLoss)
    {
        var activation = fiber.CurrentActivation;
        if (activation is null || !activation.IsCurrent ||
            activation.Phase is ActivationPhase.Unwinding or ActivationPhase.Unwound)
        {
            return;
        }

        if (activation.Phase == ActivationPhase.Installing && !activation.ApplyLanded)
        {
            // Inertial divert (paper §4.4): let the in-flight Apply land, then
            // its inverse is registered by StepInstalled before unwind runs.
            activation.UnwindRequested = true;
            activation.Cancellation.Cancel();
            return;
        }

        // Relied guard (paper Def 54): provider stays Active/Committed until
        // consumers and children finished teardown, so their Cleanup can
        // still Require the dependency.
        activation.Cancellation.Cancel();

        var consumers = _fibers.Values.Where(other =>
            !ReferenceEquals(other, fiber) &&
            other.Committed.Values.Any(b =>
                b.FiberId == fiber.Id && b.ActivationId == activation.Id)).ToArray();

        // Only a child that still owns an activation can report back through
        // UnwindCompleted and release this waiter count. An Unwinding child is
        // included because its in-flight teardown will report; Unwound is already
        // terminal. A Pending/Failed child has no activation at all, so it must
        // be retired here instead of stranding the parent's waiter count.
        var liveChildren = new List<Fiber>();
        foreach (var child in fiber.ChildList.ToArray())
        {
            if (HasReportableActivation(child))
            {
                liveChildren.Add(child);
            }
            else
            {
                RetireActivationlessChild(child);
            }
        }

        var waiting = consumers.Length + liveChildren.Count;
        if (waiting > 0)
        {
            _unwindWaiters[activation.Id] = waiting;
            foreach (var child in liveChildren)
            {
                child.Intent = LifecycleIntent.Retired;
                StartUnwind(child, dependencyLoss: false);
            }

            foreach (var other in consumers)
            {
                StartUnwind(other, dependencyLoss: true);
            }

            return;
        }

        BeginUnwindExecution(activation);
    }

    /// <summary>
    /// A child participates in teardown only while it has a current activation
    /// whose completion produces a report. Unwinding still counts: that unwind
    /// is already running and will release the parent's waiter.
    /// </summary>
    private static bool HasReportableActivation(Fiber child) =>
        child.CurrentActivation is { } activation &&
        activation.IsCurrent &&
        activation.Phase != ActivationPhase.Unwound;

    /// <summary>
    /// Terminal handling for a child with no activation to unwind. It can never
    /// send UnwindCompleted, so give it the same Gone bookkeeping a completed
    /// unwind would have produced and detach it from the parent.
    /// </summary>
    private void RetireActivationlessChild(Fiber child)
    {
        child.Intent = LifecycleIntent.Retired;
        child.State = FiberState.Gone;
        _fibers.Remove(child.Id);
        child.Parent?.RemoveChild(child);
        child.SignalGone();
        CompleteDispose(child);
        _bindingsChanged = true;
    }

    private void BeginUnwindExecution(Activation activation)
    {
        if (activation.Phase is ActivationPhase.Unwinding or ActivationPhase.Unwound ||
            !_unwindRunning.Add(activation.Id))
        {
            return;
        }

        activation.Phase = ActivationPhase.Unwinding;
        activation.Fiber.State = FiberState.Unloading;
        _unwinding.Add(activation.Id);
        Events.Append(nameof(RuntimeEventType.ActivationUnloading), activation.Fiber.Id, activation.Id);
        _ = Task.Run(() => RunUnwindAsync(activation));
    }

    private async Task RunUnwindAsync(Activation activation)
    {
        List<Exception>? failures = null;
        try
        {
            for (var i = activation.Effects.Count - 1; i >= 0; i--)
            {
                var slot = activation.Effects[i];
                if (slot.State is EffectState.Undone or EffectState.Failed || slot.Inverse is null)
                {
                    continue;
                }

                slot.State = EffectState.Undoing;
                try
                {
                    await slot.Inverse();
                    slot.State = EffectState.Undone;
                }
                catch (Exception error)
                {
                    // Keep unwinding the rest of the stack, but never report a
                    // failed inverse as a clean Undo.
                    (failures ??= []).Add(error);
                    slot.State = EffectState.Failed;
                }
            }
        }
        catch
        {
            // best effort: still report whatever was collected so far
        }

        TryWrite(new UnwindCompletedCommand(
            activation,
            failures is null ? null : new CleanupIncompleteException(failures)));
    }

    // ---- capability resolution ---------------------------------------------

    private static Realm EffectiveRealm(Fiber fiber, ICapabilityKey key) =>
        fiber.RealmOverrides.TryGetValue(key, out var realm) ? realm : fiber.Realm;

    private Fiber? FindProvider(Realm realm, ICapabilityKey key) =>
        ReferenceEquals(realm, Realm.Default)
            ? _providers.GetValueOrDefault(key)
            : _realmProviders.TryGetValue(realm, out var table) ? table.GetValueOrDefault(key) : null;

    private void SetProvider(Realm realm, ICapabilityKey key, Fiber fiber)
    {
        if (ReferenceEquals(realm, Realm.Default))
        {
            _providers[key] = fiber;
            return;
        }

        if (!_realmProviders.TryGetValue(realm, out var table))
        {
            table = new Dictionary<ICapabilityKey, Fiber>(ReferenceEqualityComparer.Instance);
            _realmProviders[realm] = table;
        }

        table[key] = fiber;
    }

    private void RemoveProvider(Realm realm, ICapabilityKey key)
    {
        if (ReferenceEquals(realm, Realm.Default))
        {
            _providers.Remove(key);
            return;
        }

        if (_realmProviders.TryGetValue(realm, out var table))
        {
            table.Remove(key);
            if (table.Count == 0)
            {
                _realmProviders.Remove(realm);
            }
        }
    }

    /// <summary>Short critical section for user-side registry/binding writes.</summary>
    internal void Mutate(Action mutation)
    {
        lock (_stateLock)
        {
            mutation();
        }
    }

    internal T RequireValue<T>(CapabilityKey key, Activation consumer)
    {
        T result = default!;
        Exception? failure = null;
        Mutate(() =>
        {
            try
            {
                var provider = FindProvider(EffectiveRealm(consumer.Fiber, key), key);
                if (provider is null || provider.State != FiberState.Active ||
                    provider.CurrentActivation?.Phase != ActivationPhase.Committed ||
                    ReferenceEquals(provider, consumer.Fiber) ||
                    !provider.Provided.TryGetValue(key, out var value))
                {
                    throw new DependencyMissingException(
                        $"'{consumer.Fiber.Component.Name}' requires '{key.Name}' but no active provider exists");
                }

                consumer.Fiber.Committed[key] = new ProviderBinding(
                    key, provider.Id, provider.ActivationId, provider, value);
                result = (T)value!;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        if (failure is not null)
        {
            throw failure;
        }

        return result;
    }

    internal void RegisterVersioned<T>(VersionedKey<T> key, T value, Activation provider) =>
        Mutate(() =>
        {
            var realm = EffectiveRealm(provider.Fiber, key);
            var duplicate = _versionedProviders.FirstOrDefault(b =>
                b.Realm == realm && b.Name == key.Name && b.ValueType == typeof(T) &&
                b.Version == key.Version && b.ActivationId != provider.Id);
            if (duplicate is not null)
            {
                throw new DuplicateProviderException($"key '{key.Name}' version {key.Version} already provided");
            }

            _versionedProviders.RemoveAll(b =>
                b.ActivationId == provider.Id && b.Name == key.Name && b.ValueType == typeof(T));
            _versionedProviders.Add(new VersionedBinding(
                realm, key.Name, typeof(T), key.Version, provider.Fiber, provider.Id, value!));
            _bindingsChanged = true;
        });

    internal void RemoveVersioned(Activation provider, ICapabilityKey key) =>
        Mutate(() => _versionedProviders.RemoveAll(b =>
            b.ActivationId == provider.Id && b.Name == key.Name));

    internal void MarkBindingsChanged() => Mutate(() => _bindingsChanged = true);

    private VersionedBinding? TargetVersioned(IVersionedKey key, Fiber consumer) =>
        _versionedProviders
            .Where(b => b.Realm == EffectiveRealm(consumer, (ICapabilityKey)key) &&
                        b.Name == key.Name && b.ValueType == key.ValueType &&
                        key.Accepts(b.Version) &&
                        b.Provider.State == FiberState.Active &&
                        b.ActivationId != consumer.ActivationId)
            .OrderByDescending(b => b.Version)
            .FirstOrDefault();

    internal T ResolveVersioned<T>(VersionedKey<T> key, Activation consumer)
    {
        T result = default!;
        Exception? failure = null;
        Mutate(() =>
        {
            try
            {
                var binding = TargetVersioned(key, consumer.Fiber);
                if (binding is null)
                {
                    throw new DependencyMissingException(
                        $"'{consumer.Fiber.Component.Name}' requires '{key.Name}' matching " +
                        $"'{key.Constraint ?? (IFormattable)key.Version}' but no active provider exists");
                }

                consumer.Fiber.Committed[key] = new ProviderBinding(
                    key, binding.Provider.Id, binding.ActivationId, binding.Provider, binding.Value);
                result = (T)binding.Value;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        if (failure is not null)
        {
            throw failure;
        }

        return result;
    }

    internal ValueTask RegisterHandler<T>(EventKey<T> key, Func<T, ValueTask> handler, Activation owner)
    {
        Mutate(() =>
        {
            if (!_handlers.TryGetValue(key, out var list))
            {
                list = [];
                _handlers[key] = list;
            }

            list.Add(new Handler(owner.Fiber, owner.Id, p => handler((T)p!)));
            owner.Effects.Add(new EffectSlot(() =>
            {
                list.RemoveAll(h => h.ActivationId == owner.Id);
                return ValueTask.CompletedTask;
            }) { State = EffectState.Committed });
        });
        return ValueTask.CompletedTask;
    }

    internal async ValueTask EmitAsync<T>(EventKey<T> key, T payload, Activation emitter)
    {
        Handler[] handlers = [];
        Mutate(() => handlers = _handlers.TryGetValue(key, out var list) ? list.ToArray() : []);
        Exception? failure = null;
        foreach (var handler in handlers)
        {
            try
            {
                await handler.Invoke(payload);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    // ---- declarations / cycles ---------------------------------------------

    private void ValidateDeclarations(Component component, Realm realm)
    {
        EnsureUnique(component.Inject(), "inject");
        EnsureUnique(component.Provide(), "provide");
        foreach (var key in component.Provide())
        {
            foreach (var fiber in _fibers.Values.Where(f => f.State != FiberState.Gone))
            {
                if (fiber.CapturedProvideKeys.Any(k =>
                        ReferenceEquals(k, key) && ReferenceEquals(EffectiveRealm(fiber, k), realm)))
                {
                    throw new DuplicateProviderException($"key '{key.Name}' is already provided by '{fiber.Component.Name}'");
                }
            }
        }
    }

    private static void EnsureUnique(IReadOnlyList<ICapabilityKey> keys, string side)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = i + 1; j < keys.Count; j++)
            {
                if (ReferenceEquals(keys[i], keys[j]))
                {
                    throw new UndeclaredCapabilityException($"duplicate {side} declaration '{keys[i].Name}'");
                }
            }
        }
    }

    private string? FindCycle()
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var chain = new List<string>();
        var versioned = _fibers.Values
            .SelectMany(f => f.CapturedProvideKeys.OfType<IVersionedKey>()
                .Select(k => (Fiber: f, Key: k))).ToArray();
        var declared = new Dictionary<(Realm, ICapabilityKey), Fiber>();
        foreach (var fiber in _fibers.Values)
        {
            foreach (var k in fiber.CapturedProvideKeys)
            {
                declared.TryAdd((EffectiveRealm(fiber, k), k), fiber);
            }
        }

        bool Visit(Fiber fiber)
        {
            if (!visiting.Add(fiber.Id))
            {
                chain.Add(fiber.Component.Name);
                return true;
            }

            if (!visited.Add(fiber.Id))
            {
                visiting.Remove(fiber.Id);
                return false;
            }

            foreach (var key in fiber.CapturedInjectKeys.Where(k => k is not IMetaKey))
            {
                Fiber? provider = null;
                if (key is IVersionedKey required)
                {
                    provider = versioned
                        .Where(c => c.Key.Name == required.Name && c.Key.ValueType == required.ValueType &&
                                    required.Accepts(c.Key.Version))
                        .OrderByDescending(c => c.Key.Version)
                        .Select(c => c.Fiber).FirstOrDefault();
                }
                else
                {
                    declared.TryGetValue((EffectiveRealm(fiber, key), key), out provider);
                }

                if (provider is not null && Visit(provider))
                {
                    chain.Add(fiber.Component.Name);
                    return true;
                }
            }

            visiting.Remove(fiber.Id);
            return false;
        }

        foreach (var fiber in _fibers.Values)
        {
            chain.Clear();
            if (Visit(fiber))
            {
                chain.Reverse();
                return string.Join(" -> ", chain);
            }
        }

        return null;
    }

    private void PublishSnapshot()
    {
        _snapshot = new RuntimeSnapshot(_fibers.Values.Select(f => new FiberSnapshot(
            f.Id, f.Component.Name, f.State, (int)f.ActivationId, f.Parent?.Id,
            f.CapturedRequires,
            f.CapturedProvides,
            f.Committed.Values.Select(b => $"{b.Key.Name}->{b.FiberId}").ToArray(),
            f.Failure?.Message)
        {
            Revision = f.Revision,
            CleanupIncomplete = f.CleanupIncomplete,
            CleanupFailures = [.. f.CleanupFailures.Select(error => error.Message)],
        }).ToArray())
        {
            CleanupFailures = [.. _cleanupFailures],
        };
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        // CloseAsync reached a terminal state for every fiber; join the
        // decision loop so no command can pin a plugin assembly after return.
        _commands.Writer.TryComplete();
        if (_loop is not null)
        {
            await _loop;
            _loop = null;
        }

        GC.SuppressFinalize(this);
    }
}
