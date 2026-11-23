using Gocordis.Core;
using Gocordis.Runtime.Contracts;

namespace Gocordis.Runtime.Process;

/// <summary>
/// Process implementation of the unified backend protocol. The child is
/// spawned and handshaken at Prepare/Activate, published through Core at
/// Activate, and reclaimed by the Core effect inverse. Unlike the other two
/// backends, a failed start degrades to an explicit error vocabulary instead
/// of failing the fiber — declared via <see cref="DegradationPolicy"/>.
/// </summary>
public sealed class ProcessBackend : IPluginBackend
{
    private readonly string _executable;
    private readonly Key<string>? _key;
    private readonly Key<ProcessDiagnostics> _diagnosticsKey;
    private readonly Key<string> _errorKey;
    private readonly ProcessCapabilityBridge? _capabilityBridge;
    private readonly bool _capabilityOnly;
    private ProcessPluginClient? _client;
    private ProcessDiagnostics? _diagnostics;
    private bool _released;
    private bool _disposed;

    public ProcessBackend(
        string executable,
        Key<string>? key,
        Key<ProcessDiagnostics>? diagnosticsKey = null,
        Key<string>? errorKey = null,
        ProcessCapabilityBridge? capabilityBridge = null)
    {
        _executable = executable;
        _key = key;
        _capabilityOnly = key is null;
        _capabilityBridge = capabilityBridge;
        // Per-plugin keys keep several process plugins from colliding on the
        // one-provider-per-key rule; defaults preserve the original single
        // plugin vocabulary used by existing runtime tests.
        _diagnosticsKey = diagnosticsKey ?? ProcessKeys.Diagnostics;
        _errorKey = errorKey ?? ProcessKeys.ErrorVocabulary;
    }

    public string BackendId => "process";

    public BackendDegradationPolicy DegradationPolicy =>
        _capabilityOnly ? BackendDegradationPolicy.FailFiber : BackendDegradationPolicy.ErrorVocabulary;

    public BackendHandle? LastHandle { get; private set; }

    public ValueTask<BackendHandle> PrepareAsync(IBackendContext context)
    {
        context.ThrowIfDiverted();
        _diagnostics = new ProcessDiagnostics();
        if (!_capabilityOnly)
        {
            context.Provide(_diagnosticsKey, _diagnostics);
        }

        LastHandle = new BackendHandle(BackendId, 0, BackendPhase.Prepared, _executable);
        return ValueTask.FromResult(LastHandle);
    }

    public async ValueTask<BackendHandle> ActivateAsync(IBackendContext context, BackendHandle prepared)
    {
        context.ThrowIfDiverted();
        try
        {
            _client = await ProcessPluginClient.StartAsync(_executable);
            if (_capabilityBridge is not null)
            {
                _capabilityBridge.Attach(_client);
                _client.RequestHandler = (method, parameters) =>
                    _capabilityBridge.HandleRequest(method, parameters);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await _client.CallAsync("echo", new { text = "ready" }, timeout.Token);
            if (_key is not null)
            {
                context.Provide(_key, result?["text"]?.GetValue<string>() ?? "unavailable");
                context.Provide(_errorKey, "ok");
            }
            LastHandle = prepared.WithPhase(BackendPhase.Activated, resourceId: _client.ProcessId.ToString());
            return LastHandle;
        }
        catch (Exception ex)
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            // Explicit degradation: declaration stays Active, consumers get a
            // stable error vocabulary and diagnostics carry the raw message.
            // A capability-only provider has no error vocabulary to degrade to;
            // publishing a "normal" endpoint would violate Active ⇔ ready, so
            // the activation raises instead (C33: failure = raise).
            if (_diagnostics is not null)
            {
                _diagnostics.LastError = ex.Message;
            }

            if (_key is not null)
            {
                context.Provide(_key, ProcessErrorPolicy.ErrorVocabulary);
                context.Provide(_errorKey, ProcessErrorPolicy.ErrorVocabulary);
            }
            else
            {
                throw;
            }
            LastHandle = prepared.WithPhase(BackendPhase.Activated, ProcessErrorPolicy.ErrorVocabulary);
            return LastHandle;
        }
    }

    public async ValueTask<BackendHandle> RollbackAsync(
        IBackendContext context, BackendHandle? prepared, Exception reason)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        var handle = prepared ?? new BackendHandle(BackendId, 0, BackendPhase.Prepared, _executable);
        LastHandle = handle.WithPhase(BackendPhase.RolledBack, reason.Message);
        return LastHandle;
    }

    public async ValueTask<BackendHandle> ReleaseAsync(IBackendContext context, BackendHandle active)
    {
        if (_released)
        {
            LastHandle = active.WithPhase(BackendPhase.Released);
            return LastHandle;
        }

        _released = true;
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
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
