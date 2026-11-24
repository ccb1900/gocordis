using System.Text;
using System.Text.Json;
using Gocordis.Core;
using Gocordis.Runtime.Contracts;

namespace Gocordis.Runtime.Wasm;

/// <summary>
/// Declaration reader for the <c>gocordis.capabilities</c> custom section. A
/// declaration is data, not CLR metadata, so a WASM module can name the same
/// capability as a Managed plugin without sharing an assembly or a Key instance.
/// </summary>
public static class WasmCapabilityDeclarations
{
    public const string SectionName = "gocordis.capabilities";

    public static IReadOnlyList<CapabilityDeclaration> ReadFromWat(string wat) =>
        ReadJson(WasmCustomSections.ReadFromWat(wat, SectionName));

    public static IReadOnlyList<CapabilityDeclaration> Read(byte[] wasm) =>
        ReadJson(WasmCustomSections.Read(wasm, SectionName));

    public static IReadOnlyList<CapabilityDeclaration> ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var result = new List<CapabilityDeclaration>();
        foreach (var (property, direction) in new[]
                 {
                     ("provides", "provide"),
                     ("requires", "require"),
                 })
        {
            if (!document.RootElement.TryGetProperty(property, out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                var id = ReadString(entry, "id")
                         ?? throw new FormatException("WASM capability declaration is missing 'id'");
                var kind = ReadString(entry, "kind") ?? "endpoint";
                var versionOrRange = direction == "provide"
                    ? ReadString(entry, "version") ?? string.Empty
                    : ReadString(entry, "range") ?? string.Empty;
                var wire = string.Join('|', direction, id, kind, versionOrRange,
                    ReadString(entry, "mediaType") ?? "application/json",
                    ReadString(entry, "schema") ?? string.Empty);
                var declaration = CapabilityDeclaration.Parse(wire);
                if (declaration.ValueKind != CapabilityValueKind.Endpoint)
                {
                    throw new FormatException(
                        "WASM capabilities must declare kind 'endpoint'; " +
                        "value kinds are not supported across backends in this release");
                }

                result.Add(declaration);
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// Host imports used by a WASM guest to call capabilities provided by any
/// backend. The guest writes UTF-8 strings into linear memory and passes
/// (pointer, length); the host writes call results into a fixed response buffer.
/// </summary>
public sealed class WasmCapabilityBridge
{
    /// <summary>Guest-visible scratch area; the guest asks for it via capability_buffer.</summary>
    public const int RequestBufferOffset = 8192;

    public const int ResponseBufferOffset = 16384;
    public const int MaxPayloadBytes = 16384;

    private readonly Dictionary<int, ICapabilityEndpoint> _handles = [];
    private readonly object _gate = new();
    private Func<CapabilityId, ICapabilityEndpoint>? _require;
    private IReadOnlySet<CapabilityId>? _allowed;
    private WasmPluginInstance? _instance;
    private int _nextHandle = 1;
    private bool _disposed;
    private CancellationToken _cancellation;

    public void SetRequire(Func<CapabilityId, ICapabilityEndpoint> require) => _require = require;

    /// <summary>The guest may only import capability ids it declared in its
    /// custom section. This is an allowlist, not a security sandbox: WASM still
    /// relies on the engine's memory/epoch limits and the host's OS policy.</summary>
    public void SetAllowedCapabilities(IEnumerable<CapabilityId> ids) =>
        _allowed = ids.ToHashSet();

    /// <summary>Single authorization point used by the capability_require host
    /// import. Returns false for malformed, undeclared or unavailable ids.</summary>
    public bool TryAuthorize(string idText, out ICapabilityEndpoint? endpoint)
    {
        endpoint = null;
        if (_require is null || !CapabilityId.TryParse(idText, out var id) ||
            (_allowed is not null && !_allowed.Contains(id)))
        {
            return false;
        }

        try
        {
            endpoint = _require(id);
            return endpoint is not null;
        }
        catch (DependencyMissingException)
        {
            return false;
        }
    }

    public void SetCancellationToken(CancellationToken cancellationToken) => _cancellation = cancellationToken;

    internal void Attach(WasmPluginInstance instance)
    {
        _instance = instance;
        // A component is reactivated after provider recovery: each activation
        // gets a fresh instance and a fresh handle table.
        _disposed = false;
        lock (_gate)
        {
            _handles.Clear();
        }
    }

    /// <summary>Host import gocordis.capability_buffer.</summary>
    public int Buffer() => RequestBufferOffset;

    /// <summary>Host import gocordis.capability_require(ptr, len) → handle (0 = rejected).</summary>
    public int Require(int pointer, int length)
    {
        if (!TryRead(pointer, length, out var text) || !TryAuthorize(text, out var endpoint) ||
            endpoint is null)
        {
            return 0;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            var handle = _nextHandle++;
            _handles[handle] = endpoint;
            return handle;
        }
    }

    /// <summary>Host import gocordis.capability_invoke(handle, ptr, len) → response length.</summary>
    public int Invoke(int handle, int pointer, int length)
    {
        if (!TryRead(pointer, length, out var request))
        {
            return 0;
        }

        ICapabilityEndpoint endpoint;
        lock (_gate)
        {
            if (_disposed || !_handles.TryGetValue(handle, out endpoint!))
            {
                return 0;
            }
        }

        // BLOCKING-call boundary (documented): Wasmtime host imports are
        // synchronous, so a guest capability call blocks the activation task
        // until the endpoint answers (bounded by the provider's own timeouts).
        // Self-cycles are impossible by construction — a component cannot both
        // provide and require the same key (rejected at mount) — so this call
        // can never re-enter the same guest/process that triggered it.
        var response = endpoint.InvokeAsync(
            CapabilityEnvelope.Json(request), _cancellation).AsTask().GetAwaiter().GetResult();
        var bytes = response.Payload;
        if (bytes.Length > MaxPayloadBytes)
        {
            return 0;
        }

        var memory = _instance?.Memory ?? throw new InvalidOperationException("WASM memory is not attached");
        bytes.CopyTo(memory.GetSpan(ResponseBufferOffset, bytes.Length));
        return bytes.Length;
    }

    /// <summary>Observability seam: reads the last host-written response buffer.</summary>
    public string ResponseText(int length)
    {
        var memory = _instance?.Memory
                     ?? throw new InvalidOperationException("WASM memory is not attached");
        if (length is < 0 or > MaxPayloadBytes || ResponseBufferOffset + length > memory.GetLength())
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return memory.ReadString(ResponseBufferOffset, length, Encoding.UTF8);
    }

    /// <summary>Host → guest call for a capability provided by this module.</summary>
    public async ValueTask<CapabilityEnvelope> InvokeProviderAsync(
        CapabilityId id,
        CapabilityContract contract,
        CapabilityEnvelope request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = _instance ?? throw new InvalidOperationException("WASM module is not prepared");
        var memory = instance.Memory ?? throw new InvalidOperationException("WASM memory is not exported");
        var bytes = request.Payload;
        if (bytes.Length > MaxPayloadBytes)
        {
            throw new CapabilityInvocationException(
                "invalid-payload", $"WASM capability payload exceeds {MaxPayloadBytes} bytes");
        }

        bytes.CopyTo(memory.GetSpan(RequestBufferOffset, bytes.Length));
        var result = instance.Invoke("capability_invoke", RequestBufferOffset, bytes.Length);
        if (result is object[] { Length: 2 } pair)
        {
            var pointer = Convert.ToInt32(pair[0]);
            var length = Convert.ToInt32(pair[1]);
            if (length is >= 0 and <= MaxPayloadBytes && pointer >= 0 &&
                pointer + length <= memory.GetLength())
            {
                return new CapabilityEnvelope(
                    contract.MediaType, memory.GetSpan(pointer, length).ToArray());
            }
        }

        throw new CapabilityInvocationException(
            "provider-abort", $"WASM capability '{id}' has an invalid capability_invoke result");
    }

    private bool TryRead(int pointer, int length, out string text)
    {
        text = string.Empty;
        var memory = _instance?.Memory;
        if (memory is null || length is < 1 or > MaxPayloadBytes ||
            pointer < 0 || pointer + length > memory.GetLength())
        {
            return false;
        }

        text = memory.ReadString(pointer, length, Encoding.UTF8);
        return true;
    }

    /// <summary>
    /// Releases only this consumer's handles. The endpoints come from
    /// <c>Context.Require</c> and are owned by the provider fiber's effect
    /// stack; disposing them here would kill a service shared by other
    /// consumers. Provider withdrawal unwinds the consumer fiber, which calls
    /// this method and clears the stale handles.
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
/// WASM component that declares capabilities from data and mediates calls in
/// both directions. Core still owns the fiber, declarations, effects and unwind.
/// </summary>
public sealed class WasmCapabilityComponent : Component
{
    private readonly Func<WasmCapabilityBridge, WasmPluginInstance> _createInstance;
    private readonly WasmCapabilityBridge _bridge;
    private readonly CapabilityCatalog _catalog;
    private readonly IReadOnlyList<CapabilityDeclaration> _provides;
    private readonly IReadOnlyList<CapabilityDeclaration> _requires;
    private readonly GenerationTracker _tracker;
    private readonly string _name;

    public WasmCapabilityComponent(
        string wat,
        string name,
        CapabilityCatalog catalog,
        IReadOnlyList<CapabilityDeclaration> declarations,
        GenerationTracker? tracker = null,
        WasmCapabilityBridge? capabilityBridge = null)
        : this(
            bridge => WasmPluginInstance.FromWat(wat, name, capabilityBridge: bridge),
            name, catalog, declarations, tracker, capabilityBridge)
    {
    }

    public WasmCapabilityComponent(
        byte[] wasm,
        string name,
        CapabilityCatalog catalog,
        IReadOnlyList<CapabilityDeclaration> declarations,
        GenerationTracker? tracker = null,
        WasmCapabilityBridge? capabilityBridge = null)
        : this(
            bridge => WasmPluginInstance.FromBytes(wasm, name, capabilityBridge: bridge),
            name, catalog, declarations, tracker, capabilityBridge)
    {
    }

    private WasmCapabilityComponent(
        Func<WasmCapabilityBridge, WasmPluginInstance> createInstance,
        string name,
        CapabilityCatalog catalog,
        IReadOnlyList<CapabilityDeclaration> declarations,
        GenerationTracker? tracker,
        WasmCapabilityBridge? capabilityBridge)
    {
        _bridge = capabilityBridge ?? new WasmCapabilityBridge();
        _createInstance = createInstance;
        _name = name;
        _catalog = catalog;
        _provides = declarations.Where(d => d.Direction == CapabilityDirection.Provide).ToArray();
        _requires = declarations.Where(d => d.Direction == CapabilityDirection.Require).ToArray();
        _tracker = tracker ?? new GenerationTracker();
    }

    public override string Name => _name;

    public override IReadOnlyList<ICapabilityKey> Inject() => _requires
        .Select(d => (ICapabilityKey)_catalog.KeyFor<ICapabilityEndpoint>(d.Id, CapabilityValueKind.Endpoint))
        .ToArray();

    public override IReadOnlyList<ICapabilityKey> Provide() => _provides
        .Select(d => (ICapabilityKey)_catalog.KeyFor<ICapabilityEndpoint>(d.Id, CapabilityValueKind.Endpoint))
        .ToArray();

    public async override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var bridge = _bridge;
        bridge.SetCancellationToken(context.CancellationToken);
        bridge.SetAllowedCapabilities(_requires.Select(declaration => declaration.Id));
        bridge.SetRequire(id =>
        {
            var key = _catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);
            return context.Require(key);
        });

        var backend = new WasmBackend(
            () => _createInstance(bridge), key: null, _tracker,
            requiredExports: _provides.Count > 0 ? ["capability_invoke"] : null);
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
