using Gocordis.Platform;
using Gocordis.UI.Hosts;

return await PlatformRunner.RunAsync(args);

namespace Gocordis.Platform
{

public static class PlatformRunner
{
    public const string UsageText = """
        gocordis-platform [--manifest <file>] [--store <dir>] [--address <url>]
                          [--allow-remote] [--host web|winforms|avalonia]
                          [--host-version <semver>] [--allowed-host <host> ...]

        winforms/avalonia are render-plan profiles only: no native UI window and no web console.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var options = ParseOptions(args);
            await using var app = new PlatformApplication(options);
            await app.StartAsync();

            if (options.Host == UiHostKind.Web)
            {
                System.Console.WriteLine(
                    $"Gocordis Platform [web] started: manifest={options.ManifestPath}, store={options.StorePath}");
                if (app.Address is not null)
                {
                    System.Console.WriteLine($"Web console: {app.Address}");
                }
            }
            else
            {
                System.Console.WriteLine(
                    $"Gocordis Platform [{options.Host}] backend started " +
                    "(render-plan profile only; no native UI window and no web console)");
            }

            System.Console.WriteLine("Press Ctrl+C to stop.");

            using var shutdown = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) =>
            {
                e.Cancel = true;
                shutdown.Cancel();
            };
            System.Console.CancelKeyPress += handler;
            try
            {
                await Task.Delay(Timeout.Infinite, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                // graceful stop
            }
            finally
            {
                System.Console.CancelKeyPress -= handler;
            }

            await app.StopAsync();
            return 0;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"gocordis-platform: {ex.Message}");
            return 1;
        }
    }

    private static PlatformHostOptions ParseOptions(string[] args)
    {
        var manifest = "gocordis.app.json";
        var store = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gocordis", "plugins");
        var address = "http://127.0.0.1:0";
        var allowRemote = false;
        var host = UiHostKind.Web;
        var hostVersion = "1.0.0";
        var allowedHosts = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest":
                    manifest = Value(args, ref i);
                    break;
                case "--store":
                    store = Value(args, ref i);
                    break;
                case "--address":
                    address = Value(args, ref i);
                    break;
                case "--allow-remote":
                    allowRemote = true;
                    break;
                case "--host-version":
                    hostVersion = Value(args, ref i);
                    break;
                case "--allowed-host":
                    allowedHosts.Add(Value(args, ref i));
                    break;
                case "--host":
                    var value = Value(args, ref i);
                    if (!Enum.TryParse<UiHostKind>(value, ignoreCase: true, out host))
                    {
                        throw new ArgumentException(
                            $"unknown host '{value}'; use web|winforms|avalonia " +
                            "(winforms/avalonia are render-plan profiles, not native UI hosts)");
                    }

                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        return new PlatformHostOptions(manifest, store, address, allowRemote, host, hostVersion, allowedHosts);
    }

    private static string Value(string[] args, ref int index)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"missing value after '{args[index - 1]}'");
        }

        return args[index];
    }

    private static void PrintUsage() => System.Console.Error.WriteLine(UsageText);
}
}
