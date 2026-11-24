using Koishi.ConsoleApp;

var server = args.Length > 0 ? args[0] : "http://127.0.0.1:8170";
var listen = args.Length > 1 ? args[1] : "http://127.0.0.1:8171";

await using var app = new KoishiConsoleApp();
await app.StartAsync(server, listen);
Console.WriteLine($"Koishi console (independent Runtime) listening on {listen}; server={server}");
Console.WriteLine("Press Ctrl+C to stop.");
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
}
