using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Extensions;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Host;

var root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "gocordis-industrial");
var address = args.Length > 1 ? args[1] : "http://127.0.0.1:0";
Directory.CreateDirectory(root);

await using var runtime = new Gocordis.Core.Runtime();
var console = new ConsoleHost(runtime);
await using var registry = new PluginRegistry(runtime);
console.AttachRegistry(registry);
registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
registry.ExternalCapabilities.Add(ApiGateway.Key);
registry.ExternalCapabilities.Add(TimerKeys.Service);
await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));
await runtime.LoadAsync(new TimerExtensionComponent());

var config = new Dictionary<string, object?>
{
    ["root"] = root,
    ["allowDevelopmentKey"] =
        Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY") ?? "false",
    ["seedResource"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SEED_RESOURCE") ?? "false",
    ["sourceRoot"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SOURCE_ROOT")
                     ?? Path.Combine(root, "source"),
    ["resourceId"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_RESOURCE") ?? "source-1",
};
IndustrialComponents.RegisterFactories(registry, config);

await registry.ApplyAsync(IndustrialComponents.Types
    .Select(type => IndustrialComponents.Declaration(type, config)).ToArray());

await using var server = new WebUiServer(console, address, registry: registry);
await server.StartAsync();
Console.WriteLine($"Industrial console listening on {server.Address} (root={root})");
Console.WriteLine("Press Ctrl+C to stop.");
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };
try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
}
