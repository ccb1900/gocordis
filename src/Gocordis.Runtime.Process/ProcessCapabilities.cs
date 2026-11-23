using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;

namespace Gocordis.Runtime.Process;

/// <summary>
/// Host side of declared Process capabilities. The child may call the host only
/// for the two declared methods; everything else keeps the -32601 policy. The
/// child never sees a Core Fiber, Key instance or lifecycle state — only a handle.
/// </summary>
public sealed class ProcessCapabilityBridge
{
    private readonly Dictionary<int, ICapabilityEndpoint> _handles = [];
    private readonly object _gate = new();
    private Func<CapabilityId, ICapabilityEndpoint>? _require;
    private ProcessPluginClient? _client;
    private int _nextHandle = 1;
    private bool _disposed;
    private CancellationToken _cancellation;

    public void SetRequire(Func<CapabilityId, ICapabilityEndpoint> require) => _require = require;

    public void SetCancellationToken(CancellationToken cancellationToken) => _cancellation = cancellationToken;

    internal void Attach(ProcessPluginClient client)
    {
        _client = client;
        _disposed = false;
        lock (_gate)
        {
            _handles.Clear();
        }
    }

    public JsonNode? HandleRequest(string method, JsonNode? parameters) => method switch
    {
        "capability/require" => Require(parameters),
        "capability/invoke" => Invoke(parameters),
        _ => throw new NotSupportedException($"capability method '{method}' is not declared"),
    };

    private JsonNode Require(JsonNode? parameters)
    {
        var text = parameters?["id"]?.GetValue<string>();
        if (_require is null || text is null || !CapabilityId.TryParse(text, out var id))
        {
            throw new NotSupportedException("invalid capability/require declaration");
        }

        ICapabilityEndpoint endpoint;
        try
        {
            endpoint = _require(id);
        }
        catch (DependencyMissingException)
        {
            throw new NotSupportedException($"capability '{id}' has no active provider");
        }

        lock (_gate)
        {
            if (_disposed)
            {
                throw new NotSupportedException("capability bridge is released");
            }

            var handle = _nextHandle++;
            _handles[handle] = endpoint;
            return new JsonObject { ["handle"] = handle };
        }
    }

    private JsonNode Invoke(JsonNode? parameters)
    {
        var handle = parameters?["handle"]?.GetValue<int>() ?? 0;
        var request = parameters?["request"]?.GetValue<string>() ?? string.Empty;
        ICapabilityEndpoint endpoint;
        lock (_gate)
        {
            if (_disposed || !_handles.TryGetValue(handle, out endpoint!))
            {
                throw new NotSupportedException($"unknown capability handle {handle}");
            }
        }

        // BLOCKING-call boundary (documented): the request is answered inside
        // the host's read loop, so the endpoint must complete without the SAME
        // client being re-entered (self-cycles are rejected at mount); the
        // activation token bounds the wait cooperatively.
        var response = endpoint.InvokeAsync(
            CapabilityEnvelope.Json(request), _cancellation).AsTask().GetAwaiter().GetResult();
        return new JsonObject { ["response"] = response.AsText() };
    }

    /// <summary>Host → child call for a capability provided by this process.</summary>
    public async ValueTask<CapabilityEnvelope> InvokeProviderAsync(
        CapabilityId id,
        CapabilityContract contract,
        CapabilityEnvelope request,
        CancellationToken cancellationToken)
    {
        var client = _client
                     ?? throw new CapabilityInvocationException(
                         "provider-abort", "process plugin is not attached");
        var result = await client.CallAsync(
            "capability/invoke",
            new { id = id.ToString(), request = request.AsText() },
            cancellationToken);
        var text = result?["response"]?.GetValue<string>()
                   ?? throw new CapabilityInvocationException(
                       "invalid-payload", $"process capability '{id}' returned no response");
        return new CapabilityEnvelope(contract.MediaType, System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Releases only this consumer's handles. Endpoints are provider-owned via
    /// <c>Context.Require</c>; a consumer must never dispose a shared service.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _handles.Clear();
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Process component whose declarations come from the package manifest. Core
/// owns the fiber and its effect stack; the child process is just a transport.
/// </summary>
public sealed class ProcessCapabilityComponent : Component
{
    private readonly string _executable;
    private readonly CapabilityCatalog _catalog;
    private readonly IReadOnlyList<CapabilityDeclaration> _provides;
    private readonly IReadOnlyList<CapabilityDeclaration> _requires;

    public ProcessCapabilityComponent(
        string executable,
        CapabilityCatalog catalog,
        IReadOnlyList<CapabilityDeclaration> declarations)
    {
        _executable = executable;
        _catalog = catalog;
        _provides = declarations.Where(d => d.Direction == CapabilityDirection.Provide).ToArray();
        _requires = declarations.Where(d => d.Direction == CapabilityDirection.Require).ToArray();
    }

    public override string Name => "process-plugin";

    public override IReadOnlyList<ICapabilityKey> Inject() => _requires
        .Select(d => (ICapabilityKey)_catalog.KeyFor<ICapabilityEndpoint>(d.Id, CapabilityValueKind.Endpoint))
        .ToArray();

    public override IReadOnlyList<ICapabilityKey> Provide() => _provides
        .Select(d => (ICapabilityKey)_catalog.KeyFor<ICapabilityEndpoint>(d.Id, CapabilityValueKind.Endpoint))
        .ToArray();

    public async override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var bridge = new ProcessCapabilityBridge();
        bridge.SetCancellationToken(context.CancellationToken);
        bridge.SetRequire(id =>
        {
            var key = _catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);
            return context.Require(key);
        });

        var backend = new ProcessBackend(_executable, key: null, capabilityBridge: bridge);
        await BackendProtocol.RunAsync(backend, context);

        await context.EffectAsync(
            static () => ValueTask.CompletedTask,
            async () => await bridge.DisposeAsync());

        foreach (var declaration in _provides)
        {
            var key = _catalog.KeyFor<ICapabilityEndpoint>(declaration.Id, CapabilityValueKind.Endpoint);
            var contract = _catalog.TryDescribe(declaration.Id, out var described)
                ? described
                : new CapabilityContract(
                    declaration.Id, CapabilityValueKind.Endpoint,
                    declaration.Version is null ? null : NuGet.Versioning.NuGetVersion.Parse(declaration.Version),
                    declaration.MediaType, declaration.SchemaRef);
            var endpoint = new DelegatingCapabilityEndpoint(
                declaration.Id,
                contract,
                (request, cancellationToken) => bridge.InvokeProviderAsync(
                    declaration.Id, contract, request, cancellationToken));
            context.Provide(key, endpoint);
            await context.EffectAsync(
                static () => ValueTask.CompletedTask,
                async () => await endpoint.DisposeAsync());
        }

        return null;
    }
}
