using System.Collections.Concurrent;
using System.Diagnostics;
using SystemProcess = System.Diagnostics.Process;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gocordis.Runtime.Process;

/// <summary>
/// Host side of the Go proc protocol: handshake line + line-delimited JSON-RPC 2.0
/// over stdin/stdout. One client owns one process; the inverse of activation is a
/// graceful shutdown followed by a bounded kill backstop.
/// </summary>
public sealed class ProcessPluginClient : IAsyncDisposable
{
    private const string Handshake = "GORIDIS-PROC-PLUGIN 1";
    private readonly SystemProcess _process;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _callGate = new(1, 1);
    private readonly ConcurrentQueue<string> _stderr = new();
    private long _nextId;

    /// <summary>Bounded tail of plugin stderr, useful for degraded diagnostics.</summary>
    public IReadOnlyList<string> StderrTail => _stderr.ToArray();

    private ProcessPluginClient(SystemProcess process, StreamWriter writer, StreamReader reader, ConcurrentQueue<string> stderr)
    {
        _process = process;
        _writer = writer;
        _reader = reader;
        _stderr = stderr;
    }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public static async Task<ProcessPluginClient> StartAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        TimeSpan? handshakeTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments ?? [])
        {
            start.ArgumentList.Add(argument);
        }

        var process = SystemProcess.Start(start) ??
                      throw new InvalidOperationException($"failed to start '{executable}'");
        var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        var reader = new StreamReader(process.StandardOutput.BaseStream, new UTF8Encoding(false));
        var stderr = new ConcurrentQueue<string>();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.Enqueue(args.Data);
                while (stderr.Count > 100 && stderr.TryDequeue(out string _))
                {
                }
            }
        };
        process.BeginErrorReadLine();

        try
        {
            await writer.WriteLineAsync(Handshake);
            using var timeout = new CancellationTokenSource(handshakeTimeout ?? TimeSpan.FromSeconds(5));
            var answer = await reader.ReadLineAsync(timeout.Token) ??
                         throw new InvalidDataException("plugin closed during handshake");
            if (answer.Trim() != Handshake)
            {
                throw new InvalidDataException($"unexpected handshake: '{answer}'");
            }

            return new ProcessPluginClient(process, writer, reader, stderr);
        }
        catch
        {
            writer.Dispose();
            reader.Dispose();
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
            throw;
        }
    }

    public async Task<JsonNode?> CallAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_process.HasExited, this);
        using var bounded = timeout is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        cancellationToken = bounded.Token;
        await _callGate.WaitAsync(cancellationToken);
        try
        {
            return await CallCoreAsync(method, parameters, cancellationToken);
        }
        finally
        {
            _callGate.Release();
        }
    }

    private async Task<JsonNode?> CallCoreAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null)
        {
            request["params"] = JsonSerializer.SerializeToNode(parameters);
        }

        await WriteAsync(request.ToJsonString(), cancellationToken);
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken) ??
                       throw new EndOfStreamException($"plugin exited during '{method}'");
            var response = JsonNode.Parse(line)?.AsObject() ??
                           throw new InvalidDataException($"invalid RPC response: '{line}'");
            if (response["id"]?.GetValue<long>() != id)
            {
                continue; // notifications are ignored
            }

            if (response["error"] is JsonObject error)
            {
                throw new InvalidOperationException(error["message"]?.GetValue<string>() ?? "RPC error");
            }

            return response["result"]?.DeepClone();
        }
    }

    public async Task ShutdownAsync()
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await CallAsync("shutdown", null, timeout.Token, TimeSpan.FromSeconds(2));
        }
        catch
        {
            _process.Kill(entireProcessTree: true);
        }

        using var backstop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _process.WaitForExitAsync(backstop.Token);
        }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        _writer.Dispose();
        _reader.Dispose();
        _process.Dispose();
        _writeGate.Dispose();
        _callGate.Dispose();
    }
}
