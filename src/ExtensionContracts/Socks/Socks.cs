using Gocordis.Core;

namespace Gocordis.Extensions;

public static class SocksKeys
{
    public static readonly Key<ISocksRegistry> Service = new("gocordis.extension.socks");
}

public sealed class SocksConnectException : InvalidOperationException
{
    public SocksConnectException(byte replyCode)
        : base($"SOCKS5 CONNECT failed: {Describe(replyCode)}") => ReplyCode = replyCode;

    public byte ReplyCode { get; }

    public static string Describe(byte replyCode) => replyCode switch
    {
        1 => "general failure",
        2 => "connection not allowed by ruleset",
        3 => "network unreachable",
        4 => "host unreachable",
        5 => "connection refused",
        6 => "TTL expired",
        7 => "command not supported",
        8 => "address type not supported",
        _ => $"unknown REP code {replyCode}",
    };
}

public interface ISocksRegistry : IAsyncDisposable
{
    IHttpService? Http { get; }
    IDisposable Register(string scheme, string address);
    SocksDialer? Resolve(string scheme);
    Task<Stream> ConnectAsync(string scheme, string host, int port, CancellationToken token = default);
}
