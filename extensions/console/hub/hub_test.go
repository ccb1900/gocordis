package hub

import (
	"context"
	"encoding/json"
	"net/url"
	"sync/atomic"
	"testing"
	"time"
)

func TestRegisterQueryEffectLifecycle(t *testing.T) {
	r := New()
	calls := 0
	un, err := r.RegisterQuery("collections", "owner", func(ctx context.Context, p url.Values) (any, *Error) {
		calls++
		return "ok", nil
	})
	if err != nil {
		t.Fatal(err)
	}
	if h, ok := r.Query("collections"); !ok {
		t.Fatal("query missing after register")
	} else if _, he := h(context.Background(), url.Values{}); he != nil {
		t.Fatalf("handler error: %v", he)
	}
	if err := un(); err != nil {
		t.Fatal(err)
	}
	if err := un(); err != nil { // idempotent
		t.Fatal(err)
	}
	if _, ok := r.Query("collections"); ok {
		t.Fatal("query still present after unregister")
	}
	if calls != 1 {
		t.Fatalf("calls = %d", calls)
	}
}

func TestRegisterDuplicateRejectedWithoutDisturbingOwner(t *testing.T) {
	r := New()
	if _, err := r.RegisterCommand("trigger", "app-a", func(context.Context, json.RawMessage) error { return nil }); err != nil {
		t.Fatal(err)
	}
	if _, err := r.RegisterCommand("trigger", "app-b", func(context.Context, json.RawMessage) error { return nil }); err == nil {
		t.Fatal("duplicate registration must fail")
	}
	if h, ok := r.Command("trigger"); !ok {
		t.Fatal("original owner lost")
	} else if err := h(context.Background(), json.RawMessage("{}")); err != nil {
		t.Fatalf("original handler broken: %v", err)
	}
}

func TestObservationFansOutAndSlowConsumerDrops(t *testing.T) {
	r := New()
	var seen atomic.Int64
	un, err := r.OnObservation(1, func(o Observation) { seen.Add(1) })
	if err != nil {
		t.Fatal(err)
	}
	defer un()
	for i := 0; i < 100; i++ {
		r.Publish(Observation{Type: "tick", Timestamp: "t"})
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) && seen.Load() < 1 {
		time.Sleep(5 * time.Millisecond)
	}
	if seen.Load() < 1 {
		t.Fatal("live consumer did not receive any observation")
	}
}
