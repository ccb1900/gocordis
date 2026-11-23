using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.UI.Hosts;

return await PlatformRunner.RunAsync(args);

namespace Gocordis.Platform
{

/// <summary>Optional presentation hooks; the composition path stays in Platform.</summary>
public sealed class PlatformRunHooks
{
    /// <summary>Called after StartAsync, before any wait or shutdown.</summary>
    public Action<PlatformApplication>? AfterStart { get; init; }

    /// <summary>Overrides the default plain-text stderr rendering of a failure.</summary>
    public Func<Exception, string>? FormatError { get; init; }

    /// <summary>Suppresses the product banner; callers may print their own output.</summary>
    public bool WriteStartupBanner { get; init; } = true;

    /// <summary>--validate-only or an embedding caller that exits after startup.</summary>
    public bool WaitForShutdown { get; init; } = true;
}

public static class PlatformRunner
{
    public const string UsageText = """
        gocordis-platform [--manifest <file>] [--store <dir>] [--address <url>]
                          [--allow-remote] [--host web]
                          [--host-version <semver>] [--allowed-host <host> ...]
                          [--trust development|trusted-only] [--public-key <pem> ...]
                          [--validate-only]

        Web is the only runnable UI host. WinForms/Avalonia are render-plan
        profiles for future native hosts; they are not CLI host options.
        --trust/--public-key configure the same installer policy used by Console uploads.
        --allow-remote only opens the listener; remote writes still require a
        host-supplied ConsoleAccessPolicy (programmatic composition root).
        --validate-only reconciles and activates every declaration, then exits without opening a transport.
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
            return await RunAsync(ParseOptions(args), null);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"gocordis-platform: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> RunAsync(PlatformHostOptions options, PlatformRunHooks? hooks = null)
    {
        try
        {
            await using var app = new PlatformApplication(options);
            await app.StartAsync();
            hooks?.AfterStart?.Invoke(app);

            if (options.ValidateOnly || hooks?.WaitForShutdown == false)
            {
                // StartAsync already completed every declaration and effect;
                // stop here so validation never leaves a listener or fiber.
                await app.StopAsync();
                return 0;
            }

            if (hooks?.WriteStartupBanner ?? true)
            {
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
            }

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
            System.Console.Error.WriteLine(hooks?.FormatError?.Invoke(ex) ?? $"gocordis-platform: {ex.Message}");
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
        var publicKeys = new List<string>();
        var validateOnly = false;
        string? trust = null;

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
                case "--validate-only":
                    validateOnly = true;
                    break;
                case "--host-version":
                    hostVersion = Value(args, ref i);
                    break;
                case "--allowed-host":
                    allowedHosts.Add(Value(args, ref i));
                    break;
                case "--trust":
                    trust = Value(args, ref i);
                    break;
                case "--public-key":
                    publicKeys.Add(Value(args, ref i));
                    break;
                case "--host":
                    var value = Value(args, ref i);
                    if (!string.Equals(value, "web", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException(
                            $"host '{value}' is not runnable; web is the only product UI host. " +
                            "WinForms/Avalonia are render-plan profiles for future native hosts.");
                    }

                    host = UiHostKind.Web;
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        return new PlatformHostOptions(
            manifest, store, address, allowRemote, host, hostVersion, allowedHosts, validateOnly,
            PluginTrustPolicy.FromPemFiles(trust, publicKeys));
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
