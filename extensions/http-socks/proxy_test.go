package socks

import (
	"context"
	"io"
	"net"
	"testing"
	"time"
)

func TestProxyURL(t *testing.T) {
	cases := []struct {
		scheme string
		addr   string
		want   string
	}{
		{"socks", "127.0.0.1:1080", "socks5://127.0.0.1:1080"},
		{"socks5", "127.0.0.1:1080", "socks5://127.0.0.1:1080"},
		{"socks5h", "proxy.example:1080", "socks5://proxy.example:1080"},
		{"socks4", "10.0.0.1:1080", "socks4://10.0.0.1:1080"},
		{"socks4a", "proxy.example:1080", "socks4a://proxy.example:1080"},
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

func TestDialerSupportedSchemes(t *testing.T) {
	for _, raw := range []string{
		"socks://127.0.0.1:1080",
		"socks5://127.0.0.1:1080",
		"socks5h://proxy.example:1080",
		"socks4://10.0.0.1:1080",
		"socks4a://proxy.example:1080",
		"socks5://user:pass@127.0.0.1:1080?timeout=5s",
	} {
		d, err := Dialer(raw)
		if err != nil {
			t.Errorf("Dialer(%q): %v", raw, err)
			continue
		}
		if d == nil {
			t.Errorf("Dialer(%q) returned nil dialer", raw)
		}
	}
}

func TestDialerRejectsInvalidInput(t *testing.T) {
	for _, raw := range []string{
		"http://127.0.0.1:8080",
		"ftp://127.0.0.1:21",
		"://bad",
		"socks5://",
		"socks5://user@127.0.0.1:1080", // password missing
		"socks5://127.0.0.1:1080?timeout=abc",
		"",
	} {
		if d, err := Dialer(raw); err == nil {
			t.Errorf("Dialer(%q) returned a dialer (%v), want error", raw, d != nil)
		}
	}
}

// fakeSOCKS is a minimal local SOCKS server used to prove the dialer really
// speaks the protocol. It records the destination host the dialer requested
// and echoes application bytes back to the caller.
type fakeSOCKS struct {
	addr     string
	seenHost chan string
	version  byte
	t        *testing.T
}

func startFakeSOCKS(t *testing.T, version byte) *fakeSOCKS {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	f := &fakeSOCKS{addr: ln.Addr().String(), seenHost: make(chan string, 2), version: version, t: t}
	t.Cleanup(func() { _ = ln.Close() })
	go func() {
		for {
			conn, err := ln.Accept()
			if err != nil {
				return
			}
			go f.serve(conn)
		}
	}()
	return f
}

func (f *fakeSOCKS) serve(conn net.Conn) {
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(3 * time.Second))
	head := make([]byte, 2)
	if _, err := io.ReadFull(conn, head); err != nil {
		return
	}
	switch head[0] {
	case 5:
		f.serve5(conn, head)
	case 4:
		f.serve4(conn, head)
	}
}

func (f *fakeSOCKS) serve5(conn net.Conn, head []byte) {
	methods := make([]byte, int(head[1]))
	if _, err := io.ReadFull(conn, methods); err != nil {
		return
	}
	if _, err := conn.Write([]byte{5, 0}); err != nil { // no auth
		return
	}
	req := make([]byte, 4)
	if _, err := io.ReadFull(conn, req); err != nil {
		return
	}
	var host string
	switch req[3] {
	case 1: // IPv4
		b := make([]byte, 4)
		if _, err := io.ReadFull(conn, b); err != nil {
			return
		}
		host = net.IP(b).String()
	case 3: // domain name (remote DNS)
		lb := make([]byte, 1)
		if _, err := io.ReadFull(conn, lb); err != nil {
			return
		}
		hb := make([]byte, int(lb[0]))
		if _, err := io.ReadFull(conn, hb); err != nil {
			return
		}
		host = string(hb)
	default:
		return
	}
	port := make([]byte, 2)
	if _, err := io.ReadFull(conn, port); err != nil {
		return
	}
	f.seenHost <- host
	_, _ = conn.Write([]byte{5, 0, 0, 1, 0, 0, 0, 0, 0, 0})
	_, _ = io.Copy(conn, conn)
}

func (f *fakeSOCKS) serve4(conn net.Conn, head []byte) {
	rest := make([]byte, 6) // port(2) + ip(4)
	if _, err := io.ReadFull(conn, rest); err != nil {
		return
	}
	ip := net.IP(rest[2:6])
	host := ""
	if ip.Equal(net.IPv4(0, 0, 0, 1)) { // SOCKS4a: user id (empty) then hostname
		_ = readCString(conn)
		host = readCString(conn)
	} else {
		host = ip.String()
		readCString(conn) // user id
	}
	f.seenHost <- host
	_, _ = conn.Write([]byte{0, 90, 0, 0, 0, 0, 0, 0})
	_, _ = io.Copy(conn, conn)
}

func readCString(conn net.Conn) string {
	var b []byte
	buf := make([]byte, 1)
	for i := 0; i < 512; i++ {
		if _, err := conn.Read(buf); err != nil || buf[0] == 0 {
			break
		}
		b = append(b, buf[0])
	}
	return string(b)
}

// dialThrough connects via the dialer and verifies the echo path.
func dialThrough(t *testing.T, proxyURL, target string, wantHost string, seen <-chan string) {
	t.Helper()
	d, err := Dialer(proxyURL)
	if err != nil {
		t.Fatalf("Dialer(%q): %v", proxyURL, err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	conn, err := d(ctx, "tcp", target)
	if err != nil {
		t.Fatalf("dial %q via %q: %v", target, proxyURL, err)
	}
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(3 * time.Second))
	if _, err := conn.Write([]byte("ping")); err != nil {
		t.Fatalf("write: %v", err)
	}
	buf := make([]byte, 4)
	if _, err := io.ReadFull(conn, buf); err != nil {
		t.Fatalf("read: %v", err)
	}
	if string(buf) != "ping" {
		t.Fatalf("echo = %q, want ping", buf)
	}
	select {
	case host := <-seen:
		if host != wantHost {
			t.Errorf("proxy saw host %q, want %q", host, wantHost)
		}
	case <-ctx.Done():
		t.Error("proxy never recorded destination host")
	}
}

func TestDialerSOCKS5Integration(t *testing.T) {
	f := startFakeSOCKS(t, 5)
	for _, raw := range []string{
		"socks://" + f.addr, // alias mapped to socks5
		"socks5://" + f.addr,
		"socks5h://" + f.addr, // mapped to socks5
	} {
		dialThrough(t, raw, "echo.example:80", "echo.example", f.seenHost)
	}
}

func TestDialerSOCKS4Integration(t *testing.T) {
	f := startFakeSOCKS(t, 4)
	// SOCKS4 resolves locally: use a numeric target.
	dialThrough(t, "socks4://"+f.addr, "127.0.0.1:80", "127.0.0.1", f.seenHost)
	// SOCKS4a forwards the hostname to the proxy.
	dialThrough(t, "socks4a://"+f.addr, "echo.example:80", "echo.example", f.seenHost)
}
