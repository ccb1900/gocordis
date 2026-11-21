package main

import (
	"context"
	"go/parser"
	"go/token"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"testing"
	"time"
)

func TestGenerateScaffold(t *testing.T) {
	dir := t.TempDir()
	if err := Generate(dir, "example.com/myapp"); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"go.mod", "manifest.toml", "main.go", "internal/app/component.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); err != nil {
			t.Fatalf("missing %s: %v", name, err)
		}
	}
	mod, err := os.ReadFile(filepath.Join(dir, "go.mod"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(mod), "module example.com/myapp") {
		t.Fatalf("go.mod: %s", mod)
	}
	manifest, err := os.ReadFile(filepath.Join(dir, "manifest.toml"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(manifest), `[[components]]`) || !strings.Contains(string(manifest), "enabled = true") {
		t.Fatalf("manifest.toml: %s", manifest)
	}
	main, err := os.ReadFile(filepath.Join(dir, "main.go"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(main), "config.NewController") {
		t.Fatalf("main.go lacks runtime composition root: %s", main)
	}
}

func TestGenerateRejectsInvalidInput(t *testing.T) {
	if err := Generate(t.TempDir(), ""); err == nil {
		t.Fatal("empty module path accepted")
	}
	if err := Generate(t.TempDir(), "bad path"); err == nil {
		t.Fatal("invalid module path accepted")
	}
}

func TestGeneratedGoFilesParse(t *testing.T) {
	dir := t.TempDir()
	if err := Generate(dir, "example.com/generated"); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"main.go", "internal/app/component.go"} {
		if _, err := parser.ParseFile(token.NewFileSet(), filepath.Join(dir, name), nil, parser.AllErrors); err != nil {
			t.Fatalf("%s does not parse: %v", name, err)
		}
	}
}

func TestGenerateRollsBackOnConflict(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "manifest.toml"), []byte("keep"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := Generate(dir, "example.com/rollback"); err == nil {
		t.Fatal("existing file did not block generation")
	}
	for _, name := range []string{"go.mod", "main.go", "internal/app/component.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatalf("%s leaked after failed generation: %v", name, err)
		}
	}
}

func TestGenerateRejectsNonEmptyDirBeforeWriting(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "unrelated.txt"), []byte("keep"), 0o644); err != nil {
		t.Fatal(err)
	}
	err := Generate(dir, "example.com/x")
	if err == nil || !strings.Contains(err.Error(), "not empty") {
		t.Fatalf("Generate error = %v, want a not-empty target dir error", err)
	}
	if _, err := os.Stat(filepath.Join(dir, "go.mod")); !os.IsNotExist(err) {
		t.Fatalf("go.mod was written into a non-empty dir: %v", err)
	}
}

func TestGenerateRollsBackCreatedDirectories(t *testing.T) {
	dir := t.TempDir()
	original := postWriteHook
	postWriteHook = func(name string) error {
		if name == "internal/app/component.go" {
			return os.ErrPermission
		}
		return nil
	}
	defer func() { postWriteHook = original }()

	if err := Generate(dir, "example.com/x"); err == nil {
		t.Fatal("injected write failure was ignored")
	}
	for _, name := range []string{"go.mod", "manifest.toml", "main.go", "internal/app/component.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatalf("%s leaked after rollback: %v", name, err)
		}
	}
	if _, err := os.Stat(filepath.Join(dir, "internal")); !os.IsNotExist(err) {
		t.Fatalf("created directory was not removed: %v", err)
	}
}

// localRepoRoot derives the checked-out gocordis source from this test file so
// the acceptance test works in any checkout instead of one hard-coded path.
func localRepoRoot(t *testing.T) string {
	t.Helper()
	_, thisFile, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("runtime.Caller failed: cannot derive the repository root")
	}
	abs, err := filepath.Abs(thisFile)
	if err != nil {
		t.Fatalf("resolve test source path: %v", err)
	}
	root := filepath.Clean(filepath.Join(filepath.Dir(abs), "..", ".."))
	if st, err := os.Stat(filepath.Join(root, "go.mod")); err != nil || st.IsDir() {
		t.Fatalf("derived repo root %s has no go.mod: %v", root, err)
	}
	return root
}

func TestGeneratedProjectBuilds(t *testing.T) {
	goBin, err := exec.LookPath("go")
	if err != nil {
		t.Skipf("go toolchain unavailable: %v", err)
	}
	localRepo := localRepoRoot(t)

	dir := t.TempDir()
	if err := Generate(dir, "example.com/generatedbuild"); err != nil {
		t.Fatal(err)
	}
	modPath := filepath.Join(dir, "go.mod")
	body, err := os.ReadFile(modPath)
	if err != nil {
		t.Fatal(err)
	}
	const oldReplace = "replace dynamic-runtime => github.com/ccb1900/gocordis v0.0.0"
	replaced := strings.Replace(string(body), oldReplace, "replace dynamic-runtime => "+localRepo, 1)
	if replaced == string(body) {
		t.Fatalf("generated go.mod lacks the expected replace directive:\n%s", body)
	}
	if err := os.WriteFile(modPath, []byte(replaced), 0o644); err != nil {
		t.Fatal(err)
	}

	env := append(os.Environ(), "GOPROXY=off", "GOFLAGS=-mod=mod", "GOSUMDB=off")
	unavailable := func(text string) bool {
		for _, marker := range []string{"GOPROXY=off", "module lookup disabled", "missing go.sum entry", "no required module provides"} {
			if strings.Contains(text, marker) {
				return true
			}
		}
		return false
	}
	run := func(args ...string) {
		t.Helper()
		cmd := exec.Command(goBin, args...)
		cmd.Dir = dir
		cmd.Env = env
		out, err := cmd.CombinedOutput()
		if err == nil {
			return
		}
		// Only skip when the toolchain genuinely cannot resolve cached deps;
		// a compile error in the generated source is a real failure.
		if unavailable(string(out)) {
			t.Skipf("cached dependencies unavailable for `go %v`: %v\n%s", strings.Join(args, " "), err, string(out))
		}
		t.Fatalf("go %v failed: %v\n%s", strings.Join(args, " "), err, string(out))
	}
	run("mod", "tidy")
	run("build", "./...")

	// Smoke-run the generated app. It blocks on <-ctx.Done() after a successful
	// reconcile, so a short guard timeout is expected. Send SIGINT to the whole
	// process group (go run ignores SIGINT itself but its child handles it),
	// which lets the app exit cleanly and avoids orphaned children holding the
	// output pipes. No fixed sleeps are used for synchronization.
	runCtx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	logPath := filepath.Join(dir, "run.log")
	logFile, err := os.Create(logPath)
	if err != nil {
		t.Fatal(err)
	}
	runCmd := exec.CommandContext(runCtx, goBin, "run", ".")
	runCmd.Dir = dir
	runCmd.Env = env
	runCmd.Stdout, runCmd.Stderr = logFile, logFile
	runCmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true}
	runCmd.Cancel = func() error { return syscall.Kill(-runCmd.Process.Pid, syscall.SIGINT) }
	runCmd.WaitDelay = 2 * time.Second
	runErr := runCmd.Run()
	if err := logFile.Close(); err != nil {
		t.Fatal(err)
	}
	logOut, err := os.ReadFile(logPath)
	if err != nil {
		t.Fatal(err)
	}
	if runErr != nil && runCtx.Err() != context.DeadlineExceeded {
		if unavailable(string(logOut)) {
			t.Skipf("cached dependencies unavailable for `go run .`: %v\n%s", runErr, string(logOut))
		}
		t.Fatalf("go run . failed: %v\n%s", runErr, string(logOut))
	}
	// Prove the generated app actually started and reconciled, rather than
	// merely being killed before it got going.
	if !strings.Contains(string(logOut), "hello from GoCordis") {
		t.Fatalf("generated app did not reach its running state:\n%s", logOut)
	}
}
