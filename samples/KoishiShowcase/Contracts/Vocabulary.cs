using Gocordis.Core;

namespace Koishi.Contracts;

/// <summary>Host vocabulary published by the sample's plugins. Consumers see
/// only these contracts, never a provider implementation assembly.</summary>
public interface IDatabase
{
    ValueTask<string> QueryAsync(string sql);
}

public interface IChatAdapter
{
    ValueTask<string> SendAsync(string channel, string text);
}

public interface ICommandRegistry
{
    IDisposable Register(string name, Func<string, ValueTask<string>> handler);
    ValueTask<string> InvokeAsync(string name, string argument);
}

public static class KoishiKeys
{
    public static readonly Key<IDatabase> Database = new("koishi.database");
    public static readonly Key<IChatAdapter> Adapter = new("koishi.adapter");
    public static readonly Key<ICommandRegistry> Commands = new("koishi.commands");
    public static readonly EventKey<string> Message = new("koishi.message");
}
