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
// a rotated JSONL file.
func New(ringSize int, filePath string) *Store {
	s := &Store{cap: ringSize}
	if filePath != "" {
		_ = s.SetFile(filePath, 10<<20)
	}
	return s
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
	s.file = f
	s.maxSize = maxSize
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
	_ = s.file.Close()
	_ = os.Rename(s.file.Name(), s.file.Name()+".1")
	f, err := os.OpenFile(s.file.Name(), os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644)
	if err != nil {
		s.file = nil
		s.fileErr = err
		return
	}
	s.file = f
	s.size = 0
}

// Latest returns up to n entries, newest first, filtered by minimum level and
// an optional substring match on message+attributes.
func (s *Store) Latest(n int, minLevel, contains string) []Entry {
	s.mu.Lock()
	defer s.mu.Unlock()
	min := levelRank(minLevel)
	out := make([]Entry, 0, n)
	for i := len(s.ring) - 1; i >= 0 && len(out) < n; i-- {
		e := s.ring[i]
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
