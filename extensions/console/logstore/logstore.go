// Package logstore provides the application log pipeline as console
// infrastructure: a bounded in-memory ring for the console, a size-rotated
// JSONL file for durability, and an slog.Handler that feeds both — plus the
// standard "logs" hub query (limit/level/contains) every console log panel
// consumes. Applications wire the handler into their logger; the query
// registration is one call.
package logstore

import (
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"
)

// Entry is one structured log line.
type Entry struct {
	Time    time.Time         `json:"time"`
	Level   string            `json:"level"`
	Msg     string            `json:"msg"`
	Attrs   map[string]string `json:"attrs,omitempty"`
	AttrStr string            `json:"-"` // preformatted attribute suffix for the ring
}

// Store is a bounded ring of log entries with optional JSONL persistence.
type Store struct {
	mu      sync.Mutex
	ring    []Entry
	cap     int
	file    *os.File
	fileErr error
	size    int64
	maxSize int64
}

// New creates a store with the given ring capacity, optionally persisting to
// a rotated JSONL file. A persistence-setup failure is NOT silently dropped:
// it is recorded in the store (Err()) so the durability contract is honest —
// the store keeps operating on the ring alone.
func New(ringSize int, filePath string) *Store {
	s := &Store{cap: ringSize}
	if filePath != "" {
		s.fileErr = s.SetFile(filePath, 10<<20)
	}
	return s
}

// Err reports the terminal persistence failure, if any: a setup failure from
// New/SetFile, or a rotation failure. nil means every log line is durably
// persisted (when a file was configured).
func (s *Store) Err() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.fileErr
}

// Close releases the persistence file (idempotent). After Close, Add keeps
// feeding the ring but stops persisting.
func (s *Store) Close() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.file == nil {
		return nil
	}
	err := s.file.Close()
	s.file = nil
	return err
}

var defaultStore = New(2000, "")

// Default returns the process-wide log store (nil-safe handlers operate on it).
func Default() *Store { return defaultStore }

// SetFile enables JSONL persistence with size-based rotation
// (<path> -> <path>.1). Must be called before the first write.
func (s *Store) SetFile(path string, maxSize int64) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if path == "" {
		return nil
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	f, err := os.OpenFile(path, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	if st, err := f.Stat(); err == nil {
		s.size = st.Size()
	}
	// P3-8: a previous handle must not leak when SetFile is called again.
	if s.file != nil {
		_ = s.file.Close()
	}
	s.file = f
	s.maxSize = maxSize
	s.fileErr = nil
	return nil
}

// Add appends one entry to the ring and the file (if configured).
func (s *Store) Add(e Entry) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.ring = append(s.ring, e)
	if len(s.ring) > s.cap {
		s.ring = s.ring[len(s.ring)-s.cap:]
	}
	if s.file != nil {
		line, _ := json.Marshal(e)
		line = append(line, '\n')
		if s.size+int64(len(line)) > s.maxSize {
			s.rotateLocked()
		}
		if n, err := s.file.Write(line); err == nil {
			s.size += int64(n)
		}
	}
}

func (s *Store) rotateLocked() {
	// P2-5: a failed rotation must stay honest and keep the size accounting
	// correct. The old code ignored the Rename error and reset s.size to 0 on
	// reopen, so a failed rename left the old file at ~maxSize with the
	// accounting reset — the size check never tripped again and the file grew
	// unbounded, all invisible (fileErr was write-only).
	if err := s.file.Close(); err != nil {
		s.fileErr = err
		s.file = nil
		return
	}
	if err := os.Rename(s.file.Name(), s.file.Name()+".1"); err != nil {
		// Keep the CURRENT file open and its size accounting intact; retry the
		// rotation on the next overflow. Persisting continues (the file just
		// exceeds maxSize until a later rotation succeeds).
		s.fileErr = err
		return
	}
	f, err := os.OpenFile(s.file.Name(), os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644)
	if err != nil {
		s.fileErr = err
		s.file = nil
		return
	}
	s.file = f
	s.size = 0
}

// Latest returns up to n entries, newest first, filtered by minimum level and
// an optional substring match on message+attributes.
func (s *Store) Latest(n int, minLevel, contains string) []Entry {
	// P3-9: snapshot the ring under the lock and scan OUTSIDE it — `contains`
	// is unbounded HTTP input and the O(entries x needle) scan under s.mu
	// stalled every Add() (every log line app-wide) for its duration. The
	// needle itself is also capped: beyond the cap it cannot match a ring
	// entry's realistic line anyway.
	if len(contains) > maxNeedleLen {
		contains = contains[:maxNeedleLen]
	}
	s.mu.Lock()
	ring := append([]Entry(nil), s.ring...)
	s.mu.Unlock()
	min := levelRank(minLevel)
	out := make([]Entry, 0, n)
	for i := len(ring) - 1; i >= 0 && len(out) < n; i-- {
		e := ring[i]
		if levelRank(e.Level) < min {
			continue
		}
		if contains != "" && !containsFold(e.Msg+e.AttrStr, contains) {
			continue
		}
		// 返回副本：调用方修改 Attrs 不得影响环内条目。
		if e.Attrs != nil {
			attrs := make(map[string]string, len(e.Attrs))
			for k, v := range e.Attrs {
				attrs[k] = v
			}
			e.Attrs = attrs
		}
		out = append(out, e)
	}
	return out
}

func containsFold(haystack, needle string) bool {
	h, n := []rune(haystack), []rune(needle)
	ln := len([]rune(strings.ToLower(needle)))
	lh := len(h)
	if ln > lh {
		return false
	}
	for i := 0; i+ln <= lh; i++ {
		ok := true
		for j := 0; j < ln; j++ {
			a, b := h[i+j], n[j]
			if a >= 'A' && a <= 'Z' {
				a += 32
			}
			if b >= 'A' && b <= 'Z' {
				b += 32
			}
			if a != b {
				ok = false
				break
			}
		}
		if ok {
			return true
		}
	}
	return false
}

func levelRank(l string) int {
	switch l {
	case "DEBUG":
		return 0
	case "INFO":
		return 1
	case "WARN":
		return 2
	case "ERROR":
		return 3
	}
	return 1
}

// NewHandler returns an slog.Handler that feeds the ring (and mirrors every
// record to the provided writer — pass nil to only feed the ring). The JSON
// encoding flows through one path only: a naive "Add in the handler, then
// delegate" implementation would store every record twice.
func (s *Store) NewHandler(mirror io.Writer) slog.Handler {
	var out io.Writer = &ringWriter{s}
	if mirror != nil {
		out = io.MultiWriter(mirror, out)
	}
	return slog.NewJSONHandler(out, &slog.HandlerOptions{Level: slog.LevelDebug})
}

type ringWriter struct{ s *Store }

func (w *ringWriter) Write(p []byte) (int, error) {
	var raw map[string]any
	_ = json.Unmarshal(p, &raw)
	e := Entry{
		Time:  time.Now(),
		Level: fmt.Sprint(raw["level"]),
		Msg:   fmt.Sprint(raw["msg"]),
		Attrs: map[string]string{},
	}
	keys := make([]string, 0, len(raw))
	for k := range raw {
		if k == "time" || k == "level" || k == "msg" {
			continue
		}
		keys = append(keys, k)
	}
	sort.Strings(keys)
	for _, k := range keys {
		e.Attrs[k] = fmt.Sprint(raw[k])
	}
	e.AttrStr = e.Msg
	for _, k := range keys {
		e.AttrStr += " " + k + "=" + e.Attrs[k]
	}
	w.s.Add(e)
	return len(p), nil
}

// maxNeedleLen bounds the user-supplied substring filter (P3-9): beyond this,
// a match against any realistic log line is impossible, so scanning further is
// wasted work that used to run under the store mutex.
const maxNeedleLen = 4096
