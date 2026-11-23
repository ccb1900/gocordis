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

    /// <summary>Protocol guard: one JSON-RPC frame (request or response) may
    /// not exceed this size, so a plugin cannot exhaust host memory with a
    /// single stdout line.</summary>
    internal const int MaxFrameBytes = 1024 * 1024;
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

    /// <summary>
    /// Declared plugin→host capability calls. Anything not handled here keeps the
    /// v1 policy: rejected with -32601. The handler never writes lifecycle state;
    /// it only marshals a capability call and is owned by the activation adapter.
    /// </summary>
    public Func<string, JsonNode?, JsonNode?>? RequestHandler { get; set; }

    private ProcessPluginClient(SystemProcess process, StreamWriter writer, StreamReader reader, ConcurrentQueue<string> stderr)
    {
        _process = process;
        _writer = writer;
        _reader = reader;
        _stderr = stderr;
    }

    public int ProcessId => _process.Id;

    public bool HasExited => SafeHasExited(_process);

    private static bool SafeHasExited(SystemProcess process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Covers "no process is associated" and a disposed handle: gone.
            return true;
        }
    }

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
            var answer = await ReadLineBoundedAsync(reader, timeout.Token);
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
        ObjectDisposedException.ThrowIf(SafeHasExited(_process), this);
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
        catch (InvalidDataException)
        {
            // A protocol frame violation ends the session deterministically:
            // kill the child instead of leaving a blocked writer behind.
            await TerminateAsync(_process);
            throw;
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

        var requestText = request.ToJsonString();
        if (Encoding.UTF8.GetByteCount(requestText) > MaxFrameBytes)
        {
            throw new InvalidDataException($"request frame exceeds {MaxFrameBytes} bytes");
        }

        await WriteAsync(requestText, cancellationToken);
        while (true)
        {
            var line = await ReadLineBoundedAsync(_reader, cancellationToken);

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

            // Plugin -> host method: only explicitly declared capability calls
            // are routed; everything else keeps the -32601 policy.
            if (response["method"] is not null && response["result"] is null && response["error"] is null)
            {
                var requestId = response["id"]?.DeepClone() ?? 0;
                JsonObject reply;
                try
                {
                    if (RequestHandler is null)
                    {
                        throw new NotSupportedException("plugin-to-host calls are not supported");
                    }

                    var requestMethod = response["method"]!.GetValue<string>();
                    var result = RequestHandler(requestMethod, response["params"]);
                    reply = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = requestId,
                        ["result"] = result?.DeepClone() ?? (JsonNode?)null,
                    };
                }
                catch (NotSupportedException ex)
                {
                    reply = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = requestId,
                        ["error"] = new JsonObject
                        {
                            ["code"] = -32601,
                            ["message"] = ex.Message,
                        },
                    };
                }
                catch (Exception ex)
                {
                    // Every plugin→host request gets an answer: an unhandled
                    // handler failure becomes -32603 instead of leaving the
                    // child waiting for a reply the host never sends.
                    reply = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = requestId,
                        ["error"] = new JsonObject
                        {
                            ["code"] = -32603,
                            ["message"] = Truncate(ex.Message),
                        },
                    };
                }

                await WriteAsync(reply.ToJsonString(), cancellationToken);
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
        if (SafeHasExited(_process))
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

    internal static string Truncate(string message, int maxLength = 2000) =>
        message.Length <= maxLength ? message : message[..maxLength];

    /// <summary>
    /// Reads one UTF-8 line with a hard byte ceiling. StreamReader has no line
    /// length limit, so a hostile plugin could otherwise stream one unbounded
    /// line and exhaust host memory.
    /// </summary>
    private static async Task<string?> ReadLineBoundedAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var character = buffer[0];
            if (character == '\n')
            {
                return builder.ToString();
            }

            if (character != '\r')
            {
                builder.Append(character);
                // Character count is the cheap upper-bound proxy: UTF-8 JSON-RPC
                // frames are overwhelmingly ASCII, and a multibyte payload is
                // capped 4x stricter (bytes >= chars).
                if (builder.Length > MaxFrameBytes)
                {
                    throw new InvalidDataException(
                        $"plugin frame exceeds {MaxFrameBytes} characters");
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
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
