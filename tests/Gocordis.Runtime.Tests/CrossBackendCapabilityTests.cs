using System.Text.Json;
using Gocordis.Core;
using Gocordis.Runtime.Process;
using Gocordis.Runtime.Wasm;
using Xunit;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Executable evidence for the unified cross-backend capability contract:
/// declarations become host-owned Core keys, and every backend uses those keys
/// through the one Runtime rather than a backend-local dependency manager.
/// </summary>
public sealed class CrossBackendCapabilityTests
{
    private const string WasmProviderWat = """
        (module
          (@custom "gocordis.capabilities" "{\"provides\":[{\"id\":\"org.example.calc/add\",\"kind\":\"endpoint\",\"version\":\"1.0.0\"}]}")
          (memory (export "memory") 1)
          (func (export "runtime_component_create") (result i32) i32.const 5)
          (func (export "runtime_component_destroy") (param i32))
          (func (export "capability_invoke") (param i32 i32) (result i32 i32)
            i32.const 2048
            i32.const 55
            i32.store8
            i32.const 2048
            i32.const 1))
        """;

    private const string WasmConsumerWat = """
        (module
          (@custom "gocordis.capabilities" "{\"requires\":[{\"id\":\"a/b\",\"kind\":\"endpoint\"}]}")
          (import "gocordis" "capability_buffer" (func $buffer (result i32)))
          (import "gocordis" "capability_require" (func $require (param i32 i32) (result i32)))
          (import "gocordis" "capability_invoke" (func $invoke (param i32 i32 i32) (result i32)))
          (memory (export "memory") 1)
          (func (export "runtime_component_destroy") (param i32))
          (func (export "runtime_component_create") (result i32)
            (local $buf i32)
            (local $handle i32)
            (local.set $buf (call $buffer))
            (i32.store8 (local.get $buf) (i32.const 97))
            (i32.store8 (i32.add (local.get $buf) (i32.const 1)) (i32.const 47))
            (i32.store8 (i32.add (local.get $buf) (i32.const 2)) (i32.const 98))
            (local.set $handle (call $require (local.get $buf) (i32.const 3)))
            (i32.store8 (local.get $buf) (i32.const 120))
            (call $invoke (local.get $handle) (local.get $buf) (i32.const 1))))
        """;

    [Fact]
    public void CatalogAllocatesOneTypedKeyPerDeclaredCapability()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse(
            "provide|org.example.calc/add|endpoint|1.2.0|application/json|calc");
        catalog.Declare([declaration]);
        var id = new CapabilityId("org.example.calc", "add");

        var first = catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);
        var second = catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);

        Assert.Same(first, second);
        Assert.True(catalog.IsDeclared(id));
        Assert.True(catalog.TryDescribe(id, out var contract));
        Assert.Equal("1.2.0", contract.Version!.ToNormalizedString());
        Assert.Throws<UndeclaredCapabilityException>(
            () => catalog.KeyFor<string>(id, CapabilityValueKind.Endpoint));
        Assert.Throws<UndeclaredCapabilityException>(
            () => catalog.KeyFor<ICapabilityEndpoint>(
                new CapabilityId("org.example.calc", "missing"), CapabilityValueKind.Endpoint));
    }

    [Fact]
    public void CapabilityIdRejectsAmbiguousOrUnsafeNames()
    {
        Assert.False(CapabilityId.TryParse("a/b/c", out _));
        Assert.False(CapabilityId.TryParse("/name", out _));
        Assert.False(CapabilityId.TryParse("ns/na me", out _));
        Assert.True(CapabilityId.TryParse("org.example/name_1", out _));
        Assert.Throws<FormatException>(() => CapabilityDeclaration.Parse("bad|ns/name|endpoint"));
    }

    [Fact]
    public async Task WasmProviderServesAManagedConsumerThroughTheCatalog()
    {
        var catalog = new CapabilityCatalog();
        var declarations = WasmCapabilityDeclarations.ReadFromWat(WasmProviderWat);
        catalog.Declare(declarations);
        var id = new CapabilityId("org.example.calc", "add");
        var key = catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(
            new WasmCapabilityComponent(WasmProviderWat, "calc", catalog, declarations));
        Assert.Equal(FiberState.Active, provider.State);

        string? observed = null;
        await runtime.LoadAsync(new ConsumerComponent("managed-consumer", context =>
        {
            var endpoint = context.Require(key);
            observed = endpoint.InvokeAsync(
                CapabilityEnvelope.Json("1+2"), CancellationToken.None)
                .AsTask().GetAwaiter().GetResult().AsText();
        }, [key]));

        Assert.Equal("7", observed);

        await runtime.DisposeAsync(provider);
        Assert.Equal(FiberState.Pending, runtime.Snapshot().Fibers
            .Single(fiber => fiber.Name == "managed-consumer").State);
    }

    [Fact]
    public async Task WasmConsumerCallsAManagedProviderWithoutSharedContracts()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse("provide|a/b|endpoint|1.0.0|application/json|");
        catalog.Declare([declaration]);
        var key = catalog.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("a", "b"), CapabilityValueKind.Endpoint);

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(new EndpointProviderComponent(
            key, (_, _) => ValueTask.FromResult(CapabilityEnvelope.Text("ok"))));
        Assert.Equal(FiberState.Active, provider.State);

        var declarations = WasmCapabilityDeclarations.ReadFromWat(WasmConsumerWat);
        var consumer = await runtime.LoadAsync(
            new WasmCapabilityComponent(WasmConsumerWat, "wasm-consumer", catalog, declarations));

        Assert.True(
            consumer.State == FiberState.Active,
            $"state={consumer.State} failure={consumer.Failure}");

        await runtime.DisposeAsync(provider);
        Assert.Equal(FiberState.Pending, consumer.State);
    }

    [Fact]
    public async Task ManagedPluginInItsOwnAlcUsesTheHostCatalogWithoutSharedContracts()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse(
            "provide|org.example.catalog/ping|endpoint|1.0.0|text/plain|");
        catalog.Declare([declaration]);
        var id = new CapabilityId("org.example.catalog", "ping");
        var key = catalog.KeyFor<ICapabilityEndpoint>(id, CapabilityValueKind.Endpoint);

        var assembly = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/fixtures/CatalogPlugin/bin/Debug/net10.0/CatalogPlugin.dll"));
        Assert.True(File.Exists(assembly), $"fixture plugin was not built: {assembly}");

        await using var loader = new Gocordis.Runtime.Managed.ManagedPluginLoader();
        var plugin = loader.Load(
            "catalog-fixture", assembly, "CatalogPlugin.CatalogComponent",
            sharedAssemblies: null, catalog: catalog);

        // The plugin lives in its own collectible ALC and shares no contract DLL
        // with the host, yet its declaration is the exact host-owned Key instance.
        Assert.Same(key, Assert.Single(plugin.Component.Provide()));
        Assert.NotEqual(typeof(ConsumerComponent).Assembly, plugin.Component.GetType().Assembly);

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(plugin.Component);
        Assert.Equal(FiberState.Active, provider.State);

        string? observed = null;
        await runtime.LoadAsync(new ConsumerComponent("managed-consumer", context =>
        {
            var endpoint = context.Require(key);
            observed = endpoint.InvokeAsync(
                CapabilityEnvelope.Text("ping"), CancellationToken.None)
                .AsTask().GetAwaiter().GetResult().AsText();
        }, [key]));
        Assert.Equal("pong", observed);
    }

    [Fact]
    public async Task ProcessProviderServesAManagedConsumerThroughDeclaredRpc()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var script = Path.Combine(Path.GetTempPath(), $"gocordis-cap-{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(script, """
            #!/usr/bin/env python3
            import sys, json
            sys.stdout.write("GORIDIS-PROC-PLUGIN 1\n")
            sys.stdout.flush()
            for line in sys.stdin:
                try:
                    req = json.loads(line)
                except Exception:
                    continue
                method = req.get("method")
                ident = req.get("id")
                if method == "echo":
                    result = {"text": "ready"}
                elif method == "capability/invoke":
                    result = {"response": "9"}
                elif method == "shutdown":
                    sys.stdout.write(json.dumps({"jsonrpc":"2.0","id":ident,"result":{}}) + "\n")
                    sys.stdout.flush()
                    break
                else:
                    sys.stdout.write(json.dumps({"jsonrpc":"2.0","id":ident,
                        "error":{"code":-32601,"message":"unknown"}}) + "\n")
                    sys.stdout.flush()
                    continue
                sys.stdout.write(json.dumps({"jsonrpc":"2.0","id":ident,"result":result}) + "\n")
                sys.stdout.flush()
            """);
        try
        {
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var catalog = new CapabilityCatalog();
            var declaration = CapabilityDeclaration.Parse(
                "provide|org.example.proc/value|endpoint|1.0.0|application/json|");
            catalog.Declare([declaration]);
            var key = catalog.KeyFor<ICapabilityEndpoint>(
                new CapabilityId("org.example.proc", "value"), CapabilityValueKind.Endpoint);

            await using var runtime = new Gocordis.Core.Runtime();
            var provider = await runtime.LoadAsync(
                new ProcessCapabilityComponent(script, catalog, [declaration]));
            Assert.Equal(FiberState.Active, provider.State);

            string? observed = null;
            await runtime.LoadAsync(new ConsumerComponent("managed-consumer", context =>
            {
                var endpoint = context.Require(key);
                observed = endpoint.InvokeAsync(
                    CapabilityEnvelope.Json("value"), CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult().AsText();
            }, [key]));

            Assert.Equal("9", observed);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task ProcessProviderServesAWasmConsumerThroughRealRpcAndImports()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var id = "org.example.proc/value";
        var declaration = CapabilityDeclaration.Parse(
            "provide|org.example.proc/value|endpoint|1.0.0|text/plain|");
        var catalog = new CapabilityCatalog();
        catalog.Declare([declaration]);

        var script = Path.Combine(Path.GetTempPath(), $"gocordis-proc-provider-{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(script, """
            #!/usr/bin/env python3
            import sys, json
            def send(obj):
                sys.stdout.write(json.dumps(obj) + "\n")
                sys.stdout.flush()
            sys.stdout.write("GORIDIS-PROC-PLUGIN 1\n")
            sys.stdout.flush()
            for line in sys.stdin:
                try:
                    req = json.loads(line)
                except Exception:
                    continue
                method, ident = req.get("method"), req.get("id")
                if method == "echo":
                    send({"jsonrpc":"2.0","id":ident,"result":{"text":"ready"}})
                elif method == "capability/invoke":
                    send({"jsonrpc":"2.0","id":ident,"result":{"response":"9"}})
                elif method == "shutdown":
                    send({"jsonrpc":"2.0","id":ident,"result":{}})
                    break
                else:
                    send({"jsonrpc":"2.0","id":ident,
                          "error":{"code":-32601,"message":"unknown"}})
            """);
        try
        {
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await using var runtime = new Gocordis.Core.Runtime();
            var provider = await runtime.LoadAsync(
                new ProcessCapabilityComponent(script, catalog, [declaration]));
            Assert.Equal(FiberState.Active, provider.State);

            var bridge = new WasmCapabilityBridge();
            var consumerWat = ConsumerWatFor(id);
            var declarations = WasmCapabilityDeclarations.ReadFromWat(consumerWat);
            var consumer = await runtime.LoadAsync(new WasmCapabilityComponent(
                consumerWat, "wasm-consumer", catalog, declarations, capabilityBridge: bridge));

            Assert.Equal(FiberState.Active, consumer.State);
            Assert.Equal("9", bridge.ResponseText(1));

            await runtime.DisposeAsync(provider);
            Assert.Equal(FiberState.Pending, consumer.State);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task WasmProviderServesAProcessConsumerThroughRealImportsAndRpc()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string id = "org.example.wasm/value";
        const string providerWat = """
            (module
              (@custom "gocordis.capabilities" "{\"provides\":[{\"id\":\"org.example.wasm/value\",\"kind\":\"endpoint\",\"version\":\"1.0.0\",\"mediaType\":\"text/plain\"}]}")
              (memory (export "memory") 1)
              (func (export "runtime_component_destroy") (param i32))
              (func (export "runtime_component_create") (result i32) i32.const 1)
              (func (export "capability_invoke") (param i32 i32) (result i32 i32)
                i32.const 2048
                i32.const 55
                i32.store8
                i32.const 2048
                i32.const 1))
            """;
        var provide = CapabilityDeclaration.Parse(
            "provide|org.example.wasm/value|endpoint|1.0.0|text/plain|");
        var require = CapabilityDeclaration.Parse(
            "require|org.example.wasm/value|endpoint||text/plain|");
        var catalog = new CapabilityCatalog();
        catalog.Declare([provide, require]);

        var observed = Path.Combine(Path.GetTempPath(), $"gocordis-proc-consumer-{Guid.NewGuid():N}.txt");
        var script = Path.Combine(Path.GetTempPath(), $"gocordis-proc-consumer-{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(script, """
            #!/usr/bin/env python3
            import sys, json
            def send(obj):
                sys.stdout.write(json.dumps(obj) + "\n")
                sys.stdout.flush()
            def reply(request_id):
                while True:
                    line = sys.stdin.readline()
                    if not line:
                        sys.exit(1)
                    response = json.loads(line)
                    if response.get("id") == request_id:
                        return response
            sys.stdout.write("GORIDIS-PROC-PLUGIN 1\n")
            sys.stdout.flush()
            for line in sys.stdin:
                try:
                    req = json.loads(line)
                except Exception:
                    continue
                method, ident = req.get("method"), req.get("id")
                if method == "echo":
                    send({"jsonrpc":"2.0","id":9001,"method":"capability/require",
                          "params":{"id":"__ID__"}})
                    handle = reply(9001)["result"]["handle"]
                    send({"jsonrpc":"2.0","id":9002,"method":"capability/invoke",
                          "params":{"handle":handle,"request":"x"}})
                    response = reply(9002)["result"]["response"]
                    open("__OBSERVED__", "w").write(response)
                    send({"jsonrpc":"2.0","id":ident,"result":{"text":"ready"}})
                elif method == "shutdown":
                    send({"jsonrpc":"2.0","id":ident,"result":{}})
                    break
                else:
                    send({"jsonrpc":"2.0","id":ident,
                          "error":{"code":-32601,"message":"unknown"}})
            """.Replace("__ID__", id).Replace("__OBSERVED__", observed.Replace("\\", "\\\\").Replace("\"", "\\\"")));
        try
        {
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await using var runtime = new Gocordis.Core.Runtime();
            var provider = await runtime.LoadAsync(new WasmCapabilityComponent(
                providerWat, "wasm-provider", catalog, [provide]));
            Assert.Equal(FiberState.Active, provider.State);

            var consumer = await runtime.LoadAsync(
                new ProcessCapabilityComponent(script, catalog, [require]));
            Assert.Equal(FiberState.Active, consumer.State);
            Assert.Equal("7", await File.ReadAllTextAsync(observed));

            await runtime.DisposeAsync(provider);
            Assert.Equal(FiberState.Pending, consumer.State);
        }
        finally
        {
            File.Delete(script);
            if (File.Exists(observed))
            {
                File.Delete(observed);
            }
        }
    }

    [Fact]
    public async Task ProviderMediaTypeAndOversizedPayloadHaveOneContractSemantic()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse(
            "provide|org.example.media/value|endpoint|1.0.0|text/plain|");
        catalog.Declare([declaration]);
        var key = catalog.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.media", "value"), CapabilityValueKind.Endpoint);

        const string wat = """
            (module
              (@custom "gocordis.capabilities" "{\"provides\":[{\"id\":\"org.example.media/value\",\"kind\":\"endpoint\",\"version\":\"1.0.0\",\"mediaType\":\"text/plain\"}]}")
              (memory (export "memory") 1)
              (func (export "runtime_component_destroy") (param i32))
              (func (export "runtime_component_create") (result i32) i32.const 1)
              (func (export "capability_invoke") (param i32 i32) (result i32 i32)
                i32.const 2048
                i32.const 55
                i32.store8
                i32.const 2048
                i32.const 1))
            """;
        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(
            new WasmCapabilityComponent(wat, "media-provider", catalog, [declaration]));
        Assert.Equal(FiberState.Active, provider.State);

        var endpoint = runtime.RequireCapability(key);
        var response = await endpoint.InvokeAsync(
            CapabilityEnvelope.Json("x"), CancellationToken.None);
        Assert.Equal("text/plain", response.MediaType);

        var error = await Assert.ThrowsAsync<CapabilityInvocationException>(async () =>
            await endpoint.InvokeAsync(
                CapabilityEnvelope.Json(new string('x', 20_000)), CancellationToken.None));
        Assert.Equal("invalid-payload", error.Code);
    }

    [Fact]
    public async Task ConsumerReleaseDoesNotDisposeTheSharedProviderEndpoint()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse("provide|a/b|endpoint|1.0.0|text/plain|");
        catalog.Declare([declaration]);
        var key = catalog.KeyFor<ICapabilityEndpoint>(new CapabilityId("a", "b"), CapabilityValueKind.Endpoint);

        await using var runtime = new Gocordis.Core.Runtime();
        var provider = await runtime.LoadAsync(new EndpointProviderComponent(
            key, (_, _) => ValueTask.FromResult(CapabilityEnvelope.Text("ok"))));

        var wat = ConsumerWatFor("a/b");
        var declarations = WasmCapabilityDeclarations.ReadFromWat(wat);
        var firstConsumer = await runtime.LoadAsync(
            new WasmCapabilityComponent(wat, "consumer-1", catalog, declarations));
        Assert.Equal(FiberState.Active, firstConsumer.State);

        // The first consumer's bridge used to dispose the provider-owned
        // endpoint, breaking every later caller.
        await runtime.DisposeAsync(firstConsumer);
        var shared = runtime.RequireCapability(key);
        Assert.Equal("ok", (await shared.InvokeAsync(
            CapabilityEnvelope.Text("x"), CancellationToken.None)).AsText());

        var secondConsumer = await runtime.LoadAsync(
            new WasmCapabilityComponent(wat, "consumer-2", catalog, declarations));
        Assert.Equal(FiberState.Active, secondConsumer.State);

        await runtime.DisposeAsync(provider);
        Assert.Equal(FiberState.Pending, secondConsumer.State);

        var recovered = await runtime.LoadAsync(new EndpointProviderComponent(
            key, (_, _) => ValueTask.FromResult(CapabilityEnvelope.Text("ok"))));
        Assert.Equal(FiberState.Active, recovered.State);
        Assert.Equal(FiberState.Active, secondConsumer.State);
        Assert.Equal("ok", (await runtime.RequireCapability(key).InvokeAsync(
            CapabilityEnvelope.Text("x"), CancellationToken.None)).AsText());
    }

    [Fact]
    public async Task WasmTrapFailsTheProviderWhileWithdrawalOnlyPendsTheConsumer()
    {
        var catalog = new CapabilityCatalog();
        var declaration = CapabilityDeclaration.Parse(
            "provide|org.example.trap/value|endpoint|1.0.0|text/plain|");
        catalog.Declare([declaration]);
        var key = catalog.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.trap", "value"), CapabilityValueKind.Endpoint);

        await using var runtime = new Gocordis.Core.Runtime();
        await runtime.LoadAsync(new ConsumerComponent("consumer", _ => { }, [key]));
        Assert.Equal(FiberState.Pending, runtime.Snapshot().Fibers
            .Single(fiber => fiber.Name == "consumer").State);

        const string trappingWat = """
            (module
              (memory (export "memory") 1)
              (func (export "runtime_component_destroy") (param i32))
              (func (export "runtime_component_create") (result i32) unreachable))
            """;
        var failed = await runtime.LoadAsync(new WasmCapabilityComponent(
            trappingWat, "trap-provider", catalog, [declaration]));
        Assert.Equal(FiberState.Failed, failed.State);
        Assert.NotNull(failed.Failure);
        await runtime.DisposeAsync(failed);
        Assert.Equal(FiberState.Pending, runtime.Snapshot().Fibers
            .Single(fiber => fiber.Name == "consumer").State);

        const string healthyWat = """
            (module
              (memory (export "memory") 1)
              (func (export "runtime_component_destroy") (param i32))
              (func (export "runtime_component_create") (result i32) i32.const 1))
            """;
        var provider = await runtime.LoadAsync(new WasmCapabilityComponent(
            healthyWat, "healthy-provider", catalog, [declaration]));
        Assert.Equal(FiberState.Active, provider.State);
        Assert.Equal(FiberState.Active, runtime.Snapshot().Fibers
            .Single(fiber => fiber.Name == "consumer").State);

        await runtime.DisposeAsync(provider);
        var consumerAfterWithdrawal = runtime.Snapshot().Fibers
            .Single(fiber => fiber.Name == "consumer");
        Assert.Equal(FiberState.Pending, consumerAfterWithdrawal.State);
        Assert.Null(consumerAfterWithdrawal.Failure);
    }

    private static string ConsumerWatFor(string capabilityId)
    {
        var stores = string.Join("\n            ", capabilityId.Select((c, index) =>
            $"(i32.store8 (i32.add (local.get $buf) (i32.const {index})) (i32.const {(int)c}))"));
        return $$"""
            (module
              (@custom "gocordis.capabilities" "{\"requires\":[{\"id\":\"{{capabilityId}}\",\"kind\":\"endpoint\"}]}")
              (import "gocordis" "capability_buffer" (func $buffer (result i32)))
              (import "gocordis" "capability_require" (func $require (param i32 i32) (result i32)))
              (import "gocordis" "capability_invoke" (func $invoke (param i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (func (export "runtime_component_destroy") (param i32))
              (func (export "runtime_component_create") (result i32)
                (local $buf i32)
                (local $handle i32)
                (local.set $buf (call $buffer))
                {{stores}}
                (local.set $handle (call $require (local.get $buf) (i32.const {{capabilityId.Length}})))
                (i32.store8 (local.get $buf) (i32.const 120))
                (call $invoke (local.get $handle) (local.get $buf) (i32.const 1))))
            """;
    }

    private sealed class ConsumerComponent : Component
    {
        private readonly Action<Context> _apply;
        private readonly IReadOnlyList<ICapabilityKey> _inject;

        public ConsumerComponent(string name, Action<Context> apply, IReadOnlyList<ICapabilityKey> inject)
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

    private sealed class EndpointProviderComponent : Component
    {
        private readonly Key<ICapabilityEndpoint> _key;
        private readonly Func<CapabilityEnvelope, CancellationToken, ValueTask<CapabilityEnvelope>> _invoke;

        public EndpointProviderComponent(
            Key<ICapabilityEndpoint> key,
            Func<CapabilityEnvelope, CancellationToken, ValueTask<CapabilityEnvelope>> invoke)
        {
            _key = key;
            _invoke = invoke;
        }

        public override string Name => "managed-provider";
        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public async override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            var endpoint = new DelegatingCapabilityEndpoint(
                new CapabilityId("a", "b"),
                new CapabilityContract(
                    new CapabilityId("a", "b"), CapabilityValueKind.Endpoint,
                    NuGet.Versioning.NuGetVersion.Parse("1.0.0")),
                _invoke);
            context.Provide(_key, endpoint);
            await context.EffectAsync(
                static () => ValueTask.CompletedTask,
                async () => await endpoint.DisposeAsync());
            return null;
        }
    }
}
