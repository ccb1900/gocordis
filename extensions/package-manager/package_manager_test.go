package packagemanager_test

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/package-manager"
)

func writeMod(t *testing.T, dir, body string) string {
	t.Helper()
	p := filepath.Join(dir, "go.mod")
	if err := os.WriteFile(p, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
	return p
}

func TestAgentDetectAndReport(t *testing.T) {
	dir := t.TempDir()
	writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n\nrequire (\n\told.example/lib v1.2.3\n)\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	agent, err := svc.Agent(ctx)
	if err != nil {
		t.Fatalf("agent: %v", err)
	}
	if agent.Name != "go" {
		t.Fatalf("agent = %q, want go", agent.Name)
	}
	mods, err := svc.Report(ctx)
	if err != nil {
		t.Fatalf("report: %v", err)
	}
	if len(mods) != 1 || mods[0].Path != "old.example/lib" || mods[0].Version != "v1.2.3" {
		t.Fatalf("report = %+v", mods)
	}
}

func TestApplyAddUpdateRemove(t *testing.T) {
	dir := t.TempDir()
	writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	res, err := svc.Apply(ctx, []packagemanager.Change{
		{Module: "example.com/a", Version: "v1.0.0"},
		{Module: "example.com/b", Version: "v0.1.0"},
	})
	if err != nil {
		t.Fatalf("add: %v", err)
	}
	if len(res.Added) != 2 {
		t.Fatalf("added = %v", res.Added)
	}
	res, err = svc.Apply(ctx, []packagemanager.Change{{Module: "example.com/a", Version: "v1.1.0"}})
	if err != nil || len(res.Updated) != 1 {
		t.Fatalf("update: res=%+v err=%v", res, err)
	}
	res, err = svc.Apply(ctx, []packagemanager.Change{{Module: "example.com/b", Version: ""}})
	if err != nil || len(res.Removed) != 1 {
		t.Fatalf("remove: res=%+v err=%v", res, err)
	}

	body, err := os.ReadFile(filepath.Join(dir, "go.mod"))
	if err != nil {
		t.Fatal(err)
	}
	got := string(body)
	if !strings.Contains(got, "example.com/a v1.1.0") || strings.Contains(got, "example.com/b") {
		t.Fatalf("go.mod after changes:\n%s", got)
	}
	mods, err := svc.Report(ctx)
	if err != nil || len(mods) != 1 || mods[0].Version != "v1.1.0" {
		t.Fatalf("report after changes = %+v, err=%v", mods, err)
	}
}

func TestApplyRestoresOnFailure(t *testing.T) {
	dir := t.TempDir()
	original := "module example.com/demo\n\ngo 1.24.0\n\nrequire example.com/keep v1.0.0\n"
	p := writeMod(t, dir, original)
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	_, err = svc.Apply(ctx, []packagemanager.Change{{Module: "BAD MODULE", Version: "v1.0.0"}})
	if err == nil {
		t.Fatal("invalid module path was accepted")
	}
	body, err := os.ReadFile(p)
	if err != nil || string(body) != original {
		t.Fatalf("go.mod was not restored: %q err=%v", string(body), err)
	}
}

func TestInvalidChangeDoesNotTouchOtherEntries(t *testing.T) {
	dir := t.TempDir()
	p := writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n\nrequire example.com/keep v1.0.0\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	_, err = svc.Apply(ctx, []packagemanager.Change{
		{Module: "example.com/ok", Version: "v1.0.0"},
		{Module: "bad path", Version: "v1.0.0"},
	})
	if err == nil {
		t.Fatal("batch with invalid module path was accepted")
	}
	body, err := os.ReadFile(p)
	if err != nil || strings.Contains(string(body), "example.com/ok") {
		t.Fatalf("partial change leaked: %q err=%v", string(body), err)
	}
}

func TestApplyReportsBeforeAfterAndPreservesMode(t *testing.T) {
	dir := t.TempDir()
	p := writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n")
	if err := os.Chmod(p, 0o640); err != nil {
		t.Fatal(err)
	}
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	res, err := svc.Apply(ctx, []packagemanager.Change{{Module: "example.com/a", Version: "v1.0.0"}})
	if err != nil {
		t.Fatal(err)
	}
	if len(res.Details) != 1 || res.Details[0].Before != "" || res.Details[0].After != "v1.0.0" {
		t.Fatalf("details = %+v", res.Details)
	}
	info, err := os.Stat(p)
	if err != nil || info.Mode().Perm() != 0o640 {
		t.Fatalf("go.mod mode = %v, err=%v", info, err)
	}
	_, err = svc.Apply(ctx, []packagemanager.Change{{Module: "BAD PATH", Version: "v1.0.0"}})
	if err == nil {
		t.Fatal("invalid change accepted")
	}
	info, err = os.Stat(p)
	if err != nil || info.Mode().Perm() != 0o640 {
		t.Fatalf("go.mod mode after rollback = %v, err=%v", info, err)
	}
}

func TestApplyDuplicateChangesDedupeAndDetails(t *testing.T) {
	dir := t.TempDir()
	p := writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	res, err := svc.Apply(ctx, []packagemanager.Change{
		{Module: "example.com/dup", Version: "v1.0.0"},
		{Module: "example.com/other", Version: "v1.5.0"},
		{Module: "example.com/dup", Version: "v1.2.0"}, // last one wins
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(res.Added) != 2 || len(res.Details) != 2 {
		t.Fatalf("added=%v details=%+v", res.Added, res.Details)
	}
	for _, d := range res.Details {
		if d.Module == "example.com/dup" && d.After != "v1.2.0" {
			t.Fatalf("duplicate detail did not reflect final state: %+v", d)
		}
	}
	body, err := os.ReadFile(p)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(body), "example.com/dup") != 1 || !strings.Contains(string(body), "example.com/dup v1.2.0") {
		t.Fatalf("go.mod after deduped apply:\n%s", body)
	}
}

func TestApplyDuplicateAddThenRemoveIsNoop(t *testing.T) {
	dir := t.TempDir()
	p := writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n\nrequire example.com/keep v1.0.0\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	res, err := svc.Apply(ctx, []packagemanager.Change{
		{Module: "example.com/transient", Version: "v1.0.0"},
		{Module: "example.com/transient", Version: ""},
	})
	if err != nil {
		t.Fatal(err)
	}
	if len(res.Added)+len(res.Removed)+len(res.Updated)+len(res.Details) != 0 {
		t.Fatalf("net no-op batch produced a result: %+v", res)
	}
	body, err := os.ReadFile(p)
	if err != nil || strings.Contains(string(body), "example.com/transient") {
		t.Fatalf("transient module leaked: %q err=%v", string(body), err)
	}
}

func TestApplyInvalidVersion(t *testing.T) {
	original := "module example.com/demo\n\ngo 1.24.0\n\nrequire example.com/keep v1.0.0\n"
	dir := t.TempDir()
	p := writeMod(t, dir, original)
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	_, err = svc.Apply(ctx, []packagemanager.Change{{Module: "example.com/a", Version: "not-a-version"}})
	if err == nil {
		t.Fatal("invalid semver version was accepted")
	}
	body, err := os.ReadFile(p)
	if err != nil || string(body) != original {
		t.Fatalf("go.mod changed after invalid version: %q err=%v", string(body), err)
	}
}

func TestApplyRollbackPreservesContentAndMode(t *testing.T) {
	original := "module example.com/demo\n\ngo 1.24.0\n\nrequire example.com/keep v1.0.0\n"
	dir := t.TempDir()
	p := writeMod(t, dir, original)
	if err := os.Chmod(p, 0o640); err != nil {
		t.Fatal(err)
	}
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	_, err = svc.Apply(ctx, []packagemanager.Change{
		{Module: "example.com/add", Version: "v1.0.0"},
		{Module: "example.com/bad", Version: "nope"},
	})
	if err == nil {
		t.Fatal("batch with invalid version was accepted")
	}
	body, err := os.ReadFile(p)
	if err != nil || string(body) != original {
		t.Fatalf("go.mod content changed on rollback: %q err=%v", string(body), err)
	}
	info, err := os.Stat(p)
	if err != nil || info.Mode().Perm() != 0o640 {
		t.Fatalf("go.mod mode changed on rollback: %v err=%v", info, err)
	}
}

func TestConcurrentReportDuringApply(t *testing.T) {
	dir := t.TempDir()
	writeMod(t, dir, "module example.com/demo\n\ngo 1.24.0\n")
	svc, err := packagemanager.New(packagemanager.Config{Root: dir})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	changes := make([]packagemanager.Change, 200)
	for i := range changes {
		changes[i] = packagemanager.Change{
			Module:  fmt.Sprintf("example.com/m%03d", i),
			Version: "v1.0.0",
		}
	}

	errCh := make(chan error, 64)
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := 0; j < 100; j++ {
				mods, err := svc.Report(ctx)
				if err != nil {
					errCh <- err
					return
				}
				if n := len(mods); n != 0 && n != len(changes) {
					errCh <- fmt.Errorf("Report observed partial state: %d modules", n)
					return
				}
			}
		}()
	}
	wg.Add(1)
	go func() {
		defer wg.Done()
		if _, err := svc.Apply(ctx, changes); err != nil {
			errCh <- err
		}
	}()
	wg.Wait()
	close(errCh)
	for err := range errCh {
		t.Error(err)
	}
}
