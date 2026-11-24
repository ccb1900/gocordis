using Gocordis.Platform;
using Gocordis.UI.Hosts;

var manifest = args.Length > 0 ? args[0] : throw new ArgumentException("manifest path required");
var store = args.Length > 1 ? args[1] : throw new ArgumentException("store path required");
var address = args.Length > 2 ? args[2] : "http://127.0.0.1:0";

// No raw factories: every industrial capability arrives through a real
// .gocordis-plugin package and the host's single binder/composition root.
await using var app = new PlatformApplication(new PlatformHostOptions(
    manifest, store, Address: address, Host: UiHostKind.Web, HostVersion: "1.0.0"));
await app.StartAsync();
Console.WriteLine($"Packaged industrial console: {app.Address}");
Console.WriteLine("Press Ctrl+C to stop.");
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try { await Task.Delay(Timeout.Infinite, shutdown.Token); }
catch (OperationCanceledException) { }
