package watch

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"testing"
	"time"
)

// engineTest is a deterministic observation source: tests mutate a revision and
// then pulse the session.
type engineTest struct {
	mu  sync.Mutex
	rev Revision
}

func (e *engineTest) probe() Revision {
	e.mu.Lock()
	defer e.mu.Unlock()
	return e.rev
}

func (e *engineTest) set(r Revision) {
	e.mu.Lock()
	e.rev = r
	e.mu.Unlock()
}

func (e *engineTest) wait(pulse <-chan struct{}, ready chan struct{}) sessionWait {
	var once sync.Once
	return func(ctx context.Context, stopped <-chan struct{}) (sessionAction, error) {
		once.Do(func() {
			if ready != nil {
				close(ready)
			}
		})
		select {
		case <-pulse:
			return actionEvent, nil
		case <-ctx.Done():
			return actionCtx, nil
		case <-stopped:
			return actionStop, nil
		}
	}
}

// startEngine sets the baseline revision, starts the session, and blocks until
// the baseline has been captured (the session reached its first wait).
func startEngine(t *testing.T, e *engineTest, pulse chan struct{}, src Source, initial Revision) (*subscription, context.CancelFunc) {
	t.Helper()
	e.set(initial)
	sub := newSubscription(DefaultBufferSize)
	ready := make(chan struct{})
	ctx, cancel := context.WithCancel(context.Background())
	go runSession(ctx, sub, src, e.probe, e.wait(pulse, ready))
	t.Cleanup(func() { cancel(); sub.Close() })
	<-ready
	return sub, cancel
}

func recvChg(t *testing.T, sub *subscription, d time.Duration) (Change, bool) {
	t.Helper()
	select {
	case c, ok := <-sub.Changes():
		return c, ok
	case <-time.After(d):
		return Change{}, false
	}
}

// W13/W1 — baseline: no initial (fake) change, and same-revision pulses are
// deduplicated.
func TestEngineNoInitialChange(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{Exists: true, ID: "v1"})

	select {
	case c := <-sub.Changes():
		t.Fatalf("unexpected initial change: %+v", c)
	case <-time.After(80 * time.Millisecond):
	}
	e.set(Revision{Exists: true, ID: "v1"})
	pulse <- struct{}{}
	select {
	case c := <-sub.Changes():
		t.Fatalf("dedup failed, got %+v", c)
	case <-time.After(120 * time.Millisecond):
	}
}

// W5/W15 — revision dedup: many pulses with the same revision emit one change.
func TestEngineRevisionDedup(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{Exists: true, ID: "a"})

	e.set(Revision{Exists: true, ID: "b"})
	for i := 0; i < 5; i++ {
		pulse <- struct{}{}
	}
	c, ok := recvChg(t, sub, 2*time.Second)
	if !ok {
		t.Fatal("expected one change")
	}
	if c.Kind != ChangeModified || c.Current.ID != "b" {
		t.Fatalf("change = %+v", c)
	}
	select {
	case extra := <-sub.Changes():
		t.Fatalf("duplicate change delivered: %+v", extra)
	case <-time.After(150 * time.Millisecond):
	}
}

// W6 — invalid Previous/Current transitions rejected.
func TestEngineChangeValidation(t *testing.T) {
	cases := []Change{
		{SourceID: "s", URI: "u", Kind: ChangeAdded, Previous: Revision{Exists: true, ID: "a"}, Current: Revision{Exists: true, ID: "b"}},
		{SourceID: "s", URI: "u", Kind: ChangeModified, Previous: Revision{Exists: true, ID: "a"}, Current: Revision{Exists: true, ID: "a"}},
		{SourceID: "s", URI: "u", Kind: ChangeModified, Previous: Revision{Exists: false}, Current: Revision{Exists: true, ID: "b"}},
		{SourceID: "s", URI: "u", Kind: ChangeRemoved, Previous: Revision{Exists: false}, Current: Revision{}},
		{SourceID: "s", URI: "u", Kind: ChangeAdded, Previous: Revision{}, Current: Revision{}},
	}
	for i, c := range cases {
		if err := validateChange(c); !errors.Is(err, ErrInvalidChange) {
			t.Fatalf("case %d: err = %v, want ErrInvalidChange", i, err)
		}
	}
	if err := validateChange(Change{SourceID: "s", URI: "u", Kind: ChangeAdded, Previous: Revision{}, Current: Revision{Exists: true, ID: "b"}}); err != nil {
		t.Fatalf("valid added rejected: %v", err)
	}
}

// W7/P1 — ordering: when the consumer reads as changes arrive, delivered order
// equals the observation order.
func TestEngineOrdering(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 4)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{})

	for _, id := range []string{"v1", "v2", "v3"} {
		e.set(Revision{Exists: true, ID: id})
		pulse <- struct{}{}
		c, ok := recvChg(t, sub, 2*time.Second)
		if !ok {
			t.Fatalf("missing change for %s", id)
		}
		if c.Current.ID != id {
			t.Fatalf("got %s, want %s", c.Current.ID, id)
		}
	}
}

// W3/W4 — removal and reappearance produce Removed then Added.
func TestEngineRemoveReappear(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{Exists: true, ID: "a"})

	e.set(Revision{})
	pulse <- struct{}{}
	c1, ok := recvChg(t, sub, 2*time.Second)
	if !ok || c1.Kind != ChangeRemoved {
		t.Fatalf("expected Removed, got %+v ok=%v", c1, ok)
	}
	e.set(Revision{Exists: true, ID: "a2"})
	pulse <- struct{}{}
	c2, ok := recvChg(t, sub, 2*time.Second)
	if !ok || c2.Kind != ChangeAdded {
		t.Fatalf("expected Added on reappearance, got %+v ok=%v", c2, ok)
	}
}

// W14/P3 — coalescing: a slow consumer receives a monotonic subsequence and
// never loses the latest revision.
func TestEngineCoalescingLatest(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 512)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{})

	for i := 0; i < 200; i++ {
		e.set(Revision{Exists: true, ID: fmt.Sprintf("%05d", i)})
		pulse <- struct{}{}
	}

	last := -1
	for {
		c, ok := recvChg(t, sub, 400*time.Millisecond)
		if !ok {
			break
		}
		var n int
		if _, err := fmt.Sscanf(c.Current.ID, "%05d", &n); err != nil {
			t.Fatalf("bad id %q", c.Current.ID)
		}
		if n <= last {
			t.Fatalf("out of order: %d after %d", n, last)
		}
		last = n
	}
	if last != 199 {
		t.Fatalf("final delivered index = %d, want 199 (latest state lost?)", last)
	}
}

// W10/P5 — Close: idempotent; channel closes; Err nil on normal close.
func TestEngineSubscriptionClose(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	sub, _ := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{})
	e.set(Revision{Exists: true, ID: "x"})
	pulse <- struct{}{}
	if _, ok := recvChg(t, sub, 2*time.Second); !ok {
		t.Fatal("expected change")
	}
	if err := sub.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if err := sub.Close(); err != nil {
		t.Fatalf("second Close: %v", err)
	}
	select {
	case _, open := <-sub.Changes():
		if open {
			t.Fatal("channel still open after Close")
		}
	case <-time.After(2 * time.Second):
		t.Fatal("channel did not close")
	}
	if err := sub.Err(); err != nil {
		t.Fatalf("Err after normal close = %v, want nil", err)
	}
}

// W12 — context cancellation terminates with the context error.
func TestEngineContextCancellation(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	sub, cancel := startEngine(t, e, pulse, Source{ID: "s", Kind: "file", URI: "file:///x"}, Revision{})
	cancel()
	deadline := time.Now().Add(2 * time.Second)
	for sub.Err() == nil && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if !errors.Is(sub.Err(), context.Canceled) {
		t.Fatalf("Err = %v, want context.Canceled", sub.Err())
	}
}

// W18 — a fatal watcher error is reported through Err() and closes the
// subscription.
func TestEngineFatalError(t *testing.T) {
	e := &engineTest{}
	pulse := make(chan struct{}, 16)
	fatal := make(chan error, 1)
	sub := newSubscription(DefaultBufferSize)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	src := Source{ID: "s", Kind: "file", URI: "file:///x"}
	wait := func(ctx context.Context, stopped <-chan struct{}) (sessionAction, error) {
		select {
		case <-pulse:
			return actionEvent, nil
		case err := <-fatal:
			return actionFatal, err
		case <-ctx.Done():
			return actionCtx, nil
		case <-stopped:
			return actionStop, nil
		}
	}
	go runSession(ctx, sub, src, e.probe, wait)

	boom := errors.New("watcher boom")
	fatal <- boom
	deadline := time.Now().Add(2 * time.Second)
	for sub.Err() == nil && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if !errors.Is(sub.Err(), boom) {
		t.Fatalf("Err = %v, want watcher error", sub.Err())
	}
	select {
	case _, open := <-sub.Changes():
		if open {
			t.Fatal("channel open after fatal error")
		}
	case <-time.After(2 * time.Second):
		t.Fatal("channel did not close after fatal error")
	}
	_ = sub.Close()
}

// W8 — subscription isolation: closing one subscription never affects another
// observing the same source state.
func TestEngineSubscriptionIsolation(t *testing.T) {
	e := &engineTest{}
	src := Source{ID: "s", Kind: "file", URI: "file:///x"}
	e.set(Revision{})

	s1 := newSubscription(DefaultBufferSize)
	s2 := newSubscription(DefaultBufferSize)
	p1 := make(chan struct{}, 4)
	p2 := make(chan struct{}, 4)
	r1 := make(chan struct{})
	r2 := make(chan struct{})
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	go runSession(ctx, s1, src, e.probe, e.wait(p1, r1))
	go runSession(ctx, s2, src, e.probe, e.wait(p2, r2))
	<-r1
	<-r2

	e.set(Revision{Exists: true, ID: "v1"})
	p1 <- struct{}{}
	p2 <- struct{}{}
	if _, ok := recvChg(t, s1, 2*time.Second); !ok {
		t.Fatal("s1 missed change")
	}
	if _, ok := recvChg(t, s2, 2*time.Second); !ok {
		t.Fatal("s2 missed change")
	}

	if err := s1.Close(); err != nil {
		t.Fatal(err)
	}
	e.set(Revision{Exists: true, ID: "v2"})
	p2 <- struct{}{}
	c, ok := recvChg(t, s2, 2*time.Second)
	if !ok || c.Current.ID != "v2" {
		t.Fatalf("s2 affected by s1 close: %+v ok=%v", c, ok)
	}
	_ = s2.Close()
}
