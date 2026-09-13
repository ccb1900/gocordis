package logstore

import (
	"context"
	"encoding/json"
	"log/slog"
	"net/url"
	"strings"
	"testing"
	"time"

	consolehub "dynamic-runtime/extensions/console/hub"
)

func TestRingAndFilters(t *testing.T) {
	s := New(100, "")
	h := s.NewHandler(nil)
	rec := slog.NewRecord(time.Now(), slog.LevelInfo, "hello world", 0)
	if err := h.Handle(context.Background(), rec); err != nil {
		t.Fatal(err)
	}
	rec = slog.NewRecord(time.Now(), slog.LevelError, "boom", 0)
	if err := h.Handle(context.Background(), rec); err != nil {
		t.Fatal(err)
	}
	if got := len(s.Latest(10, "", "")); got != 2 {
		t.Fatalf("latest = %d entries, want 2", got)
	}
	if got := s.Latest(10, "ERROR", ""); len(got) != 1 || got[0].Level != "ERROR" {
		t.Fatalf("level filter = %+v", got)
	}
	if got := s.Latest(10, "", "boom"); len(got) != 1 {
		t.Fatalf("contains filter = %+v", got)
	}
	if got := s.Latest(1, "", ""); len(got) != 1 || got[0].Msg != "boom" {
		t.Fatalf("newest-first = %+v", got)
	}
}

// The registered hub query serves the ring with the documented parameters.
func TestRegisterLogsQuery(t *testing.T) {
	s := New(100, "")
	h := s.NewHandler(nil)
	rec := slog.NewRecord(time.Now(), slog.LevelWarn, "disk almost full", 0)
	if err := h.Handle(context.Background(), rec); err != nil {
		t.Fatal(err)
	}

	hub := consolehub.New()
	un, err := RegisterLogsQuery(hub, s)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = un() })

	handler, ok := hub.Query("logs")
	if !ok {
		t.Fatal("logs query not registered")
	}
	out, herr := handler(context.Background(), url.Values{"limit": []string{"10"}, "level": []string{"WARN"}})
	if herr != nil {
		t.Fatalf("query error: %v", herr)
	}
	data, _ := json.Marshal(out)
	if !strings.Contains(string(data), `"disk almost full"`) {
		t.Fatalf("query result = %s", data)
	}
}

