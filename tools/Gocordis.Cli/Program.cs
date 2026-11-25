using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Gocordis.Client;
using Gocordis.Console.WebUi;
using Gocordis.Core;
using Gocordis.Packaging;
using Gocordis.Platform;
using Gocordis.Plugins;
using Gocordis.UI.Hosts;
using NuGet.Versioning;

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
                "registry" => await RegistryAsync(args[1..]),
                "console" => await ConsoleAsync(args[1..]),
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

        // The generated app references the published host package, not this
        // developer's checkout. `dotnet pack` below seeds a local feed so the
        // scaffold is immediately buildable without nuget.org credentials.
        var project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="gocordis-platform" Version="{{hostVersion}}" />
              </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(Path.Combine(output, id + ".csproj"), project);

        var program = "return await Gocordis.Platform.PlatformRunner.RunAsync(args);\n";
        await File.WriteAllTextAsync(Path.Combine(output, "Program.cs"), program);

        var nugetConfig = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="./local-packages" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;
        await File.WriteAllTextAsync(Path.Combine(output, "NuGet.Config"), nugetConfig);

        await File.WriteAllTextAsync(Path.Combine(output, "build.sh"),
            "#!/bin/sh\nset -e\ndotnet build\n");
        await File.WriteAllTextAsync(Path.Combine(output, "run.sh"),
            "#!/bin/sh\nexec dotnet run -- --manifest gocordis.app.json --store ./plugins\n");
        await File.WriteAllTextAsync(Path.Combine(output, "build.cmd"),
            "@echo off\r\ndotnet build || exit /b 1\r\n");
        await File.WriteAllTextAsync(Path.Combine(output, "run.cmd"),
            "@echo off\r\ndotnet run -- --manifest gocordis.app.json --store ./plugins\r\n");

        var feed = Path.GetFullPath(Path.Combine(output, "local-packages"));
        var pack = await RunProcessAsync("dotnet",
            $"pack \"{root}/Gocordis.sln\" -c Release -o \"{feed}\" " +
            $"-p:PackageVersion={hostVersion} --nologo -m:1", output);
        if (pack.Code != 0)
        {
            return Fail($"failed to seed local host packages: {pack.Error}");
        }

        Write(new { created = output, id, version, hostVersion, localFeed = "local-packages" });
        return 0;
    }

    private static async Task<(int Code, string Out, string Error)> RunProcessAsync(
        string file, string arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"cannot start '{file}'");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await outTask, await errorTask);
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
        return command switch
        {
            "pack" or "package" => await PluginPackAsync(rest),
            "create" => await PluginCreateAsync(rest),
            "validate" => await PluginValidateAsync(rest),
            "keygen" => await PluginKeygenAsync(rest),
            "sign" => await PluginSignAsync(rest),
            "doctor" => await PluginDoctorAsync(rest),
            "preflight" or "sandbox" => await PluginPreflightAsync(rest),
            "fetch" => await PluginFetchAsync(rest),
            "install" or "upgrade" or "uninstall" or "list" or "resolve" => await PluginStoreAsync(command, rest),
            _ => Fail($"unknown plugin command '{command}'"),
        };
    }

    private static async Task<int> PluginPackAsync(string[] args)
    {
        var source = Positional(args, 0);
        var output = Positional(args, 1);
        if (source is null || output is null)
        {
            return Fail("usage: plugin pack|package <sourceDir> <output.gocordis-plugin> " +
                        "[--id] [--version] [--type] [--host-range] [--backend] [--entry-assembly] [--entry-type] " +
                        "[--shared-assembly <name> ...] [--provides <name> ...] [--dependency <id>@<range> ...] " +
                        "[--config-schema <json-file>] " +
                        "[--capability <direction>|<ns>/<name>|<kind>|<version-or-range>|<media-type>|<schema> ...]");
        }

        var shared = OptionValues(args, "--shared-assembly");
        var capabilities = OptionValues(args, "--capability");
        var provides = OptionValues(args, "--provides");
        var dependencies = OptionValues(args, "--dependency")
            .Select(value =>
            {
                var separator = value.IndexOf('@');
                if (separator <= 0 || separator == value.Length - 1)
                {
                    throw new ArgumentException(
                        $"invalid dependency '{value}'; expected <id>@<version-range>");
                }

                return new PluginDependency(value[..separator], value[(separator + 1)..]);
            })
            .ToArray();
        var manifest = new PluginPackageManifest(
            Id: Option(args, "--id") ?? new DirectoryInfo(source).Name,
            Version: Option(args, "--version") ?? "1.0.0",
            Type: Option(args, "--type") ?? Option(args, "--id") ?? new DirectoryInfo(source).Name,
            HostVersionRange: Option(args, "--host-range") ?? "[1.0,)",
            Backend: Option(args, "--backend"),
            EntryAssembly: Option(args, "--entry-assembly"),
            EntryType: Option(args, "--entry-type"),
            SharedAssemblies: shared.Count > 0 ? shared : null,
            Provides: provides.Count > 0 ? provides : null!,
            Dependencies: dependencies.Length > 0 ? dependencies : null!,
            Capabilities: capabilities.Count > 0 ? capabilities : null);
        var schemaPath = Option(args, "--config-schema");
        if (schemaPath is not null)
        {
            manifest = manifest with
            {
                ConfigSchema = JsonNode.Parse(await File.ReadAllTextAsync(schemaPath)),
            };
        }

        var written = await PluginPackageWriter.CreateAsync(source, manifest, output);
        Write(new { packed = output, written.Id, written.Version, written.Checksum });
        return 0;
    }

    /// <summary>
    /// Generates a signing key pair as two PEM files. Existing files are never
    /// replaced unless --force is given; a late write failure rolls back any
    /// file created in this invocation.
    /// </summary>
    private static async Task<int> PluginKeygenAsync(string[] args)
    {
        var outputDirectory = Option(args, "--out") ?? Directory.GetCurrentDirectory();
        var name = Option(args, "--name") ?? "plugin-signing";
        if (string.IsNullOrWhiteSpace(name) || name.Any(c =>
                !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            return Fail($"invalid key name '{name}' (want letters, digits, '.', '_' or '-')");
        }

        var force = args.Contains("--force", StringComparer.Ordinal);
        var directory = Path.GetFullPath(outputDirectory);
        var privatePath = Path.Combine(directory, name + ".private.pem");
        var publicPath = Path.Combine(directory, name + ".public.pem");
        if (!force && (File.Exists(privatePath) || File.Exists(publicPath)))
        {
            return Fail($"key file already exists for '{name}' in '{directory}' (use --force to overwrite)");
        }

        var (privatePem, publicPem) = PluginPackageSigner.CreateKeyPair();
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        try
        {
            await WriteKeyFileAsync(privatePath, privatePem, force, written);
            await WriteKeyFileAsync(publicPath, publicPem, force, written);
        }
        catch
        {
            foreach (var path in written.AsEnumerable().Reverse())
            {
                TryDeleteFileQuietly(path);
            }

            throw;
        }

        Write(new { privateKey = privatePath, publicKey = publicPath });
        return 0;

        static async Task WriteKeyFileAsync(string path, string content, bool overwrite, List<string> tracked)
        {
            tracked.Add(path); // track before opening so partial writes are rolled back
            await using var stream = new FileStream(
                path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
        }
    }

    /// <summary>Signs a package into a new file; the source package is untouched.</summary>
    private static async Task<int> PluginSignAsync(string[] args)
    {
        var package = Positional(args, 0);
        var key = Option(args, "--key");
        if (package is null || key is null)
        {
            return Fail("usage: plugin sign <package> --key <privatePem> [--output <path>]");
        }

        var privateKeyPem = await File.ReadAllTextAsync(key);
        var signed = await PluginPackageSigner.SignAsync(package, privateKeyPem, Option(args, "--output"));
        Write(new { signed, algorithm = PluginPackageSigner.Algorithm });
        return 0;
    }

    private static async Task<int> PluginStoreAsync(string command, string[] rest)
    {
        var storePath = Option(rest, "--store") ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gocordis", "plugins");
        var hostVersion = Option(rest, "--host-version") ?? "1.0.0";
        var store = new PluginStore(storePath, hostVersion);

        switch (command)
        {
            case "install":
            case "upgrade":
            {
                var package = Positional(rest, 0);
                if (package is null)
                {
                    return Fail($"usage: plugin {command} <package> [--store] [--host-version]");
                }

                PluginTrustPolicy trust;
                try
                {
                    trust = BuildTrustPolicy(rest);
                }
                catch (ArgumentException ex)
                {
                    return Fail(ex.Message);
                }

                var result = command == "install"
                    ? await store.InstallAsync(package, default, trust)
                    : await store.UpgradeAsync(package, default, trust);
                Write(new { result.Changes, trust = trust.RequireSignature ? "trusted-only" : "development" });
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

    /// <summary>
    /// Downloads a package from a read-only HTTP registry. The returned path is
    /// an ordinary package file that existing install/doctor commands consume;
    /// no package code runs here and no store state is touched.
    /// </summary>
    private static async Task<int> PluginFetchAsync(string[] args)
    {
        var id = Positional(args, 0);
        var range = Positional(args, 1);
        var sourceUrl = Option(args, "--source");
        if (id is null || range is null || sourceUrl is null)
        {
            return Fail("usage: plugin fetch <id> <versionRange> --source <url> [--output <path>] [--host-version]");
        }

        var downloaded = await new HttpPluginPackageSource(sourceUrl).ResolveAsync(id, range);
        var finalPath = downloaded;
        try
        {
            string? version = null;
            if (Option(args, "--host-version") is { } hostVersion)
            {
                // Host compatibility is opt-in: with no --host-version the file
                // stays unverified against a host until doctor/install runs.
                version = (await PluginPackageReader.ReadAsync(downloaded, hostVersion)).Manifest.Version;
            }

            var output = Option(args, "--output");
            if (output is not null)
            {
                finalPath = Path.GetFullPath(output);
                var parent = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Move(downloaded, finalPath, overwrite: true);
            }

            Write(new { path = finalPath, id, versionRange = range, version, source = sourceUrl });
            return 0;
        }
        catch
        {
            if (!string.Equals(finalPath, downloaded, StringComparison.Ordinal))
            {
                TryDeleteFileQuietly(finalPath);
            }

            TryDeleteFileQuietly(downloaded);
            throw;
        }
    }

    /// <summary>Serves a directory of packages as a read-only HTTP registry.</summary>
    private static async Task<int> RegistryAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("registry requires a subcommand");
        }

        var command = args[0];
        var rest = args[1..];
        if (!string.Equals(command, "serve", StringComparison.Ordinal))
        {
            return Fail($"unknown registry command '{command}'");
        }

        var directory = Positional(rest, 0);
        if (directory is null)
        {
            return Fail("usage: registry serve <directory> [--address] [--allow-remote]");
        }

        var address = Option(rest, "--address") ?? "http://127.0.0.1:0";
        var allowRemote = rest.Contains("--allow-remote", StringComparer.Ordinal) ||
                          Option(rest, "--allow-remote") is "true" or "1";
        await using var server = new PluginSourceServer(directory, address, allowRemote);
        await server.StartAsync();
        Write(new { address = server.Address, indexUrl = server.IndexUrl, note = "Press Ctrl+C to stop." });
        await WaitForShutdownAsync();
        return 0;
    }

    private static void TryDeleteFileQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Scaffolds a standalone plugin project. Every write is tracked so a late
    /// I/O failure cannot leave a half-generated scaffold behind.
    /// </summary>
    private static async Task<int> PluginCreateAsync(string[] args)
    {
        var output = Positional(args, 0);
        var id = Option(args, "--id");
        var name = Option(args, "--name");
        var type = Option(args, "--type");
        var version = Option(args, "--version") ?? "1.0.0";
        if (output is null || id is null || name is null || type is null)
        {
            return Fail("usage: plugin create <outputDir> --id <id> --name <name> --type <type> [--version] [--sdk-version <v>] [--feed <local-feed>] [--contract <name> ...]");
        }

        if (!IsPluginId(id))
        {
            return Fail($"invalid plugin id '{id}' (want lower-case letters, digits, '.', '_' or '-')");
        }

        if (name.Length is 0 or > 200 || name.Any(c => c < 32 || c > 126))
        {
            return Fail($"invalid plugin name '{name}'");
        }

        if (!IsPluginId(type))
        {
            return Fail($"invalid plugin type '{type}'");
        }

        if (!NuGetVersion.TryParse(version, out var parsedVersion))
        {
            return Fail($"invalid plugin version '{version}'");
        }

        version = parsedVersion.ToNormalizedString();
        var root = FindRepoRoot();
        var fullOutput = Path.GetFullPath(output);
        if (Directory.Exists(fullOutput) && Directory.EnumerateFileSystemEntries(fullOutput).Any())
        {
            return Fail($"plugin create target '{output}' is not empty");
        }

        var projectName = id + ".csproj";
        var created = new List<string>();
        var createdDirectory = false;

        async Task WriteAsync(string path, string content)
        {
            created.Add(path); // track before the write so a partial file is rolled back too
            await File.WriteAllTextAsync(path, content);
        }

        try
        {
            if (!Directory.Exists(fullOutput))
            {
                Directory.CreateDirectory(fullOutput);
                createdDirectory = true;
            }

            // Third-party developers use the packaged SDK, not a path into the
            // runtime repository. The local feed is produced by `dotnet pack`.
            var contractNames = OptionValues(args, "--contract");
            foreach (var contract in contractNames)
            {
                if (!contract.All(char.IsLetterOrDigit) || contract.Length == 0 || !char.IsLetter(contract[0]))
                {
                    return Fail($"invalid contract name '{contract}'");
                }
            }

            var sdkVersion = Option(args, "--sdk-version") ?? "1.0.0";
            var contractRefs = string.Concat(contractNames.Select(contract =>
                $"                    <PackageReference Include=\"Gocordis.Extensions.Contracts.{contract}\" Version=\"{sdkVersion}\" />\n"));
            var project = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <LangVersion>latest</LangVersion>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Gocordis.Plugin.Sdk" Version="__SDK_VERSION__" />
                __CONTRACT_REFS__
                  </ItemGroup>
                </Project>
                """
                .Replace("__SDK_VERSION__", sdkVersion, StringComparison.Ordinal)
                .Replace("__CONTRACT_REFS__", contractRefs.TrimEnd(), StringComparison.Ordinal);

            var plugin = """
                namespace __NAMESPACE__;

                /// <summary>
                /// Scaffolded plugin component. The reversible effect writes a log field
                /// and publishes it under a key, so its presence is observable via Context.
                /// </summary>
                public sealed class Plugin : Component
                {
                    public static readonly Key<string> LogKey = new("__ID__.log");
                    private static readonly List<string> Entries = [];

                    public static IReadOnlyList<string> Log => Entries;

                    public override string Name => "__NAME__";

                    public override IReadOnlyList<ICapabilityKey> Provide() => [LogKey];

                    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
                    {
                        var entry = $"{Name}: effect installed";
                        await context.EffectAsync(
                            install: () =>
                            {
                                Entries.Add(entry);
                                context.Provide(LogKey, entry);
                                return ValueTask.CompletedTask;
                            },
                            inverse: () =>
                            {
                                Entries.Remove(entry);
                                return ValueTask.CompletedTask;
                            });
                        // Ownership is single: EffectAsync's inverse removes
                        // exactly this entry. Returning another cleanup here
                        // would clear entries owned by other fibers.
                        return null;
                    }
                }
                """
                .Replace("__NAMESPACE__", PluginNamespace(id), StringComparison.Ordinal)
                .Replace("__ID__", id, StringComparison.Ordinal)
                .Replace("__NAME__", CSharpLiteral(name), StringComparison.Ordinal);

            var manifest = """
                [[components]]
                id = "__ID__"
                type = "__TYPE__"
                enabled = true
                version = "__VERSION__"

                [components.config]
                message = "scaffolded plugin"
                """
                .Replace("__ID__", id, StringComparison.Ordinal)
                .Replace("__TYPE__", type, StringComparison.Ordinal)
                .Replace("__VERSION__", version, StringComparison.Ordinal);

            var nugetConfig = """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="__FEED__" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """.Replace("__FEED__", Option(args, "--feed") ?? Path.Combine(root, "local-packages"), StringComparison.Ordinal);
            var readme = """
                # __ID__

                Generated by `gocordis plugin create`. Build with `dotnet build`,
                validate with `gocordis plugin validate .`, then pack/install.
                The runtime repository is not required: only the SDK package feed.
                """.Replace("__ID__", id, StringComparison.Ordinal);
            await WriteAsync(Path.Combine(fullOutput, projectName), project);
            await WriteAsync(Path.Combine(fullOutput, "Plugin.cs"), plugin);
            await WriteAsync(Path.Combine(fullOutput, "manifest.toml"), manifest);
            await WriteAsync(Path.Combine(fullOutput, "NuGet.Config"), nugetConfig);
            await WriteAsync(Path.Combine(fullOutput, "README.md"), readme);

            Write(new
            {
                created = fullOutput, id, name, type, version,
                entryType = PluginNamespace(id) + ".Plugin",
                files = new[] { projectName, "Plugin.cs", "manifest.toml", "NuGet.Config", "README.md" },
            });
            return 0;
        }
        catch (Exception)
        {
            foreach (var path in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            if (createdDirectory)
            {
                try
                {
                    if (Directory.Exists(fullOutput) && !Directory.EnumerateFileSystemEntries(fullOutput).Any())
                    {
                        Directory.Delete(fullOutput);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            throw; // Main renders the failure as a structured error with a non-zero exit code
        }
    }

    /// <summary>
    /// Local authoring preflight: validates declaration fields, capability wire
    /// format and backend compatibility without building or installing. Errors
    /// are returned as structured diagnostics with a non-zero exit code.
    /// </summary>
    private static async Task<int> PluginValidateAsync(string[] args)
    {
        var directory = Positional(args, 0) ?? ".";
        var path = Path.Combine(directory, "manifest.toml");
        if (!File.Exists(path))
        {
            return Fail($"manifest not found: {path}");
        }

        IReadOnlyList<PluginManifest> manifests;
        try
        {
            manifests = PluginManifest.ParseToml(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex)
        {
            Write(new { diagnostics = new[] { new { severity = "error", code = "MANIFEST_PARSE", message = ex.Message } } });
            return 1;
        }

        var diagnostics = new List<object>();
        foreach (var manifest in manifests)
        {
            if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Type))
            {
                diagnostics.Add(new { severity = "error", code = "IDENTITY", message = "component id and type are required" });
            }

            if (!NuGetVersion.TryParse(manifest.Version, out _))
            {
                diagnostics.Add(new { severity = "error", code = "VERSION", message = $"invalid version '{manifest.Version}'" });
            }

            if (manifest.Backend is { } backend &&
                backend is not ("builtin" or "managed" or "process" or "wasm"))
            {
                diagnostics.Add(new { severity = "error", code = "BACKEND", message = $"unsupported backend '{backend}'" });
            }

            foreach (var wire in manifest.CapabilitiesOrEmpty)
            {
                try
                {
                    var declaration = CapabilityDeclaration.Parse(wire);
                    if (declaration.ValueKind != CapabilityValueKind.Endpoint)
                    {
                        diagnostics.Add(new
                        {
                            severity = "error", code = "CAPABILITY_KIND",
                            message = $"capability '{declaration.Id}' must be endpoint in this release",
                        });
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new
                    {
                        severity = "error", code = "CAPABILITY_DECLARATION",
                        message = $"invalid capability declaration '{wire}': {ex.Message}",
                    });
                }
            }
        }

        var errors = diagnostics.Count;
        Write(new { diagnostics, errors, components = manifests.Count });
        return errors == 0 ? 0 : 1;
    }

    /// <summary>Delegates to the single product trust parser in Packaging so the
    /// CLI installer and the product runner cannot drift apart.</summary>
    private static PluginTrustPolicy BuildTrustPolicy(string[] args) =>
        PluginTrustPolicy.FromPemFiles(Option(args, "--trust"), OptionValues(args, "--public-key"));

    /// <summary>
    /// Validates a package without installing it. Diagnostics are always a JSON
    /// array of severity/code/message; errors (not warnings) drive exit code 1.
    /// </summary>
    private static async Task<int> PluginDoctorAsync(string[] args)
    {
        var package = Positional(args, 0);
        if (package is null)
        {
            return Fail("usage: plugin doctor <packagePath> [--host-version] [--store] [--public-key <pem>]");
        }

        var (inspection, _) = await InspectPackageAsync(package, HostVersion(args), Option(args, "--store"));
        var diagnostics = new List<CliDiagnostic>(inspection);
        if (Option(args, "--public-key") is { } publicKeyPath)
        {
            // Opt-in provenance check: without --public-key doctor behaves exactly
            // as before, including for unsigned packages.
            try
            {
                var publicKeyPem = await File.ReadAllTextAsync(publicKeyPath);
                await PluginPackageSigner.VerifyAsync(package, publicKeyPem);
                diagnostics.Add(Info("SIGNATURE_OK", "package signature is valid"));
            }
            catch (PackageSignatureException ex)
            {
                diagnostics.Add(Error("SIGNATURE_INVALID", ex.Message));
            }
            catch (Exception ex)
            {
                diagnostics.Add(Error("SIGNATURE_INVALID", $"cannot verify package signature: {ex.Message}"));
            }
        }

        WriteDiagnostics(diagnostics);
        return diagnostics.Any(diagnostic => diagnostic.Severity == "error") ? 1 : 0;
    }

    /// <summary>
    /// Declaration-only preflight in a throwaway store/runtime. Package payloads
    /// are never executed and no host factory is involved: this is neither a
    /// security sandbox nor a plugin host, just a lifecycle check with a clean
    /// shutdown. `sandbox` remains a legacy alias for this command.
    /// </summary>
    private static async Task<int> PluginPreflightAsync(string[] args)
    {
        var package = Positional(args, 0);
        if (package is null)
        {
            return Fail("usage: plugin preflight <packagePath> [--host-version]");
        }

        var hostVersion = HostVersion(args);
        var diagnostics = new List<CliDiagnostic>
        {
            Warn("NO_HOST_FACTORY",
                "declaration-only preflight: no host plugin factory is used and the package payload is not executed; this is not a security sandbox or a plugin host"),
        };
        var (inspection, content) = await InspectPackageAsync(package, hostVersion, storePath: null);
        diagnostics.AddRange(inspection);
        if (content is null || diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
        {
            WritePreflightResult(null, null, diagnostics);
            return 1;
        }

        var tempStore = Path.Combine(Path.GetTempPath(), $"gocordis-preflight-{Guid.NewGuid():N}");
        Gocordis.Core.Runtime? runtime = null;
        PluginRegistry? registry = null;
        try
        {
            // Installation happens only in the temp store; no user store or
            // application manifest is touched. InstallAsync also enforces that
            // every declared package dependency is already installed there.
            var store = new PluginStore(tempStore, hostVersion);
            var installed = await store.InstallAsync(package);
            diagnostics.Add(Info("TEMP_STORE_LOADED",
                $"package loaded into temporary store with {installed.Changes.Count} change(s)"));

            runtime = new Gocordis.Core.Runtime();
            registry = new PluginRegistry(runtime);
            var type = content.Manifest.Type;
            registry.RegisterFactory(type, _ => new PreflightDeclarationComponent(type));
            var application = new ApplicationManifest(
                $"preflight-{content.Manifest.Id}",
                "1.0.0",
                hostVersion,
                [new ApplicationPlugin(content.Manifest.Id, $"[{content.Manifest.Version}]", true)]);
            await new ApplicationLoader(store, registry).ApplyAsync(application);

            WritePreflightResult(content.Manifest, runtime.Snapshot(), diagnostics);
            return 0;
        }
        catch (Exception ex)
        {
            diagnostics.Add(ex switch
            {
                MissingPluginDependencyException => Error("MISSING_DEPENDENCY", ex.Message),
                IncompatiblePluginException => Error("VERSION_MISMATCH", ex.Message),
                InvalidPluginPackageException => Error("INVALID_PACKAGE", ex.Message),
                _ => Error("PREFLIGHT_LOAD", ex.Message),
            });
            WritePreflightResult(content.Manifest, runtime?.Snapshot(), diagnostics);
            return 1;
        }
        finally
        {
            // LIFO teardown: declarations first, then runtime, then the temp store.
            if (registry is not null)
            {
                await registry.DisposeAsync();
            }

            if (runtime is not null)
            {
                await runtime.DisposeAsync();
            }

            TryDeleteDirectory(tempStore);
        }
    }

    /// <summary>Shared declaration inspection for doctor and preflight; never throws.</summary>
    private static async Task<(IReadOnlyList<CliDiagnostic> Diagnostics, PluginPackageContent? Content)> InspectPackageAsync(
        string packagePath, string hostVersion, string? storePath)
    {
        var diagnostics = new List<CliDiagnostic>();
        if (!NuGetVersion.TryParse(hostVersion, out _))
        {
            diagnostics.Add(Error("INVALID_VERSION", $"host version '{hostVersion}' is not a valid version"));
            return (diagnostics, null);
        }

        if (!File.Exists(packagePath))
        {
            diagnostics.Add(Error("INVALID_PACKAGE", $"package not found: '{packagePath}'"));
            return (diagnostics, null);
        }

        PluginPackageContent content;
        try
        {
            content = await PluginPackageReader.ReadAsync(packagePath, hostVersion);
        }
        catch (IncompatiblePluginException ex)
        {
            diagnostics.Add(Error("VERSION_MISMATCH", ex.Message));
            return (diagnostics, null);
        }
        catch (InvalidPluginPackageException ex)
        {
            diagnostics.Add(Error("INVALID_PACKAGE", ex.Message));
            return (diagnostics, null);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("INVALID_PACKAGE", $"cannot inspect package: {ex.Message}"));
            return (diagnostics, null);
        }

        var manifest = content.Manifest;
        if (!IsPluginId(manifest.Id) || !NuGetVersion.TryParse(manifest.Version, out _) ||
            string.IsNullOrWhiteSpace(manifest.Type))
        {
            diagnostics.Add(Error("INVALID_PACKAGE", "package manifest has an invalid id, version or type"));
            return (diagnostics, null);
        }

        diagnostics.Add(Info("PACKAGE_OK",
            $"package '{manifest.Id}' {manifest.Version} (type '{manifest.Type}') is structurally valid"));
        diagnostics.Add(Info("CHECKSUM_OK", "package checksum and payload hashes match the manifest"));
        diagnostics.Add(Info("HOST_RANGE_OK", $"host {hostVersion} satisfies '{manifest.HostVersionRange}'"));

        var dependencies = manifest.DependenciesOrEmpty;
        if (dependencies.Count == 0)
        {
            diagnostics.Add(Info("DEPENDENCIES_OK", "package declares no dependencies"));
        }
        else if (storePath is null)
        {
            diagnostics.Add(Warn("DEPENDENCY_UNVERIFIED",
                $"package declares {dependencies.Count} dependency(ies); supply --store to verify installation"));
        }
        else
        {
            var installed = await new PluginStore(storePath, hostVersion).ListAsync();
            foreach (var dependency in dependencies)
            {
                var found = installed.Any(p =>
                    string.Equals(p.Id, dependency.Id, StringComparison.Ordinal) &&
                    VersionRange.Parse(dependency.VersionRange).Satisfies(NuGetVersion.Parse(p.Version)));
                diagnostics.Add(found
                    ? Info("DEPENDENCY_OK", $"dependency '{dependency.Id}' {dependency.VersionRange} is installed")
                    : Error("MISSING_DEPENDENCY", $"dependency '{dependency.Id}' {dependency.VersionRange} is not installed"));
            }
        }

        return (diagnostics, content);
    }

    private static void WriteDiagnostics(IReadOnlyList<CliDiagnostic> diagnostics) =>
        System.Console.WriteLine(JsonSerializer.Serialize(diagnostics, Json));

    private static void WritePreflightResult(
        PluginPackageManifest? manifest, RuntimeSnapshot? snapshot, IReadOnlyList<CliDiagnostic> diagnostics) =>
        Write(new
        {
            mode = "declaration-preflight",
            securityBoundary = false,
            note = "declaration preflight only: package payload is not executed; this is neither a security sandbox nor a plugin host",
            package = manifest is null
                ? null
                : new { manifest.Id, manifest.Version, manifest.Type, manifest.Checksum },
            diagnostics,
            snapshot = snapshot ?? new RuntimeSnapshot([]),
        });

    private static CliDiagnostic Error(string code, string message) => new("error", code, message);

    private static CliDiagnostic Warn(string code, string message) => new("warn", code, message);

    private static CliDiagnostic Info(string code, string message) => new("info", code, message);

    private static bool IsPluginId(string id) =>
        id.Length is > 0 and <= 128 &&
        (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0])) &&
        id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.');

    private static string PluginNamespace(string id)
    {
        var text = string.Concat(id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
        return char.IsAsciiDigit(text[0]) ? "_" + text : text;
    }

    private static string CSharpLiteral(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record CliDiagnostic(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);

    /// <summary>
    /// User-facing plugin management goes through the HTTP control plane that
    /// owns the one ApplicationLoader/PluginRegistry. The CLI never edits a
    /// store or manifest directly here; offline package tools stay under
    /// `plugin doctor|preflight|fetch`.
    /// </summary>
    private static async Task<int> ConsoleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("console requires a subcommand: restore|install|upgrade|uninstall|enable|disable|list|audit");
        }

        var command = args[0];
        var rest = args[1..];
        var address = Option(rest, "--address") ?? "http://127.0.0.1:8170";
        using var client = new ConsoleClient(address);
        switch (command)
        {
            case "restore":
            case "install":
            case "upgrade":
            {
                var packagePath = Positional(rest, 0);
                if (packagePath is not null)
                {
                    await using var stream = File.OpenRead(packagePath);
                    var upload = await client.UploadPackageAsync(stream, Path.GetFileName(packagePath));
                    var mutation = command == "upgrade"
                        ? await client.UpgradePluginAsync(upload.Id, upload.UploadKey)
                        : await client.InstallPluginAsync(upload.UploadKey);
                    Write(new
                    {
                        action = "console-" + command, upload.Id, upload.Version,
                        mutation.Accepted, mutation.Code, mutation.Error,
                    });
                    return mutation.Accepted ? 0 : Fail(mutation.Error ?? mutation.Code);
                }

                var recovered = await client.RecoverHostAsync();
                Write(new
                {
                    action = "console-restore", recovered.Id,
                    recovered.Accepted, recovered.Code, recovered.Error,
                });
                return recovered.Accepted ? 0 : Fail(recovered.Error ?? recovered.Code);
            }
            case "uninstall":
            {
                var id = Positional(rest, 0);
                if (id is null)
                {
                    return Fail("usage: console uninstall <pluginId> [--address <url>]");
                }

                var removed = await client.UninstallPluginAsync(id);
                Write(new { action = "console-uninstall", id, removed.Accepted, removed.Code, removed.Error });
                return removed.Accepted ? 0 : Fail(removed.Error ?? removed.Code);
            }
            case "enable":
            case "disable":
            {
                var id = Positional(rest, 0);
                if (id is null)
                {
                    return Fail($"usage: console {command} <pluginId> [--address <url>]");
                }

                var changed = await client.SetPluginEnabledAsync(id, command == "enable");
                Write(new { action = "console-" + command, id, changed.Accepted, changed.Code, changed.Error });
                return changed.Accepted ? 0 : Fail(changed.Error ?? changed.Code);
            }
            case "list":
            {
                var plugins = await client.GetPluginsAsync();
                Write(new
                {
                    plugins = plugins.Select(p => new { p.Id, p.Version, p.State, p.Enabled, p.Protected }),
                });
                return 0;
            }
            case "audit":
            {
                var audit = await client.GetAuditAsync();
                Write(new
                {
                    audit = audit.Select(entry => new
                    {
                        entry.Revision, entry.Timestamp, entry.Source,
                        entry.Action, entry.Id, entry.Outcome, entry.Code,
                    }),
                });
                return 0;
            }
            default:
                return Fail($"unknown console command '{command}'");
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
            "add" => await AppAddAsync(rest),
            "enable" => await AppToggleAsync(rest, enabled: true),
            "disable" => await AppToggleAsync(rest, enabled: false),
            "run" => await AppRunAsync(rest),
            _ => Fail($"unknown app command '{command}'"),
        };
    }

    /// <summary>Declares an installed plugin in an application manifest.</summary>
    private static async Task<int> AppAddAsync(string[] args)
    {
        var path = Positional(args, 0);
        var pluginId = Positional(args, 1);
        if (path is null || pluginId is null)
        {
            return Fail("usage: app add <manifest> <pluginId> [--range <version-range>] [--store] [--host-version]");
        }

        var application = await ApplicationManifest.LoadAsync(path);
        if (application.Plugins.Any(item => item.Id == pluginId))
        {
            return Fail($"plugin '{pluginId}' is already declared");
        }

        var storePath = Option(args, "--store") ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(path))!, "plugins");
        var store = new PluginStore(storePath, HostVersion(args));
        var range = Option(args, "--range") ?? "1.0.0";
        if (!VersionRange.TryParse(range, out _))
        {
            return Fail($"invalid version range '{range}'");
        }

        var resolved = await store.ResolveAsync(pluginId, range);
        if (resolved is null)
        {
            return Fail($"plugin '{pluginId}' matching '{range}' is not installed in '{storePath}'");
        }

        var application2 = application with
        {
            Plugins =
            [
                .. application.Plugins,
                new ApplicationPlugin(
                    pluginId, range, true,
                    LockedVersion: resolved.Version,
                    PackageHash: resolved.Manifest.Checksum),
            ],
        };
        await application2.SaveAsync(path);
        Write(new
        {
            manifest = path, id = pluginId, versionRange = range,
            lockedVersion = resolved.Version, packageHash = resolved.Manifest.Checksum,
        });
        return 0;
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

    /// <summary>
    /// The single place where `app run` flags become host options. Public so a
    /// CLI-level regression test can prove flags are not silently dropped.
    /// </summary>
    public static PlatformHostOptions BuildAppRunOptions(string[] args, string? manifestPath = null)
    {
        var path = manifestPath ?? Positional(args, 0)
            ?? throw new ArgumentException("app run requires a manifest path");
        return new PlatformHostOptions(
            path,
            StorePath(args),
            Option(args, "--address") ?? "http://127.0.0.1:0",
            AllowRemote: args.Contains("--allow-remote", StringComparer.Ordinal),
            Host: UiHostKind.Web,
            HostVersion: HostVersion(args),
            ValidateOnly: args.Contains("--validate-only", StringComparer.Ordinal),
            Trust: BuildTrustPolicy(args),
            AllowedHosts: OptionValues(args, "--allowed-host"));
    }

    private static async Task<int> AppRunAsync(string[] args)
    {
        var path = Positional(args, 0);
        if (path is null)
        {
            return Fail("usage: app run <manifest> [--store] [--host-version] [--validate-only] [--address] [--allow-remote] [--allowed-host <host> ...] [--trust development|trusted-only] [--public-key <pem>]");
        }

        var validateOnly = args.Contains("--validate-only", StringComparer.Ordinal);
        PlatformHostOptions options;
        try
        {
            options = BuildAppRunOptions(args, path);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }

        return await PlatformRunner.RunAsync(options, new PlatformRunHooks
        {
            WriteStartupBanner = false,
            WaitForShutdown = !validateOnly,
            FormatError = ex => JsonSerializer.Serialize(new { error = ex.Message }, Json),
            AfterStart = app =>
            {
                var manifest = app.Manifest!;
                Write(new
                {
                    manifest.Id,
                    manifest.Version,
                    manifest.HostVersion,
                    resolved = app.ResolvedPlugins.Select(item => new
                    {
                        item.Id,
                        item.Version,
                        item.Enabled,
                    }),
                });
                if (!validateOnly)
                {
                    System.Console.WriteLine($"Gocordis app '{manifest.Id}': {app.Address}");
                    System.Console.WriteLine("Press Ctrl+C to stop.");
                }
            },
        });
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
    /// Declaration-only adapter used by `plugin preflight` (an explicit no-host-
    /// factory check). `app run` no longer uses it: product assembly belongs
    /// to PlatformApplication, which dispatches real backends.
    /// </summary>
    private sealed class PreflightDeclarationComponent : Component
    {
        private readonly Key<string> _key = new($"preflight-plugin-{Guid.NewGuid():N}");
        private readonly string _type;

        public PreflightDeclarationComponent(string type) => _type = type;

        public override string Name => $"preflight-plugin:{_type}";

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

    private static List<string> OptionValues(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                values.Add(args[i + 1]);
            }
        }

        values.AddRange(args
            .Where(a => a.StartsWith(name + "=", StringComparison.Ordinal))
            .Select(a => a[(name.Length + 1)..]));
        return values;
    }

    private static string? Positional(string[] args, int index)
    {
        var options = new HashSet<string>(StringComparer.Ordinal)
        {
            "--store", "--host-version", "--address", "--id", "--name", "--version", "--type", "--host-range", "--source",
            "--output", "--out", "--key", "--public-key", "--backend", "--entry-assembly", "--entry-type",
            "--shared-assembly", "--allowed-host", "--host-version-range", "--provides", "--dependency", "--contract",
            "--config-schema", "--capability", "--trust", "--public-key",
        };
        var flags = new HashSet<string>(StringComparer.Ordinal)
        {
            "--validate-only", "--allow-remote", "--force",
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
        gocordis plugin create <outputDir> --id <id> --name <name> --type <type> [--version] [--sdk-version <v>] [--feed <local-feed>] [--contract <name> ...]
        gocordis plugin pack|package <sourceDir> <output> [--id] [--version] [--type] [--host-range]
                            [--backend managed|builtin|wasm|process] [--entry-assembly <dll|wat|wasm|exe>]
                            [--entry-type <type>]
                            [--shared-assembly <name> ...]
                            [--capability <direction>|<ns>/<name>|<kind>|<version-or-range>|<media-type>|<schema> ...]
        gocordis plugin keygen [--out <dir>] [--name <key>] [--force]
        gocordis plugin sign <package> --key <privatePem> [--output <path>]
        gocordis plugin validate <sourceDir>          # local declaration preflight
        gocordis plugin doctor <packagePath> [--host-version] [--store] [--public-key <pem>]
        gocordis plugin preflight <packagePath> [--host-version]   # alias: sandbox
        gocordis plugin fetch <id> <versionRange> --source <url> [--output <path>] [--host-version]
        gocordis registry serve <directory> [--address] [--allow-remote]
        gocordis console restore [--address <url>]                       # last-known-good manifest via HTTP
        gocordis console install|upgrade <package> [--address <url>]      # HTTP control plane, never direct store writes
        gocordis console uninstall|enable|disable <pluginId> [--address <url>]
        gocordis console list|audit [--address <url>]
        gocordis plugin install|upgrade <package> [--store] [--host-version] [--trust development|trusted-only] [--public-key <pem> ...]
        gocordis plugin uninstall <id> [--version] [--store] [--host-version]
        gocordis plugin list [--store] [--host-version]
        gocordis plugin resolve <id> <range> [--source] [--host-version]
        gocordis app validate <manifest> [--store] [--host-version]
        gocordis app add <manifest> <pluginId> [--range <version-range>] [--store]
        gocordis app enable|disable <manifest> <pluginId> [--store] [--host-version]
        gocordis app run <manifest> [--store] [--host-version] [--validate-only] [--address]
                              [--allow-remote] [--allowed-host <host> ...]
                              [--trust development|trusted-only] [--public-key <pem> ...]
        """);
}
