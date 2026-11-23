package webui

import (
	"fmt"
	"log"
	"net"
	"strings"
)

// ValidateListenAddr is the reusable remote-listen safety gate for console Web
// UI hosts. Loopback addresses are accepted by default; any non-loopback
// address (including an empty host, which net/http treats as all interfaces)
// requires an explicit opt-in from the deployer.
//
// The Web UI ships NO built-in authentication by design. When remote listening
// is opted in, the deployer MUST provide authentication and network isolation
// (firewall / VPN / reverse proxy); this helper emits that warning loudly.
func ValidateListenAddr(addr string, allowRemote bool) error {
	return validateListenAddr(addr, allowRemote, true)
}

// ValidateListenAddrSilent applies the same gate without emitting the remote
// warning. Use it at a secondary boundary (e.g. just before ListenAndServe)
// when startup validation has already warned the operator.
func ValidateListenAddrSilent(addr string, allowRemote bool) error {
	return validateListenAddr(addr, allowRemote, false)
}

func validateListenAddr(addr string, allowRemote, warn bool) error {
	if strings.TrimSpace(addr) == "" {
		return fmt.Errorf("invalid listen address %q: empty address listens on all interfaces; webui has no built-in authentication — deployer must supply authentication and network isolation and explicitly opt in to remote listening", addr)
	}
	host, port, err := net.SplitHostPort(addr)
	if err != nil {
		return fmt.Errorf("invalid listen address %q: %v; webui has no built-in authentication — deployer must supply authentication and network isolation for any remote access", addr, err)
	}
	if strings.TrimSpace(port) == "" {
		return fmt.Errorf("invalid listen address %q: missing port; webui has no built-in authentication — deployer must supply authentication and network isolation for any remote access", addr)
	}
	if isLoopbackHost(host) {
		return nil
	}
	if !allowRemote {
		return fmt.Errorf("refusing non-loopback listen address %q: webui has NO built-in authentication; deployer must supply authentication and network isolation (firewall/VPN/reverse proxy) and explicitly opt in to remote listening", addr)
	}
	if warn {
		log.Printf("WARNING: webui listening on non-loopback address %q: NO built-in authentication — deployer MUST supply authentication and network isolation (firewall/VPN/reverse proxy)", addr)
	}
	return nil
}

// RequireLoopback is the stricter spelling of ValidateListenAddr with an
// explicit-allow switch; both names are kept for call-site clarity.
func RequireLoopback(addr string, explicitAllow bool) error {
	return ValidateListenAddr(addr, explicitAllow)
}

func isLoopbackHost(host string) bool {
	h := strings.Trim(strings.TrimSpace(host), "[]")
	if h == "" {
		return false // net/http binds all interfaces for an empty host
	}
	if i := strings.LastIndex(h, "%"); i >= 0 { // IPv6 zone, e.g. ::1%lo0
		h = h[:i]
	}
	if strings.EqualFold(h, "localhost") {
		return true
	}
	ip := net.ParseIP(h)
	return ip != nil && ip.IsLoopback()
}
