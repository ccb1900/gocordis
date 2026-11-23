using System.Collections.Concurrent;
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
    private bool _disposed;

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

    public ValueTask<CapabilityEnvelope> InvokeAsync(CapabilityEnvelope request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _invoke(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_dispose is not null)
        {
            await _dispose();
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

public sealed class CapabilityCatalog : ICapabilityCatalog
{
    private readonly object _gate = new();
    private readonly List<CapabilityDeclaration> _declarations = [];
    private readonly Dictionary<CapabilityId, List<CapabilityContract>> _contracts = [];
    private readonly Dictionary<CapabilityId, CapabilityValueKind> _kinds = [];
    private readonly ConcurrentDictionary<(CapabilityId Id, CapabilityValueKind Kind), ICapabilityKey> _keys = new();

    public IReadOnlyList<CapabilityDeclaration> Declarations
    {
        get { lock (_gate) { return [.. _declarations]; } }
    }

    public bool IsDeclared(CapabilityId id)
    {
        lock (_gate)
        {
            return _kinds.ContainsKey(id);
        }
    }

    public bool TryDescribe(CapabilityId id, out CapabilityContract contract)
    {
        lock (_gate)
        {
            if (_contracts.TryGetValue(id, out var contracts) && contracts.Count > 0)
            {
                contract = contracts.OrderByDescending(c => c.Version).First();
                return true;
            }

            contract = null!;
            return false;
        }
    }

    /// <summary>Adds declarations during assembly, before any fiber activates.</summary>
    public void Declare(IEnumerable<CapabilityDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        lock (_gate)
        {
            foreach (var declaration in declarations)
            {
                _declarations.Add(declaration);
                if (_kinds.TryGetValue(declaration.Id, out var existing) && existing != declaration.ValueKind)
                {
                    throw new InvalidOperationException(
                        $"capability '{declaration.Id}' is declared with conflicting kinds " +
                        $"'{existing}' and '{declaration.ValueKind}'");
                }

                _kinds[declaration.Id] = declaration.ValueKind;
                if (declaration.Direction == CapabilityDirection.Provide)
                {
                    if (!_contracts.TryGetValue(declaration.Id, out var contracts))
                    {
                        contracts = [];
                        _contracts[declaration.Id] = contracts;
                    }

                    contracts.Add(new CapabilityContract(
                        declaration.Id, declaration.ValueKind,
                        declaration.Version is null ? null : NuGetVersion.Parse(declaration.Version),
                        declaration.MediaType, declaration.SchemaRef));
                }
            }
        }
    }

    /// <summary>
    /// Assembly-time version gate. A missing provider is allowed (the consumer
    /// stays Pending), but an incompatible provider version or two providers
    /// for one id is rejected. Version selection therefore happens once, here,
    /// before Core sees any key; Core keeps its single provider-per-key rule.
    /// </summary>
    public void ValidateCompatibility()
    {
        lock (_gate)
        {
            foreach (var group in _declarations
                         .Where(declaration => declaration.Direction == CapabilityDirection.Provide)
                         .GroupBy(declaration => declaration.Id))
            {
                var versions = group
                    .Select(declaration => NuGetVersion.Parse(declaration.Version ?? "0.0.0"))
                    .Distinct()
                    .ToArray();
                if (versions.Length > 1)
                {
                    throw new CapabilityVersionException(
                        $"capability '{group.Key}' is provided at {versions.Length} different versions " +
                        "[" + string.Join(", ", versions) + "]; use distinct ids or Broker<T> for multi-implementation");
                }
            }

            foreach (var requirement in _declarations.Where(declaration =>
                         declaration.Direction == CapabilityDirection.Require &&
                         declaration.VersionRange is not null))
            {
                var provider = _declarations.FirstOrDefault(declaration =>
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
    }

    public Key<T> KeyFor<T>(CapabilityId id, CapabilityValueKind kind)
    {
        if (!IsDeclared(id))
        {
            throw new UndeclaredCapabilityException(
                $"capability '{id}' is not declared by any installed package");
        }

        CapabilityValueKind declared;
        lock (_gate)
        {
            if (!_kinds.TryGetValue(id, out declared))
            {
                throw new UndeclaredCapabilityException(
                    $"capability '{id}' is not declared by any installed package");
            }
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
