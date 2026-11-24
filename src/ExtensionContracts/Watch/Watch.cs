using Gocordis.Core;

namespace Gocordis.Extensions;

public static class WatchKeys
{
    public static readonly Key<IWatchService> Service = new("gocordis.extension.watch");
}

public enum WatchEventKind { Change, Ready, Error }

public sealed record WatchEvent(WatchEventKind Kind, string Path, long Revision = 0, string? Error = null);

public sealed record WatchOptions(bool Recursive = false, string? Filter = null);

public interface IWatchService : IAsyncDisposable
{
    long Revision(string path);
    IDisposable Watch(string path, Action<WatchEvent> onEvent, WatchOptions? options = null);
    IDisposable WatchOwned(Context owner, string path, Action<WatchEvent> onEvent, WatchOptions? options = null);
    ValueTask<IDisposable> WatchOwnedAsync(Context owner, string path, Action<WatchEvent> onEvent, WatchOptions? options = null);
}
