using Gocordis.Core;
using Gocordis.Runtime.Wasm;
using Wasmtime;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class WasmRuntimeTests
{
    private const string Wat = """
        (module
          (func (export "runtime_component_create") (result i32) i32.const 42)
          (func (export "runtime_component_destroy") (param i32))
          (func (export "add") (param i32 i32) (result i32)
            local.get 0
            local.get 1
            i32.add))
        """;

    [Fact]
    public void WasmtimeHostInvokesExportedFunctions()
    {
        using var instance = WasmPluginInstance.FromWat(Wat);

        var result = instance.Invoke("add", 2, 3);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task WasmComponentFollowsCoreActivationAndDisposal()
    {
        var key = new Key<int>("wasm-value");
        await using var runtime = new Gocordis.Core.Runtime();
        var fiber = await runtime.LoadAsync(new WasmComponent(Wat, key));

        Assert.Equal(FiberState.Active, fiber.State);

        var observed = new List<int>();
        await runtime.LoadAsync(new ProbeComponent(
            "consumer",
            context => observed.Add(context.Require(key)),
            [key]));
        Assert.Equal(42, Assert.Single(observed));

        await runtime.DisposeAsync(fiber);
        Assert.Equal(FiberState.Gone, fiber.State);
    }


    [Fact]
    public void ModuleTypeCustomSectionIsReadFromTheBinaryModule()
    {
        const string wat = """
            (module
              (@custom "module_type" "industrial.camera")
              (func (export "runtime_component_create") (result i32) i32.const 1)
              (func (export "runtime_component_destroy") (param i32)))
            """;

        Assert.Equal("industrial.camera", WasmCustomSections.ReadFromWat(wat, "module_type"));
        Assert.Null(WasmCustomSections.ReadFromWat(wat, "missing"));
    }

    [Fact]
    public void HostCapabilityBridgeReceivesGuestCalls()
    {
        const string wat = """
            (module
              (import "gocordis" "report" (func $report (param i32)))
              (func (export "runtime_component_create") (result i32)
                i32.const 7
                call $report
                i32.const 11))
            """;
        var bridge = new WasmHostBridge();

        using var instance = WasmPluginInstance.FromWat(wat, "bridged", bridge);
        var value = instance.Invoke("runtime_component_create");

        Assert.Equal(11, value);
        Assert.Equal(new[] { 7 }, bridge.Reports);
    }

    [Fact]
    public async Task WarmHotSwapReplacesTheModuleAndKeepsTheOldFiberGone()
    {
        var key = new Key<int>("hmr-value");
        const string v1 = """
            (module
              (func (export "runtime_component_create") (result i32) i32.const 42)
              (func (export "runtime_component_destroy") (param i32)))
            """;
        const string v2 = """
            (module
              (func (export "runtime_component_create") (result i32) i32.const 99)
              (func (export "runtime_component_destroy") (param i32)))
            """;

        await using var runtime = new Gocordis.Core.Runtime();
        var swapper = new WasmHotSwapper(runtime, key);
        var first = await swapper.ReplaceAsync(v1);
        var second = await swapper.ReplaceAsync(v2);

        Assert.Equal(FiberState.Gone, first.State);
        Assert.Equal(FiberState.Active, second.State);
        Assert.Same(second, swapper.Current);

        var observed = new List<int>();
        await runtime.LoadAsync(new ProbeComponent("consumer", ctx => observed.Add(ctx.Require(key)), [key]));
        Assert.Equal(99, Assert.Single(observed));

        const string invalid = """
            (module (func (export "broken") (result i32)))
            """;
        await Assert.ThrowsAnyAsync<Exception>(() => swapper.ReplaceAsync(invalid));
        Assert.Same(second, swapper.Current);
    }


    [Fact]
    public async Task WasmComponentUsesTheSuppliedHostBridge()
    {
        const string wat = """
            (module
              (import "gocordis" "report" (func $report (param i32)))
              (func (export "runtime_component_create") (result i32)
                i32.const 33 call $report i32.const 5)
              (func (export "runtime_component_destroy") (param i32)))
            """;
        var bridge = new WasmHostBridge();
        var key = new Key<int>("bridged-component");
        await using var runtime = new Gocordis.Core.Runtime();

        var fiber = await runtime.LoadAsync(new WasmComponent(wat, key, bridge));

        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal(new[] { 33 }, bridge.Reports);
    }

    [Fact]
    public async Task ConcurrentWasmReplacementsAreSerialized()
    {
        var key = new Key<int>("serialized-hmr");
        const string wat = """
            (module
              (func (export "runtime_component_create") (result i32) i32.const 8)
              (func (export "runtime_component_destroy") (param i32)))
            """;
        await using var runtime = new Gocordis.Core.Runtime();
        var swapper = new WasmHotSwapper(runtime, key);

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => swapper.ReplaceAsync(wat)));

        Assert.Equal(4, results.Length);
        Assert.All(results, fiber => Assert.NotNull(fiber));
        Assert.Equal(FiberState.Active, swapper.Current!.State);
        Assert.Single(runtime.Snapshot().Fibers);
    }

    [Fact]
    public void ModuleLargerThanSixtyFourMiBIsRejectedBeforeCompilation()
    {
        Assert.Equal(64L * 1024 * 1024, WasmPluginInstance.MaxModuleBytes);

        Assert.Throws<WasmModuleTooLargeException>(() =>
            WasmPluginInstance.EnsureModuleSize(WasmPluginInstance.MaxModuleBytes + 1));

        var oversized = new byte[WasmPluginInstance.MaxModuleBytes + 1];
        var error = Assert.Throws<WasmModuleTooLargeException>(() =>
            WasmPluginInstance.FromBytes(oversized));
        Assert.Equal(oversized.LongLength, error.Size);
    }

    private sealed class ProbeComponent : Component
    {
        private readonly Action<Context> _apply;
        private readonly IReadOnlyList<ICapabilityKey> _inject;

        public ProbeComponent(string name, Action<Context> apply, IReadOnlyList<ICapabilityKey> inject)
        {
            Name = name;
            _apply = apply;
            _inject = inject;
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => _inject;

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _apply(context);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }
}
