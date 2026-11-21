package main

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

func TestBuildCommandNameAliasesAndVersionFlag(t *testing.T) {
	if got := Build("0.0.1").Name(); got != "run" {
		t.Fatalf("Build().Name() = %q, want run", got)
	}

	var buf bytes.Buffer
	cmd := buildWithWriter("1.2.3", &buf)
	aliases := cmd.Aliases()
	if len(aliases) != 1 || aliases[0] != "r" {
		t.Fatalf("Aliases() = %v, want [r]", aliases)
	}

	if err := cmd.Execute(context.Background(), nil, map[string]string{"version": "true"}); err != nil {
		t.Fatalf("Execute(--version): %v", err)
	}
	if got := buf.String(); !strings.Contains(got, "1.2.3") || !strings.Contains(got, "cli-cordis") {
		t.Fatalf("version output = %q, want cli-cordis and 1.2.3", got)
	}

	buf.Reset()
	if err := cmd.Execute(context.Background(), nil, map[string]string{"version": "false"}); err != nil {
		t.Fatalf("Execute(no --version): %v", err)
	}
	if got := buf.String(); got != "" {
		t.Fatalf("output without --version = %q, want empty", got)
	}
}

func TestVersionCommandNameAliasesAndOutput(t *testing.T) {
	if got := VersionCommand("0.0.1").Name(); got != "version" {
		t.Fatalf("VersionCommand().Name() = %q, want version", got)
	}

	var buf bytes.Buffer
	cmd := versionWithWriter("2.0.0", &buf)
	aliases := cmd.Aliases()
	if len(aliases) != 1 || aliases[0] != "v" {
		t.Fatalf("Aliases() = %v, want [v]", aliases)
	}
	if err := cmd.Execute(context.Background(), nil, nil); err != nil {
		t.Fatalf("Execute: %v", err)
	}
	if got := buf.String(); !strings.Contains(got, "2.0.0") {
		t.Fatalf("version output = %q, want 2.0.0", got)
	}
}
