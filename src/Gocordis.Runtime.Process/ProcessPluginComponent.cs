using Gocordis.Core;
using Gocordis.Runtime.Contracts;

namespace Gocordis.Runtime.Process;

/// <summary>
/// Process backend as a normal Core component: the unified protocol driver
/// starts the child at activation and installs its reclamation as a Core
/// effect. Failure degrades the fiber instead of taking down the host; there
/// is no automatic restart.
/// </summary>
public sealed class ProcessPluginComponent : Component
{
    private readonly string _executable;
    private readonly Key<string> _key;
    private readonly Key<ProcessDiagnostics> _diagnosticsKey;
    private readonly Key<string> _errorKey;

    public ProcessPluginComponent(
        string executable,
        Key<string> key,
        Key<ProcessDiagnostics>? diagnosticsKey = null,
        Key<string>? errorKey = null)
    {
        _executable = executable;
        _key = key;
        _diagnosticsKey = diagnosticsKey ?? ProcessKeys.Diagnostics;
        _errorKey = errorKey ?? ProcessKeys.ErrorVocabulary;
    }

    public override string Name => "process-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [_key, _diagnosticsKey, _errorKey];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        BackendProtocol.RunAsync(
            new ProcessBackend(_executable, _key, _diagnosticsKey, _errorKey), context);
}
