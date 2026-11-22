using Gocordis.Console.WebUi;
using KoishiCase;

namespace PlatformHost;

/// <summary>
/// Composition root for the browser-facing console. KoishiHost already owns the
/// single Core Runtime and its PluginRegistry; BrowserConsoleApp contributes the
/// UI page, and WebUiServer is only the HTTP transport adapter. Every lifetime is
/// still owned by those Core runtimes - this file only wires them together and
/// turns Ctrl+C into orderly disposal.
/// </summary>
internal static class Program
{
    private const string DefaultAddress = "http://127.0.0.1:0";

    private static async Task<int> Main(string[] args)
    {
        var (options, parseError) = Options.Parse(args);
        if (parseError is not null || options is null)
        {
            Console.Error.WriteLine($"PlatformHost: {parseError}");
            Options.WriteUsage();
            return 2;
        }

        if (options.Help)
        {
            Options.WriteUsage();
            return 0;
        }

        // Fail fast on an unsafe bind before any runtime is loaded.
        try
        {
            ListenGuard.Validate(options.Address, options.AllowRemote);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"PlatformHost: invalid --address: {ex.Message}");
            return 2;
        }

        var host = new KoishiHost();
        var browser = new BrowserConsoleApp();
        WebUiServer? ui = null;
        var uiStarted = false;

        try
        {
            await host.StartAsync();
            await browser.StartAsync();

            ui = new WebUiServer(
                host: browser.Console,
                address: options.Address,
                allowRemote: options.AllowRemote,
                registry: host.Registry);
            await ui.StartAsync();
            uiStarted = true;

            Console.WriteLine($"Gocordis PlatformHost UI: {ui.Address}");
            Console.WriteLine(options.AllowRemote
                ? "Security: remote listening is enabled - put the UI behind a trusted network or reverse proxy."
                : "Security: loopback only. Pass --allow-remote to expose the UI to other hosts.");
            Console.WriteLine("Press Ctrl+C to stop.");

            await WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PlatformHost: {ex.Message}");
            return 1;
        }
        finally
        {
            // LIFO teardown: transport, then UI contribution app, then plugins/runtime.
            if (ui is not null)
            {
                if (uiStarted)
                {
                    await ui.StopAsync();
                }

                await ui.DisposeAsync();
            }

            await browser.DisposeAsync();
            await host.DisposeAsync();
        }

        return 0;
    }

    private static async Task WaitForShutdownAsync()
    {
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, signal) =>
        {
            signal.Cancel = true;
            shutdown.Cancel();
        };

        Console.CancelKeyPress += handler;
        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Ctrl+C: fall through to the orderly teardown in finally.
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private sealed record Options(string Address, bool AllowRemote, bool Help)
    {
        public static (Options? Options, string? Error) Parse(string[] args)
        {
            var address = DefaultAddress;
            var allowRemote = false;
            var help = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--address":
                        if (++i >= args.Length || args[i].Length == 0 ||
                            args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            return (null, "--address requires a value");
                        }

                        address = args[i];
                        break;
                    case var arg when arg.StartsWith("--address=", StringComparison.Ordinal):
                        address = arg["--address=".Length..];
                        break;
                    case "--allow-remote":
                        allowRemote = true;
                        break;
                    case "--help" or "-h":
                        help = true;
                        break;
                    default:
                        return (null, $"unknown option '{args[i]}'");
                }
            }

            return (new Options(address, allowRemote, help), null);
        }

        public static void WriteUsage()
        {
            Console.WriteLine("Usage: PlatformHost [--address <url>] [--allow-remote] [--help]");
            Console.WriteLine();
            Console.WriteLine($"  --address <url>  UI listen address (default: {DefaultAddress}; port 0 picks a free port)");
            Console.WriteLine("  --allow-remote   allow a non-loopback/wildcard bind (default: false)");
            Console.WriteLine("  --help, -h       show this help");
        }
    }
}
