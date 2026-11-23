using Gocordis.Core;
using Gocordis.Plugins;

namespace KoishiCase;

// Koishi contributes only chatbot-domain vocabulary; the primitives come from Core.
public interface IDatabase { string Backend { get; } }
public interface IChatAdapter { string Platform { get; } }

public sealed class CommandRegistry
{
    public sealed record Invocation(string Name, string Argument, string Result);

    private readonly Dictionary<string, Func<string, string>> _commands = new(StringComparer.Ordinal);
    private readonly List<Invocation> _invocations = [];
    private readonly object _sync = new();

    public IReadOnlyCollection<string> Names => _commands.Keys;

    public IReadOnlyList<Invocation> Invocations
    {
        get { lock (_sync) { return _invocations.ToArray(); } }
    }

    public IDisposable Register(string name, Func<string, string> handler)
    {
        _commands.Add(name, handler);
        return new Registration(() => _commands.Remove(name));
    }

    public string Execute(string name, string argument)
    {
        var result = _commands.TryGetValue(name, out var handler) ? handler(argument) : $"unknown:{name}";
        lock (_sync)
        {
            _invocations.Add(new Invocation(name, argument, result));
        }

        return result;
    }

    private sealed class Registration : IDisposable
    {
        private Action? _undo;
        public Registration(Action undo) => _undo = undo;
        public void Dispose() => Interlocked.Exchange(ref _undo, null)?.Invoke();
    }
}

public static class K
{
    public static readonly Key<IDatabase> Database = new("koishi.database");
    public static readonly Key<IChatAdapter> Adapter = new("koishi.adapter");
    public static readonly Key<CommandRegistry> Commands = new("koishi.commands");
    public static readonly EventKey<string> Message = new("koishi.message");
}
