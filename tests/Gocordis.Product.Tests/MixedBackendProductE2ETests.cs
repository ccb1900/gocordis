using System.Diagnostics;
using System.Text;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;
using Xunit;

namespace Gocordis.Product.Tests;

/// <summary>
/// Task 05 gate: one application manifest with Managed → Process → WASM
/// capabilities, real calls, withdrawal/reactivation through the control plane,
/// and resource reclamation after host stop.
/// </summary>
public sealed class MixedBackendProductE2ETests
{
    private const string HostVersion = "1.0.0";
    private const string Ping = "org.example.catalog/ping";
    private const string Echo = "org.example.mixed/echo";

    [PosixOnlyFact]
    public async Task MixedBackendsComposeRecoverAndReleaseThroughOneHost()
    {
        using var temp = new TempDir();
        var store = temp.At("store");
        Directory.CreateDirectory(store);

        var managedDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/fixtures/CatalogPlugin/bin/Debug/net10.0/CatalogPlugin.dll"));
        Assert.True(File.Exists(managedDll), $"missing managed fixture '{managedDll}'");
        await InstallAsync(temp, store, "mixed-p", new Dictionary<string, byte[]>
        {
            ["CatalogPlugin.dll"] = await File.ReadAllBytesAsync(managedDll),
        }, new PluginPackageManifest(
            "mixed-p", "1.0.0", "mixed-p", "[1.0,)",
            Backend: "managed", EntryAssembly: "CatalogPlugin.dll",
            EntryType: "CatalogPlugin.CatalogComponent",
            Capabilities: [$"provide|{Ping}|endpoint|1.0.0|text/plain|"]));

        var observed = temp.At("observed.txt");
        var pidFile = temp.At("pid.txt");
        await InstallAsync(temp, store, "mixed-a", new Dictionary<string, byte[]>
        {
            ["plugin.py"] = Encoding.UTF8.GetBytes(ProcessScript(observed, pidFile)),
        }, new PluginPackageManifest(
            "mixed-a", "1.0.0", "mixed-a", "[1.0,)",
            Backend: "process", EntryAssembly: "plugin.py",
            Capabilities:
            [
                $"require|{Ping}|endpoint||text/plain|",
                $"provide|{Echo}|endpoint|1.0.0|text/plain|",
            ]), executable: true);

        await InstallAsync(temp, store, "mixed-b", new Dictionary<string, byte[]>
        {
            ["plugin.wat"] = Encoding.UTF8.GetBytes(ConsumerWat(Echo, "text/plain")),
        }, new PluginPackageManifest(
            "mixed-b", "1.0.0", "mixed-b", "[1.0,)",
            Backend: "wasm", EntryAssembly: "plugin.wat",
            Capabilities: [$"require|{Echo}|endpoint||text/plain|"]));

        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("mixed-app", "1.0.0", HostVersion,
            [
                new ApplicationPlugin("mixed-p", "1.0.0", true),
                new ApplicationPlugin("mixed-a", "1.0.0", true),
                new ApplicationPlugin("mixed-b", "1.0.0", true),
            ]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
        await app.StartAsync();

        Assert.All(app.Registry.Entries.Values,
            entry => Assert.Equal(FiberState.Active, entry.Fiber!.State));
        Assert.Equal("pong", await File.ReadAllTextAsync(observed));

        var echoKey = app.Capabilities.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.mixed", "echo"), CapabilityValueKind.Endpoint);
        var endpoint = app.Runtime.RequireCapability(echoKey);
        Assert.Equal("pong", (await endpoint.InvokeAsync(
            CapabilityEnvelope.Text("go"), CancellationToken.None)).AsText());

        var childId = int.Parse(await File.ReadAllTextAsync(pidFile));
        Assert.True(IsAlive(childId), "process child should be alive while active");

        // Withdraw consumers before provider; the control plane rejects the
        // reverse order to avoid dangling capability declarations.
        foreach (var id in new[] { "mixed-b", "mixed-a", "mixed-p" })
        {
            Assert.True((await app.Control!.SetEnabledAsync(id, false)).Accepted);
        }

        Assert.All(app.Registry.Entries.Values,
            entry => Assert.Equal(FiberState.Gone, entry.Fiber?.State ?? FiberState.Gone));
        Assert.False(IsAlive(childId));

        foreach (var id in new[] { "mixed-p", "mixed-a", "mixed-b" })
        {
            Assert.True((await app.Control!.SetEnabledAsync(id, true)).Accepted);
        }

        Assert.All(app.Registry.Entries.Values,
            entry => Assert.Equal(FiberState.Active, entry.Fiber!.State));
        Assert.True(IsAlive(int.Parse(await File.ReadAllTextAsync(pidFile))));

        // Full product closure: uninstall in reverse dependency order, then stop.
        foreach (var id in new[] { "mixed-b", "mixed-a", "mixed-p" })
        {
            System.Console.WriteLine($"CAPS-BEFORE-{id}: {string.Join(" | ", app.Capabilities.Declarations.Select(d => d.ToWireString()))}");
            var removal = await app.Control!.UninstallAsync(id);
            System.Console.WriteLine($"UNINSTALL-{id}: accepted={removal.Accepted} code={removal.Code} error={removal.Error}");
            Assert.True(removal.Accepted, $"uninstall '{id}' failed: {removal.Error}");
        }

        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, fiber =>
            fiber.Name.StartsWith("mixed-", StringComparison.Ordinal));
        Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(pidFile))));

        await app.StopAsync();
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, fiber =>
            fiber.Name.StartsWith("mixed-", StringComparison.Ordinal));
        Assert.False(IsAlive(int.Parse(await File.ReadAllTextAsync(pidFile))));
    }

    [PosixOnlyFact]
    public async Task DynamicInstallPlansDeclarationsAndUninstallRollsThemBack()
    {
        using var temp = new TempDir();
        var store = temp.At("store");
        Directory.CreateDirectory(store);

        var managedDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/fixtures/CatalogPlugin/bin/Debug/net10.0/CatalogPlugin.dll"));
        Assert.True(File.Exists(managedDll), $"missing managed fixture '{managedDll}'");
        await InstallAsync(temp, store, "dyn-p", new Dictionary<string, byte[]>
        {
            ["CatalogPlugin.dll"] = await File.ReadAllBytesAsync(managedDll),
        }, new PluginPackageManifest(
            "dyn-p", "1.0.0", "dyn-p", "[1.0,)",
            Backend: "managed", EntryAssembly: "CatalogPlugin.dll",
            EntryType: "CatalogPlugin.CatalogComponent",
            Capabilities: [$"provide|{Ping}|endpoint|1.0.0|text/plain|"]));

        var manifestPath = temp.At("app.json");
        await new ApplicationManifest("dyn-app", "1.0.0", HostVersion,
            [new ApplicationPlugin("dyn-p", "1.0.0", true)]).SaveAsync(manifestPath);

        await using var app = new PlatformApplication(new PlatformHostOptions(
            manifestPath, store, Host: UiHostKind.WinForms, HostVersion: HostVersion));
        await app.StartAsync();

        // A runtime install goes through the SAME declaration plan: the process
        // provider's capability becomes resolvable without a host restart.
        var providerPackage = temp.At("dyn-a.gocordis-plugin");
        var providerSource = temp.At("source-dyn-a");
        Directory.CreateDirectory(providerSource);
        var providerScript = Path.Combine(providerSource, "plugin.py");
        await File.WriteAllTextAsync(providerScript, ProcessProviderScript());
        File.SetUnixFileMode(providerScript,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await PluginPackageWriter.CreateAsync(providerSource, new PluginPackageManifest(
            "dyn-a", "1.0.0", "dyn-a", "[1.0,)",
            Backend: "process", EntryAssembly: "plugin.py",
            Capabilities: [$"provide|{Echo}|endpoint|1.0.0|text/plain|"]), providerPackage);

        var beforeFailedInstall = app.Capabilities.Declarations.Count;
        var blocked = await app.Control!.UploadAsync(File.OpenRead(providerPackage), "dyn-a.gocordis-plugin");
        // This install declares no shared contracts, so it must be accepted;
        // the negative case is covered by ControlBoundaryTests.
        var installed = await app.Control!.InstallAsync(blocked.UploadKey);
        Assert.True(installed.Accepted, $"dynamic install failed: {installed.Error}");
        Assert.Equal(Echo, Assert.Single(app.Capabilities.Declarations,
            d => d.Direction == CapabilityDirection.Provide && d.Id.ToString() == Echo).Id.ToString());

        var echoKey = app.Capabilities.KeyFor<ICapabilityEndpoint>(
            new CapabilityId("org.example.mixed", "echo"), CapabilityValueKind.Endpoint);
        var endpoint = app.Runtime.RequireCapability(echoKey);
        Assert.Equal("pong", (await endpoint.InvokeAsync(
            CapabilityEnvelope.Text("go"), CancellationToken.None)).AsText());

        var removed = await app.Control!.UninstallAsync("dyn-a");
        Assert.True(removed.Accepted, $"uninstall failed: {removed.Error}");
        Assert.Equal(beforeFailedInstall, app.Capabilities.Declarations.Count);
        Assert.Throws<DependencyMissingException>(() => app.Runtime.RequireCapability(echoKey));

        await app.StopAsync();
        Assert.DoesNotContain(app.Runtime.Snapshot().Fibers, fiber =>
            fiber.Name.StartsWith("dyn-", StringComparison.Ordinal));
    }

    private static string ProcessProviderScript() => """
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
                send({"jsonrpc":"2.0","id":ident,"result":{"response":"pong"}})
            elif method == "shutdown":
                send({"jsonrpc":"2.0","id":ident,"result":{}})
                break
            else:
                send({"jsonrpc":"2.0","id":ident,
                      "error":{"code":-32601,"message":"unknown"}})
        """;

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task InstallAsync(
        TempDir temp,
        string store,
        string id,
        IReadOnlyDictionary<string, byte[]> payload,
        PluginPackageManifest manifest,
        bool executable = false)
    {
        var source = temp.At("source-" + id);
        Directory.CreateDirectory(source);
        foreach (var (name, bytes) in payload)
        {
            var path = Path.Combine(source, name);
            await File.WriteAllBytesAsync(path, bytes);
            if (executable && name == manifest.EntryAssembly && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var package = temp.At(id + ".gocordis-plugin");
        await PluginPackageWriter.CreateAsync(source, manifest, package);
        await new PluginStore(store, HostVersion).InstallAsync(package);

    }

    private static string ProcessScript(string observed, string pidFile) => """
        #!/usr/bin/env python3
        import sys, json, os
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
        open("__PID__", "w").write(str(os.getpid()))
        sys.stdout.write("GORIDIS-PROC-PLUGIN 1\n")
        sys.stdout.flush()
        captured = ""
        for line in sys.stdin:
            try:
                req = json.loads(line)
            except Exception:
                continue
            method, ident = req.get("method"), req.get("id")
            if method == "echo":
                send({"jsonrpc":"2.0","id":9001,"method":"capability/require",
                      "params":{"id":"__PING__"}})
                handle = reply(9001)["result"]["handle"]
                send({"jsonrpc":"2.0","id":9002,"method":"capability/invoke",
                      "params":{"handle":handle,"request":"ping"}})
                captured = reply(9002)["result"]["response"]
                open("__OBSERVED__", "w").write(captured)
                send({"jsonrpc":"2.0","id":ident,"result":{"text":"ready"}})
            elif method == "capability/invoke":
                send({"jsonrpc":"2.0","id":ident,"result":{"response":captured}})
            elif method == "shutdown":
                send({"jsonrpc":"2.0","id":ident,"result":{}})
                break
            else:
                send({"jsonrpc":"2.0","id":ident,
                      "error":{"code":-32601,"message":"unknown"}})
        """.Replace("__PID__", pidFile).Replace("__PING__", Ping).Replace("__OBSERVED__", observed);

    private static string ConsumerWat(string id, string mediaType = "application/json")
    {
        var stores = string.Join("\n            ", id.Select((c, index) =>
            $"(i32.store8 (i32.add (local.get $buf) (i32.const {index})) (i32.const {(int)c}))"));
        return $$"""
            (module
              (@custom "gocordis.capabilities" "{\"requires\":[{\"id\":\"{{id}}\",\"kind\":\"endpoint\",\"mediaType\":\"{{mediaType}}\"}]}")
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
                (local.set $handle (call $require (local.get $buf) (i32.const {{id.Length}})))
                (call $invoke (local.get $handle) (local.get $buf) (i32.const 1))))
            """;
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "gocordis-mixed-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string At(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
