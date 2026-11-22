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

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var client = await ProcessPluginClient.StartAsync(_executable);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await client.CallAsync("echo", new { text = "ready" }, timeout.Token);
            var text = result?["text"]?.GetValue<string>() ?? "unavailable";
            context.Provide(_key, text);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        return async () => await client.DisposeAsync();
    }
}
