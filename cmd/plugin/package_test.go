package main

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"reflect"
	"regexp"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/market"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/runtime"
)

func writePluginDir(t *testing.T) string {
	t.Helper()
	dir := filepath.Join(t.TempDir(), "plugin")
	if err := os.MkdirAll(filepath.Join(dir, "assets"), 0o755); err != nil {
		t.Fatal(err)
	}
	files := map[string]string{
		"plugin.toml":     validManifest,
		"component.go":    "package plugin\n",
		"assets/data.bin": "binary-ish payload\n",
	}
	for name, body := range files {
		if err := os.WriteFile(filepath.Join(dir, filepath.FromSlash(name)), []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	return dir
}

func readBundle(t *testing.T, path string) ([]string, map[string]string) {
	t.Helper()
	f, err := os.Open(path)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	gz, err := gzip.NewReader(f)
	if err != nil {
		t.Fatal(err)
	}
	defer gz.Close()
	var names []string
	contents := make(map[string]string)
	tr := tar.NewReader(gz)
	for {
		hdr, err := tr.Next()
		if err == io.EOF {
			break
		}
		if err != nil {
			t.Fatal(err)
		}
		body, err := io.ReadAll(tr)
		if err != nil {
			t.Fatal(err)
		}
		names = append(names, hdr.Name)
		contents[hdr.Name] = string(body)
	}
	return names, contents
}

func packagedManifest(t *testing.T, dir string) pluginmeta.Manifest {
	t.Helper()
	data, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
	if err != nil {
		t.Fatal(err)
	}
	m, err := pluginmeta.Parse(data, "toml")
	if err != nil {
		t.Fatal(err)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Fatalf("packaged manifest is invalid: %v", err)
	}
	return m
}

func checksumOf(t *testing.T, dir string) string {
	t.Helper()
	m := packagedManifest(t, dir)
	if m.Package == nil {
		t.Fatal("packaged manifest has no package checksum")
	}
	return m.Package.Checksum
}

func TestPackageBundleContentsAndChecksum(t *testing.T) {
	dir := writePluginDir(t)
	out := filepath.Join(t.TempDir(), "camera.tar.gz")
	gotOut, err := Package(dir, out)
	if err != nil {
		t.Fatal(err)
	}
	if gotOut != out {
		t.Fatalf("bundle path = %q, want %q", gotOut, out)
	}

	sum := checksumOf(t, dir)
	if !regexp.MustCompile(`^[0-9a-f]{64}$`).MatchString(sum) {
		t.Fatalf("source.checksum is not a sha256 hex digest: %q", sum)
	}

	names, contents := readBundle(t, out)
	wantNames := []string{"plugin.toml", "assets/data.bin", "component.go"}
	if !reflect.DeepEqual(names, wantNames) {
		t.Fatalf("bundle entries = %v, want %v", names, wantNames)
	}
	manifestData, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
	if err != nil {
		t.Fatal(err)
	}
	if contents["plugin.toml"] != string(manifestData) {
		t.Error("bundled manifest differs from updated plugin.toml")
	}
	if contents["component.go"] != "package plugin\n" || contents["assets/data.bin"] != "binary-ish payload\n" {
		t.Errorf("bundled artifact contents are wrong: %v", contents)
	}
	if !strings.Contains(contents["plugin.toml"], sum) {
		t.Error("bundled manifest does not contain the written checksum")
	}
}

func TestPackageChecksumChangesWithArtifact(t *testing.T) {
	dir := writePluginDir(t)
	out := filepath.Join(t.TempDir(), "a.tar.gz")
	if _, err := Package(dir, out); err != nil {
		t.Fatal(err)
	}
	first := checksumOf(t, dir)
	if err := os.WriteFile(filepath.Join(dir, "component.go"), []byte("package plugin // changed\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := Package(dir, out); err != nil {
		t.Fatal(err)
	}
	if second := checksumOf(t, dir); second == first {
		t.Fatal("checksum did not change when an artifact changed")
	}
}

func TestPackageIsDeterministic(t *testing.T) {
	newDir := func() string {
		dir := filepath.Join(t.TempDir(), "plugin")
		if err := os.MkdirAll(filepath.Join(dir, "assets"), 0o755); err != nil {
			t.Fatal(err)
		}
		for name, body := range map[string]string{
			"plugin.toml":     validManifest,
			"z.go":            "package plugin\n",
			"a.go":            "package plugin\n",
			"assets/data.bin": "payload\n",
		} {
			if err := os.WriteFile(filepath.Join(dir, filepath.FromSlash(name)), []byte(body), 0o644); err != nil {
				t.Fatal(err)
			}
		}
		return dir
	}
	outDir := t.TempDir()
	one, two := filepath.Join(outDir, "one.tar.gz"), filepath.Join(outDir, "two.tar.gz")
	if _, err := Package(newDir(), one); err != nil {
		t.Fatal(err)
	}
	if _, err := Package(newDir(), two); err != nil {
		t.Fatal(err)
	}
	a, err := os.ReadFile(one)
	if err != nil {
		t.Fatal(err)
	}
	b, err := os.ReadFile(two)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(a, b) {
		t.Fatal("identical plugin directories produced different bundles")
	}
}

func TestPackageRefusesInvalidManifest(t *testing.T) {
	for _, body := range []string{`id = ""`, `not valid = = toml`} {
		dir := t.TempDir()
		manifest := filepath.Join(dir, "plugin.toml")
		if err := os.WriteFile(manifest, []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
		out := filepath.Join(t.TempDir(), "bundle.tar.gz")
		if _, err := Package(dir, out); err == nil {
			t.Errorf("invalid manifest %q was accepted", body)
		}
		if _, err := os.Stat(out); !os.IsNotExist(err) {
			t.Errorf("bundle was written for invalid manifest %q: %v", body, err)
		}
		data, _ := os.ReadFile(manifest)
		if string(data) != body {
			t.Errorf("invalid manifest was modified: %q", data)
		}
	}
}

func TestPackageRejectsEscapePaths(t *testing.T) {
	dir := writePluginDir(t)
	outside := filepath.Join(t.TempDir(), "secret.txt")
	if err := os.WriteFile(outside, []byte("secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	link := filepath.Join(dir, "escape.go")
	if err := os.Symlink(outside, link); err != nil {
		t.Skipf("symlinks unavailable: %v", err)
	}
	out := filepath.Join(t.TempDir(), "bundle.tar.gz")
	if _, err := Package(dir, out); err == nil {
		t.Fatal("symlink escaping the plugin directory was accepted")
	} else if !strings.Contains(err.Error(), "symlink") {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, err := os.Stat(out); !os.IsNotExist(err) {
		t.Fatalf("bundle leaked after escape rejection: %v", err)
	}
}

func TestValidateBundleNameRejectsEscape(t *testing.T) {
	for _, name := range []string{"../evil", "a/../b", "/abs", "a/../../b", `win\..\evil`} {
		if err := validateBundleName(name); err == nil {
			t.Errorf("%q was accepted", name)
		}
	}
	for _, name := range []string{"plugin.toml", "assets/data.bin", "a.go"} {
		if err := validateBundleName(name); err != nil {
			t.Errorf("%q was rejected: %v", name, err)
		}
	}
}

func TestPackageDefaultOutputExcludesOwnBundle(t *testing.T) {
	dir := writePluginDir(t)
	if _, err := Package(dir, ""); err != nil {
		t.Fatal(err)
	}
	wantOut := filepath.Join(dir, "camera.tar.gz")
	if _, err := os.Stat(wantOut); err != nil {
		t.Fatalf("default output %s missing: %v", wantOut, err)
	}
	first := checksumOf(t, dir)
	if _, err := Package(dir, ""); err != nil {
		t.Fatal(err)
	}
	if second := checksumOf(t, dir); second != first {
		t.Fatalf("checksum changed on repackage: %s -> %s", first, second)
	}
	names, _ := readBundle(t, wantOut)
	for _, name := range names {
		if strings.HasSuffix(name, ".tar.gz") {
			t.Fatalf("bundle contains itself: %v", names)
		}
	}
}

func TestPackageCommandDispatch(t *testing.T) {
	dir := writePluginDir(t)
	out := filepath.Join(t.TempDir(), "cli.tar.gz")
	var outBuf, errBuf bytes.Buffer
	if code := run([]string{"package", "--out", out, dir}, &outBuf, &errBuf); code != 0 {
		t.Fatalf("package --out exited %d: %s%s", code, outBuf.String(), errBuf.String())
	}
	if _, err := os.Stat(out); err != nil {
		t.Fatalf("no bundle from CLI: %v", err)
	}
	if !strings.Contains(outBuf.String(), out) {
		t.Fatalf("CLI did not report bundle path: %s", outBuf.String())
	}
	var out2, err2 bytes.Buffer
	if code := run([]string{"package", dir}, &out2, &err2); code != 0 {
		t.Fatalf("package with default out exited %d: %s%s", code, out2.String(), err2.String())
	}
}

func TestPackageCommandRejectsBadArgs(t *testing.T) {
	var out, errOut bytes.Buffer
	if code := run([]string{"package"}, &out, &errOut); code == 0 {
		t.Error("package without a directory was accepted")
	}
	if code := run([]string{"package", "a", "b"}, &out, &errOut); code == 0 {
		t.Error("package with two directories was accepted")
	}
}

// --- unified checksum contract -------------------------------------------
// source.checksum = sha256 of the ONE artifact at source.location.
// package.checksum = deterministic aggregate over all packaged artifacts.

const packageManifest = `id = "camera"
name = "Camera Plugin"
version = "1.0.0"
backend = "wasm"
module_type = "industrial.camera"
[compatibility]
runtime = ">=1.0.0"
api = ">=1.0.0"
[source]
kind = "wasm"
location = "module.wasm"
`

func writePluginDirWithArtifact(t *testing.T, module string) string {
	t.Helper()
	dir := filepath.Join(t.TempDir(), "camera")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		t.Fatal(err)
	}
	files := map[string]string{
		"plugin.toml":  packageManifest,
		"module.wasm":  module,
		"component.go": "package plugin\n",
	}
	for name, body := range files {
		if err := os.WriteFile(filepath.Join(dir, name), []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	return dir
}

func sha256Hex(data string) string {
	sum := sha256.Sum256([]byte(data))
	return hex.EncodeToString(sum[:])
}

func TestPackageWritesSourceAndPackageChecksums(t *testing.T) {
	dir := writePluginDirWithArtifact(t, "wasm-bytes")
	out := filepath.Join(t.TempDir(), "camera.tar.gz")
	if _, err := Package(dir, out); err != nil {
		t.Fatal(err)
	}

	m := packagedManifest(t, dir)
	wantSource := sha256Hex("wasm-bytes")
	if m.Source.Checksum != wantSource {
		t.Fatalf("source.checksum = %q, want single-artifact digest %q", m.Source.Checksum, wantSource)
	}

	// The aggregate is length-framed over sorted artifact names+contents.
	h := sha256.New()
	for _, f := range []struct{ name, body string }{
		{"component.go", "package plugin\n"},
		{"module.wasm", "wasm-bytes"},
	} {
		fmt.Fprintf(h, "%d:%s\x00", len(f.name), f.name)
		h.Write([]byte(f.body))
	}
	wantPackage := hex.EncodeToString(h.Sum(nil))
	if m.Package == nil || m.Package.Checksum != wantPackage {
		t.Fatalf("package.checksum = %+v, want aggregate %q", m.Package, wantPackage)
	}
	if m.Package.Checksum == m.Source.Checksum {
		t.Fatal("source and package checksums must not be the same digest")
	}
}

// Real end-to-end path: plugin dir -> Package() -> Market Scan -> Market Install.
// A market install verifies source.checksum against the single artifact, so a
// successful install proves the package tool wrote the right field/value.
func TestPackageToMarketInstallEndToEnd(t *testing.T) {
	dir := writePluginDirWithArtifact(t, "real-wasm-module")
	out := filepath.Join(t.TempDir(), "camera.tar.gz")
	if _, err := Package(dir, out); err != nil {
		t.Fatalf("Package: %v", err)
	}

	inner, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = inner.Close(context.Background()) })
	reg := config.NewFactoryRegistry()
	if err := reg.Register("industrial.camera", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &e2eMarker{}, nil
	})); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(inner, reg)
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(ctx)
	})
	svc, err := market.NewService(market.Options{
		Controller:     ctrl,
		CatalogDirs:    []string{dir},
		RuntimeVersion: "1.0.0",
		APIVersion:     "1.0.0",
	})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	scan, err := market.Scan([]string{dir}, "1.0.0", "1.0.0", "local")
	if err != nil {
		t.Fatalf("Scan: %v", err)
	}
	if len(scan.Entries) != 1 || scan.Entries[0].ID != "camera" || !scan.Entries[0].Compatible {
		t.Fatalf("Scan entries = %+v", scan.Entries)
	}
	cat, err := svc.Catalog(ctx)
	if err != nil || len(cat.Entries) != 1 || cat.Entries[0].Installed {
		t.Fatalf("Catalog before install = %+v, err %v", cat, err)
	}
	if err := svc.Install(ctx, "camera"); err != nil {
		t.Fatalf("Install with generated manifest/checksum: %v", err)
	}
	cat, err = svc.Catalog(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if len(cat.Entries) != 1 || !cat.Entries[0].Installed {
		t.Fatalf("camera should be installed: %+v", cat.Entries)
	}
	found := false
	for _, owned := range ctrl.Owned() {
		if owned.ID == "camera" {
			found = true
		}
	}
	if !found {
		t.Fatal("Install did not produce a controller declaration for camera")
	}
}

type e2eMarker struct{}

func (*e2eMarker) Name() string                  { return "camera" }
func (*e2eMarker) Inject() []runtime.Dependency  { return nil }
func (*e2eMarker) Provide() []runtime.Capability { return nil }
func (*e2eMarker) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}
