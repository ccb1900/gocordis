// Package socks adapts SOCKS proxy schemes to the HTTP service's proxy
// registration API. The accepted schemes are socks (alias of socks5),
// socks5/socks5h and socks4/socks4a; each maps to the matching h12.io/socks
// dialer, so a registration really dials instead of only producing a URL.
package socks

import (
	"context"
	"fmt"
	"net"

	exthttp "dynamic-runtime/extensions/http"
)

// ProxyURL forms the proxy URL consumed by http.Service.RegisterProxy. It
// accepts socks (mapped to socks5), socks5/socks5h, socks4 and socks4a. An
// empty address or an unknown scheme is an error.
func ProxyURL(scheme, addr string) (string, error) {
	if addr == "" {
		return "", fmt.Errorf("socks: empty proxy address for scheme %q", scheme)
	}
	switch scheme {
	case "socks", "socks5", "socks5h":
		return "socks5://" + addr, nil
	case "socks4":
		return "socks4://" + addr, nil
	case "socks4a":
		return "socks4a://" + addr, nil
	default:
		return "", fmt.Errorf("socks: unsupported proxy scheme %q", scheme)
	}
}

// Dialer returns a context-aware dial function backed by h12.io/socks for the
// given SOCKS proxy URL. socks and socks5h are mapped to socks5; socks4 and
// socks4a select their respective h12.io/socks protocols. Unknown schemes and
// malformed URLs are rejected.
func Dialer(proxyURL string) (func(context.Context, string, string) (net.Conn, error), error) {
	d, err := exthttp.NewSOCKSProxyDialer(proxyURL)
	if err != nil {
		return nil, err
	}
	return d, nil
}
