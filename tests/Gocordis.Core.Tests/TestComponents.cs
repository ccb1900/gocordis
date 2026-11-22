using Gocordis.Core;

namespace Gocordis.Core.Tests;

public sealed class DelegateComponent : Component
{
    private readonly Func<Context, ValueTask<Cleanup?>> _apply;

    public DelegateComponent(
        string name,
        Func<Context, ValueTask<Cleanup?>> apply,
        IReadOnlyList<ICapabilityKey>? inject = null,
        IReadOnlyList<ICapabilityKey>? provide = null)
    {
        Name = name;
        _apply = apply;
        InjectKeys = inject ?? [];
        ProvideKeys = provide ?? [];
    }

    public override string Name { get; }

    public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

    public override IReadOnlyList<ICapabilityKey> Provide() => ProvideKeys;

    private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

    private IReadOnlyList<ICapabilityKey> ProvideKeys { get; }

    public override ValueTask<Cleanup?> ApplyAsync(Context context) => _apply(context);
}

public sealed class IterativeComponent : Component, IIterativeComponent
{
    private readonly Func<Context, IAsyncEnumerable<EffectRegistration>> _iterate;

    public IterativeComponent(
        string name,
        Func<Context, IAsyncEnumerable<EffectRegistration>> iterate,
        IReadOnlyList<ICapabilityKey>? inject = null)
    {
        Name = name;
        _iterate = iterate;
        InjectKeys = inject ?? [];
    }

    public override string Name { get; }

    public override IReadOnlyList<ICapabilityKey> Inject() => InjectKeys;

    private IReadOnlyList<ICapabilityKey> InjectKeys { get; }

    public override ValueTask<Cleanup?> ApplyAsync(Context context) =>
        throw new NotSupportedException("iterative components use IterateAsync");

    public IAsyncEnumerable<EffectRegistration> IterateAsync(Context context) => _iterate(context);
}

public static class Steps
{
    public static async IAsyncEnumerable<EffectRegistration> From(
        Func<Context, IAsyncEnumerable<EffectRegistration>> factory, Context context)
    {
        await foreach (var step in factory(context))
        {
            yield return step;
        }
    }
}
