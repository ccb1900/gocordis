using System.Text.Json;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

public sealed record ApplicationPlugin(
    string Id,
    string VersionRange,
    bool Enabled,
    IReadOnlyDictionary<string, object?>? Config = null);

public sealed record ApplicationManifest(
    string Id,
    string Version,
    string HostVersion,
    IReadOnlyList<ApplicationPlugin> Plugins)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<ApplicationManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<ApplicationManifest>(bytes, JsonOptions)
               ?? throw new InvalidDataException($"application manifest '{path}' is empty");
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// Turns an application declaration into PluginRegistry declarations. It does
/// not own fibers and never writes runtime state directly.
/// </summary>
public sealed class ApplicationLoader
{
    private readonly PluginStore _store;
    private readonly PluginRegistry _registry;

    /// <summary>
    /// The capability declaration plan is part of the SAME transaction as the
    /// package/registry apply: the plan runs before the registry sees any
    /// component, and a failed apply restores the previous plan so a rolled
    /// back install cannot leave declarations behind.
    /// </summary>
    public Func<IReadOnlyList<string>, Task>? DeclarationPlan { get; init; }

    public Func<Task>? DeclarationRollback { get; init; }

    public ApplicationLoader(PluginStore store, PluginRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public async Task ApplyAsync(ApplicationManifest application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!string.Equals(application.HostVersion, _store.HostVersion, StringComparison.Ordinal))
        {
            throw new IncompatiblePluginException(
                $"application host version '{application.HostVersion}' does not match store host '{_store.HostVersion}'");
        }

        var declarations = new List<PluginManifest>(application.Plugins.Count);
        foreach (var plugin in application.Plugins)
        {
            var installed = await _store.ResolveAsync(plugin.Id, plugin.VersionRange, cancellationToken)
                            ?? throw new MissingPluginDependencyException(
                                $"application '{application.Id}' declares '{plugin.Id}' {plugin.VersionRange}, which is not installed");

            declarations.Add(new PluginManifest(
                installed.Id,
                installed.Manifest.Type,
                plugin.Enabled,
                plugin.Config ?? new Dictionary<string, object?>(),
                installed.Manifest.DependenciesOrEmpty.Select(d => d.Id).ToArray(),
                installed.Manifest.ProvidesOrEmpty,
                installed.Version,
                installed.InstallPath,
                installed.Manifest.Checksum,
                installed.Manifest.Backend,
                installed.Manifest.EntryAssembly,
                installed.Manifest.EntryType,
                installed.Manifest.SharedAssemblies,
                installed.Manifest.Capabilities));
        }

        var planWire = declarations
            .SelectMany(manifest => manifest.CapabilitiesOrEmpty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (DeclarationPlan is { } plan)
        {
            await plan(planWire);
        }

        try
        {
            await _registry.ApplyAsync(declarations);
        }
        catch
        {
            if (DeclarationRollback is { } rollback)
            {
                await rollback();
            }

            throw;
        }
    }
}
