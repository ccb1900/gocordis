package webui

import (
	"bytes"
	"log"
	"strings"
	"testing"
)

func TestValidateListenAddrLoopbackByDefault(t *testing.T) {
	for _, addr := range []string{
		"127.0.0.1:8170",
		"127.10.2.3:9000", // whole 127/8 is loopback
		"localhost:8170",
		"[::1]:8170",
		"[::1%lo0]:8170",
	} {
		if err := ValidateListenAddr(addr, false); err != nil {
			t.Errorf("ValidateListenAddr(%q, false) = %v, want allowed", addr, err)
		}
	}
}

func TestValidateListenAddrRejectsRemoteWithoutOptIn(t *testing.T) {
	for _, addr := range []string{
		"0.0.0.0:8170",
		":8170", // net/http treats an empty host as all interfaces
		"192.168.1.20:8170",
		"[::]:8170",
		"example.com:8170",
		"",
		"8170", // not host:port
	} {
		err := ValidateListenAddr(addr, false)
		if err == nil {
			t.Errorf("ValidateListenAddr(%q, false) = nil, want rejection", addr)
			continue
		}
		if !strings.Contains(err.Error(), "auth") || !strings.Contains(err.Error(), "isolat") {
			t.Errorf("ValidateListenAddr(%q, false) error %q must state that the deployer supplies auth and network isolation", addr, err)
		}
	}
}

func TestValidateListenAddrRemoteOptIn(t *testing.T) {
	for _, addr := range []string{"0.0.0.0:8170", ":8170", "[::]:8170", "192.168.1.20:8170"} {
		if err := ValidateListenAddr(addr, true); err != nil {
			t.Errorf("ValidateListenAddr(%q, true) = %v, want allowed with a warning", addr, err)
		}
	}
}

// TestSilentValidationDoesNotWarn keeps the secondary listener-boundary gate
// quiet: startup already emitted the single operator warning.
func TestSilentValidationDoesNotWarn(t *testing.T) {
	var buf bytes.Buffer
	prev := log.Writer()
	log.SetOutput(&buf)
	defer log.SetOutput(prev)
	if err := ValidateListenAddrSilent("0.0.0.0:8170", true); err != nil {
		t.Fatal(err)
	}
	if buf.Len() != 0 {
		t.Fatalf("silent validation logged %q", buf.String())
	}
	if err := ValidateListenAddr("0.0.0.0:8170", true); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(buf.String(), "WARNING") {
		t.Fatalf("startup validation must warn, got %q", buf.String())
	}
}
