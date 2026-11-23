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
/// graceful shutdown followed by a bounded kill backstop. Timeouts are normalized
/// to TimeoutException and always reclaim the child (see ProcessErrorPolicy).
/// </summary>
public sealed class ProcessPluginClient : IAsyncDisposable
{
    private const string Handshake = "GORIDIS-PROC-PLUGIN 1";
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KillBackstop = TimeSpan.FromSeconds(5);

    private readonly SystemProcess _process;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _callGate = new(1, 1);
    private readonly ConcurrentQueue<string> _stderr = new();
    private long _nextId;
    private bool _disposed;

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
                while (stderr.Count > 100 && stderr.TryDequeue(out string? _))
                {
                }
            }
        };
        process.BeginErrorReadLine();

        var limit = handshakeTimeout ?? DefaultHandshakeTimeout;
        using var timeout = new CancellationTokenSource(limit);
        var handedOff = false;
        try
        {
            await writer.WriteLineAsync(Handshake);
            var answer = await reader.ReadLineAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (answer is null)
            {
                throw new InvalidDataException("plugin closed during handshake");
            }

            if (answer.Trim() != Handshake)
            {
                throw new InvalidDataException($"unexpected handshake: '{answer}'");
            }

            handedOff = true;
            return new ProcessPluginClient(process, writer, reader, stderr);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            // Normalize the ambient cancellation into a domain timeout. The
            // finally block still reaps the child before this reaches the caller.
            throw ProcessErrorPolicy.HandshakeTimeout(limit, ex);
        }
        finally
        {
            if (!handedOff)
            {
                await TerminateAsync(process);
                TryDispose(writer.Dispose);
                TryDispose(reader.Dispose);
            }
        }
    }

    public async Task<JsonNode?> CallAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_process.HasExited, this);
        var limit = timeout ?? DefaultCallTimeout;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(limit);

        var acquired = false;
        try
        {
            await _callGate.WaitAsync(bounded.Token);
            acquired = true;
            return await CallCoreAsync(method, parameters, bounded.Token);
        }
        catch (OperationCanceledException ex)
            when (bounded.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A call timeout is not a protocol error and does not kill the
            // child; the process stays available for the next call.
            throw ProcessErrorPolicy.CallTimeout(method, limit, ex);
        }
        finally
        {
            if (acquired)
            {
                _callGate.Release();
            }
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
            var line = await _reader.ReadLineAsync(cancellationToken);

            // ReadLineAsync can surface cancellation as an empty/short read on
            // some platforms. Never parse that as JSON: turn it back into a
            // cancellation so CallAsync can apply the documented timeout policy.
            cancellationToken.ThrowIfCancellationRequested();
            if (line is null)
            {
                throw new EndOfStreamException($"plugin exited during '{method}'");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? response;
            try
            {
                response = JsonNode.Parse(line)?.AsObject();
            }
            catch (System.Text.Json.JsonException ex)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    $"plugin call '{method}' canceled while reading a response", ex, cancellationToken);
            }

            if (response is null)
            {
                throw new InvalidDataException($"invalid RPC response: '{line}'");
            }

            // Plugin -> host method calls are not supported by the v1 protocol.
            if (response["method"] is not null && response["result"] is null && response["error"] is null)
            {
                var rejection = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = response["id"]?.DeepClone() ?? 0,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32601,
                        ["message"] = "plugin-to-host calls are not supported",
                    },
                };
                await WriteAsync(rejection.ToJsonString(), cancellationToken);
                continue;
            }

            if (response["id"]?.GetValue<long>() != id)
            {
                continue; // notifications are ignored
            }

            if (response["error"] is JsonObject error)
            {
                throw ProcessErrorPolicy.RpcError(error["message"]?.GetValue<string>());
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

        var graceful = true;
        try
        {
            await CallAsync("shutdown", null, default, ShutdownTimeout);
        }
        catch
        {
            graceful = false;
        }

        if (_process.HasExited)
        {
            return;
        }

        if (!graceful)
        {
            // A plugin that ignored shutdown will not exit on its own; go
            // straight to the bounded kill backstop instead of waiting.
            await TerminateAsync(_process);
            return;
        }

        try
        {
            using var backstop = new CancellationTokenSource(KillBackstop);
            await _process.WaitForExitAsync(backstop.Token);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(_process);
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

    /// <summary>Kill + bounded wait + dispose, safe to call on an already-exited child.</summary>
    private static async Task TerminateAsync(SystemProcess process)
    {
        SafeKill(process);
        try
        {
            using var backstop = new CancellationTokenSource(KillBackstop);
            await process.WaitForExitAsync(backstop.Token);
        }
        catch (OperationCanceledException)
        {
            SafeKill(process);
            using var final = new CancellationTokenSource(KillBackstop);
            try
            {
                await process.WaitForExitAsync(final.Token);
            }
            catch (OperationCanceledException)
            {
                // best effort: never mask the original activation error
            }
        }
        finally
        {
            TryDispose(process.Dispose);
        }
    }

    private static void SafeKill(SystemProcess process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                process.Kill();
            }
            catch (NotSupportedException)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        catch (NotSupportedException)
        {
            // platform without process kill; backstop still waits
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // best effort: never mask the original activation error
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await ShutdownAsync();
        }
        catch (IOException)
        {
            // A crashed plugin closes the pipe; nothing left to shut down.
        }
        catch (ObjectDisposedException)
        {
            // already reaped by a previous shutdown
        }

        TryDispose(_writer.Dispose);
        TryDispose(_reader.Dispose);
        TryDispose(_process.Dispose);
        TryDispose(_writeGate.Dispose);
        TryDispose(_callGate.Dispose);
    }

    private static void TryDispose(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
            // broken pipe after plugin exit
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }
}
