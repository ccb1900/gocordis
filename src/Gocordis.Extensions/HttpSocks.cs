using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Gocordis.Extensions;

/// <summary>Dials a target host through a registered SOCKS proxy.</summary>
public delegate Task<Stream> SocksDialer(string host, int port, CancellationToken cancellationToken);

/// <summary>
/// Reversible SOCKS5 proxy registrations with a minimal CONNECT dialer built on
/// System.Net.Sockets. Registrations are owned by identity: withdrawing one
/// never removes another registration for the same scheme.
/// </summary>
public sealed class SocksRegistry : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<Entry>> _registrations = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>
    /// Normalizes socks/socks5/socks5h to socks5. Both socks5 and socks5h use
    /// remote-DNS CONNECT semantics: the (IDNA-encoded) target name is sent to
    /// the proxy instead of being resolved locally; there is no local-resolution
    /// variant. socks4/socks4a are explicitly unsupported: this implementation
    /// only offers SOCKS5 CONNECT.
    /// </summary>
    public static string NormalizeScheme(string scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var normalized = scheme.Trim().ToLowerInvariant();
        return normalized switch
        {
            "socks" or "socks5" or "socks5h" => "socks5",
            "socks4" or "socks4a" => throw new NotSupportedException(
                "only SOCKS5 CONNECT is supported (socks/socks5/socks5h forward the " +
                "target name to the proxy); got '" + normalized + "'"),
            _ => throw new ArgumentException("unsupported proxy scheme '" + scheme + "'", nameof(scheme)),
        };
    }

    /// <summary>Forms the normalized SOCKS5 proxy URL for an address.</summary>
    public static string ProxyUrl(string scheme, string address)
    {
        var normalized = NormalizeScheme(scheme);
        var (host, port) = ParseAddress(address);
        return $"{normalized}://{host}:{port}";
    }

    /// <summary>Registers a proxy for one scheme; the handle withdraws only this entry.</summary>
    public IDisposable Register(string scheme, string address)
    {
        var normalized = NormalizeScheme(scheme);
        var (host, port) = ParseAddress(address);
        var entry = new Entry(this, normalized, host, port);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!_registrations.TryGetValue(normalized, out var entries))
            {
                entries = [];
                _registrations[normalized] = entries;
            }

            entries.Add(entry);
        }

        return entry;
    }

    /// <summary>Returns the winning proxy address for a scheme, or null if unregistered.</summary>
    public string? ResolveAddress(string scheme)
    {
        var normalized = NormalizeScheme(scheme);
        lock (_sync)
        {
            return TryGetTop(normalized, out var host, out var port)
                ? $"socks5://{host}:{port}"
                : null;
        }
    }

    /// <summary>Returns a dialer bound to the winning registration, or null.</summary>
    public SocksDialer? Resolve(string scheme)
    {
        var normalized = NormalizeScheme(scheme);
        string host;
        int port;
        lock (_sync)
        {
            if (!TryGetTop(normalized, out host, out port))
            {
                return null;
            }
        }

        return (targetHost, targetPort, token) =>
            ConnectSocks5Async(host, port, targetHost, targetPort, token);
    }

    /// <summary>Dials a target through the winning registration for a scheme.</summary>
    public async Task<Stream> ConnectAsync(string scheme, string host, int port, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var dialer = Resolve(scheme)
            ?? throw new InvalidOperationException($"no SOCKS proxy registered for scheme '{scheme}'");
        return await dialer(host, port, token);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            lock (_sync)
            {
                _registrations.Clear();
            }
        }

        return ValueTask.CompletedTask;
    }

    private bool TryGetTop(string normalized, out string host, out int port)
    {
        if (_registrations.TryGetValue(normalized, out var entries) && entries.Count > 0)
        {
            var top = entries[^1];
            host = top.Host;
            port = top.Port;
            return true;
        }

        host = string.Empty;
        port = 0;
        return false;
    }

    private void Unregister(Entry entry)
    {
        lock (_sync)
        {
            if (!_registrations.TryGetValue(entry.Scheme, out var entries))
            {
                return;
            }

            var index = entries.FindIndex(candidate => ReferenceEquals(candidate, entry));
            if (index < 0)
            {
                return;
            }

            entries.RemoveAt(index);
            if (entries.Count == 0)
            {
                _registrations.Remove(entry.Scheme);
            }
        }
    }

    private static (string Host, int Port) ParseAddress(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var text = address.Trim();
        var schemeMarker = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeMarker >= 0)
        {
            text = text[(schemeMarker + 3)..];
        }

        text = text.TrimEnd('/');
        string host;
        string portText;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close <= 1 || close == text.Length - 1 || text[close + 1] != ':')
            {
                throw new ArgumentException("SOCKS proxy address must be host:port", nameof(address));
            }

            // Brackets are URL syntax for IPv6 literals; TcpClient.ConnectAsync
            // cannot resolve "[::1]" as a host name, so keep the bare address.
            host = text[1..close];
            portText = text[(close + 2)..];
        }
        else
        {
            var separator = text.LastIndexOf(':');
            if (separator <= 0 || separator == text.Length - 1)
            {
                throw new ArgumentException("SOCKS proxy address must be host:port", nameof(address));
            }

            host = text[..separator];
            portText = text[(separator + 1)..];
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("SOCKS proxy port must be between 1 and 65535", nameof(address));
        }

        return (host, port);
    }

    private static async Task<Stream> ConnectSocks5Async(
        string proxyHost, int proxyPort, string targetHost, int targetPort, CancellationToken token)
    {
        // Callers may forward a target copied from a URL; strip URL brackets
        // before parsing so a bracketed IPv6 literal is still ATYP=4.
        var target = targetHost.Length > 1 && targetHost[0] == '[' && targetHost[^1] == ']'
            ? targetHost[1..^1]
            : targetHost;

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(proxyHost, proxyPort, token);
            var stream = client.GetStream();

            // Greeting: SOCKS5, one method, no authentication.
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, token);
            var method = await ReadExactlyAsync(stream, 2, token);
            if (method[0] != 5 || method[1] != 0)
            {
                throw new InvalidOperationException(
                    $"SOCKS5 method negotiation failed (VER={method[0]}, METHOD={method[1]})");
            }

            var request = new List<byte> { 5, 1, 0 }; // VER, CMD=CONNECT, RSV
            if (IPAddress.TryParse(target, out var address))
            {
                switch (address.AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        request.Add(1); // ATYP=IPv4
                        break;
                    case AddressFamily.InterNetworkV6:
                        request.Add(4); // ATYP=IPv6
                        break;
                    default:
                        throw new ArgumentException(
                            $"unsupported target address family {address.AddressFamily}", nameof(targetHost));
                }

                request.AddRange(address.GetAddressBytes());
            }
            else
            {
                // socks5 and socks5h share remote-DNS semantics: forward the
                // IDNA-encoded name to the proxy rather than resolving locally.
                var ascii = new IdnMapping().GetAscii(target);
                var name = Encoding.ASCII.GetBytes(ascii);
                if (name.Length is 0 or > 255)
                {
                    throw new ArgumentException(
                        "target host name must be 1..255 bytes after IDNA encoding " +
                        "(socks5/socks5h forward the name to the proxy)", nameof(targetHost));
                }

                request.Add(3); // ATYP=DOMAINNAME
                request.Add((byte)name.Length);
                request.AddRange(name);
            }

            request.Add((byte)(targetPort >> 8));
            request.Add((byte)(targetPort & 0xFF));
            await stream.WriteAsync(request.ToArray(), token);

            var reply = await ReadExactlyAsync(stream, 4, token);
            if (reply[0] != 5)
            {
                throw new InvalidOperationException($"invalid SOCKS5 reply version {reply[0]}");
            }

            if (reply[1] != 0)
            {
                throw new SocksConnectException(reply[1]);
            }

            // Skip the bound address (BND.ADDR) and bound port (BND.PORT).
            var skip = reply[3] switch
            {
                1 => 4,
                4 => 16,
                3 => (await ReadExactlyAsync(stream, 1, token))[0] + 2,
                _ => throw new InvalidOperationException($"invalid SOCKS5 bound address type {reply[3]}"),
            };
            if (skip > 0)
            {
                await ReadExactlyAsync(stream, skip, token);
            }

            return stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), token);
            if (read == 0)
            {
                throw new EndOfStreamException("SOCKS5 proxy closed the connection early");
            }

            offset += read;
        }

        return buffer;
    }

    private sealed class Entry : IDisposable
    {
        private readonly SocksRegistry _owner;
        private int _disposed;

        public Entry(SocksRegistry owner, string scheme, string host, int port)
        {
            _owner = owner;
            Scheme = scheme;
            Host = host;
            Port = port;
        }

        public string Scheme { get; }
        public string Host { get; }
        public int Port { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Unregister(this);
            }
        }
    }
}

/// <summary>A SOCKS5 CONNECT failure carrying the REP code from the proxy.</summary>
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
