// Package socks adapts SOCKS proxy schemes to the HTTP service's proxy URL
// setting. It mirrors @cordisjs/http-socks: socks5/socks5h map to socks5:// and
// socks4/socks4a map to socks4:// (the "h"/"a" suffix selects remote DNS in the
// JS world; in Go the URL alone is enough to form the proxy configuration).
package socks

import "fmt"

// ProxyURL forms the proxy URL consumed by http.Service.SetProxy. It only
// accepts the four SOCKS schemes this extension registers; an empty address or
// an unknown scheme is an error.
func ProxyURL(scheme, addr string) (string, error) {
	if addr == "" {
		return "", fmt.Errorf("socks: empty proxy address for scheme %q", scheme)
	}
	switch scheme {
	case "socks5", "socks5h":
		return "socks5://" + addr, nil
	case "socks4", "socks4a":
		return "socks4://" + addr, nil
	default:
		return "", fmt.Errorf("socks: unsupported proxy scheme %q", scheme)
	}
}
