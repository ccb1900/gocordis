using Gocordis.Core;

namespace Gocordis.Runtime.Process;

/// <summary>Degrade vocabulary: a failed backend stays declared and active.</summary>
public sealed class ProcessDiagnostics
{
    public string? LastError { get; internal set; }

    public bool Degraded => LastError is not null;
}

public static class ProcessKeys
{
    public static readonly Key<ProcessDiagnostics> Diagnostics = new("process-diagnostics");
    public static readonly Key<string> ErrorVocabulary = new("plugin_error");
}

/// <summary>
/// Explicit error-propagation policy for the process backend. Timeouts become
/// TimeoutException (with the child process already reaped), JSON-RPC errors
/// become InvalidOperationException, and activation errors degrade to the
/// "plugin_error" vocabulary rather than failing the fiber. No automatic
/// restart is attempted anywhere in this chain.
/// </summary>
public static class ProcessErrorPolicy
{
    public const string ErrorVocabulary = "plugin_error";
    public const string HandshakePhrase = "handshake timed out";
    public const string CallPhrase = "timed out";
    public const string RpcErrorPhrase = "RPC error";

    public static TimeoutException HandshakeTimeout(TimeSpan limit, Exception inner) =>
        new($"plugin {HandshakePhrase} after {limit}", inner);

    public static TimeoutException CallTimeout(string method, TimeSpan limit, Exception inner) =>
        new($"plugin call '{method}' {CallPhrase} after {limit}", inner);

    public static InvalidOperationException RpcError(string? message) =>
        new($"{RpcErrorPhrase}: {message ?? "unknown"}");
}
