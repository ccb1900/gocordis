using Gocordis.Core;
using Koishi.Contracts;

namespace Koishi.VocabularyPlugin;

public sealed class Vocabulary : Component
{
    public override string Name => "koishi-vocabulary";

    public override IReadOnlyList<ICapabilityKey> Provide() =>
        [KoishiKeys.Commands, KoishiKeys.Message];

    public override ValueTask<Cleanup?> ApplyAsync(Context context)
    {
        context.Provide(KoishiKeys.Commands, new Commands());
        // Providing the event key establishes the resolvable dependency root;
        // consumers declare Inject(Message) and may subscribe or emit on it.
        context.Provide(KoishiKeys.Message, "ready");
        return ValueTask.FromResult<Cleanup?>(null);
    }

    private sealed class Commands : ICommandRegistry
    {
        private readonly Dictionary<string, Func<string, ValueTask<string>>> _handlers =
            new(StringComparer.Ordinal);

        public IDisposable Register(string name, Func<string, ValueTask<string>> handler)
        {
            _handlers.Add(name, handler);
            return new Registration(this, name);
        }

        public ValueTask<string> InvokeAsync(string name, string argument) =>
            _handlers.TryGetValue(name, out var handler)
                ? handler(argument)
                : ValueTask.FromResult($"unknown:{name}");

        private sealed class Registration : IDisposable
        {
            private readonly Commands _owner;
            public Registration(Commands owner, string name) => (_owner, Name) = (owner, name);
            public string Name { get; }

            public void Dispose() => _owner._handlers.Remove(Name);
        }
    }
}
