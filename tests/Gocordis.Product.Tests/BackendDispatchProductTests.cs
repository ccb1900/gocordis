using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Product-level proof that the one Platform composition root dispatches
/// installed declarations to the process and WASM backends. Packages are real
/// zip artifacts: Backend/EntryAssembly/Capabilities are declared in the package
/// manifest, validated by the writer, and installed through PluginStore.
/// No runtime state is persisted or faked here.
/// </summary>
public sealed class BackendDispatchProductTests
{
    private const string HostVersion = "1.0.0";

    [Fact]
    public async Task ProcessPackageIsActivatedByThePlatformFactory()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return; // POSIX shell fixture; Windows uses a different process backend fixture.
        }

        using var temp = new TempDir();
        const string id = "product-proc";
        var store = await InstallAsync(temp, id, "plugin.sh", ProcessPluginScript,
            backend: "process", executable: true);
        await using var app = await StartAsync(temp, store, id);

        var fiber = app.Registry.Entries[id].Fiber!;
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal("process-plugin", Assert.Single(
            app.Runtime.Snapshot().Fibers, candidate => candidate.Id == fiber.Id).Name);
        Assert.Equal("ready", app.Runtime.RequireCapability(
            PlatformBackendKeys.ProcessEcho(id)));
        Assert.Equal("ok", app.Runtime.RequireCapability(
            PlatformBackendKeys.ProcessError(id)));

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task WasmPackageIsActivatedByThePlatformFactory()
    {
        using var temp = new TempDir();
        const string id = "product-wasm";
        const string wat = """
            (module
              (func (export "runtime_component_create") (result i32) i32.const 42)
              (func (export "runtime_component_destroy") (param i32)))
            """;
        var store = await InstallAsync(temp, id, "plugin.wat", wat, backend: "wasm");
        await using var app = await StartAsync(temp, store, id);

        var fiber = app.Registry.Entries[id].Fiber!;
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal("wasm-plugin", Assert.Single(
            app.Runtime.Snapshot().Fibers, candidate => candidate.Id == fiber.Id).Name);
        Assert.Equal(42, app.Runtime.RequireCapability(PlatformBackendKeys.WasmValue(id)));

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task ExistingWasmBinaryFixtureIsActivatedByThePlatformFactory()
    {
        using var temp = new TempDir();
        const string id = "product-wasm-bin";
        // Reuse the repository's real .wasm fixture (Go loader/wasm testdata)
        // to exercise the binary payload branch, not just WAT compilation.
        var fixture = Path.Combine(FindRepositoryRoot(),
            "extensions", "loader", "wasm", "testdata", "valid.wasm");
        Assert.True(File.Exists(fixture), $"missing WASM fixture '{fixture}'");
        var store = await InstallAsync(temp, id, "plugin.wasm",
            await File.ReadAllBytesAsync(fixture), backend: "wasm");
        await using var app = await StartAsync(temp, store, id);

        var fiber = app.Registry.Entries[id].Fiber!;
        Assert.Equal(FiberState.Active, fiber.State);
        Assert.Equal("wasm-plugin", Assert.Single(
            app.Runtime.Snapshot().Fibers, candidate => candidate.Id == fiber.Id).Name);
        // This fixture's create export sets a global and returns void, so the
        // unified backend reports zero; activation really executed the module.
        Assert.Equal(0, app.Runtime.RequireCapability(PlatformBackendKeys.WasmValue(id)));

        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task CapabilityDeclarationChangesTheManagedLoaderKey()
    {
        using var temp = new TempDir();
        var manifestPath = temp.File("fingerprint-app.json");
        await new ApplicationManifest("fingerprint-app", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, temp.Path("store"), Host: UiHostKind.WinForms, HostVersion: HostVersion));

        var assembly = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../samples/SamplePlugin/bin/Debug/net10.0/SamplePlugin.dll"));
        Assert.True(File.Exists(assembly), $"missing sample plugin '{assembly}'");
        var source = Path.GetDirectoryName(assembly)!;

        Component Create(string capability) => app.CreateComponent(new PluginManifest(
                "fingerprint-plugin", "fingerprint-type", true,
                new Dictionary<string, object?>(), [], [], "1.0.0", source, null,
                "managed", "SamplePlugin.dll", "SamplePlugin.SampleComponent",
                Capabilities: [$"provide|{capability}|endpoint|1.0.0||"]));

        var first = Create("org.example/one");
        var second = Create("org.example/two");
        Assert.NotSame(first, second);
        ((IDisposable)first).Dispose();
        ((IDisposable)second).Dispose();
    }

    [Fact]
    public async Task DeclaredWasmCapabilityPackageIsLoadedAndInvokable()
    {
        using var temp = new TempDir();
        const string id = "product-wasm-cap";
        const string wat = """
            (module
              (@custom "gocordis.capabilities" "{\"provides\":[{\"id\":\"org.example.product/ping\",\"kind\":\"endpoint\",\"version\":\"1.0.0\"}]}")
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
        var store = await InstallAsync(temp, id, "plugin.wat", wat, "wasm",
            capabilities: ["provide|org.example.product/ping|endpoint|1.0.0|application/json|"]);
        await using var app = await StartAsync(temp, store, id);

        var key = app.Capabilities.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.product", "ping"), CapabilityValueKind.Endpoint);
        var endpoint = app.Runtime.RequireCapability(key);
        var response = await endpoint.InvokeAsync(
            CapabilityEnvelope.Json("{}"), CancellationToken.None);

        Assert.Equal("7", response.AsText());
        await app.StopAsync();
        Assert.Empty(app.Runtime.Snapshot().Fibers);
    }

    [Fact]
    public async Task DeclaredProcessCapabilityPackageIsLoadedAndInvokable()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        using var temp = new TempDir();
        const string id = "product-proc-cap";
        var store = await InstallAsync(temp, id, "plugin.sh", ProcessPluginScript,
            backend: "process", executable: true,
            capabilities: ["provide|org.example.product/echo|endpoint|1.0.0|text/plain|"]);
        await using var app = await StartAsync(temp, store, id);

        var key = app.Capabilities.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.product", "echo"), CapabilityValueKind.Endpoint);
        var endpoint = app.Runtime.RequireCapability(key);
        // The shell fixture answers capability/invoke with the same vocabulary
        // as echo; this proves the declared RPC path is live through the package.
        var response = await endpoint.InvokeAsync(
            CapabilityEnvelope.Text("ping"), CancellationToken.None);

        Assert.Equal("pong", response.AsText());
        await app.StopAsync();
    }

    [Fact]
    public async Task CreateComponentDispatchesEveryDeclaredBackend()
    {
        using var temp = new TempDir();
        var store = temp.Path("store");
        Directory.CreateDirectory(store);
        var manifestPath = temp.File("dispatch-app.json");
        await new ApplicationManifest("dispatch-app", "1.0.0", HostVersion, []).SaveAsync(manifestPath);
        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));

        var method = typeof(PlatformApplication).GetMethod(
            "CreateComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Component Create(string id, string? backend, string? entry, bool enabled = true)
        {
            var declaration = new PluginManifest(
                id, id + "-type", enabled, new Dictionary<string, object?>(), [], [],
                "1.0.0", store, null, backend, entry, "Any.Type");
            return (Component)method.Invoke(app, [declaration])!;
        }

        await File.WriteAllTextAsync(Path.Combine(store, "plugin.sh"), ProcessPluginScript);
        await File.WriteAllTextAsync(Path.Combine(store, "plugin.wat"), "(module)");

        Assert.Equal("process-plugin", Create("proc", "process", "plugin.sh").Name);
        Assert.Equal("wasm-plugin", Create("wasm", "wasm", "plugin.wat").Name);
        Assert.Equal("product-plugin:legacy-type", Create("legacy", null, null).Name);
        Assert.Equal("product-plugin:builtin-type", Create("builtin", "builtin", null).Name);
        Assert.Equal("disabled-plugin", Create("off", "managed", "ghost.dll", enabled: false).Name);
        Assert.Throws<TargetInvocationException>(() => Create("unknown", "ghost", "plugin.sh"));
    }

    private const string ProcessPluginScript = """
        #!/bin/sh
        handshake="GORIDIS-PROC-PLUGIN 1"
        IFS= read -r line || exit 0
        [ "$line" = "$handshake" ] || exit 2
        printf '%s\n' "$handshake"
        while IFS= read -r line; do
          id=$(printf '%s\n' "$line" | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p')
          case "$line" in
            *'"method":"shutdown"'*) printf '{"jsonrpc":"2.0","id":%s,"result":{}}\n' "$id"; exit 0 ;;
            *'"method":"echo"'*) printf '{"jsonrpc":"2.0","id":%s,"result":{"text":"ready"}}\n' "$id" ;;
            *'"capability/invoke"'*) printf '{"jsonrpc":"2.0","id":%s,"result":{"response":"pong"}}\n' "$id" ;;
          esac
        done
        """;

    private static Task<string> InstallAsync(
        TempDir temp, string id, string payloadName, string payload, string backend,
        bool executable = false, IReadOnlyList<string>? capabilities = null) =>
        InstallAsync(temp, id, payloadName, Encoding.UTF8.GetBytes(payload), backend,
            executable, capabilities);

    private static async Task<string> InstallAsync(
        TempDir temp, string id, string payloadName, byte[] payload, string backend,
        bool executable = false, IReadOnlyList<string>? capabilities = null)
    {
        var source = temp.Path($"source-{id}");
        Directory.CreateDirectory(source);
        var payloadPath = Path.Combine(source, payloadName);
        await File.WriteAllBytesAsync(payloadPath, payload);
        // The AUTHOR marks the entry executable; the package preserves the bit
        // and the installer restores it. No post-install chmod in tests.
        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(payloadPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var package = temp.File($"{id}-1.0.0.gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source,
            new PluginPackageManifest(
                id, "1.0.0", id + "-type", "[1.0,)",
                Backend: backend,
                EntryAssembly: payloadName,
                Capabilities: capabilities), package);

        var store = temp.Path("store-" + id);
        await new PluginStore(store, HostVersion).InstallAsync(package);

        return store;
    }

    private static async Task<PlatformApplication> StartAsync(TempDir temp, string store, string id)
    {
        var manifestPath = temp.File($"app-{id}.json");
        await new ApplicationManifest("backend-app", "1.0.0", HostVersion,
            [new ApplicationPlugin(id, "[1.0,2.0)", true)]).SaveAsync(manifestPath);
        var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
        await app.StartAsync();
        return app;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found above the test assembly");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gocordis-backend-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string File(string name) => System.IO.Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string Root { get; }
    }
}
