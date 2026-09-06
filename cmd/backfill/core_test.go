package main

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

func day(y int, m time.Month, d int) time.Time {
	return time.Date(y, m, d, 0, 0, 0, 0, time.Local)
}

func fixedNow(t time.Time) func() time.Time { return func() time.Time { return t } }

// testEnv builds an isolated data root with a deterministic clock.
func testEnv(t *testing.T, since string, now time.Time) Config {
	t.Helper()
	root := t.TempDir()
	cfg := Config{
		Site:        "plant-1",
		Line:        "line-a",
		Template:    filepath.Join(root, "in", "{site}", "{line}", "{yyyymmdd}", "raw.csv"),
		Since:       since,
		JournalPath: filepath.Join(root, "journal", "done.log"),
		OutDir:      filepath.Join(root, "out"),
		Now:         fixedNow(now),
	}
	return cfg
}

// writeSource creates a deterministic raw.csv for one day: header + 2 rows.
func writeSource(t *testing.T, cfg Config, d time.Time) {
	t.Helper()
	p := renderPath(cfg.Template, cfg.meta(), d)
	if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
		t.Fatal(err)
	}
	content := "ts,value\n" + d.Format("2006-01-02") + ",1\n" + d.Format("2006-01-02") + ",2\n"
	if err := os.WriteFile(p, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

// lineCount returns the number of CSV lines in one normalized output file
// (header included). Used to prove re-collection never duplicates content.
func lineCount(t *testing.T, path string) int {
	t.Helper()
	rows, err := readCSV(path)
	if err != nil {
		t.Fatalf("read %s: %v", path, err)
	}
	return len(rows)
}

// B-01 — Late start (host powered off for 4 days): the first run after the
// outage collects every expected day [Since .. yesterday], nothing is lost.
func TestB01LateStartCatchesUp(t *testing.T) {
	now := time.Date(2026, 9, 5, 8, 0, 0, 0, time.Local) // yesterday = 2026-09-04
	cfg := testEnv(t, "2026-09-01", now)
	for d := day(2026, 9, 1); !d.After(day(2026, 9, 4)); d = d.AddDate(0, 0, 1) {
		writeSource(t, cfg, d)
	}

	reports, err := RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("RunCatchUp: %v", err)
	}
	if len(reports) != 4 {
		t.Fatalf("collected %d days, want 4 (09-01..09-04)", len(reports))
	}
	for _, r := range reports {
		if r.Rows != 3 { // header + 2 data lines
			t.Fatalf("%s lines = %d, want 3", formatDate(r.Date), r.Rows)
		}
	}
	out, err := sortedOutFiles(cfg.OutDir)
	if err != nil || len(out) != 4 {
		t.Fatalf("out files = %v (%v), want 4", out, err)
	}

	// A second run has nothing left to do.
	reports2, err := RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("second RunCatchUp: %v", err)
	}
	if len(reports2) != 0 {
		t.Fatalf("second run collected %d days, want 0", len(reports2))
	}
}

// B-02 — Power loss between two days: days already marked survive; only the
// unmarked days are re-collected by the next run (no loss, no duplication).
func TestB02PowerLossBetweenDays(t *testing.T) {
	now := time.Date(2026, 9, 6, 8, 0, 0, 0, time.Local) // yesterday = 2026-09-05
	cfg := testEnv(t, "2026-09-01", now)
	for d := day(2026, 9, 1); !d.After(day(2026, 9, 5)); d = d.AddDate(0, 0, 1) {
		writeSource(t, cfg, d)
	}

	// Simulate the first run crashing right after 09-02 was committed: its
	// journal marks 09-01..09-02 exist, 09-03..09-05 do not.
	j, err := openJournal(cfg.JournalPath)
	if err != nil {
		t.Fatal(err)
	}
	att, err := openAttempts(attemptsPathOf(cfg))
	if err != nil {
		t.Fatal(err)
	}
	for _, d := range []time.Time{day(2026, 9, 1), day(2026, 9, 2)} {
		collectDate(context.Background(), cfg, j, att, d)
	}

	// The machine comes back: the next run must fill 09-03..09-05 only.
	reports, err := RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("RunCatchUp after outage: %v", err)
	}
	var got []string
	for _, r := range reports {
		got = append(got, formatDate(r.Date))
	}
	want := []string{"2026-09-03", "2026-09-04", "2026-09-05"}
	if len(got) != len(want) {
		t.Fatalf("re-collected %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("re-collected %v, want %v", got, want)
		}
	}

	// Every day has exactly its own output (no duplication of 09-01/09-02).
	out, err := sortedOutFiles(cfg.OutDir)
	if err != nil || len(out) != 5 {
		t.Fatalf("out files = %v (%v), want 5", out, err)
	}
	for _, p := range out {
		if n := lineCount(t, p); n != 3 { // header + 2 data lines, exactly once
			t.Fatalf("%s lines = %d, want 3 (no duplicated re-collection)", filepath.Base(p), n)
		}
	}

	// Journal now marks all five days.
	j2, err := openJournal(cfg.JournalPath)
	if err != nil {
		t.Fatal(err)
	}
	for _, d := range []time.Time{day(2026, 9, 1), day(2026, 9, 2), day(2026, 9, 3), day(2026, 9, 4), day(2026, 9, 5)} {
		if !j2.Done(d) {
			t.Fatalf("journal missing %s after recovery", formatDate(d))
		}
	}
}

// B-03 — Cancellation safety: a canceled pass persists nothing and returns the
// context error; a later run is unaffected.
func TestB03CancellationSafety(t *testing.T) {
	cfg := testEnv(t, "2026-09-01", time.Date(2026, 9, 5, 8, 0, 0, 0, time.Local))
	for d := day(2026, 9, 1); !d.After(day(2026, 9, 4)); d = d.AddDate(0, 0, 1) {
		writeSource(t, cfg, d)
	}

	ctx, cancel := context.WithCancel(context.Background())
	cancel() // already canceled before the pass starts
	reports, err := RunCatchUp(ctx, cfg)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("RunCatchUp = %v, want context.Canceled", err)
	}
	if len(reports) != 0 {
		t.Fatalf("canceled pass produced %d reports", len(reports))
	}
	out, _ := sortedOutFiles(cfg.OutDir)
	if len(out) != 0 {
		t.Fatalf("canceled pass wrote outputs: %v", out)
	}

	// A fresh run still collects every expected day.
	reports, err = RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("run after cancel: %v", err)
	}
	if len(reports) != 4 {
		t.Fatalf("run after cancel collected %d days, want 4", len(reports))
	}
}

// B-04 — Path template rendering carries date + metadata business info.
func TestB04PathRendering(t *testing.T) {
	tmpl := "/data/{site}/{line}/{batch}/{shift}/{YYYY}/{MM}/{DD}/{yyyymmdd}/raw.csv"
	d := day(2026, 9, 6)
	meta := map[string]string{"site": "plant-1", "line": "line-a", "batch": "B7", "shift": "night"}
	got := renderPath(tmpl, meta, d)
	want := "/data/plant-1/line-a/B7/night/2026/09/06/20260906/raw.csv"
	if got != want {
		t.Fatalf("renderPath = %q, want %q", got, want)
	}
}

// B-05 — Journal crash-safety: a torn final line is ignored; valid marks
// survive reopen.
func TestB05JournalTornTail(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "done.log")
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	content := "2026-09-01\n2026-09-02\n2026-09-0" // torn final line (power loss)
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
	j, err := openJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	if !j.Done(day(2026, 9, 1)) || !j.Done(day(2026, 9, 2)) {
		t.Fatal("valid marks lost after reopen")
	}
	if j.Done(day(2026, 9, 3)) {
		t.Fatal("torn tail wrongly treated as a completed day")
	}
}

// B-06 — Remote/UNC defer semantics: a missing source day is NOT written off.
// It stays out of the completion journal and accumulates in the attempts
// ledger across process restarts; once the file arrives, the next run collects
// it and clears the attempts.
func TestB06MissingDayDeferredAcrossRestarts(t *testing.T) {
	now := time.Date(2026, 9, 3, 8, 0, 0, 0, time.Local) // yesterday = 2026-09-02
	cfg := testEnv(t, "2026-09-01", now)
	writeSource(t, cfg, day(2026, 9, 1)) // 09-02 source intentionally absent

	// Run 1 (simulating a fresh process): 09-01 collected, 09-02 deferred.
	reports, err := RunCatchUp(context.Background(), cfg)
	if err == nil {
		t.Fatal("RunCatchUp = nil, want a per-day error for the missing source")
	}
	if len(reports) != 2 {
		t.Fatalf("reports = %d, want 2 (09-01 ok, 09-02 failed)", len(reports))
	}
	if reports[0].Err != nil || reports[1].Err == nil {
		t.Fatalf("reports[0]=%+v reports[1]=%+v, want first ok second failed", reports[0], reports[1])
	}
	if reports[1].Kind != AttemptMissing {
		t.Fatalf("failed day kind = %q, want missing", reports[1].Kind)
	}

	// The failed day must NOT be in the completion journal ...
	j, err := openJournal(cfg.JournalPath)
	if err != nil {
		t.Fatal(err)
	}
	if !j.Done(day(2026, 9, 1)) {
		t.Fatal("09-01 should be marked done")
	}
	if j.Done(day(2026, 9, 2)) {
		t.Fatal("09-02 must NOT be marked done (deferred, not written off)")
	}

	// ... but it must be recorded in the attempts ledger.
	att, err := openAttempts(attemptsPathOf(cfg))
	if err != nil {
		t.Fatal(err)
	}
	out := att.Snapshot()
	if len(out) != 1 || out[0].Date != day(2026, 9, 2) || out[0].Count != 1 || out[0].Kind != AttemptMissing {
		t.Fatalf("attempts = %+v, want one 09-02/missing/1", out)
	}

	// Run 2, still missing (e.g. the source machine is still down): the day is
	// retried and the attempt count grows — nothing is lost.
	if _, err := RunCatchUp(context.Background(), cfg); err == nil {
		t.Fatal("second RunCatchUp = nil, want the day still deferred")
	}
	att2, _ := openAttempts(attemptsPathOf(cfg))
	out2 := att2.Snapshot()
	if len(out2) != 1 || out2[0].Count != 2 {
		t.Fatalf("attempts after run 2 = %+v, want count 2", out2)
	}

	// Run 3: the source file finally arrives (late sync); the day is collected
	// and its attempts are cleared.
	writeSource(t, cfg, day(2026, 9, 2))
	if _, err := RunCatchUp(context.Background(), cfg); err != nil {
		t.Fatalf("RunCatchUp after file arrives: %v", err)
	}
	j3, _ := openJournal(cfg.JournalPath)
	if !j3.Done(day(2026, 9, 2)) {
		t.Fatal("09-02 should be marked done after late arrival")
	}
	att3, _ := openAttempts(attemptsPathOf(cfg))
	if out3 := att3.Snapshot(); len(out3) != 0 {
		t.Fatalf("attempts after success = %+v, want cleared", out3)
	}
}

// B-07 — MissingOK on a LOCAL source: an absent file means "no data that day";
// the day is marked done with 0 rows and never pollutes the attempts ledger.
func TestB07MissingOKWritesDayOff(t *testing.T) {
	now := time.Date(2026, 9, 3, 8, 0, 0, 0, time.Local)
	cfg := testEnv(t, "2026-09-01", now)
	cfg.MissingOK = true
	writeSource(t, cfg, day(2026, 9, 1)) // 09-02 intentionally absent

	if _, err := RunCatchUp(context.Background(), cfg); err != nil {
		t.Fatalf("RunCatchUp with MissingOK: %v", err)
	}
	j, _ := openJournal(cfg.JournalPath)
	if !j.Done(day(2026, 9, 1)) || !j.Done(day(2026, 9, 2)) {
		t.Fatal("both days should be marked done under MissingOK")
	}
	att, _ := openAttempts(attemptsPathOf(cfg))
	if out := att.Snapshot(); len(out) != 0 {
		t.Fatalf("attempts under MissingOK = %+v, want empty", out)
	}
}

// mapSink is a test-only Sink: it stores one day's rows in memory. Swapping it
// in proves that changing the storage target touches only Config.Sink, not the
// catch-up loop, journal or attempts semantics.
type mapSink struct {
	mu   sync.Mutex
	rows map[string][][]string
}

func newMapSink() *mapSink { return &mapSink{rows: make(map[string][][]string)} }

func (m *mapSink) Write(date time.Time, rows [][]string) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.rows[formatDate(date)] = rows
	return nil
}

func (m *mapSink) Location(date time.Time) string { return "memory:" + formatDate(date) }

func (m *mapSink) get(date string) [][]string {
	m.mu.Lock()
	defer m.mu.Unlock()
	return m.rows[date]
}

// writeTSVSource writes a tab-delimited source file for one day.
func writeTSVSource(t *testing.T, cfg Config, d time.Time) {
	t.Helper()
	p := renderPath(cfg.Template, cfg.meta(), d)
	if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
		t.Fatal(err)
	}
	content := "ts\tvalue\n" + d.Format("2006-01-02") + "\t1\n" + d.Format("2006-01-02") + "\t2\n"
	if err := os.WriteFile(p, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

// B-08 — Source format change is a local change: switching Config.Format to a
// TSV decoder reuses the whole pipeline (journal, attempts, sink, catch-up).
func TestB08FormatSwitch(t *testing.T) {
	now := time.Date(2026, 9, 3, 8, 0, 0, 0, time.Local)
	cfg := testEnv(t, "2026-09-01", now)
	cfg.Format = "tsv"
	writeTSVSource(t, cfg, day(2026, 9, 1))
	writeTSVSource(t, cfg, day(2026, 9, 2))

	reports, err := RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("RunCatchUp with tsv: %v", err)
	}
	if len(reports) != 2 {
		t.Fatalf("reports = %d, want 2", len(reports))
	}
	for _, r := range reports {
		if r.Rows != 3 { // header + 2 data lines decoded from TSV
			t.Fatalf("%s rows = %d, want 3", formatDate(r.Date), r.Rows)
		}
	}
	out, err := sortedOutFiles(cfg.OutDir)
	if err != nil || len(out) != 2 {
		t.Fatalf("out files = %v (%v), want 2", out, err)
	}

	// An unsupported format fails loudly and never marks a day.
	cfgBad := testEnv(t, "2026-09-01", now)
	cfgBad.Format = "parquet"
	writeSource(t, cfgBad, day(2026, 9, 1))
	if _, err := RunCatchUp(context.Background(), cfgBad); err == nil || !strings.Contains(err.Error(), "unknown source format") {
		t.Fatalf("unknown format RunCatchUp = %v, want unknown source format error", err)
	}
	j, _ := openJournal(cfgBad.JournalPath)
	if j.Done(day(2026, 9, 1)) {
		t.Fatal("day marked done despite unsupported format")
	}
}

// B-09 — Storage change is a local change: swapping Config.Sink to a memory
// store reuses the whole pipeline; the day is committed exactly once.
func TestB09SinkSwitch(t *testing.T) {
	now := time.Date(2026, 9, 3, 8, 0, 0, 0, time.Local)
	cfg := testEnv(t, "2026-09-01", now)
	ms := newMapSink()
	cfg.Sink = ms
	writeSource(t, cfg, day(2026, 9, 1))
	writeSource(t, cfg, day(2026, 9, 2))

	reports, err := RunCatchUp(context.Background(), cfg)
	if err != nil {
		t.Fatalf("RunCatchUp with map sink: %v", err)
	}
	if len(reports) != 2 {
		t.Fatalf("reports = %d, want 2", len(reports))
	}
	if rows := ms.get("2026-09-01"); len(rows) != 3 { // header + 2 data lines
		t.Fatalf("map sink rows for 09-01 = %d, want 3", len(rows))
	}
	if rows := ms.get("2026-09-02"); len(rows) != 3 {
		t.Fatalf("map sink rows for 09-02 = %d, want 3", len(rows))
	}

	// A second pass has nothing to do (journal), so the memory sink is not
	// written twice.
	if _, err := RunCatchUp(context.Background(), cfg); err != nil {
		t.Fatalf("second RunCatchUp: %v", err)
	}
	if rows := ms.get("2026-09-01"); len(rows) != 3 {
		t.Fatalf("map sink rows after rerun = %d, want still 3 (no duplicate write)", len(rows))
	}
}
