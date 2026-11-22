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

    public ProcessPluginComponent(string executable, Key<string> key)
    {
        _executable = executable;
        _key = key;
    }

    public override string Name => "process-plugin";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [_key, ProcessKeys.Diagnostics, ProcessKeys.ErrorVocabulary];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        BackendProtocol.RunAsync(new ProcessBackend(_executable, _key), context);
}
