using Gocordis.Core;

namespace Gocordis.Extensions;

/// <summary>
/// Base for official extension providers. The service is published as an
/// ordinary Core capability; its disposal is installed as an effect BEFORE the
/// capability so LIFO unwind withdraws the binding first and only then releases
/// the service resource. All fibers stay owned by the one Runtime.
/// </summary>
public abstract class ExtensionProviderComponent<T> : Component where T : notnull
{
    private readonly Key<T> _key;
    private readonly string _name;
    private readonly Func<T> _create;

    protected ExtensionProviderComponent(Key<T> key, string name, Func<T> create)
    {
        _key = key;
        _name = name;
        _create = create;
    }

    public override string Name => _name;

    public override IReadOnlyList<ICapabilityKey> Provide() => [_key];

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        ExtensionPublish.PublishAsync(context, _key, _create);
}

public static class ExtensionPublish
{
    /// <summary>Publish an already-created service with correct LIFO order.</summary>
    public static ValueTask<Cleanup?> PublishAsync<K>(Context context, Key<K> key, K service)
        where K : notnull => PublishAsync(context, key, () => service);

    /// <summary>
    /// Create and publish a service. If effect installation or binding fails,
    /// the freshly created service is disposed before the failure propagates.
    /// </summary>
    public static async ValueTask<Cleanup?> PublishAsync<K>(Context context, Key<K> key, Func<K> create)
        where K : notnull
    {
        var service = create();
        // One-shot: a divert inside EffectAsync can run the inverse before
        // throwing, and a failed Provide can be followed by unwind. Neither
        // path may dispose a non-idempotent service twice.
        var disposed = 0;
        ValueTask DisposeOnceAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0 && service is IAsyncDisposable disposable)
            {
                return disposable.DisposeAsync();
            }

            return ValueTask.CompletedTask;
        }

        try
        {
            // Installed first => undone last: withdraw before disposal.
            await context.EffectAsync(static () => ValueTask.CompletedTask, () => DisposeOnceAsync());
            context.Provide(key, service);
            return null;
        }
        catch
        {
            await DisposeOnceAsync();
            throw;
        }
    }
}

public sealed class TimerExtensionComponent : ExtensionProviderComponent<TimerService>
{
    public TimerExtensionComponent() : base(ExtensionKeys.Timer, "extension.timer", static () => new TimerService()) { }
}

public sealed class HttpExtensionComponent : ExtensionProviderComponent<HttpService>
{
    public HttpExtensionComponent() : base(ExtensionKeys.Http, "extension.http", static () => new HttpService()) { }
}

public sealed class FileSystemExtensionComponent : ExtensionProviderComponent<FileSystemService>
{
    public FileSystemExtensionComponent() : base(ExtensionKeys.FileSystem, "extension.fs", static () => new FileSystemService()) { }
}

public sealed class WatchExtensionComponent : ExtensionProviderComponent<ReversibleWatchService>
{
    public WatchExtensionComponent() : base(ExtensionKeys.Watch, "extension.watch", static () => new ReversibleWatchService()) { }
}

/// <summary>
/// Route guard provider. Host ACL policies are installed inside the provider
/// fiber (as part of the capability publication), so a withdrawn/reactivated
/// provider always carries the current policy instead of a stale host copy.
/// </summary>
public sealed class RouteGuardExtensionComponent : Component
{
    private readonly IReadOnlyList<string>? _allowedHosts;

    public RouteGuardExtensionComponent(IReadOnlyList<string>? allowedHosts = null) =>
        _allowedHosts = allowedHosts;

    public override string Name => "extension.route-guard";

    public override IReadOnlyList<ICapabilityKey> Provide() => [ExtensionKeys.RouteGuard];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var guard = new ServerRouteGuard();
        if (_allowedHosts is { Count: > 0 })
        {
            guard.Add(HostAcl.Policy(_allowedHosts, []));
        }

        return ExtensionPublish.PublishAsync(context, ExtensionKeys.RouteGuard, guard);
    }
}

public sealed class DiagnosticsExtensionComponent : ExtensionProviderComponent<DiagnosticsService>
{
    public DiagnosticsExtensionComponent(string hostVersion)
        : base(ExtensionKeys.Diagnostics, "extension.devtools", () => new DiagnosticsService(hostVersion)) { }
}

/// <summary>
/// Env depends on FS: it stays Pending until a FileSystem provider is Active,
/// reads its dotenv layers through that capability, and withdraws on loss.
/// </summary>
public sealed class EnvExtensionComponent : Component
{
    private readonly string _directory;

    public EnvExtensionComponent(string directory = ".") => _directory = directory;

    public override string Name => "extension.env";

    public override IReadOnlyList<ICapabilityKey> Inject() => [ExtensionKeys.FileSystem];

    public override IReadOnlyList<ICapabilityKey> Provide() => [ExtensionKeys.Env];

    public override async ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var fs = context.Require(ExtensionKeys.FileSystem);
        var env = new EnvService();
        try
        {
            var mode = EnvService.ResolveMode(null);
            // FS service is mount-based: a provider reads through its own scoped
            // mount and removes it after the declaration set has been loaded.
            var prefix = "/env-" + Guid.NewGuid().ToString("N");
            using var mount = fs.MountScoped(prefix, _directory);
            foreach (var name in new[] { ".env", ".env.local", $".env.{mode}", $".env.{mode}.local" })
            {
                var path = prefix + "/" + name;
                if (await fs.ExistsAsync(path))
                {
                    env.Apply(EnvService.Parse(await fs.ReadAllTextAsync(path)));
                }
            }

            return await ExtensionPublish.PublishAsync(context, ExtensionKeys.Env, env);
        }
        catch
        {
            // EnvService mutates process state via ApplyLayer; a failed load or
            // a diverted publish must never leave those variables behind.
            await env.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// SOCKS depends on HTTP: the registry is composed with the HTTP capability
/// and can only be activated after that provider is Active.
/// </summary>
public sealed class SocksExtensionComponent : Component
{
    public override string Name => "extension.http-socks";

    public override IReadOnlyList<ICapabilityKey> Inject() => [ExtensionKeys.Http];

    public override IReadOnlyList<ICapabilityKey> Provide() => [ExtensionKeys.Socks];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        var http = context.Require(ExtensionKeys.Http);
        return ExtensionPublish.PublishAsync(context, ExtensionKeys.Socks, new SocksRegistry(http));
    }
}
