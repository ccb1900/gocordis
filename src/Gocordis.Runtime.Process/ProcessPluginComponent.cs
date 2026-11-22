using System.Text.Json.Nodes;
using Gocordis.Core;

namespace Gocordis.Runtime.Process;

/// <summary>
/// Process backend as a normal Core component: activation spawns and handshakes,
/// the effect inverse shuts the process down. Failure degrades the fiber instead
/// of taking down the host; there is no automatic restart.
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

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var diagnostics = new ProcessDiagnostics();
        context.Provide(ProcessKeys.Diagnostics, diagnostics);
        ProcessPluginClient? client = null;
        try
        {
            client = await ProcessPluginClient.StartAsync(_executable);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await client.CallAsync("echo", new { text = "ready" }, timeout.Token);
            context.Provide(_key, result?["text"]?.GetValue<string>() ?? "unavailable");
        }
        catch (Exception ex)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            // Degrade instead of failing the fiber: the declaration remains
            // active and consumers can observe the error vocabulary.
            diagnostics.LastError = ex.Message;
            context.Provide(_key, "plugin_error");
            return null;
        }

        return async () => await client.DisposeAsync();
    }
}
