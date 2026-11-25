namespace Gocordis.Packaging;

/// <summary>
/// Product-owned plugin packages shipped beside the host, not samples. The
/// catalog is a path resolver only: installation still goes through the one
/// PluginStore / IPluginControlPlane. A deployment can override the directory
/// with GOCORDIS_BUILTIN_PACKAGES; development resolves from the repo checkout.
/// </summary>
public static class BuiltInPackages
{
    public const string ConsoleId = "gocordis-console";
    public const string DirectoryName = "builtin-packages";

    /// <summary>Resolve a built-in package by short name or plugin id.</summary>
    public static string Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var fileName = name switch
        {
            "console" or ConsoleId => "gocordis-console-1.0.0.gocordis-plugin",
            _ => throw new ArgumentException(
                $"unknown built-in package '{name}'; available: console", nameof(name)),
        };

        var overrideDirectory = Environment.GetEnvironmentVariable("GOCORDIS_BUILTIN_PACKAGES");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            var overridden = Path.Combine(overrideDirectory, fileName);
            if (File.Exists(overridden)) return Path.GetFullPath(overridden);
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var besideHost = Path.Combine(directory.FullName, DirectoryName, fileName);
                if (File.Exists(besideHost)) return besideHost;

                var sourceTree = Path.Combine(
                    directory.FullName, "src", "Gocordis.Console.Plugin", "builtin", fileName);
                if (File.Exists(sourceTree)) return sourceTree;

                if (File.Exists(Path.Combine(directory.FullName, "Gocordis.sln")))
                {
                    break; // repository root reached without a package
                }
            }
        }

        throw new FileNotFoundException(
            $"built-in package '{name}' was not found; set GOCORDIS_BUILTIN_PACKAGES or rebuild " +
            "src/Gocordis.Console.Plugin/build-and-pack.sh", fileName);
    }

    public static string ConsolePackagePath => Resolve(ConsoleId);
}
