package integration

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/scheduler"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Platform Convergence E2E Gate — PC-10..PC-13, PC-16, PC-17 (extension
// embedding into the unified lifecycle model).
// ---------------------------------------------------------------------------

// pcConfigEnv builds a Runtime + FactoryRegistry + Config Controller whose
// types map onto the pc* provider/consumer components plus registry roles.
type pcConfigEnv struct {
	rt   *runtime.Runtime
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newPCConfigEnv(t *testing.T) *pcConfigEnv {
	t.Helper()
	rt := pcNewRT(t)
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	t.Cleanup(func() { _ = ctrl.CloseContext(shortCtx()) })
	register := func(typ string, build func(cc config.ComponentConfig) (runtime.Component, error)) {
		t.Helper()
		if err := reg.Register(typ, &adapterFactory{build: build}); err != nil {
			t.Fatal(err)
		}
	}
	register("prov", func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		if tag == "" {
			tag = cc.ID
		}
		return pcProv(tag), nil
	})
	register("cons", func(cc config.ComponentConfig) (runtime.Component, error) {
		return pcCons(nil, nil), nil
	})
	register("regowner", func(cc config.ComponentConfig) (runtime.Component, error) {
		return regOwnerComp(&kit{}, cc.ID), nil
	})
	register("regconsumer", func(cc config.ComponentConfig) (runtime.Component, error) {
		return regConsumerComp(&kit{}, cc.ID), nil
	})
	return &pcConfigEnv{rt: rt, reg: reg, ctrl: ctrl}
}

func (e *pcConfigEnv) reconcile(t *testing.T, comps ...config.ComponentConfig) {
	t.Helper()
	if err := e.ctrl.Reconcile(pcTimeout(t), config.Config{Components: comps}); err != nil {
		t.Fatalf("reconcile: %v", err)
	}
}

// PC-10 — ConfigWatch -> Parse -> Desired -> Reconcile -> Runtime; failure
// hierarchy (parse failure leaves Applied untouched, deletion empties Desired).
func TestPC10ConfigWatchReconcile(t *testing.T) {
	e := newPCConfigEnv(t)
	fw := watch.NewFileWatcher()
	defer fw.Close()

	path := filepath.Join(t.TempDir(), "app.toml")
	write := func(t *testing.T, body string) {
		t.Helper()
		if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	write(t, tomlOf(config.ComponentConfig{ID: "p", Type: "prov", Config: map[string]any{"tag": "v1"}}))

	adapter, err := configwatch.New(configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}, e.ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer adapter.CloseContext(ctxT(t))

	// Version 1: watch -> read -> parse -> desired update -> reconcile.
	if err := adapter.Sync(pcTimeout(t)); err != nil {
		t.Fatalf("sync v1: %v", err)
	}
	f1 := fiberOf(t, &env{ctrl: e.ctrl}, "p")
	waitActive(t, f1)

	// Version 2 replaces the composition through the Runtime (new Fiber).
	write(t, tomlOf(config.ComponentConfig{ID: "p", Type: "prov", Config: map[string]any{"tag": "v2"}}))
	if err := adapter.Sync(pcTimeout(t)); err != nil {
		t.Fatalf("sync v2: %v", err)
	}
	f2 := fiberOf(t, &env{ctrl: e.ctrl}, "p")
	waitActive(t, f2)
	if f1.ID() == f2.ID() || f1.State() != runtime.StateGone {
		t.Fatalf("config v2 did not replace the runtime composition (old state %s)", f1.State())
	}

	// Parse failure: Desired update fails, Applied + Runtime stay untouched.
	write(t, "[[components\nnot a table")
	if err := adapter.Sync(pcTimeout(t)); err == nil {
		t.Fatal("bad config was accepted")
	}
	if len(e.ctrl.Owned()) != 1 || fiberOf(t, &env{ctrl: e.ctrl}, "p").ID() != f2.ID() {
		t.Fatal("parse failure changed the applied composition")
	}
	if f2.State() != runtime.StateActive {
		t.Fatalf("fiber state changed by parse failure: %s", f2.State())
	}

	// Source delete: Desired = empty -> reconciliation removes the composition.
	if err := os.Remove(path); err != nil {
		t.Fatal(err)
	}
	if err := adapter.Sync(pcTimeout(t)); err != nil {
		t.Fatalf("sync after source delete: %v", err)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatalf("owned after source delete = %d, want 0", len(e.ctrl.Owned()))
	}
	pcQuiesced(t, e.rt, "PC-10")
}

// PC-11 — HMR warm replacement: new Component/Activation, old fully unwound
// (effects), provider/event ownership released, stale identity never reused.
func TestPC11HMRWarmReplacement(t *testing.T) {
	rt := pcNewRT(t)
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))

	rec := &pcRec{}
	regFiber := func(src, tag string) {
		t.Helper()
		if err := ld.RegisterBuiltin(src, func() config.Factory {
			return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
				return pcEffectHost("pc-hmr-"+tag, rec, "A", "B", "C"), nil
			}}
		}); err != nil {
			t.Fatal(err)
		}
	}
	regFiber("builtin://pc-h-v1", "v1")
	regFiber("builtin://pc-h-v2", "v2")
	m1 := loadPCModule(t, ld, "pc-h-v1", artifact("pc-h-v1", "pc", "builtin://pc-h-v1", "1"))
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "svc", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f1, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f1)
	if err := h.Register(hmr.Target{ID: "t", ComponentID: "svc", Artifact: artifact("pc-h-v2", "pc", "builtin://pc-h-v2", "2")}); err != nil {
		t.Fatal(err)
	}
	if err := h.Bind("t", *m1, f1); err != nil {
		t.Fatal(err)
	}
	if !ld.Usage().InUse("pc-h-v1") {
		t.Fatal("old module usage not held")
	}

	if err := h.Replace(pcTimeout(t), hmr.Target{ID: "t", ComponentID: "svc", Artifact: artifact("pc-h-v2", "pc", "builtin://pc-h-v2", "2")}); err != nil {
		t.Fatalf("hmr replace: %v", err)
	}
	b, ok := h.CurrentBinding("t")
	if !ok {
		t.Fatal("no binding after replace")
	}
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("replacement reused the old fiber identity")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatalf("new fiber state = %s, want Active", b.Fiber.State())
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %s, want Gone", f1.State())
	}
	// Old activation effects fully unwound (LIFO).
	got := rec.join()
	if !stringsHasAll(got, "-C", "-B", "-A") {
		t.Fatalf("old effects not unwound: %v", rec.got())
	}
	// Old module released only after the old fiber was Gone.
	if ld.Usage().InUse("pc-h-v1") {
		t.Fatal("old module usage leaked")
	}
	if !ld.Usage().InUse("pc-h-v2") {
		t.Fatal("new module usage not held")
	}
	if ld.Has("pc-h-v1") {
		t.Fatal("old module not unloaded after replacement")
	}
}

func stringsHasAll(s string, subs ...string) bool {
	for _, sub := range subs {
		if !stringsContains(s, sub) {
			return false
		}
	}
	return true
}

func stringsContains(s, sub string) bool {
	for i := 0; i+len(sub) <= len(s); i++ {
		if s[i:i+len(sub)] == sub {
			return true
		}
	}
	return false
}

func loadPCModule(t *testing.T, ld *loader.BuiltinLoader, id string, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := ld.Load(pcTimeout(t), a)
	if err != nil {
		t.Fatalf("loader.Load(%s): %v", id, err)
	}
	return m
}

// PC-12 — HMR failure preservation: a failing V2 never takes down healthy V1
// (no V1-Gone + V2-Failed composition loss).
func TestPC12HMRFailurePreservation(t *testing.T) {
	rt := pcNewRT(t)
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))

	if err := ld.RegisterBuiltin("builtin://pc-f-v1", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcLeaf("pc-fiber-v1", nil), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	if err := ld.RegisterBuiltin("builtin://pc-f-v2", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcFailApply("pc-fiber-v2"), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	m1 := loadPCModule(t, ld, "pc-f-v1", artifact("pc-f-v1", "pc", "builtin://pc-f-v1", "1"))
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "svc", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f1, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f1)
	tgt := hmr.Target{ID: "t", ComponentID: "svc", Artifact: artifact("pc-f-v2", "pc", "builtin://pc-f-v2", "2")}
	if err := h.Register(tgt); err != nil {
		t.Fatal(err)
	}
	if err := h.Bind("t", *m1, f1); err != nil {
		t.Fatal(err)
	}

	if err := h.Replace(pcTimeout(t), tgt); err == nil {
		t.Fatal("replacement with failing Apply succeeded")
	}
	// Old remains valid; the replacement never became Active.
	if f1.State() != runtime.StateActive {
		t.Fatalf("healthy V1 damaged by failed replacement: %s", f1.State())
	}
	b, _ := h.CurrentBinding("t")
	if b.Fiber == nil || b.Fiber.ID() != f1.ID() {
		t.Fatal("binding moved off the healthy fiber after failure")
	}
	if !ld.Usage().InUse("pc-f-v1") {
		t.Fatal("V1 module usage lost after failed replacement")
	}
	// The failed V2 never became Active and did not linger as a second live
	// fiber: the kernel unwound it while preserving healthy V1.
	snap := pcSnap(t, rt)
	if len(snap.Fibers) != 1 {
		t.Fatalf("live fibers after failed replacement = %d, want exactly V1",
			len(snap.Fibers))
	}
	pcDisposeGone(t, f1)
	pcQuiesced(t, rt, "PC-12")
}

// PC-13 — Exclusive-provider withdraw-then-load fallback: no duplicate or
// stale provider, no stale dependency edge, V2 Active with clean state.
func TestPC13HMRWithdrawThenLoadFallback(t *testing.T) {
	rt := pcNewRT(t)
	k := &kit{seen: make(chan string, 8)}
	f1 := pcLoadActive(t, rt, pcProv("v1"))
	cf := pcLoadActive(t, rt, pcCons(k, k.seen))
	if got := pcRecv(t, k.seen, "v1 binding"); got != "v1" {
		t.Fatalf("consumer resolved %q", got)
	}

	// Withdraw V1 fully (Gone) before V2 may load — exclusive semantics.
	pcDisposeGone(t, f1)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	f2 := pcLoadActive(t, rt, pcProv("v2"))
	if err := cf.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("consumer did not recover: %v", err)
	}
	if got := pcRecv(t, k.seen, "v2 binding"); got != "v2" {
		t.Fatalf("consumer resolved %q after fallback", got)
	}
	snap := pcSnap(t, rt)
	providers := 0
	for _, p := range snap.Providers {
		if p.Key == pcSvcKey.Capability().String() {
			providers++
			if p.OwnerFiberID != f2.ID() {
				t.Fatalf("stale provider owned by old fiber: %+v", p)
			}
		}
	}
	if providers != 1 {
		t.Fatalf("provider count = %d, want exactly 1 (no duplicates)", providers)
	}
	okDeps := 0
	for _, d := range snap.Dependencies {
		if d.ConsumerFiberID == cf.ID() {
			if d.Status != runtime.DependencySatisfied || d.ProviderFiberID != f2.ID() {
				t.Fatalf("stale dependency edge: %+v", d)
			}
			okDeps++
		}
	}
	if okDeps == 0 {
		t.Fatal("consumer dependency missing after fallback")
	}
}

// PC-16 — Registry is a Component-created capability: withdrawal follows the
// owner activation; membership mutation is inert to the lifecycle.
func TestPC16RegistryCapability(t *testing.T) {
	rt := pcNewRT(t)
	ownerKit := &kit{}
	consKit := &kit{}
	owner := regOwnerComp(ownerKit, "reg")
	cons := regConsumerComp(consKit, "regc")
	of := pcLoadActive(t, rt, owner)
	cf := pcLoadActive(t, rt, cons)
	reg := owner.reg
	if reg == nil {
		t.Fatal("owner did not create the registry capability")
	}

	// Membership churn through the component-owned registry object never
	// disturbs the lifecycle (registry is data, not a second lifecycle).
	before := consKit.applies.Load()
	if err := reg.Add("m1", "v1"); err != nil {
		t.Fatal(err)
	}
	if err := reg.Replace("m1", "v2"); err != nil {
		t.Fatal(err)
	}
	_ = reg.Remove("m1")
	if reg.Len() != 0 {
		t.Fatalf("registry len = %d", reg.Len())
	}
	if got := consKit.applies.Load(); got != before {
		t.Fatalf("registry churn re-applied the consumer: %d -> %d", before, got)
	}
	if cf.State() != runtime.StateActive {
		t.Fatalf("consumer state = %s", cf.State())
	}

	// Owner withdrawal removes the capability; the dependent consumer loses
	// its binding; no provider/effect leaks.
	pcDisposeGone(t, of)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	snap := pcSnap(t, rt)
	for _, p := range snap.Providers {
		if p.Key == regKey.Capability().String() {
			t.Fatalf("registry provider outlived its owner: %+v", p)
		}
	}
	pcDisposeGone(t, cf)
	pcQuiesced(t, rt, "PC-16")
}

// PC-17 — Scheduler / Watch only signal through the public Runtime API; they
// never own Fiber lifecycle state.
func TestPC17SchedulerWatchBoundary(t *testing.T) {
	// Scheduler: a job drives a full Load -> Active -> Dispose -> Gone cycle
	// exclusively through public APIs; the scheduler itself creates nothing.
	rt := pcNewRT(t)
	sch := scheduler.New()
	defer sch.Close()
	done := make(chan struct{})
	if err := sch.Add(scheduler.Job{
		ID:       "pc-lifecycle",
		Schedule: scheduler.Once{At: time.Now()},
		Task: func(ctx context.Context) error {
			f, err := rt.Load(pcLeaf("pc-sched-leaf", nil))
			if err != nil {
				return err
			}
			if err := f.Ready(ctx); err != nil {
				return err
			}
			if err := f.Dispose(); err != nil {
				return err
			}
			if err := f.Gone(ctx); err != nil {
				return err
			}
			close(done)
			return nil
		},
	}); err != nil {
		t.Fatal(err)
	}
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		t.Fatal("scheduler lifecycle job did not complete")
	}
	pcQuiesced(t, rt, "PC-17 scheduler")

	// Watch: a file change only signals; the reaction mutates through
	// Runtime.Load/Dispose (no Fiber mutation from inside watch).
	fw := watch.NewFileWatcher()
	defer fw.Close()
	path := filepath.Join(t.TempDir(), "sig.txt")
	if err := os.WriteFile(path, []byte("v1"), 0o644); err != nil {
		t.Fatal(err)
	}
	sub, err := fw.Watch(pcTimeout(t), watch.Source{ID: "pc-sig", Kind: "file", URI: "file://" + path})
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer sub.Close()

	react := func() bool {
		f, err := rt.Load(pcLeaf("pc-watch-leaf", nil))
		if err != nil {
			return false
		}
		if err := f.Ready(pcTimeout(t)); err != nil {
			return false
		}
		if err := f.Dispose(); err != nil {
			return false
		}
		return f.Gone(pcTimeout(t)) == nil
	}
	var reacted atomic.Bool
	go func() {
		for range sub.Changes() {
			if react() {
				reacted.Store(true)
				return
			}
		}
	}()
	for i := 0; i < 10 && !reacted.Load(); i++ {
		// Distinct revisions on every retry: OS file events may coalesce, and
		// identical content never produces a new revision.
		content := []byte("rev-" + string(rune('a'+i)) + "\n")
		if err := os.WriteFile(path, content, 0o644); err != nil {
			t.Fatal(err)
		}
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) && !reacted.Load() {
			time.Sleep(5 * time.Millisecond)
		}
	}
	if !reacted.Load() {
		t.Fatal("watch change never reached the public-API reaction")
	}
	pcQuiesced(t, rt, "PC-17 watch")
}
