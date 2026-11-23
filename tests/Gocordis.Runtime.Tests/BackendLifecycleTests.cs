using System.Runtime.CompilerServices;
using Gocordis.Core;
using Gocordis.Plugins;
using Gocordis.Runtime.Contracts;
using Gocordis.Runtime.Managed;
using Gocordis.Runtime.Process;
using Gocordis.Runtime.Wasm;
using Xunit;
using SystemProcess = System.Diagnostics.Process;

namespace Gocordis.Runtime.Tests;

/// <summary>
/// Batch B: the three runtime backends expose one explicit Prepare → Activate
/// (Core commit) → Rollback → Release protocol and are equivalent on the
/// activate / undo / fail matrix. Backend-specific degradation is declared via
/// <see cref="IPluginBackend.DegradationPolicy"/> rather than being implicit.
/// </summary>
public sealed class BackendLifecycleTests
{
    private const string ValidWat = """
        (module
          (func (export "runtime_component_create") (result i32) i32.const 42)
          (func (export "runtime_component_destroy") (param i32)))
        """;

    private const string MissingExportWat = """
        (module (func (export "not_the_abi") (result i32) i32.const 1))
        """;

    public enum BackendKind
    {
        Managed,
        Wasm,
        Process,
    }

    public static TheoryData<BackendKind> Backends => new()
    {
        BackendKind.Managed,
        BackendKind.Wasm,
        BackendKind.Process,
    };

    // ---- cross-backend matrix ------------------------------------------------

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Matrix_Activate_PublishesCapabilityAndCommitsHandle(BackendKind kind)
    {
        await using var backend = CreateBackend(kind, valid: true);
        var context = new RecordingContext();

        var prepared = await backend.PrepareAsync(context);
        Assert.Equal(BackendPhase.Prepared, prepared.Phase);

        var active = await backend.ActivateAsync(context, prepared);
        Assert.Equal(BackendPhase.Activated, active.Phase);
        Assert.Equal(backend.BackendId, active.BackendId);
        Assert.NotNull(active.ResourceId);
        Assert.NotEmpty(context.Provided);
        Assert.Equal(ExpectedPolicy(kind), backend.DegradationPolicy);
        Assert.Equal(BackendPhase.Activated, backend.LastHandle!.Phase);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Matrix_Undo_InverseReleasesAndIsIdempotentUnderCoreAuthority(BackendKind kind)
    {
        await using var backend = CreateBackend(kind, valid: true);
        var context = new RecordingContext();

        // The unified driver commits through the Core-style effect contract.
        await BackendProtocol.RunAsync(backend, context);
        Assert.Single(context.Inverses);

        await context.Inverses[0]();
        Assert.Equal(BackendPhase.Released, backend.LastHandle!.Phase);
        Assert.True(context.InverseInvoked);

        // A second unwind must not throw or resurrect anything.
        await context.Inverses[0]();
        Assert.Equal(BackendPhase.Released, backend.LastHandle.Phase);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Matrix_Failure_FollowsTheDeclaredDegradationPolicy(BackendKind kind)
    {
        await using var backend = CreateBackend(kind, valid: false);
        var context = new RecordingContext();
        BackendHandle? prepared = null;
        Exception? failure = null;

        try
        {
            prepared = await backend.PrepareAsync(context);
            await backend.ActivateAsync(context, prepared);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (backend.DegradationPolicy == BackendDegradationPolicy.FailFiber)
        {
            Assert.NotNull(failure);
            var rolledBack = await backend.RollbackAsync(context, prepared, failure!);
            Assert.Equal(BackendPhase.RolledBack, rolledBack.Phase);
            Assert.Equal(failure!.Message, rolledBack.Error);
            Assert.Empty(context.Inverses);
        }
        else
        {
            // Process degrades: the fiber stays Active and publishes an explicit
            // error vocabulary instead of taking down the host.
            Assert.Null(failure);
            Assert.Contains(context.Provided, entry => entry.Name == ProcessKeys.ErrorVocabulary.Name);
            Assert.Equal(ProcessErrorPolicy.ErrorVocabulary, backend.LastHandle!.Error);
            Assert.Equal(BackendPhase.Activated, backend.LastHandle.Phase);
        }
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Matrix_CoreRuntimeOwnsTheBackendFiber(BackendKind kind)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var component = CreateComponent(kind, valid: true);

        var fiber = await runtime.LoadAsync(component);
        Assert.Equal(FiberState.Active, fiber.State);

        await runtime.DisposeAsync(fiber);
        await fiber.GoneAsync();
        Assert.Equal(FiberState.Gone, fiber.State);
    }

    // ---- managed-specific wiring --------------------------------------------

    [Fact]
    public async Task ManagedRetireReleasesAlcWithoutAManualUnload()
    {
        var reference = await LoadThroughRegistryAndRetire();
        Assert.True(GcAssert.Reclaimed(reference), "ALC survived a Registry retire without manual Unload");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static async Task<WeakReference> LoadThroughRegistryAndRetire()
        {
            await using var runtime = new Gocordis.Core.Runtime();
            await using var loader = new ManagedPluginLoader();
            await using var registry = new PluginRegistry(runtime);
            const string id = "registry-auto-release";
            registry.RegisterFactory("managed", _ =>
                new ManagedPluginComponent(loader, id, SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent"));

            await registry.ApplyAsync([Declaration("p", "managed")]);
            var fiber = registry.Entries["p"].Fiber!;
            Assert.Equal(FiberState.Active, fiber.State);
            var weak = new WeakReference(loader.Plugins[id].Context);

            await registry.ApplyAsync([]); // retire: effect inverse must release the ALC
            await fiber.GoneAsync();
            Assert.Equal(FiberState.Gone, fiber.State);
            Assert.False(loader.Plugins.ContainsKey(id));
            return weak;
        }
    }

    [Fact]
    public async Task ManagedStagedAlcIsReleasedWhenPrepareFails()
    {
        var reference = await FailDuringPrepare();
        Assert.True(GcAssert.Reclaimed(reference), "a staged managed ALC survived a failed Prepare");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static async Task<WeakReference> FailDuringPrepare()
        {
            await using var runtime = new Gocordis.Core.Runtime();
            await using var loader = new ManagedPluginLoader();
            await using var registry = new PluginRegistry(runtime);
            const string id = "staged-prepare-failure";
            WeakReference? weak = null;
            registry.RegisterComponentFactory("managed", _ =>
            {
                var component = new ManagedPluginComponent(
                    loader, id, SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");
                weak = new WeakReference(loader.Plugins[id].Context);
                return component;
            });
            registry.RegisterFactory("broken", _ => throw new InvalidOperationException("boom"));

            await Assert.ThrowsAnyAsync<Exception>(() => registry.ApplyAsync([
                Declaration(id, "managed"),
                Declaration("broken", "broken"),
            ]));

            Assert.False(loader.Plugins.ContainsKey(id));
            return weak!;
        }
    }

    [Fact]
    public async Task ManagedStaleGenerationCannotUnloadANewerAlc()
    {
        await using var loader = new ManagedPluginLoader();
        const string id = "managed-generation-fence";
        var plugin = loader.Load(id, SamplePath("SamplePlugin"), "SamplePlugin.SampleComponent");
        var newer = loader.GenerationsFor(id).Begin();

        Assert.False(loader.Unload(id, plugin.Generation));
        Assert.True(loader.Plugins.ContainsKey(id));

        Assert.True(loader.Unload(id, newer));
        Assert.False(loader.Plugins.ContainsKey(id));
    }

    // ---- wasm-specific wiring ------------------------------------------------

    [Fact]
    public async Task WasmRejectsMissingRequiredExportsAtLoadTime()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var key = new Key<int>($"missing-export-{Guid.NewGuid():N}");

        var fiber = await runtime.LoadAsync(new WasmComponent(MissingExportWat, key));
        Assert.Equal(FiberState.Failed, fiber.State);
        Assert.NotNull(fiber.Failure);
        Assert.Contains("runtime_component_create", fiber.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WasmStaleGenerationCannotReleaseTheCurrentFiber()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var swapper = new WasmHotSwapper(runtime, new Key<int>($"hmr-fence-{Guid.NewGuid():N}"));
        Assert.IsAssignableFrom<IAsyncDisposable>(swapper);

        var stale = swapper.BeginGeneration();
        await swapper.ReplaceAsync(ValidWat);
        var current = await swapper.ReplaceAsync(ValidWat);

        Assert.False(await swapper.ReleaseGenerationAsync(stale));
        Assert.Equal(FiberState.Active, current.State);
        Assert.Same(current, swapper.Current);

        Assert.True(await swapper.ReleaseGenerationAsync(swapper.BeginGeneration()));
        Assert.Equal(FiberState.Gone, current.State);
        Assert.Null(swapper.Current);
        await swapper.DisposeAsync();
    }

    // ---- process-specific wiring --------------------------------------------

    [PosixOnlyFact]
    public async Task ProcessHandshakeTimeoutNormalizesAndReapsTheChild()
    {
        var directory = SilentPluginDirectory();
        try
        {
            var (path, pidPath) = await WriteSilentPluginAsync(directory, answerHandshake: false);
            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                ProcessPluginClient.StartAsync(path, handshakeTimeout: TimeSpan.FromSeconds(8)));

            Assert.Contains(ProcessErrorPolicy.HandshakePhrase, error.Message, StringComparison.Ordinal);
            await AssertProcessReapedAsync(pidPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [PosixOnlyFact]
    public async Task ProcessCallTimeoutNormalizesWithoutKillingTheChild()
    {
        var directory = SilentPluginDirectory();
        try
        {
            var (path, _) = await WriteSilentPluginAsync(directory, answerHandshake: true);
            await using var client = await ProcessPluginClient.StartAsync(path);
            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                client.CallAsync("echo", timeout: TimeSpan.FromMilliseconds(250)));

            Assert.Contains(ProcessErrorPolicy.CallPhrase, error.Message, StringComparison.Ordinal);
            Assert.False(client.HasExited);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessRpcErrorKeepsItsDocumentedExceptionType()
    {
        await using var client = await ProcessPluginClient.StartAsync(ProcessExecutable());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync("nope"));
        Assert.Contains(ProcessErrorPolicy.RpcErrorPhrase, error.Message, StringComparison.Ordinal);
    }

    // ---- fixtures ------------------------------------------------------------

    private static IPluginBackend CreateBackend(BackendKind kind, bool valid) => kind switch
    {
        BackendKind.Managed => new ManagedBackend(
            new ManagedPluginLoader(),
            $"matrix-{Guid.NewGuid():N}",
            valid ? SamplePath("SamplePlugin") : "/definitely/not/a/plugin.dll",
            "SamplePlugin.SampleComponent"),
        BackendKind.Wasm => new WasmBackend(
            valid ? ValidWat : MissingExportWat,
            new Key<int>($"matrix-{Guid.NewGuid():N}")),
        BackendKind.Process => new ProcessBackend(
            valid ? ProcessExecutable() : "/definitely/not/a/plugin.exe",
            new Key<string>($"matrix-{Guid.NewGuid():N}")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static Component CreateComponent(BackendKind kind, bool valid) => kind switch
    {
        BackendKind.Managed => new ManagedPluginComponent(
            new ManagedPluginLoader(),
            $"matrix-component-{Guid.NewGuid():N}",
            valid ? SamplePath("SamplePlugin") : "/definitely/not/a/plugin.dll",
            "SamplePlugin.SampleComponent"),
        BackendKind.Wasm => new WasmComponent(
            valid ? ValidWat : MissingExportWat,
            new Key<int>($"matrix-component-{Guid.NewGuid():N}")),
        BackendKind.Process => new ProcessPluginComponent(
            valid ? ProcessExecutable() : "/definitely/not/a/plugin.exe",
            new Key<string>($"matrix-component-{Guid.NewGuid():N}")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BackendDegradationPolicy ExpectedPolicy(BackendKind kind) => kind switch
    {
        BackendKind.Process => BackendDegradationPolicy.ErrorVocabulary,
        _ => BackendDegradationPolicy.FailFiber,
    };

    private static PluginManifest Declaration(string id, string type) =>
        new(id, type, true, new Dictionary<string, object?>(), [], []);

    private static string SamplePath(string project) => Path.Combine(
        FindRoot(), "samples", project, "bin", "Debug", "net10.0", $"{project}.dll");

    private static string ProcessExecutable()
    {
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var path = Path.Combine(FindRoot(), "samples", "EchoProcPlugin", "bin", "Debug", "net10.0",
            $"EchoProcPlugin{suffix}");
        Assert.True(File.Exists(path), $"missing process plugin at '{path}'");
        return path;
    }

    private static string FindRoot()
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

        throw new DirectoryNotFoundException("Gocordis.sln not found");
    }

    private static string SilentPluginDirectory() =>
        Path.Combine(Path.GetTempPath(), $"gocordis-silent-{Guid.NewGuid():N}");

    private static async Task<(string Path, string PidPath)> WriteSilentPluginAsync(
        string directory, bool answerHandshake)
    {
        var path = Path.Combine(directory, "silent-plugin");
        var pidPath = Path.Combine(directory, "pid");
        Directory.CreateDirectory(directory);
        // A shell writes its PID before any interpreter starts. Python startup
        // can outlast the 250ms handshake timeout, which would kill the child
        // before it could record the PID the reaping assertion needs.
        var handshake = answerHandshake
            ? "read line\necho \"$line\"\n"
            : string.Empty;
        var script = "#!/bin/sh\n" +
                     "echo $$ > \"" + pidPath + "\"\n" +
                     handshake +
                     "exec sleep 3600\n";
        await File.WriteAllTextAsync(path, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return (path, pidPath);
    }

    private static async Task AssertProcessReapedAsync(string pidPath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(pidPath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(File.Exists(pidPath));
        var pid = int.Parse((await File.ReadAllTextAsync(pidPath)).Trim());
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (ProcessExists(pid) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.False(ProcessExists(pid), $"child {pid} survived the handshake timeout");
    }

    private static bool ProcessExists(int pid)
    {
        try
        {
            using var process = SystemProcess.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class RecordingContext : IBackendContext
    {
        public List<(string Name, object? Value)> Provided { get; } = [];

        public List<Func<ValueTask>> Inverses { get; } = [];

        public bool InverseInvoked { get; private set; }

        public CancellationToken CancellationToken => default;

        public bool IsDiverted => false;

        public void ThrowIfDiverted()
        {
        }

        public void Provide<T>(Key<T> key, T value) => Provided.Add((key.Name, value));

        public ValueTask EffectAsync(Func<ValueTask> install, Func<ValueTask> inverse)
        {
            Inverses.Add(async () =>
            {
                InverseInvoked = true;
                await inverse();
            });
            return install();
        }
    }
}
