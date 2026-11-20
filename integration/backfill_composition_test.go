package integration

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// P1-3 (integration audit): journal/backfill semantics through the KERNEL.
// cmd/backfill's business function (RunCatchUp) is pinned by cmd/backfill
// core_test B-01..B-09; what nothing exercised is the composition layer:
//
//	one Apply = one Activation ("one run = one catch-up pass");
//	a per-day failure becomes a Failed Fiber (isolated) while the good days'
//	journal marks survive;
//	re-enable / configwatch replacement replays idempotently — no duplicated
//	output rows, journal marks survive the fiber generation.
//
// The collector below mirrors cmd/backfill's documented contract (collect
// [since..yesterday] minus the journal, mark a day only after its output is
// fully written, FailOnError → Failed fiber) over a small fixture set of
// "days", so the kernel properties are testable at composition level without
// importing package main.
type journalCollector struct {
	id    string
	days  []string
	bad   string // a day whose write fails (isolated failure)
	force bool   // FailOnError: fail the whole Apply when any day fails
	data  string // data directory (journal/ + out/ under it)
}

func (c *journalCollector) Name() string                  { return "journal-collector:" + c.id }
func (c *journalCollector) Inject() []runtime.Dependency  { return nil }
func (c *journalCollector) Provide() []runtime.Capability { return nil }

func journaled(data, day string) bool {
	_, err := os.Stat(filepath.Join(data, "journal", day+".mark"))
	return err == nil
}

func (c *journalCollector) collectOne(day string) error {
	if day == c.bad {
		return fmt.Errorf("source for %s unreadable (isolated failure)", day)
	}
	out := filepath.Join(c.data, "out", day+".csv")
	if _, err := os.Stat(out); err == nil {
		return nil // already collected (idempotent replay): never rewrite
	}
	body := "row-" + day + "-1\nrow-" + day + "-2\n"
	if err := os.WriteFile(out, []byte(body), 0o644); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(c.data, "journal", day+".mark"), []byte(day), 0o644)
}

func (c *journalCollector) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	_ = ctx
	_ = os.MkdirAll(filepath.Join(c.data, "journal"), 0o755)
	_ = os.MkdirAll(filepath.Join(c.data, "out"), 0o755)
	var passErr error
	for _, day := range c.days {
		if journaled(c.data, day) {
			continue
		}
		if err := c.collectOne(day); err != nil && passErr == nil {
			passErr = err
		}
	}
	if passErr != nil && c.force {
		return nil, passErr // FailOnError: the fiber fails (isolated)
	}
	return nil, nil
}

func TestBackfillCompositionJournalThroughKernel(t *testing.T) {
	dir := t.TempDir()
	dataDir := filepath.Join(dir, "data")
	path := filepath.Join(dir, "app.toml")

	factory := func(cc config.ComponentConfig) (runtime.Component, error) {
		if cc.Type != "journalcollector" {
			return nil, fmt.Errorf("unknown type %q", cc.Type)
		}
		daysRaw, _ := cc.Config["days"].(string)
		bad, _ := cc.Config["bad"].(string)
		return &journalCollector{
			id:   cc.ID,
			days: strings.Split(daysRaw, ","),
			bad:  bad,
			force: func() bool {
				if bad != "" {
					return true // FailOnError semantics
				}
				return false
			}(),
			data: dataDir,
		}, nil
	}
	e := newEnv(t, factory, "journalcollector")
	fw := watch.NewFileWatcher()
	defer fw.Close()

	writeManifest := func(days []string, bad string, enabled bool) error {
		var b strings.Builder
		b.WriteString("[[components]]\nid = \"collector\"\ntype = \"journalcollector\"\n")
		b.WriteString("enabled = " + fmt.Sprintf("%v", enabled) + "\n")
		b.WriteString("\n[components.config]\n")
		b.WriteString("days = \"" + strings.Join(days, ",") + "\"\n")
		if bad != "" {
			b.WriteString("bad = \"" + bad + "\"\n")
		}
		return os.WriteFile(path, []byte(b.String()), 0o644)
	}
	outRows := func(day string) string {
		body, err := os.ReadFile(filepath.Join(dataDir, "out", day+".csv"))
		if err != nil {
			return ""
		}
		return string(body)
	}
	collectorFiber := func() *runtime.Fiber {
		for _, o := range e.ctrl.Owned() {
			if o.ID == "collector" {
				return o.Fiber
			}
		}
		return nil
	}
	waitCollectorState := func(want runtime.FiberState, what string) {
		t.Helper()
		deadline := time.Now().Add(8 * time.Second)
		for time.Now().Before(deadline) {
			if f := collectorFiber(); f != nil && f.State() == want {
				return
			}
			time.Sleep(5 * time.Millisecond)
		}
		var got runtime.FiberState
		if f := collectorFiber(); f != nil {
			got = f.State()
		}
		t.Fatalf("%s: collector never reached %v (got %v)", what, want, got)
	}

	adapter, err := configwatch.New(configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}, e.ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer adapter.CloseContext(ctxT(t))

	// --- Phase 1: v1 with one good day + one failing day (FailOnError). ---
	if err := writeManifest([]string{"d1", "d2"}, "d2", true); err != nil {
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	waitCollectorState(runtime.StateFailed, "failing day must yield a Failed fiber (FailOnError isolation)")
	if got := outRows("d1"); got != "row-d1-1\nrow-d1-2\n" {
		t.Fatalf("good day output wrong: %q", got)
	}
	if !journaled(dataDir, "d1") {
		t.Fatal("good day not journaled despite the failing sibling (isolation broken)")
	}
	if journaled(dataDir, "d2") {
		t.Fatal("failing day journaled — the isolation leaked")
	}

	// --- Phase 2: fix the day; the replacement collects exactly the gap.
	// (Sync forces read+reconcile synchronously — the watcher path is
	// exercised by the configwatch suite; this test needs determinism.) ---
	if err := writeManifest([]string{"d1", "d2"}, "", true); err != nil {
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatalf("reconcile after repair: %v", err)
	}
	waitCollectorState(runtime.StateActive, "repaired manifest must re-activate the collector")
	if got := outRows("d1"); got != "row-d1-1\nrow-d1-2\n" {
		t.Fatalf("replacement rewrote an already-journaled day (duplication): %q", got)
	}
	if got := outRows("d2"); got != "row-d2-1\nrow-d2-2\n" {
		t.Fatalf("repaired day not collected after fix: %q", got)
	}
	if !journaled(dataDir, "d2") {
		t.Fatal("repaired day not journaled")
	}

	// --- Phase 3: disable + re-enable via the declaration switch replays
	// idempotently (no rewrites; the entry survives, the fiber is the identity
	// of one enablement). ---
	before := outRows("d1")
	if err := writeManifest([]string{"d1", "d2"}, "", false); err != nil {
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatalf("disable reconcile: %v", err)
	}
	// Disabled entry semantics: the declaration SURVIVES with fiber == nil
	// ("the entry is the surviving identity, the fiber the identity of one
	// enablement") — asserting a Gone fiber here would be wrong.
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f := collectorFiber(); f == nil {
			break
		}
		time.Sleep(5 * time.Millisecond)
	}
	if f := collectorFiber(); f != nil {
		t.Fatalf("disabled entry still owns a fiber: state=%v", f.State())
	}
	if owned := e.ctrl.Owned(); len(owned) != 1 || owned[0].ID != "collector" {
		t.Fatalf("disabled entry missing from the desired set: %+v", owned)
	}
	if err := writeManifest([]string{"d1", "d2"}, "", true); err != nil {
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatalf("re-enable reconcile: %v", err)
	}
	waitCollectorState(runtime.StateActive, "re-enabled collector must go Active")
	if got := outRows("d1"); got != before {
		t.Fatalf("idempotent replay duplicated output rows: %q", got)
	}
	if !journaled(dataDir, "d1") || !journaled(dataDir, "d2") {
		t.Fatal("journal marks did not survive the disable/re-enable cycle")
	}
}

// context for adapter.CloseContext — the integration package's ctxT helper
// provides a short-lived context; keep a local alias for readability here.
var _ = context.Background
