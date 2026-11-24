using System.Net.Http;
using Gocordis.Core;

namespace Gocordis.Extensions;

public static class HttpKeys
{
    public static readonly Key<IHttpService> Service = new("gocordis.extension.http");
}

/// <summary>Dials a target host through a registered proxy.</summary>
public delegate Task<Stream> SocksDialer(string host, int port, CancellationToken cancellationToken);

public sealed record HttpState(bool Closed, int Active, int OpenStreams, int Interceptors);

public interface IHttpService : IAsyncDisposable
{
    bool IsDisposed { get; }
    int ProxyCount { get; }
    void SetBaseAddress(Uri uri);
    IDisposable AddInterceptor(Func<HttpRequestMessage, ValueTask> interceptor);
    IDisposable AddInterceptorChain(
        Func<HttpRequestMessage, Func<Task<HttpResponseMessage>>, Task<HttpResponseMessage>> interceptor);
    IDisposable RegisterProxyDialer(IEnumerable<string> schemes, SocksDialer dialer);
    IDisposable RegisterProxyUrlFactory(IEnumerable<string> schemes, Func<Uri?> resolve);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token = default);
    Task<string> GetStringAsync(string uri);
    Task<System.Net.WebSockets.WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken token = default);
    HttpState State();
}
