using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Plugins;
using Xunit;

namespace Gocordis.Extensions.Tests;

public sealed class ExtensionTests
{
    [Fact]
    public void RegistrySubscriptionIsSkippedAfterDispose()
    {
        var registry = new StableRegistry<string>();
        var callbacks = 0;
        var watch = registry.Watch(() => callbacks++);

        registry.TryAdd("a", "1");
        Assert.Equal(1, callbacks);
        watch.Dispose();
        registry.TryReplace("a", "2");
        Assert.Equal(1, callbacks); // disposed watcher skipped on next notify
    }

    [Fact]
    public async Task TimerSemanticsIncludeDebounceAndThrottle()
    {
        await using var timers = new TimerService();
        var calls = 0;
        await timers.TimeoutAsync(TimeSpan.FromMilliseconds(10), () =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });
        Assert.Equal(1, calls);

        var debounced = 0;
        await timers.DebounceAsync(TimeSpan.FromMilliseconds(10), () =>
        {
            debounced++;
            return ValueTask.CompletedTask;
        });
        Assert.Equal(1, debounced);

        Assert.True(await timers.ThrottleAsync(TimeSpan.FromMinutes(1), () => ValueTask.CompletedTask));
        Assert.False(await timers.ThrottleAsync(TimeSpan.FromMinutes(1), () => ValueTask.CompletedTask));
    }

    [Fact]
    public async Task SchedulerAdvancesJobsDeterministically()
    {
        await using var scheduler = new Scheduler();
        var fired = 0;
        var now = DateTimeOffset.UtcNow.AddMinutes(10);
        scheduler.ScheduleOnce(now.AddMinutes(-1), () =>
        {
            fired++;
            return ValueTask.CompletedTask;
        });
        var recurring = scheduler.ScheduleInterval(TimeSpan.FromMinutes(5), () =>
        {
            fired++;
            return ValueTask.CompletedTask;
        });

        Assert.NotNull(await scheduler.RunNextAsync(now));
        Assert.NotNull(await scheduler.RunNextAsync(now));
        Assert.Null(await scheduler.RunNextAsync(now));
        Assert.Equal(2, fired);
        Assert.True(scheduler.Cancel(recurring));
    }

    [Fact]
    public async Task FileSystemMountRoutesAndRejectsEscapes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gocordis-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await using var files = new FileSystemService();
        files.Mount("/data", root);

        try
        {
            await files.WriteAsync("/data/a.txt", "hello"u8.ToArray());
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(await files.ReadAsync("/data/a.txt")));
            Assert.Throws<InvalidOperationException>(() => files.ReadAsync("/data/../secret").GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => files.ReadAsync("/data/../database/other").GetAwaiter().GetResult());

            var observed = new List<string>();
            using var watcher = files.Watch("/data", path => observed.Add(path));
            await files.WriteAsync("/data/watched.txt", "x"u8.ToArray());
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (observed.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.NotEmpty(observed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HttpInterceptorsAndServerHostWorkTogether()
    {
        await using var server = new ServerHost();
        server.MapGet("/hello", () => new JsonObject { ["value"] = 7 });
        await server.StartAsync();
        using var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(server.Address!) };
        Assert.Equal(200, (int)(await client.GetAsync("/hello")).StatusCode);

        await using var http = new HttpService();
        http.SetBaseAddress(new Uri(server.Address!));
        var intercepted = 0;
        using var registration = http.AddInterceptor(request =>
        {
            intercepted++;
            return ValueTask.CompletedTask;
        });
        var body = await http.GetStringAsync("/hello");
        Assert.Contains("7", body);
        Assert.Equal(1, intercepted);
        await server.StopAsync();
    }

    [Fact]
    public async Task EnvParsePrecedenceAndRollback()
    {
        const string key = "GOCORDIS_EXT_TEST_VALUE";
        await using var env = new EnvService();
        var parsed = EnvService.Parse("# comment\nA=1\nB=\"two\"\n");
        Assert.Equal("1", parsed["A"]);
        Assert.Equal("two", parsed["B"]);

        env.Apply(new Dictionary<string, string> { [key] = "set" });
        Assert.Equal("set", Environment.GetEnvironmentVariable(key));
        await env.DisposeAsync();
        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void CliTokenizerAndAliasesWork()
    {
        var cli = new CliDispatcher();
        Assert.Equal(new[] { "echo", "hello world" }, CliDispatcher.Tokenize("echo \"hello world\""));
        cli.Register("echo", args => string.Join(",", args), "e");
        Assert.Equal("a,b", cli.Dispatch("e a b"));
        Assert.Equal("unknown command: nope", cli.Dispatch("nope"));
    }

    [Fact]
    public void BrokerAndRegistryManageMembers()
    {
        var broker = new Broker<string>();
        using var one = broker.Register("a");
        using var two = broker.Register("b");
        Assert.Equal("a", broker.RoundRobin(0));
        Assert.Equal("b", broker.RoundRobin(1));
        Assert.Equal("b", broker.RoundRobin(-1));
        two.Dispose();
        Assert.Single(broker.Providers);

        var registry = new StableRegistry<int>();
        var notifications = 0;
        using var watcher = registry.Watch(() => notifications++);
        Assert.True(registry.TryAdd("x", 1));
        Assert.True(registry.TryReplace("x", 2));
        Assert.Equal(2, registry.Snapshot()["x"]);
        Assert.True(registry.TryRemove("x"));
        Assert.Equal(3, notifications);
    }

    [Fact]
    public async Task ObserveStreamDeliversSequencedItems()
    {
        await using var stream = new ObserveStream<string>();
        Assert.Equal(1, stream.Publish("a"));
        Assert.Equal(2, stream.Publish("b"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = new List<string>();
        await foreach (var item in stream.Subscribe(2, cts.Token))
        {
            received.Add(item);
            if (received.Count == 1)
            {
                break;
            }
        }

        Assert.Equal("b", Assert.Single(received));
    }

    [Fact]
    public async Task WatchServiceIncrementsRevisions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "w.txt");
        await File.WriteAllTextAsync(file, "a");
        await using var watcher = new WatchService();
        var revisions = new List<long>();
        using var subscription = watcher.Watch(file, (_, revision) => revisions.Add(revision));

        try
        {
            await File.WriteAllTextAsync(file, "b");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (revisions.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(watcher.Revision(file) > 0);
            Assert.NotEmpty(revisions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public async Task SchedulerDoesNotRunTheSameDueJobTwice()
    {
        await using var scheduler = new Scheduler();
        var executions = 0;
        scheduler.ScheduleOnce(DateTimeOffset.UtcNow.AddMinutes(-1), async () =>
        {
            Interlocked.Increment(ref executions);
            await Task.Delay(20);
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => scheduler.RunNextAsync(DateTimeOffset.UtcNow)));

        Assert.Equal(1, executions);
        Assert.Single(results, id => id.HasValue);
    }

    [Fact]
    public void BrokerAndRegistryAreSafeUnderConcurrentMutation()
    {
        var broker = new Broker<int>();
        Parallel.For(0, 100, index => broker.Register(index).Dispose());
        Assert.Empty(broker.Providers);

        var registry = new StableRegistry<int>();
        Parallel.For(0, 100, index => registry.TryAdd(index.ToString(), index));
        Assert.Equal(100, registry.Snapshot().Count);
    }

    [Fact]
    public async Task ObserveStreamExposesSequenceGaps()
    {
        await using var stream = new ObserveStream<int>();
        for (var index = 1; index <= 100; index++)
        {
            stream.Publish(index);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var first = await stream.SubscribeWithSequence(1, cts.Token).FirstAsync(cts.Token);
        Assert.True(first.Sequence > 1, "bounded stream should expose dropped sequence numbers");
        Assert.Equal(first.Sequence, first.Item);
    }

    [Fact]
    public async Task MarketAndPackageManagerVerifyAndRollBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-market-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "plugin.toml");
        var toml = """
            [[components]]
            id = "p"
            type = "echo"
            enabled = true
            checksum = "DEADBEEF"
            """;
        await File.WriteAllTextAsync(file, toml);
        var market = new MarketService();
        var entry = Assert.Single(market.Scan(directory));
        Assert.False(market.Verify(entry, file));
        Assert.True(market.IsCompatible("1.0.0", "2.0.0"));
        Assert.False(market.IsCompatible("2.0.0", "1.0.0"));
        Directory.Delete(directory, recursive: true);

        var manifest = new PluginManifest("p", "echo", true, new Dictionary<string, object?>(), [], []);
        var manager = new PackageManager();
        var applied = manager.Apply([], [new PackageManager.Change("add", manifest)]);
        Assert.Single(applied);
        Assert.Throws<InvalidOperationException>(() =>
            manager.Apply(applied, [new PackageManager.Change("bogus", manifest)]));
        await ValueTask.CompletedTask;
    }
}
