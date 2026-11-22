using Gocordis.Core;
using Gocordis.Runtime.Process;
using Xunit;

namespace Gocordis.Runtime.Tests;

public sealed class ProcessRuntimeTests
{
    [Fact]
    public async Task HandshakeAndJsonRpcRoundTrip()
    {
        await using var client = await ProcessPluginClient.StartAsync(PluginExecutable());

        var result = await client.CallAsync("echo", new { text = "hello" });

        Assert.Equal("echo:hello", result!["text"]!.GetValue<string>());
        await client.ShutdownAsync();
        Assert.True(client.HasExited);
    }

    [Fact]
    public async Task UnknownMethodReturnsAnRpcError()
    {
        await using var client = await ProcessPluginClient.StartAsync(PluginExecutable());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CallAsync("nope"));
    }

    [Fact]
    public async Task CoreLifecycleOwnsTheProcess()
    {
        var key = new Key<string>("proc-value");
        await using var runtime = new Gocordis.Core.Runtime();
        var component = new ProcessPluginComponent(PluginExecutable(), key);

        var fiber = await runtime.LoadAsync(component);
        Assert.Equal(FiberState.Active, fiber.State);

        var observed = new List<string>();
        await runtime.LoadAsync(new ProbeComponent(
            "consumer",
            context => observed.Add(context.Require(key)),
            [key]));
        Assert.Equal("echo:ready", Assert.Single(observed));

        await runtime.DisposeAsync(fiber);
        Assert.Equal(FiberState.Gone, fiber.State);
        Assert.Empty(runtime.Snapshot().Fibers.Where(f => f.Name == "process-plugin"));
    }


    [Fact]
    public async Task LargeStderrOutputDoesNotDeadlockRpc()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX shell fixture
        }

        var directory = Path.Combine(Path.GetTempPath(), $"gocordis-stderr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "flood-plugin");
        const string script = """
            #!/usr/bin/env python3
            import json, sys
            handshake = sys.stdin.readline().strip()
            sys.stdout.write(handshake + "\n")
            sys.stdout.flush()
            sys.stderr.write("x" * 200000 + "\n")
            sys.stderr.flush()
            for line in sys.stdin:
                request = json.loads(line)
                response = {"jsonrpc": "2.0", "id": request["id"], "result": {}}
                if request.get("method") == "echo":
                    response["result"] = {"text": "flood-ok"}
                sys.stdout.write(json.dumps(response) + "\n")
                sys.stdout.flush()
                if request.get("method") == "shutdown":
                    break
            """;
        await File.WriteAllTextAsync(path, script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            await using var client = await ProcessPluginClient.StartAsync(path);
            var result = await client.CallAsync("echo", timeout: TimeSpan.FromSeconds(10));
            Assert.Equal("flood-ok", result!["text"]!.GetValue<string>());
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (client.StderrTail.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.NotEmpty(client.StderrTail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string PluginExecutable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
            {
                var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
                var path = Path.Combine(
                    directory.FullName,
                    "samples",
                    "EchoProcPlugin",
                    "bin",
                    "Debug",
                    "net10.0",
                    $"EchoProcPlugin{suffix}");
                Assert.True(File.Exists(path), $"missing process plugin at '{path}'");
                return path;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Gocordis.sln not found");
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
