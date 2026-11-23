using System.Text;
using System.Text.Json;
using Gocordis.Core;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;

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
                if (direction == "provide" && declaration.ValueKind != CapabilityValueKind.Endpoint)
                {
                    throw new FormatException(
                        "WASM capability providers must declare kind 'endpoint'");
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
    private WasmPluginInstance? _instance;
    private int _nextHandle = 1;
    private bool _disposed;

    public void SetRequire(Func<CapabilityId, ICapabilityEndpoint> require) => _require = require;

    internal void Attach(WasmPluginInstance instance) => _instance = instance;

    /// <summary>Host import gocordis.capability_buffer.</summary>
    public int Buffer() => RequestBufferOffset;

    /// <summary>Host import gocordis.capability_require(ptr, len) → handle (0 = rejected).</summary>
    public int Require(int pointer, int length)
    {
        if (_require is null || !TryRead(pointer, length, out var text) ||
            !CapabilityId.TryParse(text, out var id))
        {
            return 0;
        }

        ICapabilityEndpoint endpoint;
        try
        {
            endpoint = _require(id);
        }
        catch (DependencyMissingException)
        {
            return 0;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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

        // Wasmtime imports are synchronous; endpoints used here must complete
        // without yielding. Managed endpoints are in-process; other backends
        // are marshalled by their own adapters before they reach this point.
        var response = endpoint.InvokeAsync(
            CapabilityEnvelope.Json(request), CancellationToken.None).AsTask().GetAwaiter().GetResult();
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
        CapabilityId id, CapabilityEnvelope request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = _instance ?? throw new InvalidOperationException("WASM module is not prepared");
        var memory = instance.Memory ?? throw new InvalidOperationException("WASM memory is not exported");
        var bytes = request.Payload;
        if (bytes.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException($"WASM capability payload exceeds {MaxPayloadBytes} bytes");
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
                    request.MediaType, memory.GetSpan(pointer, length).ToArray());
            }
        }

        throw new InvalidOperationException(
            $"WASM capability '{id}' has an invalid capability_invoke result");
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

    public ValueTask DisposeAsync()
    {
        List<ICapabilityEndpoint> endpoints;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            endpoints = [.. _handles.Values];
            _handles.Clear();
        }

        return ReleaseAll(endpoints);
    }

    private static async ValueTask ReleaseAll(IEnumerable<ICapabilityEndpoint> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            await endpoint.DisposeAsync();
        }
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
        bridge.SetRequire(id =>
        {
            var key = _catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);
            return context.Require(key);
        });

        var backend = new WasmBackend(() => _createInstance(bridge), key: null, _tracker);
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
                (request, cancellationToken) => bridge.InvokeProviderAsync(declaration.Id, request, cancellationToken));
            context.Provide(key, endpoint);
            await context.EffectAsync(
                static () => ValueTask.CompletedTask,
                async () => await endpoint.DisposeAsync());
        }

        return null;
    }
}
