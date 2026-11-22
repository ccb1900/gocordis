using HttpdHost;

var address = args.Length > 0 ? args[0] : "http://127.0.0.1:8080";

await using var host = new HttpdProbeHost(address);
await host.StartAsync();

Console.WriteLine($"[httpd] serving GET {host.Address}/hello -> {{\"message\":\"hello\"}}");
Console.WriteLine("Press Ctrl+C to stop.");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, signal) =>
{
    signal.Cancel = true;
    shutdown.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    // Orderly teardown: StopAsync unwinds the listener before DisposeAsync.
}

await host.StopAsync();
