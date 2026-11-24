using Gocordis.Core;
using Gocordis.Runtime.Wasm;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Paper §6.3: the WASM capability_require host import must be constrained by
/// the module's declared capability list. This is an import allowlist, not a
/// claim that the WASM host bridge is a security sandbox.
/// </summary>
public sealed class WasmImportAllowlistTests
{
    [Fact]
    public void UndeclaredCapabilityIdIsRejectedAtTheImportBoundary()
    {
        var declared = new CapabilityId("org.example", "allowed");
        var undeclared = new CapabilityId("org.example", "forbidden");
        var endpoint = new DelegatingCapabilityEndpoint(
            declared,
            new CapabilityContract(declared, CapabilityValueKind.Endpoint, null),
            (request, _) => ValueTask.FromResult(request));

        var bridge = new WasmCapabilityBridge();
        bridge.SetAllowedCapabilities([declared]);
        bridge.SetRequire(id => id == declared
            ? endpoint
            : throw new DependencyMissingException(id.ToString()));

        Assert.True(bridge.TryAuthorize(declared.ToString(), out var authorized));
        Assert.Same(endpoint, authorized);
        Assert.False(bridge.TryAuthorize(undeclared.ToString(), out var rejected));
        Assert.Null(rejected);
        Assert.False(bridge.TryAuthorize("not-a-capability-id", out _));
    }

    [Fact]
    public void CustomSectionDeclarationsBecomeTheImportAllowlist()
    {
        const string wat = """
            (module
              (@custom "gocordis.capabilities" "{\"requires\":[{\"id\":\"org.example/from-section\",\"kind\":\"endpoint\"}]}"))
            """;
        var declaredIds = WasmCapabilityDeclarations.ReadFromWat(wat)
            .Where(declaration => declaration.Direction == CapabilityDirection.Require)
            .Select(declaration => declaration.Id)
            .ToArray();
        Assert.Equal([new CapabilityId("org.example", "from-section")], declaredIds);

        var endpoint = new DelegatingCapabilityEndpoint(
            declaredIds[0],
            new CapabilityContract(declaredIds[0], CapabilityValueKind.Endpoint, null),
            (request, _) => ValueTask.FromResult(request));
        var bridge = new WasmCapabilityBridge();
        bridge.SetAllowedCapabilities(declaredIds);
        bridge.SetRequire(_ => endpoint);

        Assert.True(bridge.TryAuthorize("org.example/from-section", out _));
        Assert.False(bridge.TryAuthorize("org.example/not-declared", out _));
    }

    [Fact]
    public void WithoutAnAllowlistTheCoreRequireResolverStillRejectsUnknownIds()
    {
        var bridge = new WasmCapabilityBridge();
        bridge.SetRequire(_ => throw new DependencyMissingException("unknown"));

        Assert.False(bridge.TryAuthorize("org.example/unknown", out _));
    }

    [Fact]
    public void DeclaredButUnavailableCapabilityIsAlsoRejected()
    {
        var declared = new CapabilityId("org.example", "allowed");
        var bridge = new WasmCapabilityBridge();
        bridge.SetAllowedCapabilities([declared]);
        bridge.SetRequire(_ => throw new DependencyMissingException(declared.ToString()));

        Assert.False(bridge.TryAuthorize(declared.ToString(), out var endpoint));
        Assert.Null(endpoint);
    }
}
