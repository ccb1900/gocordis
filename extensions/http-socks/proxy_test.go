package socks

import "testing"

func TestProxyURL(t *testing.T) {
	cases := []struct {
		scheme string
		addr   string
		want   string
	}{
		{"socks5", "127.0.0.1:1080", "socks5://127.0.0.1:1080"},
		{"socks5h", "proxy.example:1080", "socks5://proxy.example:1080"},
		{"socks4", "10.0.0.1:1080", "socks4://10.0.0.1:1080"},
		{"socks4a", "proxy.example:1080", "socks4://proxy.example:1080"},
	}
	for _, tc := range cases {
		got, err := ProxyURL(tc.scheme, tc.addr)
		if err != nil {
			t.Errorf("ProxyURL(%q, %q): %v", tc.scheme, tc.addr, err)
			continue
		}
		if got != tc.want {
			t.Errorf("ProxyURL(%q, %q) = %q, want %q", tc.scheme, tc.addr, got, tc.want)
		}
	}
}

func TestProxyURLRejectsInvalidInput(t *testing.T) {
	cases := []struct {
		scheme string
		addr   string
	}{
		{"http", "127.0.0.1:8080"},
		{"socks5://", "127.0.0.1:1080"},
		{"", "127.0.0.1:1080"},
		{"socks5", ""},
		{"socks5h", ""},
		{"", ""},
	}
	for _, tc := range cases {
		if got, err := ProxyURL(tc.scheme, tc.addr); err == nil {
			t.Errorf("ProxyURL(%q, %q) = %q, want error", tc.scheme, tc.addr, got)
		}
	}
}
