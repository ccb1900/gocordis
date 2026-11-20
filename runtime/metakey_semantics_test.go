package runtime

import (
	"errors"
	"fmt"
	"strings"
	"sync"
	"testing"
	"time"
)

// ---------------------------------------------------------------------------
// RT-12: MetaKey monoid — identity, lock scope, panic containment, and
// definition-consistency across constructions of the same Capability identity.
// ---------------------------------------------------------------------------

// metaCfg is a monoid whose identity differs from the Go zero value: zero has
// base=10, combine adds bases. RequireMeta without any declared/context
// metadata must observe the monoid identity, not the Go zero (RT-12a).
type metaCfg struct{ base int }

func metaCombine(a, b metaCfg) metaCfg { return metaCfg{base: a.base + b.base} }

var metaCfgKey = NewMetaKey[string, metaCfg]("meta.cfg.service", metaCfg{base: 10}, metaCombine)

type metaCfgProvider struct{ offset int }

func (c *metaCfgProvider) Name() string          { return "meta-cfg-provider" }
func (c *metaCfgProvider) Inject() []Dependency  { return nil }
func (c *metaCfgProvider) Provide() []Capability { return []Capability{metaCfgKey.Capability()} }
func (c *metaCfgProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, ProvideMeta(ctx, metaCfgKey, func(m metaCfg) string {
		return strings.Repeat("x", m.base+c.offset)
	})
}

type metaCfgConsumer struct {
	wantBase int
	out      chan string
}

func (c *metaCfgConsumer) Name() string { return "meta-cfg-consumer" }
func (c *metaCfgConsumer) Inject() []Dependency {
	return []Dependency{RequiresMeta(metaCfgKey, metaCfg{base: 0})}
}
func (c *metaCfgConsumer) Provide() []Capability {
	return nil
}
func (c *metaCfgConsumer) Apply(ctx *Context) (Cleanup, error) {
	v, err := RequireMeta(ctx, metaCfgKey)
	if err != nil {
		return nil, err
	}
	if c.out != nil {
		c.out <- v
	}
	return nil, nil
}

func TestRequireMetaUsesMonoidIdentity(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	out := make(chan string, 1)
	pf, err := rt.Load(&metaCfgProvider{offset: 0})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	cf, err := rt.Load(&metaCfgConsumer{out: out})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, cf)

	select {
	case v := <-out:
		if v != "xxxxxxxxxx" { // base=10 from key.zero, not Go zero
			t.Fatalf("RequireMeta observed Go zero instead of the monoid identity: %q", v)
		}
	case <-ctx.Done():
		t.Fatal("consumer never resolved")
	}
}

func TestMetaCombinePanicContained(t *testing.T) {
	boom := func(a, b metaCfg) metaCfg { panic("combine exploded") }
	key := NewMetaKey[string, metaCfg]("meta.boom.service", metaCfg{base: 1}, boom)

	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, err := rt.Load(&metaBoomProvider{key: key})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	cf, err := rt.Load(&metaBoomConsumer{key: key})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, cf)

	_, err = RequireMetaErr(rt, cf, key)
	if err == nil {
		t.Fatal("panicking combine was not contained")
	}
	if !strings.Contains(err.Error(), "panicked") {
		t.Fatalf("panic not reported as an error: %v", err)
	}
}

func TestMetaProviderPanicContained(t *testing.T) {
	key := NewMetaKey[string, metaCfg]("meta.pboom.service", metaCfg{base: 1}, metaCombine)
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, err := rt.Load(&metaPanicProvider{key: key})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	cf, err := rt.Load(&metaBoomConsumer{key: key})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, cf)

	if _, err := RequireMetaErr(rt, cf, key); err == nil {
		t.Fatal("panicking provider function was not contained")
	}
}

// TestMetaKeyMonoidRedefinitionPanics — RT-12d: reconstructing the same
// Capability identity with a diverging monoid definition must fail loudly at
// construction, not silently change the fold semantics.
func TestMetaKeyMonoidRedefinitionPanics(t *testing.T) {
	func() {
		defer func() {
			if r := recover(); r == nil {
				t.Fatal("monoid redefinition did not panic")
			} else if !strings.Contains(fmt.Sprint(r), "monoid") {
				t.Fatalf("unexpected panic: %v", r)
			}
		}()
		_ = NewMetaKey[string, metaCfg]("meta.cfg.service", metaCfg{base: 10}, func(a, b metaCfg) metaCfg {
			return metaCfg{base: a.base * b.base} // different combine
		})
	}()
	// Identical re-construction is legal.
	_ = NewMetaKey[string, metaCfg]("meta.cfg.service", metaCfg{base: 10}, metaCombine)
}

// ---------------------------------------------------------------------------
// RT-13: zero keys are refused.
// ---------------------------------------------------------------------------

func TestZeroKeyRefused(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	caught := false
	func() {
		defer func() { caught = recover() != nil }()
		_ = NewKey[string]("")
	}()
	if !caught {
		t.Fatal("NewKey accepted an empty name")
	}

	// A declaration containing a literal zero key is refused at the mount
	// boundary — before any runtime entry point could act on it.
	if _, err := rt.Load(&zeroKeyProvider{}); !errors.Is(err, ErrInvalidKey) {
		t.Fatalf("zero-key declaration not rejected at Load: %v", err)
	}

	// Defense in depth: a zero key forged past the declaration gate is refused
	// by the runtime entry points too.
	pf, err := rt.Load(&revRaceProvider{tag: "ok"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	zero := Key[string]{}
	if err := Provide(pf.activation.ctx, zero, "v"); err == nil {
		t.Fatal("zero key accepted by Provide")
	}
	if _, err := Require(pf.activation.ctx, zero); err == nil {
		t.Fatal("zero key accepted by Require")
	}
}

type zeroKeyProvider struct{}

func (c *zeroKeyProvider) Name() string          { return "zero-key-provider" }
func (c *zeroKeyProvider) Inject() []Dependency  { return nil }
func (c *zeroKeyProvider) Provide() []Capability { return []Capability{{}} }
func (c *zeroKeyProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, nil
}

// ---------------------------------------------------------------------------
// RT-14: ProviderPublished means "live and bindable".
// ---------------------------------------------------------------------------

type rtEventCollector struct{ events chan RuntimeEvent }

func (s *rtEventCollector) Emit(ev RuntimeEvent) {
	select {
	case s.events <- ev:
	default:
	}
}

func (s *rtEventCollector) drain() map[string]int {
	counts := map[string]int{}
	for {
		select {
		case ev := <-s.events:
			counts[string(ev.Type)]++
		default:
			return counts
		}
	}
}

// TestPublishedNotEmittedForSelfUndoneProvide — RT-14a: an install that
// self-undoes (context began unwinding mid-install) must not publish.
func TestPublishedNotEmittedForSelfUndoneProvide(t *testing.T) {
	sink := &rtEventCollector{events: make(chan RuntimeEvent, 256)}
	rt, err := New(WithEventSink(sink))
	if err != nil {
		t.Fatal(err)
	}
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	lateKey := NewKey[rehomeKeyVal]("rehome.publish-late")
	entered := make(chan struct{})
	resume := make(chan struct{})
	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if key == lateKey.Capability() {
			close(entered)
			<-resume
		}
	}

	fire := make(chan struct{})
	done := make(chan error, 1)
	pf, err := rt.Load(&rehomeAsyncSingleProvider{lateKey: lateKey, fire: fire, done: done})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	close(fire)
	<-entered // Provide install parked mid-flight

	// Begin unwinding while the install is parked, then release it: the slot
	// self-undoes. Dispose returns once the intent is recorded; WAIT until the
	// context has actually flipped to unwinding before releasing, otherwise the
	// install could legitimately commit first.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(8 * time.Second)
	for {
		pf.activation.ctx.mu.Lock()
		unwinding := pf.activation.ctx.state == contextUnwinding
		pf.activation.ctx.mu.Unlock()
		if unwinding {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("context never entered unwinding")
		}
		time.Sleep(2 * time.Millisecond)
	}
	close(resume)
	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("late Provide: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("late Provide never completed")
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}

	for typ, n := range sink.drain() {
		if typ == "ProviderPublished" && n > 0 {
			t.Fatalf("self-undone Provide emitted ProviderPublished %d time(s)", n)
		}
	}
}

// TestPublishedNotEmittedForRetiringProvide — RT-14b: a mid-rehome Provide
// registers retiring; the publication happens at migration, not registration.
func TestPublishedNotEmittedForRetiringProvide(t *testing.T) {
	sink := &rtEventCollector{events: make(chan RuntimeEvent, 256)}
	rt, err := New(WithEventSink(sink))
	if err != nil {
		t.Fatal(err)
	}
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	lateKey := NewKey[rehomeKeyVal]("rehome.publish-retiring")
	fire := make(chan struct{})
	provideDone := make(chan error, 1)
	pf, err := rt.Load(&rehomeWindowProvider{lateKey: lateKey, fire: fire, done: provideDone})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)

	initial := &rehomeBlockingConsumer{
		cleanupEntered: make(chan struct{}),
		releaseCleanup: make(chan struct{}),
	}
	var once sync.Once
	release := func() { once.Do(func() { close(initial.releaseCleanup) }) }
	defer release()
	cf, err := rt.Load(initial)
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, cf)

	rehomeDone := make(chan error, 1)
	go func() { rehomeDone <- pf.Rehome(ctx, RehomeWithFreshIsolation()) }()
	<-initial.cleanupEntered

	close(fire)
	if err := <-provideDone; err != nil {
		t.Fatalf("late Provide: %v", err)
	}
	release()
	if err := <-rehomeDone; err != nil {
		t.Fatalf("rehome: %v", err)
	}

	published := 0
	withdrawn := 0
	for typ, n := range sink.drain() {
		if strings.Contains(string(typ), "Published") {
			published += n
		}
		if typ == "ProviderWithdrawn" {
			withdrawn += n
		}
	}
	// Exactly ONE publication for the late key: the migration's re-publish.
	// (The initial rehomeKey provision contributes its own pair: 1 published at
	// Apply, 1 withdrawn+published at migration.)
	if published-withdrawn != 1 {
		t.Fatalf("retiring Provide publication accounting wrong: published=%d withdrawn=%d (want diff 1)", published, withdrawn)
	}
}

// metaBoomConsumer requires a meta key with a given declared metadata.
type metaBoomConsumer struct {
	key MetaKey[string, metaCfg]
}

func (c *metaBoomConsumer) Name() string { return "meta-boom-consumer" }
func (c *metaBoomConsumer) Inject() []Dependency {
	return []Dependency{RequiresMeta(c.key, metaCfg{base: 0})}
}
func (c *metaBoomConsumer) Provide() []Capability { return nil }
func (c *metaBoomConsumer) Apply(ctx *Context) (Cleanup, error) {
	return nil, nil
}

type metaBoomProvider struct {
	key MetaKey[string, metaCfg]
}

func (c *metaBoomProvider) Name() string          { return "meta-boom-provider" }
func (c *metaBoomProvider) Inject() []Dependency  { return nil }
func (c *metaBoomProvider) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *metaBoomProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, ProvideMeta(ctx, c.key, func(m metaCfg) string { return "ok" })
}

type metaPanicProvider struct {
	key MetaKey[string, metaCfg]
}

func (c *metaPanicProvider) Name() string          { return "meta-panic-provider" }
func (c *metaPanicProvider) Inject() []Dependency  { return nil }
func (c *metaPanicProvider) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *metaPanicProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, ProvideMeta(ctx, c.key, func(m metaCfg) string { panic("provider exploded") })
}

// RequireMetaErr resolves a meta key on a fiber's current activation context
// from a test goroutine.
func RequireMetaErr(rt *Runtime, f *Fiber, key MetaKey[string, metaCfg]) (string, error) {
	out := make(chan struct {
		v   string
		err error
	}, 1)
	rt.submit(&requireMetaProbe{fiber: f, key: key, out: out})
	select {
	case r := <-out:
		return r.v, r.err
	case <-time.After(8 * time.Second):
		return "", errors.New("require meta probe timeout")
	}
}

type requireMetaProbe struct {
	fiber *Fiber
	key   MetaKey[string, metaCfg]
	out   chan struct {
		v   string
		err error
	}
}

func (p *requireMetaProbe) apply(o *orchestrator) {
	if p.fiber.activation == nil || p.fiber.activation.ctx == nil {
		p.out <- struct {
			v   string
			err error
		}{"", errors.New("no activation")}
		return
	}
	v, err := RequireMeta(p.fiber.activation.ctx, p.key)
	p.out <- struct {
		v   string
		err error
	}{v, err}
}
