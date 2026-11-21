package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const validManifest = `id = "camera"
name = "Camera Plugin"
version = "1.0.0"
backend = "native"

[source]
kind = "builtin"
location = "builtin://camera"
`

func writeManifest(t *testing.T, body string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "plugin.toml")
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestDoctorValidManifest(t *testing.T) {
	var out bytes.Buffer
	if code := doctor(writeManifest(t, validManifest), &out); code != 0 {
		t.Fatalf("doctor exited %d: %s", code, out.String())
	}
	if !strings.Contains(out.String(), "0 errors") {
		t.Fatalf("unexpected doctor output: %s", out.String())
	}
}

func TestDoctorWarningOnlyExitsZero(t *testing.T) {
	body := strings.Replace(validManifest, `location = "builtin://camera"`, `location = ""`, 1)
	var out bytes.Buffer
	if code := doctor(writeManifest(t, body), &out); code != 0 {
		t.Fatalf("warning-only manifest exited %d: %s", code, out.String())
	}
	if !strings.Contains(out.String(), "EMPTY_SOURCE_LOCATION") || !strings.Contains(out.String(), "1 warnings") {
		t.Fatalf("warning diagnostic missing: %s", out.String())
	}
}

func TestDoctorErrorExitsNonZero(t *testing.T) {
	body := strings.Replace(validManifest, `id = "camera"`, `id = ""`, 1)
	var out bytes.Buffer
	if code := doctor(writeManifest(t, body), &out); code != 1 {
		t.Fatalf("error-severity diagnostic exit = %d, want 1", code)
	}
	if !strings.Contains(out.String(), "MISSING_ID") {
		t.Fatalf("error diagnostic missing: %s", out.String())
	}
}

func TestDoctorParseFailureExitsNonZero(t *testing.T) {
	path := writeManifest(t, "this is not = = toml")
	var out bytes.Buffer
	if code := doctor(path, &out); code == 0 {
		t.Fatal("parse failure did not produce a non-zero exit")
	}
	if !strings.Contains(out.String(), "PARSE_ERROR") {
		t.Fatalf("parse diagnostic missing: %s", out.String())
	}
}

func TestDoctorUnsupportedExtension(t *testing.T) {
	path := filepath.Join(t.TempDir(), "plugin.yaml")
	if err := os.WriteFile(path, []byte("id: x\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	var out bytes.Buffer
	if code := doctor(path, &out); code == 0 {
		t.Fatal("unsupported extension did not produce a non-zero exit")
	}
}

func TestDoctorCommandDispatch(t *testing.T) {
	path := writeManifest(t, validManifest)
	var out, errOut bytes.Buffer
	if code := run([]string{"doctor", path}, &out, &errOut); code != 0 {
		t.Fatalf("doctor subcommand exited %d: %s%s", code, out.String(), errOut.String())
	}
}
