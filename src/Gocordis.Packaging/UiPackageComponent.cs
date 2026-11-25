using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Gocordis.Core;
using Gocordis.Plugins;

namespace Gocordis.Packaging;

/// <summary>
/// Wraps the host's component for a package that carries UI resources. The
/// wrapper is installed by the control plane's component factory, so no host
/// code has to know the plugin: it reads the declaration materialized in the
/// Store install directory and registers page/panel/client-module
/// contributions through the owning activation's reversible effects. The
/// ContributionOwner is the plugin fiber itself (FiberId + ActivationId), so a
/// stale cleanup can never remove a newer activation's contribution.
/// </summary>
internal static class UiPackageBinder
{
    internal static IReadOnlyList<ICapabilityKey> WithUiKey(IReadOnlyList<ICapabilityKey> keys) =>
        keys.Any(key => ReferenceEquals(key, UiContributionRegistry.Key))
            ? keys
            : [.. keys, UiContributionRegistry.Key];

    internal static Component Wrap(Component inner, PluginManifest manifest)
    {
        var resources = PluginPackageUi.LoadInstalled(manifest.Source ?? string.Empty);
        if (resources.Count == 0)
        {
            return inner;
        }

        return inner is IIterativeComponent
            ? new UiPackageIterativeComponent(inner, manifest, resources)
            : new UiPackageComponent(inner, manifest, resources);
    }

    internal static async ValueTask<List<UiContributionHandle>> RegisterAsync(
        Context context, PluginManifest manifest, IReadOnlyList<PackageUiResource> resources)
    {
        var ui = context.Require(UiContributionRegistry.Key);
        var owner = ContributionOwner.ForContext(context, manifest.Id, context.ComponentName);
        var handles = new List<UiContributionHandle>(resources.Count);
        var source = manifest.Source ?? string.Empty;
        foreach (var resource in resources)
        {
            context.ThrowIfDiverted();
            if (resource.Kind.Equals("page", StringComparison.OrdinalIgnoreCase))
            {
                var obj = PluginPackageUi.DefinitionOf(resource, source);
                handles.Add(await context.RegisterUiPageAsync(ui, owner, new UiPageDefinition(
                    resource.Id,
                    Text(obj, "title") ?? resource.Id,
                    Text(obj, "route") ?? $"/plugins/{resource.Id}",
                    Text(obj, "renderer") ?? "generic",
                    Text(obj, "icon"),
                    Text(obj, "description"),
                    obj["views"]?.DeepClone(),
                    Actions(obj),
                    Number(obj, "order"),
                    Text(obj, "group"))));
            }
            else if (resource.Kind.Equals("panel", StringComparison.OrdinalIgnoreCase))
            {
                var obj = PluginPackageUi.DefinitionOf(resource, source);
                handles.Add(await context.RegisterUiPanelAsync(ui, owner, new UiPanelDefinition(
                    resource.Id,
                    Text(obj, "title") ?? resource.Id,
                    Text(obj, "position") ?? "right",
                    Text(obj, "renderer") ?? "event-feed",
                    Strings(obj, "pages"),
                    obj["views"]?.DeepClone(),
                    Number(obj, "order"))));
            }
            else if (resource.Kind.Equals("client-module", StringComparison.OrdinalIgnoreCase))
            {
                var entry = resource.Path.Replace('\\', '/');
                var hash = HashDirectory(source);
                handles.Add(await context.RegisterUiClientModuleAsync(ui, owner,
                    new UiClientModuleDefinition(
                        resource.Id,
                        $"/client-modules/{Uri.EscapeDataString(resource.Id)}/{entry}",
                        $"{manifest.Version}:{hash}",
                        entry,
                        source,
                        manifest.Id,
                        manifest.Version,
                        hash)));
            }
            else
            {
                var obj = PluginPackageUi.DefinitionOf(resource, source);
                switch (resource.Kind)
                {
                    case "route":
                        handles.Add(await context.RegisterUiRouteAsync(ui, owner, new UiRouteDefinition(
                            resource.Id,
                            Text(obj, "path") ?? $"/plugins/{resource.Id}",
                            Text(obj, "pageId") ?? resource.Id,
                            Text(obj, "layout"),
                            Number(obj, "order"))));
                        break;
                    case "navigation":
                        handles.Add(await context.RegisterUiNavigationAsync(ui, owner, new UiNavigationDefinition(
                            resource.Id,
                            Text(obj, "label") ?? resource.Id,
                            Text(obj, "route") ?? $"/plugins/{resource.Id}",
                            Text(obj, "icon"),
                            Text(obj, "group"),
                            Text(obj, "pageId"),
                            Number(obj, "order"))));
                        break;
                    case "extension":
                        handles.Add(await context.RegisterUiExtensionAsync(ui, owner, new UiExtensionDefinition(
                            resource.Id,
                            Text(obj, "slot") ?? "default",
                            Text(obj, "renderer") ?? "generic",
                            Text(obj, "pageId"),
                            obj["props"]?.DeepClone(),
                            Number(obj, "order"))));
                        break;
                    case "resource":
                        handles.Add(await context.RegisterUiResourceAsync(ui, owner, new UiResourceDefinition(
                            resource.Id,
                            Text(obj, "kind") ?? "asset",
                            Text(obj, "path") ?? resource.Path,
                            Text(obj, "mediaType"),
                            obj["metadata"]?.DeepClone())));
                        break;
                }
            }
        }

        return handles;
    }

    internal static void Unwind(List<UiContributionHandle> handles)
    {
        for (var i = handles.Count - 1; i >= 0; i--)
        {
            handles[i].Dispose();
        }
    }

    private static string? Text(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Number(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static IReadOnlyList<string> Strings(JsonObject obj, string key) =>
        obj[key] is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? string.Empty).ToArray()
            : [];

    private static IReadOnlyList<UiPageAction> Actions(JsonObject obj) =>
        obj["actions"] is JsonArray array
            ? array.OfType<JsonObject>().Select(action => new UiPageAction(
                Text(action, "label") ?? string.Empty,
                Text(action, "command") ?? string.Empty,
                action["datePicker"] is JsonValue flag && flag.TryGetValue<bool>(out var datePicker) && datePicker))
                .ToArray()
            : [];

    /// <summary>Content hash over the installed module directory: every file
    /// except the derived declaration participates, so a changed asset always
    /// gets a new URL cache key.</summary>
    private static string HashDirectory(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return string.Empty;
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .OrderBy(path => Path.GetRelativePath(source, path), StringComparer.Ordinal)
            .ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(source, file).Replace('\\', '/') + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

internal sealed class UiPackageComponent : Component, IDisposable, IAsyncDisposable
{
    private readonly Component _inner;
    private readonly PluginManifest _manifest;
    private readonly IReadOnlyList<PackageUiResource> _resources;

    internal UiPackageComponent(
        Component inner, PluginManifest manifest, IReadOnlyList<PackageUiResource> resources)
    {
        _inner = inner;
        _manifest = manifest;
        _resources = resources;
    }

    public override string Name => _inner.Name;

    public override IReadOnlyList<ICapabilityKey> Inject() => UiPackageBinder.WithUiKey(_inner.Inject());

    public override IReadOnlyList<ICapabilityKey> Provide() => _inner.Provide();

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var cleanup = await _inner.ApplyAsync(context);
        if (cleanup is not null)
        {
            // Install the inner cleanup as an effect so a UI registration
            // failure unwinds it; LIFO ordering removes UI before the cleanup.
            await context.EffectAsync(static () => ValueTask.CompletedTask, () => cleanup());
        }

        var handles = await UiPackageBinder.RegisterAsync(context, _manifest, _resources);
        return () =>
        {
            UiPackageBinder.Unwind(handles);
            return ValueTask.CompletedTask;
        };
    }

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public ValueTask DisposeAsync() =>
        _inner is IAsyncDisposable asyncDisposable
            ? asyncDisposable.DisposeAsync()
            : ValueTask.CompletedTask;
}

/// <summary>Iterative variant: UI registration is one more reversible step
/// after the wrapped component's own steps.</summary>
internal sealed class UiPackageIterativeComponent : Component, IIterativeComponent, IDisposable, IAsyncDisposable
{
    private readonly Component _inner;
    private readonly PluginManifest _manifest;
    private readonly IReadOnlyList<PackageUiResource> _resources;

    internal UiPackageIterativeComponent(
        Component inner, PluginManifest manifest, IReadOnlyList<PackageUiResource> resources)
    {
        _inner = inner;
        _manifest = manifest;
        _resources = resources;
    }

    public override string Name => _inner.Name;
    public override IReadOnlyList<ICapabilityKey> Inject() => UiPackageBinder.WithUiKey(_inner.Inject());
    public override IReadOnlyList<ICapabilityKey> Provide() => _inner.Provide();

    public override ValueTask<Cleanup?> ApplyAsync(Context context) => _inner.ApplyAsync(context);

    public async IAsyncEnumerable<EffectRegistration> IterateAsync(Context context)
    {
        if (_inner is IIterativeComponent iterative)
        {
            await foreach (var step in iterative.IterateAsync(context))
            {
                yield return step;
            }
        }

        yield return new EffectRegistration(async () =>
        {
            var handles = await UiPackageBinder.RegisterAsync(context, _manifest, _resources);
            return () =>
            {
                UiPackageBinder.Unwind(handles);
                return ValueTask.CompletedTask;
            };
        });
    }

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public ValueTask DisposeAsync() =>
        _inner is IAsyncDisposable asyncDisposable
            ? asyncDisposable.DisposeAsync()
            : ValueTask.CompletedTask;
}
