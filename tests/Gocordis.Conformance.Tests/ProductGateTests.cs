using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Conformance.Tests;

/// <summary>
/// Closes the "acceptance insufficient" part of three feature-matrix rows:
/// console Explorer/Inspector productization, integration gates and the
/// deterministic theorem facility (Thm73). Every assertion goes through
/// public Runtime / ConsoleHost / WebUiServer / PluginRegistry APIs or the
/// real HTTP wire; build artifacts are additional delivery evidence only.
///
/// PC gate numbering note: the Go gate report uses PC-19 for reconciliation
/// idempotence and PC-22 for stale completion, while the local C# suite uses
/// PC01/PC19 as local case numbers. Both spellings are exercised here: this
/// file covers idempotence, stale-completion isolation, Close/Gone, no second
/// lifecycle and stable identity/quiescence in five separate gates.
/// </summary>
public sealed class ProductGateTests
{
    // ---------------------------------------------------------------------
    // 1) ExplorerDashboardConformance — real HTTP over ConsoleHost + WebUiServer
    // ---------------------------------------------------------------------

    private static readonly Key<int> ExplorerSharedKey = new("explorer-shared");
    private static readonly Key<int> ExplorerAbsentKey = new("explorer-absent");

    [Fact]
    public async Task ExplorerDashboardConformance_HttpExposesPluginsDashboardAndInsightGraph()
    {
        var dist = FindConsoleDist();
        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("provider", _ => new ExplorerProvider());
        registry.RegisterFactory("consumer", _ => new ExplorerConsumer());
        registry.RegisterFactory("orphan", _ => new ExplorerOrphan());
        registry.RegisterFactory("noop", _ => new ExplorerNoop());
        registry.ExternalCapabilities.Add(ExplorerAbsentKey);

        await registry.ApplyAsync(
        [
            Manifest("provider", "provider"),
            Manifest("consumer", "consumer"),
            Manifest("orphan", "orphan"),
            Manifest("disabled-plugin", "noop", enabled: false),
        ]);

        var fibers = runtime.Snapshot().Fibers;
        Assert.Equal(3, fibers.Count);
        var providerFiber = Assert.Single(fibers, f => f.Name == "provider");
        var consumerFiber = Assert.Single(fibers, f => f.Name == "consumer");
        var orphanFiber = Assert.Single(fibers, f => f.Name == "orphan");
        Assert.Equal(FiberState.Active, providerFiber.State);
        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.Equal(FiberState.Pending, orphanFiber.State);

        await using var server = new WebUiServer(host, registry: registry, webDistPath: dist);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        // /api/plugins: plugin id + observable state for enabled and disabled entries.
        var plugins = JsonNode.Parse(await client.GetStringAsync("/api/plugins"))!["data"]!.AsArray();
        Assert.Equal(4, plugins.Count);
        AssertPlugin(plugins, "provider", "provider", true, "Active");
        AssertPlugin(plugins, "consumer", "consumer", true, "Active");
        AssertPlugin(plugins, "orphan", "orphan", true, "Pending");
        AssertPlugin(plugins, "disabled-plugin", "noop", false, "disabled");

        // /api/query/framework/dashboard: canonical DashboardDTO shape — the
        // console's parseDashboard reads nested `counts`, not flat totals. The
        // old flat shape made the page render all zeros despite API data.
        var dashboard = JsonNode.Parse(
            await client.GetStringAsync("/api/query/framework/dashboard"))!["data"]!;
        var counts = dashboard["counts"]!;
        Assert.Equal(fibers.Count, counts["fibers"]!.GetValue<int>());
        Assert.Equal(fibers.Count(f => f.State == FiberState.Active), counts["active"]!.GetValue<int>());
        Assert.Equal(fibers.Count(f => f.State == FiberState.Pending), counts["pending"]!.GetValue<int>());
        Assert.Equal(fibers.Count(f => f.State == FiberState.Failed), counts["failed"]!.GetValue<int>());
        Assert.NotNull(dashboard["sequence"]);
        Assert.NotNull(dashboard["state"]);
        Assert.NotNull(dashboard["recentEvents"]);
        Assert.NotNull(dashboard["diagnostics"]);
        Assert.Equal(3, counts["fibers"]!.GetValue<int>());
        Assert.Equal(2, counts["active"]!.GetValue<int>());
        Assert.Equal(1, counts["pending"]!.GetValue<int>());
        Assert.Equal(0, counts["failed"]!.GetValue<int>());

        // /api/query/runtime/events: the canonical event ring query the
        // framework-dashboard timeline consumes. Must not 404.
        var events = JsonNode.Parse(
            await client.GetStringAsync("/api/query/runtime/events"))!["data"]!;
        Assert.NotNull(events["events"]);
        Assert.True(events["sequence"]!.GetValue<long>() > 0);

        // /api/query/insight/graph: canonical Graph shape with declared/runtime
        // views — the console's InsightGraphBlock reads those, not flat nodes.
        var graph = JsonNode.Parse(
            await client.GetStringAsync("/api/query/insight/graph"))!["data"]!;
        var runtimeNodes = graph["runtime"]!["nodes"]!.AsArray();
        var runtimeEdges = graph["runtime"]!["edges"]!.AsArray();
        Assert.NotNull(graph["declared"]!["nodes"]);
        Assert.NotNull(graph["declared"]!["edges"]);
        Assert.Equal(fibers.Count, runtimeNodes.Count);
        foreach (var fiber in fibers)
        {
            Assert.Contains(runtimeNodes, node =>
                node?["fiberId"]?.GetValue<string>() == fiber.Id.ToString() &&
                node["name"]?.GetValue<string>() == fiber.Name);
        }

        Assert.NotEmpty(runtimeEdges);
        var expectedBinding = $"{ExplorerSharedKey.Name}->{providerFiber.Id}";
        Assert.Contains(runtimeEdges, edge =>
            edge?["consumerFiberId"]?.GetValue<string>() == consumerFiber.Id.ToString() &&
            edge["key"]?.GetValue<string>() == ExplorerSharedKey.Name);

        // The productized console (not the embedded bootstrap) is what is served.
        Assert.Contains("/assets/", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task ExplorerDashboardConformance_BuiltExplorerAndDashboardAssetsAreServed()
    {
        var dist = FindConsoleDist();

        // Delivery evidence from the real build output; not the sole assertion.
        // Renderer registration lives in whichever chunk Vite emits it to (the
        // main bundle today, a dedicated Explorer chunk before), so locate the
        // built asset by content rather than by a hard-coded file name.
        var allJsAssets = Directory.GetFiles(
            Path.Combine(dist, "assets"), "*.js", SearchOption.TopDirectoryOnly);
        var dashboardAsset = allJsAssets.Single(path => File.ReadAllText(path)
            .Contains("dashboard", StringComparison.OrdinalIgnoreCase));

        await using var runtime = new Gocordis.Core.Runtime();
        var host = new ConsoleHost(runtime);
        await using var server = new WebUiServer(host, webDistPath: dist);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Address!) };

        var index = await client.GetStringAsync("/");
        var script = Regex.Match(index, "src=\"(/assets/[^\"]+\\.js)\"");
        Assert.True(script.Success, "React console index must reference a hashed JS bundle");
        Assert.Equal(Path.GetFileName(dashboardAsset), Path.GetFileName(script.Groups[1].Value));
        var bundle = await client.GetStringAsync(script.Groups[1].Value);
        Assert.True(bundle.Contains("dashboard", StringComparison.OrdinalIgnoreCase),
            "served main bundle must contain the dashboard view");
        // Product renderers now arrive from the builtin Console client module,
        // not from the shell bundle. Both halves of that boundary are asserted.
        var modulePath = Path.Combine(
            AppContext.BaseDirectory, "client-modules", "gocordis-control-plane-ui", "ui.js");
        Assert.True(File.Exists(modulePath), "builtin control-plane client module must ship with the product host");
        var module = await File.ReadAllTextAsync(modulePath);
        Assert.Contains("plugin-explorer", module, StringComparison.Ordinal);
        Assert.DoesNotContain("plugin-explorer", bundle, StringComparison.Ordinal);

        // The Inspector/dashboard query remains live behind the shipped asset.
        var dashboard = JsonNode.Parse(
            await client.GetStringAsync("/api/query/framework/dashboard"))!["data"]!;
        Assert.Equal(0, dashboard["counts"]!["fibers"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------------
    // 2) PCGateParity — platform convergence semantics over public Runtime API
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> IdempotentDeclarationCases()
    {
        yield return new object[] { "a,b", "a,b" };
        yield return new object[] { "b,a", "a,b" };
        yield return new object[] { "single", "single" };
    }

    /// <summary>Go PC-19: Reconcile(Reconcile(D)) == Reconcile(D); ids are stable.</summary>
    [Theory]
    [MemberData(nameof(IdempotentDeclarationCases))]
    public async Task PCGateParity_PC01_DeclarationReconcileIsIdempotentAndIdsStayStable(
        string firstBatch, string secondBatch)
    {
        var firstIds = firstBatch.Split(',');
        var secondIds = secondBatch.Split(',');
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new Echo());

        await registry.ApplyAsync(firstIds.Select(id => Manifest(id, "echo")).ToArray());
        var first = registry.Entries.ToDictionary(
            entry => entry.Key, entry => (entry.Value.Fiber?.Id, entry.Value.Fiber?.Generation));
        var firstSnapshotIds = runtime.Snapshot().Fibers.Select(f => f.Id).OrderBy(id => id).ToArray();

        await registry.ApplyAsync(secondIds.Select(id => Manifest(id, "echo")).ToArray());
        var second = registry.Entries.ToDictionary(
            entry => entry.Key, entry => (entry.Value.Fiber?.Id, entry.Value.Fiber?.Generation));

        Assert.Equal(first, second);
        Assert.Equal(firstIds.Length, runtime.Snapshot().Fibers.Count);
        Assert.Equal(firstSnapshotIds, runtime.Snapshot().Fibers.Select(f => f.Id).OrderBy(id => id));
        Assert.All(registry.Entries.Values, entry =>
        {
            Assert.NotNull(entry.Fiber);
            Assert.Equal(FiberState.Active, entry.Fiber!.State);
        });
    }

    /// <summary>Go PC-22: a completion arriving after disposal must not touch a new generation.</summary>
    [Fact]
    public async Task PCGateParity_PC19_StaleCompletionCannotAffectAFreshActivation()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var stale = new StaleComponent();
        var loading = runtime.LoadAsync(stale);
        Task disposal = Task.CompletedTask;
        Guid staleId = Guid.Empty;
        try
        {
            await stale.Entered;
            var fiber = runtime.GetFiber(runtime.Snapshot().Fibers.Single().Id)!;
            staleId = fiber.Id;
            Assert.Equal(FiberState.Loading, fiber.State);

            disposal = runtime.DisposeAsync(fiber);
            Assert.False(disposal.IsCompleted, "in-flight install defers unwind until it lands");
        }
        finally
        {
            stale.Release();
        }

        await loading;
        await disposal;
        Assert.Equal(1, stale.Completed);
        Assert.Equal(1, stale.Undone);
        Assert.Empty(runtime.Snapshot().Fibers);

        var fresh = await runtime.LoadAsync(new Probe("fresh", _ => Task.CompletedTask));
        Assert.Equal(FiberState.Active, fresh.State);
        Assert.NotEqual(Guid.Empty, staleId);
        Assert.NotEqual(staleId, fresh.Id);
        Assert.Equal(1, fresh.Generation);
        Assert.Equal(new[] { fresh.Id }, runtime.Snapshot().Fibers.Select(f => f.Id));
    }

    /// <summary>Go PC-21: Close unwinds every fiber, is idempotent and closes the door.</summary>
    [Fact]
    public async Task PCGateParity_PC21_CloseSendsEveryFiberToGone()
    {
        var runtime = new Gocordis.Core.Runtime();
        var key = new Key<int>("pc21");
        var provider = new Probe("provider", context =>
        {
            context.Provide(key, 1);
            return Task.CompletedTask;
        }, provide: [key]);
        var consumer = new Probe("consumer", context =>
        {
            Assert.Equal(1, context.Require(key));
            return Task.CompletedTask;
        }, inject: [key]);
        var providerFiber = await runtime.LoadAsync(provider);
        var consumerFiber = await runtime.LoadAsync(consumer);

        await runtime.CloseAsync();
        await runtime.CloseAsync();

        Assert.Equal(FiberState.Gone, providerFiber.State);
        Assert.Equal(FiberState.Gone, consumerFiber.State);
        Assert.Empty(runtime.Snapshot().Fibers);
        Assert.True(providerFiber.GoneAsync().IsCompletedSuccessfully);
        Assert.True(consumerFiber.GoneAsync().IsCompletedSuccessfully);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.LoadAsync(new Probe("late", _ => Task.CompletedTask)));
        await runtime.DisposeAsync();
    }

    /// <summary>Go PC-27: a declaration never creates a second lifecycle for one fiber id.</summary>
    [Fact]
    public async Task PCGateParity_PC27_RegistryCreatesNoSecondLifecycle()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        await using var registry = new PluginRegistry(runtime);
        registry.RegisterFactory("echo", _ => new Echo());

        await registry.ApplyAsync([Manifest("a", "echo"), Manifest("b", "echo", enabled: false)]);
        var aFiber = registry.Entries["a"].Fiber!;
        Assert.Single(runtime.Snapshot().Fibers);
        Assert.Equal(2, registry.Entries.Count);
        Assert.Null(registry.Entries["b"].Fiber);

        await registry.ApplyAsync([Manifest("a", "echo"), Manifest("b", "echo")]);
        Assert.Same(aFiber, registry.Entries["a"].Fiber);
        var bFiber = registry.Entries["b"].Fiber!;
        Assert.Equal(FiberState.Active, bFiber.State);
        Assert.Equal(2, runtime.Snapshot().Fibers.Count);

        await registry.ApplyAsync([Manifest("a", "echo", enabled: false), Manifest("b", "echo")]);
        Assert.Null(registry.Entries["a"].Fiber);
        Assert.Equal(FiberState.Gone, aFiber.State);
        Assert.Same(bFiber, registry.Entries["b"].Fiber);
        Assert.Equal(new[] { bFiber.Id }, runtime.Snapshot().Fibers.Select(f => f.Id));
    }

    public static IEnumerable<object[]> QuiescenceChainCases()
    {
        yield return new object[] { 1 };
        yield return new object[] { 2 };
        yield return new object[] { 3 };
    }

    /// <summary>Go PC-30: final quiescence — all fibers Active, stable ids, then all Gone.</summary>
    [Theory]
    [MemberData(nameof(QuiescenceChainCases))]
    public async Task PCGateParity_PC30_FinalQuiescenceKeepsStableFiberIdentity(int chainLength)
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var keys = Enumerable.Range(0, chainLength)
            .Select(index => new Key<int>($"pc30-{index}"))
            .ToArray();
        var fibers = new List<Fiber>();
        for (var index = 0; index < chainLength; index++)
        {
            var position = index;
            var inject = position == 0
                ? Array.Empty<ICapabilityKey>()
                : new ICapabilityKey[] { keys[position - 1] };
            fibers.Add(await runtime.LoadAsync(new Probe($"pc30-{position}", context =>
            {
                if (position > 0)
                {
                    _ = context.Require(keys[position - 1]);
                }

                context.Provide(keys[position], position);
                return Task.CompletedTask;
            }, inject: inject, provide: [keys[position]])));
        }

        Assert.All(fibers, fiber => Assert.Equal(FiberState.Active, fiber.State));
        var identity = fibers.Select(fiber => fiber.Id).OrderBy(id => id).ToArray();
        var snapshot = runtime.Snapshot();
        Assert.Equal(chainLength, snapshot.Fibers.Count);
        Assert.Equal(identity, snapshot.Fibers.Select(f => f.Id).OrderBy(id => id));
        Assert.Equal(identity, runtime.Snapshot().Fibers.Select(f => f.Id).OrderBy(id => id));
        Assert.DoesNotContain(snapshot.Fibers, f => f.State is FiberState.Pending or FiberState.Loading or FiberState.Failed);

        await runtime.CloseAsync();

        Assert.All(fibers, fiber => Assert.Equal(FiberState.Gone, fiber.State));
        Assert.Empty(runtime.Snapshot().Fibers);
    }

    // ---------------------------------------------------------------------
    // 3) Thm73InstallingQuiescence — observable Installing barrier via Core API
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Thm73InstallingQuiescence_InstallingEffectBlocksActiveUntilRelease()
    {
        await using var runtime = new Gocordis.Core.Runtime();
        var gated = new GatedIterativeProvider();
        var consumer = new Probe("thm73-consumer", context =>
        {
            Assert.Equal(73, context.Require(gated.Key));
            return Task.CompletedTask;
        }, inject: [gated.Key]);

        var providerLoad = runtime.LoadAsync(gated);
        Task consumerLoad = Task.CompletedTask;
        try
        {
            await gated.InstallStarted;

            // The provider's effect is Installing; consumers cannot be Active yet.
            consumerLoad = runtime.LoadAsync(consumer);
            await WaitUntilAsync(() =>
            {
                var fibers = runtime.Snapshot().Fibers;
                return fibers.Count == 2 &&
                       fibers.Any(f => f.Name == "thm73-provider" && f.State == FiberState.Loading) &&
                       fibers.Any(f => f.Name == "thm73-consumer" && f.State == FiberState.Pending);
            }, "installing barrier to be observable");

            Assert.False(providerLoad.IsCompleted);
            Assert.False(consumerLoad.IsCompleted);
            Assert.DoesNotContain(runtime.Snapshot().Fibers, f => f.State == FiberState.Active);
            Assert.All(runtime.Snapshot().Fibers, f => Assert.Null(f.Failure));
        }
        finally
        {
            gated.Release();
        }

        var providerFiber = await providerLoad;
        await consumerLoad;
        var consumerFiber = runtime.GetFiber(runtime.Snapshot().Fibers
            .Single(f => f.Name == "thm73-consumer").Id)!;

        Assert.Equal(FiberState.Active, providerFiber.State);
        Assert.Equal(FiberState.Active, consumerFiber.State);
        Assert.All(runtime.Snapshot().Fibers, f => Assert.Equal(FiberState.Active, f.State));
        Assert.Equal(1, gated.Installs);
    }

    // ---- helpers --------------------------------------------------------

    private static PluginManifest Manifest(string id, string type, bool enabled = true) =>
        new(id, type, enabled, new Dictionary<string, object?>(), [], []);

    private static void AssertPlugin(
        JsonArray plugins, string id, string type, bool enabled, string state) =>
        Assert.Contains(plugins, plugin =>
            plugin?["id"]?.GetValue<string>() == id &&
            plugin["type"]?.GetValue<string>() == type &&
            plugin["enabled"]?.GetValue<bool>() == enabled &&
            plugin["state"]?.GetValue<string>() == state);

    private static string FindConsoleDist()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "web-dist") };
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "web", "console", "dist"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "web/console/dist (or its web-dist copy) is missing; build the console first");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"timed out waiting for {description}");
            }

            await Task.Delay(25);
        }
    }

    private sealed class ExplorerProvider : Component
    {
        public override string Name => "provider";

        public override IReadOnlyList<ICapabilityKey> Provide() => [ExplorerSharedKey];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(ExplorerSharedKey, 42);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class ExplorerConsumer : Component
    {
        public override string Name => "consumer";

        public override IReadOnlyList<ICapabilityKey> Inject() => [ExplorerSharedKey];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            Assert.Equal(42, context.Require(ExplorerSharedKey));
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class ExplorerOrphan : Component
    {
        public override string Name => "orphan";

        public override IReadOnlyList<ICapabilityKey> Inject() => [ExplorerAbsentKey];

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class ExplorerNoop : Component
    {
        public override string Name => "noop";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class Echo : Component
    {
        private readonly Key<int> _key = new($"echo-{Guid.NewGuid():N}");

        public override string Name => "echo";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, 1);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private sealed class Probe : Component
    {
        private readonly Func<Context, Task> _apply;

        public Probe(string name, Func<Context, Task> apply,
            IReadOnlyList<ICapabilityKey>? inject = null,
            IReadOnlyList<ICapabilityKey>? provide = null)
        {
            Name = name;
            _apply = apply;
            InjectKeys = inject ?? [];
            ProvideKeys = provide ?? [];
        }

        public override string Name { get; }

        public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

        public override IReadOnlyList<ICapabilityKey> Provide() => ProvideKeys;

        private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

        private IReadOnlyList<ICapabilityKey> ProvideKeys { get; }

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            await _apply(context);
            return null;
        }
    }

    private sealed class StaleComponent : Component
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _completed;
        private int _undone;

        public override string Name => "stale";

        public Task Entered => _entered.Task;

        public int Completed => Volatile.Read(ref _completed);

        public int Undone => Volatile.Read(ref _undone);

        public void Release() => _release.TrySetResult();

        public override async ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            _entered.TrySetResult();
            await _release.Task;
            Interlocked.Increment(ref _completed);
            return () =>
            {
                Interlocked.Increment(ref _undone);
                return ValueTask.CompletedTask;
            };
        }
    }

    /// <summary>
    /// An actual effect in the Installing phase: the barrier sits inside the
    /// install delegate of an IIterativeComponent step, so Runtime state stays
    /// Loading until the gate opens.
    /// </summary>
    private sealed class GatedIterativeProvider : Component, IIterativeComponent
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _installs;

        public Key<int> Key { get; } = new("thm73-gate");

        public override string Name => "thm73-provider";

        public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
            throw new NotSupportedException("iterative component uses IterateAsync");

        public override IReadOnlyList<ICapabilityKey> Provide() => [Key];

        public Task InstallStarted => _started.Task;

        public int Installs => Volatile.Read(ref _installs);

        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<EffectRegistration> IterateAsync(Context context)
        {
            yield return EffectRegistration.From(
                async () =>
                {
                    context.Provide(Key, 73);
                    Interlocked.Increment(ref _installs);
                    _started.TrySetResult();
                    await _release.Task;
                },
                () => ValueTask.CompletedTask);
            await Task.CompletedTask;
        }
    }
}
