using System.Text.Json;
using System.Text.Json.Serialization;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Plugins;

namespace Gocordis.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            return args[0] switch
            {
                "create" => await CreateAsync(args[1..]),
                "plugin" => await PluginAsync(args[1..]),
                "app" => await AppAsync(args[1..]),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, Json));
            return 1;
        }
    }

    private static async Task<int> CreateAsync(string[] args)
    {
        var output = Positional(args, 0);
        if (output is null)
        {
            return Fail("usage: create <outputDir> [--id] [--version] [--host-version]");
        }

        var id = Option(args, "--id") ?? new DirectoryInfo(output).Name;
        var version = Option(args, "--version") ?? "1.0.0";
        var hostVersion = Option(args, "--host-version") ?? "1.0.0";
        var root = FindRepoRoot();
        Directory.CreateDirectory(output);

        var manifest = new ApplicationManifest(id, version, hostVersion, []);
        await manifest.SaveAsync(Path.Combine(output, "gocordis.app.json"));

        var project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{root}}/src/Gocordis.Core/Gocordis.Core.csproj" />
                <ProjectReference Include="{{root}}/src/Gocordis.Plugins/Gocordis.Plugins.csproj" />
                <ProjectReference Include="{{root}}/src/Gocordis.Packaging/Gocordis.Packaging.csproj" />
                <ProjectReference Include="{{root}}/src/Gocordis.Console.WebUi/Gocordis.Console.WebUi.csproj" />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(output, id + ".csproj"), project);

        var program = $$"""
            using Gocordis.Console.WebUi;
            using Gocordis.Plugins;

            var runtime = new Gocordis.Core.Runtime();
            var host = new ConsoleHost(runtime);
            var registry = new PluginRegistry(runtime);
            var store = new Gocordis.Packaging.PluginStore(
                Path.Combine(AppContext.BaseDirectory, "plugins"), "{{hostVersion}}");
            var loader = new Gocordis.Packaging.ApplicationLoader(store, registry);
            await loader.ApplyAsync(await Gocordis.Packaging.ApplicationManifest.LoadAsync("gocordis.app.json"));

            await using var ui = new WebUiServer(host, registry: registry);
            await ui.StartAsync();
            System.Console.WriteLine($"Gocordis app '{{id}}': {ui.Address}");
            await Task.Delay(Timeout.Infinite);
            """;
        await File.WriteAllTextAsync(Path.Combine(output, "Program.cs"), program);

        Write(new { created = output, id, version, hostVersion });
        return 0;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Gocordis.sln")))
            {
                return current.FullName.Replace('\\', '/');
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("cannot locate Gocordis.sln above the CLI assembly");
    }

    private static async Task<int> PluginAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("plugin requires a subcommand");
        }

        var command = args[0];
        var rest = args[1..];
        var storePath = Option(rest, "--store") ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gocordis", "plugins");
        var hostVersion = Option(rest, "--host-version") ?? "1.0.0";
        var store = new PluginStore(storePath, hostVersion);

        switch (command)
        {
            case "pack":
            {
                var source = Positional(rest, 0);
                var output = Positional(rest, 1);
                if (source is null || output is null)
                {
                    return Fail("usage: plugin pack <sourceDir> <output.gocordis-plugin>");
                }

                var manifest = new PluginPackageManifest(
                    Option(rest, "--id") ?? new DirectoryInfo(source).Name,
                    Option(rest, "--version") ?? "1.0.0",
                    Option(rest, "--type") ?? Option(rest, "--id") ?? new DirectoryInfo(source).Name,
                    Option(rest, "--host-range") ?? "[1.0,)");
                var written = await PluginPackageWriter.CreateAsync(source, manifest, output);
                Write(new { packed = output, written.Id, written.Version, written.Checksum });
                return 0;
            }
            case "install":
            case "upgrade":
            {
                var package = Positional(rest, 0);
                if (package is null)
                {
                    return Fail($"usage: plugin {command} <package> [--store] [--host-version]");
                }

                var result = command == "install"
                    ? await store.InstallAsync(package)
                    : await store.UpgradeAsync(package);
                Write(new { result.Changes });
                return 0;
            }
            case "uninstall":
            {
                var id = Positional(rest, 0);
                if (id is null)
                {
                    return Fail("usage: plugin uninstall <id> [--version] [--store] [--host-version]");
                }

                var result = await store.UninstallAsync(id, Option(rest, "--version"));
                Write(new { result.Changes });
                return 0;
            }
            case "list":
            {
                var plugins = await store.ListAsync();
                Write(new { plugins = plugins.Select(p => new { p.Id, p.Version, p.InstallPath }) });
                return 0;
            }
            case "resolve":
            {
                var id = Positional(rest, 0);
                var range = Positional(rest, 1);
                if (id is null || range is null)
                {
                    return Fail("usage: plugin resolve <id> <range> [--source] [--host-version]");
                }

                var sourcePath = Option(rest, "--source");
                var resolved = sourcePath is null
                    ? await store.ResolveAsync(id, range)
                    : Parse(new InstalledPlugin(
                        (await PluginPackageReader.ReadAsync(
                            await new LocalDirectoryPackageSource(sourcePath, hostVersion).ResolveAsync(id, range),
                            hostVersion)).Manifest, string.Empty));
                Write(resolved is null ? new { resolved = false } : new
                {
                    resolved = true,
                    resolved.Id,
                    resolved.Version,
                    path = sourcePath is null ? resolved.InstallPath : Option(rest, "--source"),
                });
                return 0;
            }
            default:
                return Fail($"unknown plugin command '{command}'");
        }
    }

    private static async Task<int> AppAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("app requires a subcommand");
        }

        var command = args[0];
        var rest = args[1..];
        return command switch
        {
            "validate" => await AppValidateAsync(rest),
            "enable" => await AppToggleAsync(rest, enabled: true),
            "disable" => await AppToggleAsync(rest, enabled: false),
            "run" => await AppRunAsync(rest),
            _ => Fail($"unknown app command '{command}'"),
        };
    }

    private static async Task<int> AppToggleAsync(string[] args, bool enabled)
    {
        var path = Positional(args, 0);
        var pluginId = Positional(args, 1);
        if (path is null || pluginId is null)
        {
            return Fail($"usage: app {(enabled ? "enable" : "disable")} <manifest> <pluginId> [--store] [--host-version]");
        }

        var app = await ApplicationManifest.LoadAsync(path);
        if (!app.Plugins.Any(plugin => string.Equals(plugin.Id, pluginId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"plugin '{pluginId}' is not declared in application manifest '{path}'");
        }

        var updated = app with
        {
            Plugins = app.Plugins
                .Select(plugin => string.Equals(plugin.Id, pluginId, StringComparison.Ordinal)
                    ? plugin with { Enabled = enabled }
                    : plugin)
                .ToArray(),
        };
        await updated.SaveAsync(path); // Atomic temp-file replace from ApplicationManifest.
        Write(new { id = pluginId, enabled });
        return 0;
    }

    private static async Task<int> AppValidateAsync(string[] args)
    {
        var path = Positional(args, 0);
        if (path is null)
        {
            return Fail("usage: app validate <manifest> [--store] [--host-version]");
        }

        var (app, resolved) = await LoadAndResolveAsync(path, StorePath(args), HostVersion(args));
        WriteResolution(app, resolved);
        return 0;
    }

    private static async Task<int> AppRunAsync(string[] args)
    {
        var path = Positional(args, 0);
        if (path is null)
        {
            return Fail("usage: app run <manifest> [--store] [--host-version] [--validate-only] [--address]");
        }

        var storePath = StorePath(args);
        var hostVersion = HostVersion(args);
        var address = Option(args, "--address") ?? "http://127.0.0.1:0";
        var validateOnly = args.Contains("--validate-only", StringComparer.Ordinal);
        var (app, resolved) = await LoadAndResolveAsync(path, storePath, hostVersion);

        var runtime = new Runtime();
        var host = new ConsoleHost(runtime);
        var registry = new PluginRegistry(runtime);
        WebUiServer? ui = null;
        var uiStarted = false;
        try
        {
            // Packages stay platform-neutral: the product host declares a generic
            // adapter per resolved type instead of executing arbitrary package code.
            // Declaration, resolution and activation still run through Core.
            foreach (var type in resolved.Select(item => item.Type).Distinct(StringComparer.Ordinal))
            {
                registry.RegisterFactory(type, _ => new ProductPluginComponent(type));
            }

            await new ApplicationLoader(new PluginStore(storePath, hostVersion), registry).ApplyAsync(app);
            WriteResolution(app, resolved);
            if (validateOnly)
            {
                return 0;
            }

            ui = new WebUiServer(host, address, allowRemote: false, registry: registry);
            await ui.StartAsync();
            uiStarted = true;
            System.Console.WriteLine($"Gocordis app '{app.Id}': {ui.Address}");
            System.Console.WriteLine("Press Ctrl+C to stop.");
            await WaitForShutdownAsync();
            return 0;
        }
        finally
        {
            // LIFO teardown: transport, then declarations/fibers, then the runtime.
            if (ui is not null)
            {
                if (uiStarted)
                {
                    await ui.StopAsync();
                }

                await ui.DisposeAsync();
            }

            await registry.DisposeAsync();
            await runtime.DisposeAsync();
        }
    }

    private static async Task<(ApplicationManifest App, IReadOnlyList<ResolvedPlugin> Resolved)> LoadAndResolveAsync(
        string path, string storePath, string hostVersion)
    {
        var app = await ApplicationManifest.LoadAsync(path);
        var store = new PluginStore(storePath, hostVersion);
        var resolved = new List<ResolvedPlugin>(app.Plugins.Count);
        foreach (var plugin in app.Plugins)
        {
            var installed = await store.ResolveAsync(plugin.Id, plugin.VersionRange)
                            ?? throw new MissingPluginDependencyException(
                                $"'{plugin.Id}' {plugin.VersionRange} is not installed");
            resolved.Add(new ResolvedPlugin(plugin.Id, installed.Version, installed.Manifest.Type, plugin.Enabled));
        }

        return (app, resolved);
    }

    private static void WriteResolution(ApplicationManifest app, IReadOnlyList<ResolvedPlugin> resolved) =>
        Write(new
        {
            app.Id,
            app.Version,
            app.HostVersion,
            resolved = resolved.Select(item => new { item.Id, item.Version, item.Enabled }),
        });

    private static string StorePath(string[] args) =>
        Option(args, "--store") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gocordis", "plugins");

    private static string HostVersion(string[] args) => Option(args, "--host-version") ?? "1.0.0";

    private static async Task WaitForShutdownAsync()
    {
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, signal) =>
        {
            signal.Cancel = true;
            shutdown.Cancel();
        };

        System.Console.CancelKeyPress += handler;
        try
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Ctrl+C: orderly teardown happens in the caller's finally block.
        }
        finally
        {
            System.Console.CancelKeyPress -= handler;
        }
    }

    private sealed record ResolvedPlugin(string Id, string Version, string Type, bool Enabled);

    /// <summary>
    /// Declaration-only adapter used by the product CLI. Package payloads are not
    /// executed here (backend runtimes own that); activation still goes through
    /// Core so manifest errors, fibers and enable/disable are real.
    /// </summary>
    private sealed class ProductPluginComponent : Component
    {
        private readonly Key<string> _key = new($"product-plugin-{Guid.NewGuid():N}");
        private readonly string _type;

        public ProductPluginComponent(string type) => _type = type;

        public override string Name => $"product-plugin:{_type}";

        public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

        public override ValueTask<Cleanup?> ApplyAsync(Context context)
        {
            context.Provide(_key, _type);
            return ValueTask.FromResult<Cleanup?>(null);
        }
    }

    private static InstalledPlugin Parse(InstalledPlugin value) => value;

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    }

    private static string? Positional(string[] args, int index)
    {
        var options = new HashSet<string>(StringComparer.Ordinal)
        {
            "--store", "--host-version", "--address", "--id", "--version", "--type", "--host-range", "--source",
        };
        var flags = new HashSet<string>(StringComparer.Ordinal)
        {
            "--validate-only",
        };
        var values = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (options.Contains(args[i]))
            {
                i++; // skip option value
                continue;
            }

            if (flags.Contains(args[i]))
            {
                continue; // boolean switch, consumes no value
            }

            if (args[i].Contains('=', StringComparison.Ordinal) && args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(args[i]);
        }

        return index < values.Count ? values[index] : null;
    }

    private static void Write(object value) => System.Console.WriteLine(JsonSerializer.Serialize(value, Json));

    private static int Fail(string message)
    {
        System.Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() => System.Console.Error.WriteLine("""
        gocordis create <outputDir> [--id] [--version] [--host-version]
        gocordis plugin pack <sourceDir> <output> [--id] [--version] [--type] [--host-range]
        gocordis plugin install|upgrade <package> [--store] [--host-version]
        gocordis plugin uninstall <id> [--version] [--store] [--host-version]
        gocordis plugin list [--store] [--host-version]
        gocordis plugin resolve <id> <range> [--source] [--host-version]
        gocordis app validate <manifest> [--store] [--host-version]
        gocordis app enable|disable <manifest> <pluginId> [--store] [--host-version]
        gocordis app run <manifest> [--store] [--host-version] [--validate-only] [--address]
        """);
}
