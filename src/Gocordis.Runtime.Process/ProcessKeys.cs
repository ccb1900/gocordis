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
