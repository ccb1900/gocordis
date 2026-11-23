using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using NuGet.Versioning;

namespace Gocordis.Core;

/// <summary>
/// A declared consumer range cannot be satisfied by the declared provider
/// version, or more than one provider claimed the same capability id. This is
/// an assembly-time rejection; Core still owns the resulting binding.
/// </summary>
public sealed class CapabilityVersionException : InvalidOperationException
{
    public CapabilityVersionException(string message) : base(message) { }
}

/// <summary>
/// Uniform error vocabulary for capability calls across backends. The Code is
/// stable on the wire so a consumer can distinguish timeout, trap/abort,
/// withdrawal and malformed payload without parsing backend-specific text.
/// </summary>
public sealed class CapabilityInvocationException : InvalidOperationException
{
    public CapabilityInvocationException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>
/// Stable external identity of a capability. This is a host/protocol-layer value,
/// NOT Core identity: Core still matches capabilities by <see cref="ICapabilityKey"/>
/// reference identity. The host allocates exactly one Key instance per CapabilityId,
/// so every backend (Managed/WASM/Process) can refer to the same capability without
/// sharing a CLR type identity or an ALC.
/// </summary>
public readonly record struct CapabilityId
{
    public CapabilityId(string @namespace, string name)
    {
        Namespace = ValidatePart(@namespace, nameof(@namespace));
        Name = ValidatePart(name, nameof(name));
    }

    public string Namespace { get; }
    public string Name { get; }

    public override string ToString() => $"{Namespace}/{Name}";

    /// <summary>Wire form: namespace/name. Both parts are validated on construction.</summary>
    public static bool TryParse(string? text, out CapabilityId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var separator = text.IndexOf('/');
        if (separator <= 0 || separator == text.Length - 1 || text.IndexOf('/', separator + 1) >= 0)
        {
            return false;
        }

        try
        {
            id = new CapabilityId(text[..separator], text[(separator + 1)..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ValidatePart(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 128 || value[0] == '.' || value[^1] == '.')
        {
            throw new ArgumentException($"invalid capability {parameter} '{value}'", parameter);
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                throw new ArgumentException($"invalid capability {parameter} '{value}'", parameter);
            }
        }

        return value;
    }
}

/// <summary>Shape of the value that crosses a backend boundary.</summary>
public enum CapabilityValueKind
{
    /// <summary>A JSON document carried as <see cref="CapabilityEnvelope"/>.</summary>
    Json,

    /// <summary>UTF-8 text.</summary>
    Text,

    /// <summary>A 64-bit integer.</summary>
    Int64,

    /// <summary>Opaque bytes carried as <see cref="CapabilityEnvelope"/>.</summary>
    Bytes,

    /// <summary>A callable service carried as <see cref="ICapabilityEndpoint"/>.</summary>
    Endpoint,
}

/// <summary>
/// Provider-side contract. Version is compatibility metadata, never part of
/// <see cref="CapabilityId"/>; consumers gate it with a NuGet version range.
/// </summary>
public sealed record CapabilityContract(
    CapabilityId Id,
    CapabilityValueKind ValueKind,
    NuGetVersion? Version,
    string MediaType = "application/json",
    string? SchemaRef = null);

public enum CapabilityDirection
{
    Require,
    Provide,
}

/// <summary>
/// One declaration as it appears in a package manifest, WASM custom section or
/// Process handshake. Wire grammar (6 '|'-separated fields, trailing ones optional):
/// <c>direction|namespace/name|kind|version-or-range|media-type|schema-ref</c>.
/// </summary>
public sealed record CapabilityDeclaration(
    CapabilityId Id,
    CapabilityDirection Direction,
    CapabilityValueKind ValueKind = CapabilityValueKind.Json,
    string? Version = null,
    string? VersionRange = null,
    string MediaType = "application/json",
    string? SchemaRef = null)
{
    private static readonly Dictionary<string, CapabilityValueKind> Kinds = new(StringComparer.Ordinal)
    {
        ["json"] = CapabilityValueKind.Json,
        ["text"] = CapabilityValueKind.Text,
        ["int64"] = CapabilityValueKind.Int64,
        ["bytes"] = CapabilityValueKind.Bytes,
        ["endpoint"] = CapabilityValueKind.Endpoint,
    };

    /// <summary>Canonical, order-stable wire form used in manifests and checksums.</summary>
    public string ToWireString() => string.Join('|',
        Direction == CapabilityDirection.Provide ? "provide" : "require",
        Id.ToString(),
        ValueKind.ToString().ToLowerInvariant(),
        Direction == CapabilityDirection.Provide ? Version ?? string.Empty : VersionRange ?? string.Empty,
        MediaType,
        SchemaRef ?? string.Empty);

    public static CapabilityDeclaration Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = text.Split('|');
        if (parts.Length is < 3 or > 6)
        {
            throw new FormatException($"invalid capability declaration '{text}'");
        }

        var direction = parts[0] switch
        {
            "provide" => CapabilityDirection.Provide,
            "require" => CapabilityDirection.Require,
            _ => throw new FormatException($"invalid capability direction '{parts[0]}'"),
        };
        if (!CapabilityId.TryParse(parts[1], out var id))
        {
            throw new FormatException($"invalid capability id '{parts[1]}'");
        }

        if (!Kinds.TryGetValue(parts[2], out var kind))
        {
            throw new FormatException($"invalid capability kind '{parts[2]}'");
        }

        var versionText = parts.Length > 3 ? parts[3] : string.Empty;
        string? version = null;
        string? range = null;
        if (direction == CapabilityDirection.Provide)
        {
            if (!string.IsNullOrEmpty(versionText))
            {
                if (!NuGetVersion.TryParse(versionText, out var parsed))
                {
                    throw new FormatException($"invalid capability version '{versionText}'");
                }

                version = parsed.ToNormalizedString();
            }
        }
        else if (!string.IsNullOrEmpty(versionText))
        {
            if (!NuGet.Versioning.VersionRange.TryParse(versionText, out _))
            {
                throw new FormatException($"invalid capability range '{versionText}'");
            }

            range = versionText;
        }

        var mediaType = parts.Length > 4 && parts[4].Length > 0 ? parts[4] : "application/json";
        var schema = parts.Length > 5 && parts[5].Length > 0 ? parts[5] : null;
        return new CapabilityDeclaration(id, direction, kind, version, range, mediaType, schema);
    }
}

/// <summary>Wire value: media type plus payload bytes. Parsed at the boundary, trusted inside.</summary>
public sealed record CapabilityEnvelope(string MediaType, byte[] Payload)
{
    public static CapabilityEnvelope Json(string json) =>
        new("application/json", Encoding.UTF8.GetBytes(json));

    public static CapabilityEnvelope Text(string text) =>
        new("text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));

    public static CapabilityEnvelope Bytes(byte[] bytes) =>
        new("application/octet-stream", bytes);

    public string AsText() => Encoding.UTF8.GetString(Payload);
}

/// <summary>
/// Callable face of a cross-backend capability. Lifetimes belong to the owning
/// component's effect stack; this interface is only a call/marshalling primitive.
/// </summary>
public interface ICapabilityEndpoint : IAsyncDisposable
{
    CapabilityId Id { get; }
    CapabilityContract Contract { get; }
    ValueTask<CapabilityEnvelope> InvokeAsync(CapabilityEnvelope request, CancellationToken cancellationToken);
}

/// <summary>
/// Host-side endpoint adapter. A backend supplies only a marshalling delegate;
/// lifetime (registration and disposal) stays with the owning component's effect
/// stack. No backend CLR type ever crosses into Core through this type.
/// </summary>
public sealed class DelegatingCapabilityEndpoint : ICapabilityEndpoint
{
    private readonly Func<CapabilityEnvelope, CancellationToken, ValueTask<CapabilityEnvelope>> _invoke;
    private readonly Func<ValueTask>? _dispose;
    private readonly object _disposeGate = new();
    private int _disposed;
    private long _inFlight;
    private TaskCompletionSource? _drained;
    private TaskCompletionSource? _disposalCompletion;

    public DelegatingCapabilityEndpoint(
        CapabilityId id,
        CapabilityContract contract,
        Func<CapabilityEnvelope, CancellationToken, ValueTask<CapabilityEnvelope>> invoke,
        Func<ValueTask>? dispose = null)
    {
        Id = id;
        Contract = contract;
        _invoke = invoke;
        _dispose = dispose;
    }

    public CapabilityId Id { get; }
    public CapabilityContract Contract { get; }

    public async ValueTask<CapabilityEnvelope> InvokeAsync(
        CapabilityEnvelope request, CancellationToken cancellationToken)
    {
        // Enter-then-check: a caller that passed a disposed=0 check must still
        // be counted by the drain, otherwise Dispose could release the resource
        // under a call that was linearizing concurrently.
        Interlocked.Increment(ref _inFlight);
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (Interlocked.Decrement(ref _inFlight) == 0)
            {
                Interlocked.Exchange(ref _drained, null)?.TrySetResult();
            }

            throw new ObjectDisposedException(nameof(DelegatingCapabilityEndpoint));
        }

        try
        {
            return await _invoke(request, cancellationToken);
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0 && Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _drained, null)?.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Linearization point for withdrawal: disposal is rejected once (first
    /// caller wins), and the underlying resource is released only after every
    /// in-flight call has finished, so a provider teardown can never abort a
    /// call that was already linearized.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        bool owner;
        lock (_disposeGate)
        {
            owner = _disposalCompletion is null;
            if (owner)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposalCompletion = completion;
                _disposed = 1;
            }
            else
            {
                completion = _disposalCompletion!;
            }
        }

        // Every Dispose caller awaits the SAME promise: even a late caller is
        // told when the underlying provider resource is actually gone (or
        // failed to go), instead of assuming success on a no-op return.
        return owner ? new ValueTask(DisposeCoreAsync(completion)) : new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            if (Interlocked.Read(ref _inFlight) > 0)
            {
                var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _drained = drained;
                if (Interlocked.Read(ref _inFlight) > 0)
                {
                    await drained.Task;
                }
            }

            if (_dispose is not null)
            {
                await _dispose();
            }

            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
    }
}

/// <summary>
/// Host-owned Id → Key allocator shared by every backend. This is NOT a second
/// registry: it holds no provider/consumer or Fiber state. All matching, binding
/// and lifecycle decisions remain with <see cref="Runtime"/>.
/// </summary>
public interface ICapabilityCatalog
{
    IReadOnlyList<CapabilityDeclaration> Declarations { get; }

    bool IsDeclared(CapabilityId id);

    bool TryDescribe(CapabilityId id, out CapabilityContract contract);

    /// <summary>Returns the single authoritative Key instance for (Id, kind).</summary>
    Key<T> KeyFor<T>(CapabilityId id, CapabilityValueKind kind);
}

/// <summary>
/// One immutable declaration snapshot. Readers never take a lock; updates
/// publish a whole new state with an incremented Revision. Key objects are
/// owned separately and intentionally survive every revision.
/// </summary>
public sealed record CatalogState(
    ImmutableArray<CapabilityDeclaration> All,
    ImmutableDictionary<CapabilityId, CapabilityValueKind> Kinds,
    ImmutableDictionary<CapabilityId, ImmutableArray<CapabilityContract>> Contracts,
    ImmutableDictionary<(CapabilityId Id, CapabilityDirection Direction), ImmutableArray<CapabilityDeclaration>> ByIdDirection,
    long Revision)
{
    public static CatalogState Empty { get; } = new(
        [],
        ImmutableDictionary<CapabilityId, CapabilityValueKind>.Empty,
        ImmutableDictionary<CapabilityId, ImmutableArray<CapabilityContract>>.Empty,
        ImmutableDictionary<(CapabilityId, CapabilityDirection), ImmutableArray<CapabilityDeclaration>>.Empty,
        0);
}

public sealed class CapabilityCatalog : ICapabilityCatalog
{
    private readonly object _gate = new();
    private CatalogState _state = CatalogState.Empty;
    // Stable Key identity: never cleared on replacement (ADR: identity survives revisions).
    private readonly ConcurrentDictionary<(CapabilityId Id, CapabilityValueKind Kind), ICapabilityKey> _keys = new();

    public long Revision => Volatile.Read(ref _state).Revision;

    public IReadOnlyList<CapabilityDeclaration> Declarations => Volatile.Read(ref _state).All;

    public bool IsDeclared(CapabilityId id) => Volatile.Read(ref _state).Kinds.ContainsKey(id);

    public bool TryDescribe(CapabilityId id, out CapabilityContract contract)
    {
        if (Volatile.Read(ref _state).Contracts.TryGetValue(id, out var contracts) &&
            contracts.Length > 0)
        {
            contract = contracts.OrderByDescending(candidate => candidate.Version).First();
            return true;
        }

        contract = null!;
        return false;
    }

    /// <summary>
    /// One declaration plan per application revision. A fully validated
    /// immutable snapshot is published with a single volatile write; a failed
    /// plan never mutates the live state. Key instances are NOT recreated.
    /// </summary>
    public void Replace(IEnumerable<CapabilityDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var candidate = Build(declarations, Volatile.Read(ref _state).Revision + 1, validateProviders: true);
        lock (_gate)
        {
            Volatile.Write(ref _state, candidate);
        }
    }

    /// <summary>Additive path used by assembly-time probes; kind conflicts are
    /// still rejected, provider uniqueness is checked explicitly later.</summary>
    public void Declare(IEnumerable<CapabilityDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        lock (_gate)
        {
            var current = Volatile.Read(ref _state);
            Volatile.Write(ref _state,
                Build(current.All.AddRange(declarations), current.Revision + 1, validateProviders: false));
        }
    }

    private static CatalogState Build(
        IEnumerable<CapabilityDeclaration> declarations, long revision, bool validateProviders)
    {
        var all = declarations.ToImmutableArray();
        var kinds = ImmutableDictionary.CreateBuilder<CapabilityId, CapabilityValueKind>();
        var contractsLocal = new Dictionary<CapabilityId, List<CapabilityContract>>();
        var byDirectionLocal =
            new Dictionary<(CapabilityId, CapabilityDirection), List<CapabilityDeclaration>>();

        foreach (var declaration in all)
        {
            if (kinds.TryGetValue(declaration.Id, out var existing) && existing != declaration.ValueKind)
            {
                throw new InvalidOperationException(
                    $"capability '{declaration.Id}' is declared with conflicting kinds " +
                    $"'{existing}' and '{declaration.ValueKind}'");
            }

            kinds[declaration.Id] = declaration.ValueKind;
            var indexKey = (declaration.Id, declaration.Direction);
            if (!byDirectionLocal.TryGetValue(indexKey, out var indexed))
            {
                indexed = [];
                byDirectionLocal[indexKey] = indexed;
            }

            indexed.Add(declaration);

            if (declaration.Direction != CapabilityDirection.Provide)
            {
                continue;
            }

            if (!contractsLocal.TryGetValue(declaration.Id, out var contracts))
            {
                contracts = [];
                contractsLocal[declaration.Id] = contracts;
            }

            contracts.Add(new CapabilityContract(
                declaration.Id, declaration.ValueKind,
                declaration.Version is null ? null : NuGetVersion.Parse(declaration.Version),
                declaration.MediaType, declaration.SchemaRef));
        }

        if (validateProviders)
        {
            ValidateCandidateCompatibility(all);
        }

        var contractsResult = ImmutableDictionary.CreateBuilder<CapabilityId, ImmutableArray<CapabilityContract>>();
        foreach (var (id, list) in contractsLocal)
        {
            contractsResult[id] = [.. list];
        }

        var indexResult = ImmutableDictionary.CreateBuilder<
            (CapabilityId Id, CapabilityDirection Direction), ImmutableArray<CapabilityDeclaration>>();
        foreach (var (key, list) in byDirectionLocal)
        {
            indexResult[key] = [.. list];
        }

        return new CatalogState(all, kinds.ToImmutable(), contractsResult.ToImmutable(), indexResult.ToImmutable(), revision);
    }

    /// <summary>
    /// Backend metadata must not drift from the declared plan. Reads the live
    /// immutable snapshot once and uses the (Id, Direction) index.
    /// </summary>
    public void EnsureConsistent(IEnumerable<CapabilityDeclaration> moduleDeclarations)
    {
        ArgumentNullException.ThrowIfNull(moduleDeclarations);
        var state = Volatile.Read(ref _state);
        foreach (var module in moduleDeclarations)
        {
            if (!state.ByIdDirection.TryGetValue((module.Id, module.Direction), out var planned) ||
                planned.Length == 0)
            {
                throw new InvalidOperationException(
                    $"capability '{module.Id}' ({module.Direction}) is not in the declaration plan");
            }

            if (planned.Any(declaration => !declaration.Equals(module)))
            {
                throw new InvalidOperationException(
                    $"capability '{module.Id}' ({module.Direction}) conflicts with the declaration plan: " +
                    $"[{string.Join(" | ", planned.Select(p => p.ToWireString()))}] vs [{module.ToWireString()}]");
            }
        }
    }

    /// <summary>Assembly-time version gate over the current immutable snapshot.</summary>
    public void ValidateCompatibility() => ValidateCandidateCompatibility(Volatile.Read(ref _state).All);

    /// <summary>Pure version gate over a candidate declaration set.</summary>
    private static void ValidateCandidateCompatibility(ImmutableArray<CapabilityDeclaration> declarations)
    {
        foreach (var group in declarations
                     .Where(declaration => declaration.Direction == CapabilityDirection.Provide)
                     .GroupBy(declaration => declaration.Id))
        {
            if (group.Count() > 1)
            {
                throw new CapabilityVersionException(
                    $"capability '{group.Key}' has {group.Count()} providers; " +
                    "use distinct ids or Broker<T> for multi-implementation");
            }
        }

        foreach (var requirement in declarations.Where(declaration =>
                     declaration.Direction == CapabilityDirection.Require &&
                     declaration.VersionRange is not null))
        {
            var provider = declarations.FirstOrDefault(declaration =>
                declaration.Direction == CapabilityDirection.Provide &&
                declaration.Id == requirement.Id);
            if (provider is null)
            {
                continue; // Dependency loss is Pending, never a failed declaration.
            }

            var version = NuGetVersion.Parse(provider.Version ?? "0.0.0");
            var range = NuGet.Versioning.VersionRange.Parse(requirement.VersionRange!);
            if (!range.Satisfies(version))
            {
                throw new CapabilityVersionException(
                    $"capability '{requirement.Id}' provider version {version} " +
                    $"does not satisfy consumer range {range}");
            }
        }
    }

    public Key<T> KeyFor<T>(CapabilityId id, CapabilityValueKind kind)
    {
        // Single immutable snapshot read: no readers' lock, no torn state.
        var state = Volatile.Read(ref _state);
        if (!state.Kinds.TryGetValue(id, out var declared))
        {
            throw new UndeclaredCapabilityException(
                $"capability '{id}' is not declared by any installed package");
        }

        if (declared != kind)
        {
            throw new UndeclaredCapabilityException(
                $"capability '{id}' is declared as '{declared}', not '{kind}'");
        }

        var expected = ExpectedClrType(kind);
        if (typeof(T) != expected)
        {
            throw new UndeclaredCapabilityException(
                $"capability '{id}' of kind '{kind}' requires Key<{expected.Name}> but Key<{typeof(T).Name}> was requested");
        }

        return (Key<T>)_keys.GetOrAdd((id, kind), _ => new Key<T>(id.ToString()));
    }

    private static Type ExpectedClrType(CapabilityValueKind kind) => kind switch
    {
        CapabilityValueKind.Endpoint => typeof(ICapabilityEndpoint),
        CapabilityValueKind.Text => typeof(string),
        CapabilityValueKind.Int64 => typeof(long),
        _ => typeof(CapabilityEnvelope),
    };
}
