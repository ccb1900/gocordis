using Gocordis.Core;

namespace Gocordis.Runtime.Contracts;

/// <summary>Explicit stages of the unified .NET backend lifecycle protocol.</summary>
public enum BackendPhase
{
    Prepared,
    Activated,
    RolledBack,
    Released,
}

/// <summary>
/// How a backend reports activation failure. This is a declaration, not an
/// implementation detail: callers and tests can branch on it without knowing
/// which backend they are driving.
/// </summary>
public enum BackendDegradationPolicy
{
    /// <summary>Failure fails the fiber (Managed, WASM).</summary>
    FailFiber,

    /// <summary>Failure degrades to an explicit error capability (Process).</summary>
    ErrorVocabulary,
}

/// <summary>
/// Immutable view of one protocol stage. The generation is the fence used by
/// Managed and WASM so a stale unwind can never release a newer resource.
/// </summary>
public sealed record BackendHandle(
    string BackendId,
    long Generation,
    BackendPhase Phase,
    string? ResourceId = null,
    string? Error = null)
{
    public bool IsActivated => Phase == BackendPhase.Activated;

    public bool IsReleased => Phase == BackendPhase.Released;

    public BackendHandle WithPhase(BackendPhase phase, string? error = null, string? resourceId = null) =>
        this with { Phase = phase, Error = error ?? Error, ResourceId = resourceId ?? ResourceId };
}

/// <summary>
/// The slice of a Core activation context used by the protocol. Production
/// adapters wrap <see cref="Core.Context"/>; tests use a recording adapter.
/// No backend ever mutates fiber state directly — Provide and Effect route to
/// Core, which remains the single lifecycle authority.
/// </summary>
public interface IBackendContext
{
    CancellationToken CancellationToken { get; }

    bool IsDiverted { get; }

    void ThrowIfDiverted();

    void Provide<T>(Key<T> key, T value);

    ValueTask EffectAsync(Func<ValueTask> install, Func<ValueTask> inverse);
}

/// <summary>
/// One backend activation = one backend instance. Prepare stages resources
/// without publishing them, Activate commits through Core's Provide, Rollback
/// disposes staging on failure, Release is the LIFO inverse installed as an
/// effect. The Runtime (never the backend) decides when Commit/Rollback/Release
/// actually run.
/// </summary>
public interface IPluginBackend : IAsyncDisposable
{
    string BackendId { get; }

    BackendDegradationPolicy DegradationPolicy { get; }

    /// <summary>Most recent stage result; observable for diagnostics/tests.</summary>
    BackendHandle? LastHandle { get; }

    ValueTask<BackendHandle> PrepareAsync(IBackendContext context);

    ValueTask<BackendHandle> ActivateAsync(IBackendContext context, BackendHandle prepared);

    ValueTask<BackendHandle> RollbackAsync(IBackendContext context, BackendHandle? prepared, Exception reason);

    ValueTask<BackendHandle> ReleaseAsync(IBackendContext context, BackendHandle active);
}

public sealed class CoreBackendContext : IBackendContext
{
    public CoreBackendContext(Context context) => Core = context;

    /// <summary>The wrapped Core activation context (for components that delegate Apply).</summary>
    public Context Core { get; }

    public CancellationToken CancellationToken => Core.CancellationToken;

    public bool IsDiverted => Core.IsDiverted;

    public void ThrowIfDiverted() => Core.ThrowIfDiverted();

    public void Provide<T>(Key<T> key, T value) => Core.Provide(key, value);

    public ValueTask EffectAsync(Func<ValueTask> install, Func<ValueTask> inverse) =>
        Core.EffectAsync(install, inverse);
}

/// <summary>
/// Single driver shared by Managed, WASM and Process. Core's EffectAsync is the
/// commit point: if activation fails before commit, Rollback runs; after commit
/// the Runtime unwinds the effect and Release runs.
/// </summary>
public static class BackendProtocol
{
    public static async ValueTask<Cleanup?> RunAsync(IPluginBackend backend, Context context)
    {
        await RunAsync(backend, new CoreBackendContext(context));
        return null;
    }

    public static async ValueTask RunAsync(IPluginBackend backend, IBackendContext context)
    {
        BackendHandle? prepared = null;
        BackendHandle? active = null;
        try
        {
            prepared = await backend.PrepareAsync(context);
            active = await backend.ActivateAsync(context, prepared);

            // Core owns the inverse: install a no-op (commit) whose LIFO inverse
            // performs the protocol Release. Nothing is mutated outside Core.
            await context.EffectAsync(
                static () => ValueTask.CompletedTask,
                async () =>
                {
                    try
                    {
                        await backend.ReleaseAsync(context, active);
                    }
                    finally
                    {
                        await backend.DisposeAsync();
                    }
                });
        }
        catch (Exception reason)
        {
            try
            {
                // Only uncommitted activations roll back; an effect already
                // committed is unwound by Core, which calls Release instead.
                if (active is null && prepared is not null)
                {
                    await backend.RollbackAsync(context, prepared, reason);
                }
            }
            finally
            {
                await backend.DisposeAsync();
            }

            throw;
        }
    }
}
