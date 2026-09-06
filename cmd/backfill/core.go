// Backfill core: the business logic of the industrial CSV collector demo.
//
// The core invariant that makes "host was powered off for N days" safe:
//
//	every run is a CATCH-UP pass, and the per-day completion journal is the
//	source of truth. A run never needs to know whether a scheduled moment was
//	missed: it simply collects every expected day (since .. yesterday) that is
//	not yet marked in the journal, and marks each day only AFTER its output was
//	fully written. A power loss between two days therefore loses at most the
//	not-yet-marked days, and the next run re-collects exactly those.
//
// Remote (UNC/SMB) sources add one semantic: "the file is not there yet" is not
// the same as "there is no data that day". Failed days therefore never enter
// the completion journal — they stay in an ATTEMPTS ledger (count + last error)
// so the next run retries them and an operator can alert on them.
//
// EXTENSIBILITY (where each business change lands):
//
//	source file location + metadata -> Config.Template / Config.Extra (renderPath)
//	source FORMAT (csv variant, tsv, jsonl, ...) -> a new Decoder + Config.Format
//	output/storage (file, database, queue, ...)  -> a new Sink + Config.Sink
//
// The catch-up loop, journal, attempts ledger, cancellation and power-loss
// semantics do not change when any of those three things change.
package main

import (
	"bufio"
	"context"
	"encoding/csv"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

// Config is one collector's business configuration (comes from
// config.ComponentConfig.Config / a manifest). "Since" is the earliest day data
// is expected; the collector fills [Since .. yesterday] minus the journal.
type Config struct {
	// Site/Line are the built-in metadata placeholders {site}/{line}.
	Site string
	Line string
	// Extra carries arbitrary additional path metadata; every key becomes a
	// template placeholder {key} (e.g. {batch}, {shift}, {machine}).
	Extra map[string]string

	// Template renders the source file for one day, e.g.
	//   /data/{site}/{line}/{batch}/{YYYY}/{MM}/{DD}/raw.csv
	// or a UNC root:
	//   \\nas\plant-data\{site}\{line}\{YYYY}\{MM}\{DD}\raw.csv
	// Date placeholders: {YYYY} {MM} {DD} {yyyymmdd}
	Template string

	// Format selects the source Decoder ("csv" or "tsv"; empty == csv).
	Format string
	// Sink persists normalized rows. Nil uses the default file sink writing
	// one CSV file per day into OutDir.
	Sink Sink

	// Since is the earliest expected collection day (YYYY-MM-DD).
	Since string
	// JournalPath is the per-day completion log (append-only, fsynced).
	JournalPath string
	// AttemptsPath records failed/unavailable days (count + last error). When
	// empty it defaults to "<dir of JournalPath>/attempts.log".
	AttemptsPath string
	// OutDir is the default file sink's output directory.
	OutDir string

	// MissingOK: when a source file is absent, mark the day done with 0 rows
	// (legitimate "no data that day"). With a remote/UNC source keep this
	// false: an absent file may mean "not synchronized yet", and the day
	// should stay in the attempts ledger instead of being written off.
	MissingOK bool
	// FailOnError: when true, any per-day error makes the whole pass (and thus
	// the Fiber) fail after partial progress is saved. Timer/CLI mode uses this
	// so the OS scheduler sees a non-zero exit; service mode keeps it false.
	FailOnError bool
	// Period > 0 turns the activation into a long-lived service that repeats
	// the catch-up pass every Period.
	Period time.Duration

	// Now is the clock (injectable for deterministic tests).
	Now func() time.Time
}

// meta returns the full placeholder map (site/line + Extra).
func (cfg Config) meta() map[string]string {
	m := map[string]string{"site": cfg.Site, "line": cfg.Line}
	for k, v := range cfg.Extra {
		m[k] = v
	}
	return m
}

// ---------------------------------------------------------------------------
// EXTENSION POINT 1 — source format decoding
// ---------------------------------------------------------------------------

// Decoder turns one source stream into canonical rows ([][]string). A new
// source format (or a CSV variant) is a new Decoder + Config.Format; the
// catch-up loop, journal and sinks do not change.
type Decoder interface {
	Decode(r io.Reader) ([][]string, error)
}

// delimitedDecoder decodes delimiter-separated text (CSV, TSV, ...) into
// canonical rows, tolerating ragged records.
type delimitedDecoder struct {
	comma rune
}

func (d delimitedDecoder) Decode(r io.Reader) ([][]string, error) {
	rd := csv.NewReader(r)
	rd.Comma = d.comma
	rd.FieldsPerRecord = -1
	var rows [][]string
	for {
		row, err := rd.Read()
		if err == io.EOF {
			break
		}
		if err != nil {
			return nil, err
		}
		rows = append(rows, row)
	}
	return rows, nil
}

// decoderFor resolves Config.Format to a Decoder.
func decoderFor(format string) (Decoder, error) {
	switch format {
	case "", "csv":
		return delimitedDecoder{comma: ','}, nil
	case "tsv":
		return delimitedDecoder{comma: '\t'}, nil
	default:
		return nil, fmt.Errorf("unknown source format %q (supported: csv, tsv)", format)
	}
}

// ---------------------------------------------------------------------------
// EXTENSION POINT 2 — output/storage
// ---------------------------------------------------------------------------

// Sink persists one day of canonical rows. A new storage target (another file
// layout, a database, a message queue, ...) is a new Sink + Config.Sink.
//
// Sink.Write must be IDEMPOTENT per date: a power loss before the journal mark
// makes the day re-collected, so Write may be called again for the same date.
// A relational sink therefore commits per date (e.g. BEGIN; DELETE WHERE date;
// INSERT rows; COMMIT) rather than blindly appending.
type Sink interface {
	Write(date time.Time, rows [][]string) error
	// Location returns a human-readable description of where date was stored.
	Location(date time.Time) string
}

// fileSink writes one deterministic CSV file per day into OutDir.
type fileSink struct{ OutDir string }

func (s fileSink) Write(date time.Time, rows [][]string) error {
	return writeOutput(outputPath(s.OutDir, date), rows)
}

func (s fileSink) Location(date time.Time) string { return outputPath(s.OutDir, date) }

// sinkOf returns cfg.Sink or the default per-day file sink.
func sinkOf(cfg Config) Sink {
	if cfg.Sink != nil {
		return cfg.Sink
	}
	return fileSink{OutDir: cfg.OutDir}
}

// ---------------------------------------------------------------------------
// Journal / Attempts (unchanged by format/sink/metadata changes)
// ---------------------------------------------------------------------------

// Report describes one collected day.
type Report struct {
	Date time.Time
	Rows int
	Out  string
	Kind AttemptKind // set when Err != nil
	Err  error
}

// today returns the local calendar date of t.
func today(t time.Time) time.Time {
	y, m, d := t.Date()
	return time.Date(y, m, d, 0, 0, 0, 0, time.Local)
}

// yesterday returns the local date before t.
func yesterday(t time.Time) time.Time {
	return today(t).AddDate(0, 0, -1)
}

func parseDate(s string) (time.Time, error) {
	d, err := time.ParseInLocation("2006-01-02", s, time.Local)
	if err != nil {
		return time.Time{}, fmt.Errorf("invalid date %q (want YYYY-MM-DD)", s)
	}
	return d, nil
}

// formatDate is the canonical journal/attempt key.
func formatDate(d time.Time) string { return d.Format("2006-01-02") }

type journal struct {
	mu   sync.Mutex
	path string
	done map[string]struct{}
}

func openJournal(path string) (*journal, error) {
	j := &journal{path: path, done: make(map[string]struct{})}
	f, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		return j, nil // first run
	}
	if err != nil {
		return nil, fmt.Errorf("open journal %s: %w", path, err)
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" {
			continue
		}
		if _, err := parseDate(line); err != nil {
			continue // ignore torn/invalid tail line
		}
		j.done[line] = struct{}{}
	}
	if err := sc.Err(); err != nil {
		return nil, fmt.Errorf("read journal %s: %w", path, err)
	}
	return j, nil
}

func (j *journal) Done(date time.Time) bool {
	j.mu.Lock()
	defer j.mu.Unlock()
	_, ok := j.done[formatDate(date)]
	return ok
}

// Mark records date as completed and fsyncs before returning.
func (j *journal) Mark(date time.Time) error {
	j.mu.Lock()
	defer j.mu.Unlock()
	if err := os.MkdirAll(filepath.Dir(j.path), 0o755); err != nil {
		return err
	}
	f, err := os.OpenFile(j.path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
	if err != nil {
		return fmt.Errorf("open journal %s: %w", j.path, err)
	}
	if _, err := f.WriteString(formatDate(date) + "\n"); err != nil {
		f.Close()
		return fmt.Errorf("append journal: %w", err)
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return fmt.Errorf("fsync journal: %w", err)
	}
	if err := f.Close(); err != nil {
		return err
	}
	j.done[formatDate(date)] = struct{}{}
	return nil
}

// AttemptKind classifies why a day could not be collected.
type AttemptKind string

const (
	AttemptMissing     AttemptKind = "missing"
	AttemptUnreachable AttemptKind = "unreachable"
	AttemptParse       AttemptKind = "parse"
	AttemptWrite       AttemptKind = "write"
	AttemptJournal     AttemptKind = "journal"
)

// Attempt is one outstanding (not yet completed) day.
type Attempt struct {
	Date      time.Time
	Kind      AttemptKind
	Count     int
	LastError string
	LastAt    time.Time
}

type attempts struct {
	mu     sync.Mutex
	path   string
	byDate map[string]*Attempt
}

func attemptsPathOf(cfg Config) string {
	if cfg.AttemptsPath != "" {
		return cfg.AttemptsPath
	}
	return filepath.Join(filepath.Dir(cfg.JournalPath), "attempts.log")
}

func openAttempts(path string) (*attempts, error) {
	a := &attempts{path: path, byDate: make(map[string]*Attempt)}
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return a, nil
	}
	if err != nil {
		return nil, fmt.Errorf("open attempts %s: %w", path, err)
	}
	for _, line := range strings.Split(string(data), "\n") {
		line = strings.TrimSpace(line)
		if line == "" {
			continue
		}
		fields := strings.Split(line, "\t")
		if len(fields) != 5 {
			continue
		}
		d, err := parseDate(fields[0])
		if err != nil {
			continue
		}
		n, err := strconv.Atoi(fields[2])
		if err != nil {
			continue
		}
		nanos, err := strconv.ParseInt(fields[3], 10, 64)
		if err != nil {
			continue
		}
		key := formatDate(d)
		a.byDate[key] = &Attempt{
			Date:      d,
			Kind:      AttemptKind(fields[1]),
			Count:     n,
			LastError: fields[4],
			LastAt:    time.Unix(0, nanos),
		}
	}
	return a, nil
}

func (a *attempts) Note(date time.Time, kind AttemptKind, err error) {
	if err == nil {
		return
	}
	a.mu.Lock()
	defer a.mu.Unlock()
	key := formatDate(date)
	e := a.byDate[key]
	if e == nil {
		e = &Attempt{Date: date}
		a.byDate[key] = e
	}
	e.Kind = kind
	e.Count++
	e.LastError = sanitizeOneLine(err.Error())
	e.LastAt = time.Now()
	_ = a.writeLocked()
}

func (a *attempts) Clear(date time.Time) {
	a.mu.Lock()
	defer a.mu.Unlock()
	if _, ok := a.byDate[formatDate(date)]; !ok {
		return
	}
	delete(a.byDate, formatDate(date))
	_ = a.writeLocked()
}

func (a *attempts) Snapshot() []Attempt {
	a.mu.Lock()
	defer a.mu.Unlock()
	out := make([]Attempt, 0, len(a.byDate))
	for _, e := range a.byDate {
		out = append(out, *e)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Date.Before(out[j].Date) })
	return out
}

func (a *attempts) writeLocked() error {
	if err := os.MkdirAll(filepath.Dir(a.path), 0o755); err != nil {
		return err
	}
	var b strings.Builder
	for _, e := range a.byDate {
		fmt.Fprintf(&b, "%s\t%s\t%d\t%d\t%s\n",
			formatDate(e.Date), e.Kind, e.Count, e.LastAt.UnixNano(), sanitizeOneLine(e.LastError))
	}
	return writeFileAtomic(a.path, []byte(b.String()))
}

func sanitizeOneLine(s string) string {
	return strings.NewReplacer("\r", " ", "\n", " ", "\t", " ").Replace(s)
}

func snapshotAttempts(cfg Config) ([]Attempt, error) {
	a, err := openAttempts(attemptsPathOf(cfg))
	if err != nil {
		return nil, err
	}
	return a.Snapshot(), nil
}

// ---------------------------------------------------------------------------
// Path rendering with date + arbitrary metadata
// ---------------------------------------------------------------------------

// renderPath fills metadata placeholders ({site}, {line}, {batch}, ...) plus
// the date placeholders ({YYYY} {MM} {DD} {yyyymmdd}) into template. Unknown
// placeholders are left untouched so a misconfigured template fails loudly at
// open time (file not found) instead of silently corrupting a path.
func renderPath(template string, meta map[string]string, d time.Time) string {
	repl := make([]string, 0, 2*len(meta)+8)
	for k, v := range meta {
		repl = append(repl, "{"+k+"}", v)
	}
	repl = append(repl,
		"{YYYY}", fmt.Sprintf("%04d", d.Year()),
		"{MM}", fmt.Sprintf("%02d", int(d.Month())),
		"{DD}", fmt.Sprintf("%02d", d.Day()),
		"{yyyymmdd}", d.Format("20060102"),
	)
	return strings.NewReplacer(repl...).Replace(template)
}

// ---------------------------------------------------------------------------
// Catch-up pass
// ---------------------------------------------------------------------------

func planMissing(cfg Config, j *journal, now time.Time) ([]time.Time, error) {
	since, err := parseDate(cfg.Since)
	if err != nil {
		return nil, err
	}
	until := yesterday(now)
	if since.After(until) {
		return nil, nil
	}
	var out []time.Time
	for d := today(since); !d.After(until); d = d.AddDate(0, 0, 1) {
		if j.Done(d) {
			continue
		}
		out = append(out, d)
	}
	return out, nil
}

// RunCatchUp executes one full catch-up pass. It is cancellation-safe and
// crash-safe: each day is marked only after its sink persisted the day fully,
// so a context cancellation or power loss simply leaves the remaining days
// unmarked for the next run. Failed days never enter the completion journal:
// they accumulate in the attempts ledger and are retried by the next pass.
func RunCatchUp(ctx context.Context, cfg Config) ([]Report, error) {
	if cfg.Now == nil {
		cfg.Now = time.Now
	}
	j, err := openJournal(cfg.JournalPath)
	if err != nil {
		return nil, err
	}
	att, err := openAttempts(attemptsPathOf(cfg))
	if err != nil {
		return nil, err
	}
	dates, err := planMissing(cfg, j, cfg.Now())
	if err != nil {
		return nil, err
	}
	reports := make([]Report, 0, len(dates))
	var errs []error
	for _, d := range dates {
		if err := ctx.Err(); err != nil {
			return reports, err // partial progress already persisted
		}
		r := collectDate(ctx, cfg, j, att, d)
		reports = append(reports, r)
		if r.Err != nil {
			errs = append(errs, r.Err)
		}
	}
	if len(errs) > 0 {
		return reports, errors.Join(errs...)
	}
	return reports, nil
}

// collectDate reads one day's source, decodes it with the configured Decoder,
// persists it through the configured Sink, and only then marks the day in the
// journal. Any failure keeps the day unmarked and records it in the attempts
// ledger (defer/retry semantics).
func collectDate(ctx context.Context, cfg Config, j *journal, att *attempts, d time.Time) Report {
	dec, derr := decoderFor(cfg.Format)
	if derr != nil {
		return Report{Date: d, Kind: AttemptParse, Err: fmt.Errorf("%s: %w", formatDate(d), derr)}
	}
	sink := sinkOf(cfg)
	r := Report{Date: d, Out: sink.Location(d)}
	src := renderPath(cfg.Template, cfg.meta(), d)

	f, err := os.Open(src)
	if err != nil {
		kind := AttemptUnreachable
		if errors.Is(err, os.ErrNotExist) {
			kind = AttemptMissing
		}
		if kind == AttemptMissing && cfg.MissingOK {
			if merr := j.Mark(d); merr != nil {
				att.Note(d, AttemptJournal, merr)
				r.Err = fmt.Errorf("%s: %w", formatDate(d), merr)
				return r
			}
			att.Clear(d)
			return r // Rows == 0
		}
		att.Note(d, kind, err)
		r.Kind = kind
		r.Err = fmt.Errorf("%s (%s): %w", formatDate(d), kind, err)
		return r
	}

	rows, perr := dec.Decode(f)
	f.Close()
	if perr != nil {
		att.Note(d, AttemptParse, perr)
		r.Kind = AttemptParse
		r.Err = fmt.Errorf("%s (%s): %w", formatDate(d), AttemptParse, perr)
		return r
	}

	// Persist through the Sink first, then mark. Re-collecting an already
	// written day must be safe (idempotent Sink).
	if err := sink.Write(d, rows); err != nil {
		att.Note(d, AttemptWrite, err)
		r.Kind = AttemptWrite
		r.Err = fmt.Errorf("%s (%s): %w", formatDate(d), AttemptWrite, err)
		return r
	}
	if err := j.Mark(d); err != nil {
		att.Note(d, AttemptJournal, err)
		r.Kind = AttemptJournal
		r.Err = fmt.Errorf("%s (%s): %w", formatDate(d), AttemptJournal, err)
		return r
	}
	att.Clear(d)
	r.Rows = len(rows)
	return r
}

func outputPath(outDir string, d time.Time) string {
	return filepath.Join(outDir, d.Format("2006-01-02")+".csv")
}

// readCSV opens and parses a comma-separated CSV file (used by tests and the
// default file sink's consumers).
func readCSV(path string) ([][]string, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	return delimitedDecoder{comma: ','}.Decode(f)
}

// writeOutput atomically writes rows to path (temp file + rename).
func writeOutput(path string, rows [][]string) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	var b strings.Builder
	w := csv.NewWriter(&b)
	for _, row := range rows {
		if err := w.Write(row); err != nil {
			return err
		}
	}
	w.Flush()
	if err := w.Error(); err != nil {
		return err
	}
	return writeFileAtomic(path, []byte(b.String()))
}

// writeFileAtomic writes data to path via tmp + fsync + rename so a power loss
// never leaves a half-written file.
func writeFileAtomic(path string, data []byte) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	tmp := path + ".tmp"
	f, err := os.Create(tmp)
	if err != nil {
		return err
	}
	if _, err := f.Write(data); err != nil {
		f.Close()
		return err
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err := f.Close(); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Remove(path)
		if err2 := os.Rename(tmp, path); err2 != nil {
			return err2
		}
	}
	return nil
}

// sortedOutFiles lists normalized outputs in the out dir (for demo/tests).
func sortedOutFiles(outDir string) ([]string, error) {
	entries, err := os.ReadDir(outDir)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, nil
		}
		return nil, err
	}
	var files []string
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(e.Name(), ".csv") {
			continue
		}
		files = append(files, filepath.Join(outDir, e.Name()))
	}
	sort.Strings(files)
	return files, nil
}
