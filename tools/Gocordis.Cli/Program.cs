using System.Text.Json;
using System.Text.Json.Serialization;
using Gocordis.Packaging;

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
                "plugin" => await PluginAsync(args[1..]),
                "app" => await AppAsync(args[1..]),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, Json));
            return 1;
        }
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
        if (args.Length == 0 || args[0] != "validate")
        {
            return Fail("usage: app validate <manifest> [--store] [--host-version]");
        }

        var rest = args[1..];
        var path = Positional(rest, 0);
        if (path is null)
        {
            return Fail("usage: app validate <manifest> [--store] [--host-version]");
        }

        var storePath = Option(rest, "--store") ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gocordis", "plugins");
        var hostVersion = Option(rest, "--host-version") ?? "1.0.0";
        var app = await ApplicationManifest.LoadAsync(path);
        var store = new PluginStore(storePath, hostVersion);
        var resolved = new List<object>();
        foreach (var plugin in app.Plugins)
        {
            var installed = await store.ResolveAsync(plugin.Id, plugin.VersionRange)
                            ?? throw new MissingPluginDependencyException(
                                $"'{plugin.Id}' {plugin.VersionRange} is not installed");
            resolved.Add(new { installed.Id, installed.Version, plugin.Enabled });
        }

        Write(new { app.Id, app.Version, app.HostVersion, resolved });
        return 0;
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
            "--store", "--host-version", "--id", "--version", "--type", "--host-range", "--source",
        };
        var values = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (options.Contains(args[i]))
            {
                i++; // skip option value
                continue;
            }

            if (args[i].Contains('=', StringComparison.Ordinal) && args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(args[i]);
        }

        return index < values.Count ? values[index] : null;
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() => Console.Error.WriteLine("""
        gocordis plugin pack <sourceDir> <output> [--id] [--version] [--type] [--host-range]
        gocordis plugin install|upgrade <package> [--store] [--host-version]
        gocordis plugin uninstall <id> [--version] [--store] [--host-version]
        gocordis plugin list [--store] [--host-version]
        gocordis plugin resolve <id> <range> [--source] [--host-version]
        gocordis app validate <manifest> [--store] [--host-version]
        """);
}
