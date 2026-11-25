using Gocordis.Builtin.WebConsole;
using Gocordis.Core;
using Gocordis.Platform;
using Gocordis.Plugins;
using Industrial.Elsa;
using Industrial.Plugins;

var root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "gocordis-industrial");
var address = args.Length > 1 ? args[1] : "http://127.0.0.1:0";
Directory.CreateDirectory(root);

await using var runtime = new Gocordis.Core.Runtime();
var console = new ConsoleHost(runtime);
await using var registry = new PluginRegistry(runtime);
console.AttachRegistry(registry);
registry.ExternalCapabilities.Add(UiContributionRegistry.Key);
registry.ExternalCapabilities.Add(ApiGateway.Key);
await runtime.LoadAsync(new ConsoleCapabilitiesComponent(console));

registry.RegisterComponentFactory("industrial.resource", manifest =>
    new IndustrialResourceComponent(manifest.Config));
registry.RegisterComponentFactory("industrial.credential", manifest =>
    new IndustrialCredentialComponent(manifest.Config));
registry.RegisterComponentFactory("industrial.workflow", manifest =>
    new IndustrialWorkflowComponent(manifest.Config));
registry.RegisterComponentFactory("industrial.execution", manifest =>
    new IndustrialExecutionComponent(manifest.Config));
registry.RegisterComponentFactory("industrial.collection", manifest =>
    new IndustrialCollectionComponent(manifest.Config));
registry.RegisterComponentFactory("industrial.nodes", _ => new IndustrialNodesComponent());
registry.RegisterComponentFactory("industrial.ui", _ => new IndustrialUiComponent());
registry.RegisterComponentFactory("industrial.clientmodule", _ => new IndustrialClientModuleComponent());
registry.RegisterComponentFactory("industrial.elsa", _ => new IndustrialElsaComponent());

PluginManifest Declaration(string id, IReadOnlyDictionary<string, object?>? config = null) =>
    new(id, id, true, config ?? new Dictionary<string, object?>(), [], []);

await registry.ApplyAsync([
    Declaration("industrial.resource", new Dictionary<string, object?>
    {
        ["root"] = root,
        ["seedResource"] = "true",
        ["sourceRoot"] = Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_SOURCE_ROOT")
                         ?? Path.Combine(root, "source"),
    }),
    Declaration("industrial.credential", new Dictionary<string, object?>
    {
        ["root"] = root,
        ["allowDevelopmentKey"] =
            Environment.GetEnvironmentVariable("GOCORDIS_INDUSTRIAL_ALLOW_DEV_KEY") ?? "false",
    }),
    Declaration("industrial.workflow", new Dictionary<string, object?> { ["root"] = root }),
    Declaration("industrial.execution", new Dictionary<string, object?> { ["root"] = root }),
    Declaration("industrial.collection", new Dictionary<string, object?> { ["root"] = root }),
    Declaration("industrial.nodes"),
    Declaration("industrial.elsa"),
    Declaration("industrial.ui"),
    Declaration("industrial.clientmodule"),
]);

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
