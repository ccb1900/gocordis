package main

import (
	"strings"
	"testing"
)

func TestDefaultListenAddressIsLoopback(t *testing.T) {
	t.Setenv("GOCORDIS_ADDR", "")
	t.Setenv("GOCORDIS_ALLOW_REMOTE", "")
	if got := address(); got != defaultAddress {
		t.Fatalf("address() = %q, want %q", got, defaultAddress)
	}
	got, err := listenAddress()
	if err != nil {
		t.Fatal(err)
	}
	if got != defaultAddress {
		t.Fatalf("listenAddress() = %q, want %q", got, defaultAddress)
	}
}

func TestRemoteListenRequiresExplicitOptIn(t *testing.T) {
	t.Setenv("GOCORDIS_ADDR", "0.0.0.0:8170")
	t.Setenv("GOCORDIS_ALLOW_REMOTE", "")
	_, err := listenAddress()
	if err == nil {
		t.Fatal("remote listen without opt-in must fail")
	}
	if !strings.Contains(err.Error(), "GOCORDIS_ALLOW_REMOTE") {
		t.Fatalf("error %q must name the opt-in env", err)
	}
}

func TestRemoteListenOptIn(t *testing.T) {
	t.Setenv("GOCORDIS_ADDR", "0.0.0.0:8170")
	t.Setenv("GOCORDIS_ALLOW_REMOTE", "1")
	got, err := listenAddress()
	if err != nil {
		t.Fatalf("remote listen with opt-in: %v", err)
	}
	if got != "0.0.0.0:8170" {
		t.Fatalf("listenAddress() = %q", got)
	}
}
