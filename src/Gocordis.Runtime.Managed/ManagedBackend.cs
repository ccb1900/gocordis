using Gocordis.Core;
using Gocordis.Runtime.Contracts;

namespace Gocordis.Runtime.Managed;

/// <summary>Diagnostic capability published by the managed backend itself.</summary>
public static class ManagedKeys
{
    /// <summary>
    /// Legacy shared identity, kept only for source compatibility. Core keys are
    /// reference-identified; using one static instance for every component makes
    /// the second managed fiber fail with a duplicate provider.
    /// </summary>
    [Obsolete("Use ForInstance(id) and keep the returned key on the component instance.")]
    public static readonly Key<string> Instance = new("managed-instance");

    /// <summary>Create the key identity owned by one managed component instance.</summary>
    public static Key<string> ForInstance(string id) => new($"managed-instance:{id}");
}

/// <summary>
/// Managed implementation of the unified backend protocol. One instance drives
/// one activation: the collectible ALC is opened at Prepare, published through
/// Core at Activate, and released (fenced by generation) by the Core effect
/// inverse. Retiring a fiber through Core/PluginRegistry therefore reclaims the
/// ALC without any manual Loader.Unload call by the caller.
/// </summary>
public sealed class ManagedBackend : IPluginBackend
{
    private readonly ManagedPluginLoader _loader;
    private readonly string _id;
    private readonly string _assemblyPath;
    private readonly string _typeName;
    private readonly Key<string> _instanceKey;
    private LoadedManagedPlugin? _plugin;
    private Cleanup? _innerCleanup;
    private bool _released;
    private bool _disposed;

    public ManagedBackend(
        ManagedPluginLoader loader,
        string id,
        string assemblyPath,
        string typeName,
        LoadedManagedPlugin? staged = null,
        Key<string>? instanceKey = null)
    {
        _loader = loader;
        _id = id;
        _assemblyPath = assemblyPath;
        _typeName = typeName;
        _instanceKey = instanceKey ?? ManagedKeys.ForInstance(id);
        _plugin = staged;
    }

    public string BackendId => "managed";

    public BackendDegradationPolicy DegradationPolicy => BackendDegradationPolicy.FailFiber;

    public BackendHandle? LastHandle { get; private set; }

    public ValueTask<BackendHandle> PrepareAsync(IBackendContext context)
    {
        context.ThrowIfDiverted();
        _plugin ??= _loader.Load(_id, _assemblyPath, _typeName);
        LastHandle = new BackendHandle(
            BackendId, _plugin.Generation.Generation, BackendPhase.Prepared, _id);
        return ValueTask.FromResult(LastHandle);
    }

    public async ValueTask<BackendHandle> ActivateAsync(IBackendContext context, BackendHandle prepared)
    {
        context.ThrowIfDiverted();
        var plugin = _plugin ?? throw new InvalidOperationException("managed backend is not prepared");
        if (context is CoreBackendContext core)
        {
            // The plugin component is applied through the real Core context so
            // its Provide calls and effects stay under Runtime authority.
            _innerCleanup = await plugin.Component.ApplyAsync(core.Core);
        }

        // The backend publishes a handle capability under the key owned by this
        // component instance; Provide() declares that exact same key instance.
        // The wrapped plugin publishes its own keys through the same Core context.
        context.Provide(_instanceKey, _id);
        LastHandle = prepared.WithPhase(BackendPhase.Activated);
        return LastHandle;
    }

    public ValueTask<BackendHandle> RollbackAsync(
        IBackendContext context, BackendHandle? prepared, Exception reason)
    {
        if (_plugin is not null)
        {
            _loader.Unload(_id, _plugin.Generation);
            _plugin = null;
        }

        var handle = prepared ?? new BackendHandle(BackendId, 0, BackendPhase.Prepared, _id);
        LastHandle = handle.WithPhase(BackendPhase.RolledBack, reason.Message);
        return ValueTask.FromResult(LastHandle);
    }

    public async ValueTask<BackendHandle> ReleaseAsync(IBackendContext context, BackendHandle active)
    {
        if (_released)
        {
            LastHandle = active.WithPhase(BackendPhase.Released);
            return LastHandle;
        }

        _released = true;
        try
        {
            if (_innerCleanup is not null)
            {
                await _innerCleanup();
                _innerCleanup = null;
            }
        }
        finally
        {
            if (_plugin is not null)
            {
                _loader.Unload(_id, _plugin.Generation);
                _plugin = null;
            }
        }

        LastHandle = active.WithPhase(BackendPhase.Released);
        return LastHandle;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Core component adapter for a managed plugin. It declares the staged
/// component's capabilities up front (Core enforces declarations at Apply) and
/// delegates the whole lifecycle to <see cref="ManagedBackend"/>.
/// </summary>
public sealed class ManagedPluginComponent : Component, IDisposable, IAsyncDisposable
{
    private readonly ManagedPluginLoader _loader;
    private readonly string _id;
    private readonly string _assemblyPath;
    private readonly string _typeName;
    private readonly LoadedManagedPlugin _plugin;
    private readonly Key<string> _instanceKey;
    private bool _stagingReleased;

    public ManagedPluginComponent(
        ManagedPluginLoader loader, string id, string assemblyPath, string typeName)
    {
        _loader = loader;
        _id = id;
        _assemblyPath = assemblyPath;
        _typeName = typeName;
        _plugin = loader.Load(id, assemblyPath, typeName);
        _instanceKey = ManagedKeys.ForInstance(id);
    }

    public override string Name => _plugin.Component.Name;

    public override IReadOnlyList<ICapabilityKey> Inject() => _plugin.Component.Inject();

    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [.. _plugin.Component.Provide(), _instanceKey];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        BackendProtocol.RunAsync(
            new ManagedBackend(_loader, _id, _assemblyPath, _typeName, _plugin, _instanceKey), context);

    /// <summary>
    /// Fenced release for a component that was staged by a declaration batch but
    /// never reached a fiber. Once a fiber owns the activation, Core's effect
    /// inverse performs the release; this call is then a generation-fenced no-op.
    /// </summary>
    public void Dispose()
    {
        if (_stagingReleased)
        {
            return;
        }

        _stagingReleased = true;
        _loader.Unload(_id, _plugin.Generation);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
