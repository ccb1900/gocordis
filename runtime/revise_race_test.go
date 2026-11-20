package runtime

import (
	"errors"
	"fmt"
	"sync"
	"testing"
)

var revRaceKey = NewKey[rehomeKeyVal]("rev-race.service")

type revRaceProvider struct {
	tag string
	key Key[rehomeKeyVal] // zero → revRaceKey
}

func (c *revRaceProvider) Name() string         { return "rev-race-provider" }
func (c *revRaceProvider) Inject() []Dependency { return nil }
func (c *revRaceProvider) Provide() []Capability {
	if c.key != (Key[rehomeKeyVal]{}) {
		return []Capability{c.key.Capability()}
	}
	return []Capability{revRaceKey.Capability()}
}
func (c *revRaceProvider) Apply(ctx *Context) (Cleanup, error) {
	key := c.key
	if key == (Key[rehomeKeyVal]{}) {
		key = revRaceKey
	}
	return nil, Provide(ctx, key, rehomeKeyVal{tag: c.tag})
}

// TestReviseConcurrentSingleInsert — RT-06: two concurrent Revise calls on the
// same fiber must produce exactly ONE replacement. The loser is rejected with
// ErrReviseBusy while the revision is in flight (or ErrInvalidState if it
// arrived after the old fiber reached Gone) — never a second insert.
func TestReviseConcurrentSingleInsert(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, err := rt.Load(&revRaceProvider{tag: "v1"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)

	entered := make(chan struct{})
	resume := make(chan struct{})
	prev := testReviseHook
	defer func() { testReviseHook = prev }()
	testReviseHook = func() {
		close(entered)
		<-resume
	}

	type revRes struct {
		nf  *Fiber
		err error
	}
	results := make(chan revRes, 2)
	for i := 0; i < 2; i++ {
		go func() {
			nf, err := pf.Revise(ctx, &revRaceProvider{tag: "v2"})
			results <- revRes{nf, err}
		}()
	}

	<-entered // first caller parked after its CAS; the second must lose
	close(resume)

	okCount, busyCount := 0, 0
	for i := 0; i < 2; i++ {
		select {
		case r := <-results:
			switch {
			case r.err == nil:
				okCount++
				rehomeWaitState(t, r.nf, StateActive)
			case errors.Is(r.err, ErrReviseBusy) || errors.Is(r.err, ErrInvalidState):
				busyCount++
			default:
				t.Fatalf("unexpected revision error: %v", r.err)
			}
		case <-ctx.Done():
			t.Fatal("revision never returned")
		}
	}
	if okCount != 1 || busyCount != 1 {
		t.Fatalf("concurrent revisions: ok=%d busy=%d, want 1/1", okCount, busyCount)
	}
	if pf.State() != StateGone {
		t.Fatalf("old fiber not Gone after revision: %v", pf.State())
	}
}

// revBlockingParent provides revRaceKey and blocks in Cleanup; it owns the
// child under revision. Blocking the Cleanup parks the parent in Unloading —
// the state in which inserting a child would orphan it (RT-07).
type revBlockingParent struct {
	cCh            chan *Fiber
	cleanupEntered chan struct{}
	releaseCleanup chan struct{}
}

func (c *revBlockingParent) Name() string          { return "rev-blocking-parent" }
func (c *revBlockingParent) Inject() []Dependency  { return nil }
func (c *revBlockingParent) Provide() []Capability { return nil }
func (c *revBlockingParent) Apply(ctx *Context) (Cleanup, error) {
	child, err := ctx.Child(&revRaceProvider{tag: "child"})
	if err != nil {
		return nil, err
	}
	if c.cCh != nil {
		c.cCh <- child
	}
	return func() error {
		close(c.cleanupEntered)
		<-c.releaseCleanup
		return nil
	}, nil
}

// TestReviseRefusesFinalizingParent — RT-07: cmdReviseInsert must refuse a
// parent that is withdrawing/unloading (not just Gone). A child inserted under
// a finalizing owner would be orphaned by disposeChildren.
func TestReviseRefusesFinalizingParent(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)

	cCh := make(chan *Fiber, 1)
	p := &revBlockingParent{
		cCh:            cCh,
		cleanupEntered: make(chan struct{}),
		releaseCleanup: make(chan struct{}),
	}
	var once sync.Once
	release := func() { once.Do(func() { close(p.releaseCleanup) }) }
	defer release()
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	var cf *Fiber
	select {
	case cf = <-cCh:
	case <-ctx.Done():
		t.Fatal("child never spawned")
	}
	rehomeWaitState(t, cf, StateActive)

	// Dispose the parent: it withdraws, reaches Unloading and parks in Cleanup.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	select {
	case <-p.cleanupEntered:
	case <-ctx.Done():
		t.Fatal("parent Cleanup never entered")
	}
	// The parent's ownership cascade withdraws the child too; wait for it to
	// be Gone so the revision proceeds to its insert step.
	rehomeWaitState(t, cf, StateGone)

	// Revise the child: its insert arrives while the parent is Unloading —
	// must be refused, not inserted as an orphan.
	_, err = cf.Revise(ctx, &revRaceProvider{tag: "orphan"})
	if !errors.Is(err, ErrInvalidState) {
		t.Fatalf("insert under a finalizing parent was not refused: err=%v", err)
	}

	// Release the parent's Cleanup, then Close must still finish (nothing
	// stranded — the refused revision must not have left state behind).
	release()
	closeDone := make(chan error, 1)
	go func() { closeDone <- rt.Close(ctx) }()
	select {
	case err := <-closeDone:
		if err != nil {
			t.Fatalf("close: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("Close never completed")
	}
}

// TestReviseInheritsPostRehomeTable — RT-08: the replacement fiber inherits
// the old fiber's FINAL per-key isolation table (read at insert time on the
// orchestrator), so a Rehome completing inside the revision window is
// inherited rather than lost to the pre-move snapshot.
func TestReviseInheritsPostRehomeTable(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	cCh := make(chan *Fiber, 1)
	h := &reIsoInternalHost{cCh: cCh}
	hf, err := rt.Load(h)
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, hf)
	var cf *Fiber
	select {
	case cf = <-cCh:
	case <-ctx.Done():
		t.Fatal("fiber never spawned")
	}
	rehomeWaitState(t, cf, StateActive)

	cf.routeMu.RLock()
	isoBefore := cf.keyRealms[revRaceKey.Capability()]
	cf.routeMu.RUnlock()
	// Because the child declares ONLY this key, the isolated entry coincides
	// with the child's fresh base realm — what matters for RT-08 is the entry's
	// IDENTITY across the revision window, not distinctness from the base.
	if isoBefore == nil {
		t.Fatal("isolated route entry not established")
	}

	entered := make(chan struct{})
	resume := make(chan struct{})
	prev := testReviseHook
	defer func() { testReviseHook = prev }()
	testReviseHook = func() {
		close(entered)
		<-resume
	}
	revDone := make(chan struct {
		nf  *Fiber
		err error
	}, 1)
	go func() {
		nf, err := cf.Revise(ctx, &revRaceProvider{tag: "v2"})
		revDone <- struct {
			nf  *Fiber
			err error
		}{nf, err}
	}()

	// The revision is parked after its snapshot. Rehome completes inside the
	// revision window.
	select {
	case <-entered:
	case <-ctx.Done():
		t.Fatal("revision never reached the hook")
	}
	if err := cf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}
	close(resume)

	select {
	case r := <-revDone:
		if r.err != nil {
			t.Fatalf("revise: %v", r.err)
		}
		rehomeWaitState(t, r.nf, StateActive)
		r.nf.routeMu.RLock()
		inherited := r.nf.keyRealms[revRaceKey.Capability()]
		r.nf.routeMu.RUnlock()
		if inherited == isoBefore {
			t.Fatalf("replacement inherited the STALE pre-rehome table: %v", inherited)
		}
		if inherited == nil || inherited != effectiveRealm(cf, revRaceKey.Capability()) {
			t.Fatalf("replacement did not inherit the post-rehome table: %v want %v",
				inherited, effectiveRealm(cf, revRaceKey.Capability()))
		}
	case <-ctx.Done():
		t.Fatal("revision never completed")
	}
}

// reIsoInternalHost spawns a provider with Isolate(revRaceKey) so the fiber
// under revision starts with a per-key isolation entry distinct from its base.
type reIsoInternalHost struct{ cCh chan *Fiber }

func (c *reIsoInternalHost) Name() string          { return "rev-iso-host" }
func (c *reIsoInternalHost) Inject() []Dependency  { return nil }
func (c *reIsoInternalHost) Provide() []Capability { return nil }
func (c *reIsoInternalHost) Apply(ctx *Context) (Cleanup, error) {
	child, err := ctx.Child(&revRaceProvider{tag: "iso"}, Isolate(revRaceKey.Capability()))
	if err != nil {
		return nil, err
	}
	if c.cCh != nil {
		c.cCh <- child
	}
	return nil, nil
}

// ---------------------------------------------------------------------------
// RT-09: self-dependency is unresolvable and must be rejected at the mount
// boundary. Before its activation, a fiber's own provision does not exist, so
// the gate can never be satisfied by it: with no other provider the fiber
// hangs Pending forever; with one, the Apply-time Provide hits
// ErrDuplicateProvider. Fail fast with an actionable diagnostic instead.
// ---------------------------------------------------------------------------

var selfDepKey = NewKey[rehomeKeyVal]("self-dep.service")

type selfDepComponent struct{}

func (c *selfDepComponent) Name() string          { return "self-dep-component" }
func (c *selfDepComponent) Inject() []Dependency  { return []Dependency{Requires(selfDepKey)} }
func (c *selfDepComponent) Provide() []Capability { return []Capability{selfDepKey.Capability()} }
func (c *selfDepComponent) Apply(ctx *Context) (Cleanup, error) {
	return nil, nil
}

func TestSelfDependencyRejected(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	if _, err := rt.Load(&selfDepComponent{}); !errors.Is(err, ErrSelfDependency) {
		t.Fatalf("self-dependency not rejected at Load: %v", err)
	}

	// Same rejection through the Child boundary: the host's Apply fails with
	// the spawn error, so the host fiber ends Failed carrying it.
	childErr := make(chan error, 1)
	if _, err := rt.Load(&selfDepChildHost{errOut: childErr}); err != nil {
		t.Fatal(err)
	}
	select {
	case err := <-childErr:
		if !errors.Is(err, ErrSelfDependency) {
			t.Fatalf("self-dependency not rejected at Child: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("child spawn never reported an error")
	}
}

type selfDepChildHost struct{ errOut chan error }

func (c *selfDepChildHost) Name() string          { return "self-dep-host" }
func (c *selfDepChildHost) Inject() []Dependency  { return nil }
func (c *selfDepChildHost) Provide() []Capability { return nil }
func (c *selfDepChildHost) Apply(ctx *Context) (Cleanup, error) {
	_, err := ctx.Child(&selfDepComponent{})
	if c.errOut != nil {
		c.errOut <- err
	}
	return nil, err
}

// NEW-05 race regression: lastCaptured is written at finishActivation and read
// by Revise's snapshot (under f.mu on both sides after the fix). A concurrent
// Dispose/finishActivation racing a Revise must not data-race under -race.
func TestReviseReadsLastCapturedWithoutRacingActivationEnd(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	const rounds = 12
	var wg sync.WaitGroup
	for i := 0; i < rounds; i++ {
		// Per-round key: rounds must not collide on the exclusive capability.
		prov := &revRaceProvider{tag: "gen", key: NewKey[rehomeKeyVal](fmt.Sprintf("race-gen-%d", i))}
		pf, err := rt.Load(prov)
		if err != nil {
			t.Fatal(err)
		}
		rehomeWaitState(t, pf, StateActive)
		wg.Add(2)
		// goroutine A: dispose (finishActivation writes lastCaptured).
		go func() {
			defer wg.Done()
			_ = pf.Dispose()
		}()
		// goroutine B: revise (reads lastCaptured under f.mu.RLock).
		go func() {
			defer wg.Done()
			_, _ = pf.Revise(ctx, prov)
		}()
		// Both may win; either outcome is legal — the assertion is the absence
		// of a data race (this test runs under -race in CI).
	}
	wg.Wait()
}
