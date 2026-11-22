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
