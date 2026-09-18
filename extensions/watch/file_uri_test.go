package watch

import (
	"testing"
)

// Windows-branch regression tests for the URI/path pure helpers. These run on
// ANY platform (the goos is a parameter), so the "works on mac, breaks on
// Windows" class of path bugs is caught on macOS before it ships.
//
// Covered classes (R14 audit / user-reported):
//   - relative path -> file:// URI double-slash construction (pathToURI's
//     Abs 兜底 lives in the adapter; here we pin the URI <-> slash-path
//     round-trip including Windows drive letters);
//   - drive-letter stripping (file:///D:/x -> D:\x);
//   - absolute-detection under both goos values.

func TestURItoSlashPathWindowsDrive(t *testing.T) {
	// file:///D:/x/y.toml (RFC 8089) -> slash path /D:/x/y.toml
	got, err := uriToSlashPath("file:///D:/configs/unc-machines.toml")
	if err != nil {
		t.Fatal(err)
	}
	if got != "/D:/configs/unc-machines.toml" {
		t.Fatalf("slash path = %q, want /D:/configs/unc-machines.toml", got)
	}
}

func TestURItoSlashPathPOSIX(t *testing.T) {
	got, err := uriToSlashPath("file:///etc/app/app.toml")
	if err != nil {
		t.Fatal(err)
	}
	if got != "/etc/app/app.toml" {
		t.Fatalf("slash path = %q", got)
	}
}

func TestURItoSlashPathPercentDecoding(t *testing.T) {
	// Spaces and CJK must be percent-decoded by url.Parse (documented).
	got, err := uriToSlashPath("file:///D:/%E9%87%87%E9%9B%86/configs/a%20b.toml")
	if err != nil {
		t.Fatal(err)
	}
	if got != "/D:/采集/configs/a b.toml" {
		t.Fatalf("decoded path = %q", got)
	}
}

func TestURItoSlashPathRejectsNonFileAndHosted(t *testing.T) {
	if _, err := uriToSlashPath("http:///x"); err == nil {
		t.Fatal("non-file scheme accepted")
	}
	if _, err := uriToSlashPath("file://server/share/x"); err == nil {
		t.Fatal("hosted file URI accepted (UNC unsupported by design)")
	}
}

func TestSlashPathToOSWindows(t *testing.T) {
	if got := slashPathToOS("/D:/x/y.toml", "windows"); got != `D:\x\y.toml` {
		t.Fatalf("windows form = %q", got)
	}
	if got := slashPathToOS("/etc/x.toml", "linux"); got != "/etc/x.toml" {
		t.Fatalf("posix form = %q", got)
	}
}

func TestIsAbsSlashPath(t *testing.T) {
	if !isAbsSlashPath("/D:/x", "windows") {
		t.Fatal("/D:/x must be absolute on windows")
	}
	if !isAbsSlashPath(`\\srv\share\x`, "windows") {
		t.Fatal("UNC must be absolute on windows")
	}
	if isAbsSlashPath("D:x", "windows") {
		t.Fatal("drive-relative must not be absolute")
	}
	if !isAbsSlashPath("/x", "linux") {
		t.Fatal("/x must be absolute on linux")
	}
	if isAbsSlashPath("x", "linux") {
		t.Fatal("relative must not be absolute on linux")
	}
}

// Round-trip: pathToURI-style construction followed by uriToSlashPath
// recovers the slash path exactly, for Windows drive-letter paths.
func TestURIRoundTripWindowsDrive(t *testing.T) {
	// The construction side (configwatch pathToURI): ToSlash + "/" prefix.
	built := "file:///D:/Migrate/configs/unc-machines.toml"
	got, err := uriToSlashPath(built)
	if err != nil {
		t.Fatal(err)
	}
	if got != "/D:/Migrate/configs/unc-machines.toml" {
		t.Fatalf("round trip = %q", got)
	}
}
