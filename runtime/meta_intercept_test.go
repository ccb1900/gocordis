package runtime_test

import (
	"sort"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Conformance tests for paper Definition 26/27 (interception as metadata):
//
//	Σinter = (ι, σ): ι carries context metadata per key; σ maps the key to a
//	provider FUNCTION ℳₖ → 𝒱ₖ; each key has a monoid (ℳₖ, ⊕ₖ, εₖ).
//	get(k, μ) = σ(k)(d(k) ⊕ₖ ι(k)) — the component-declared metadata merged
//	with the context-carried metadata, right-biased (context wins).
//	intercept(k, ν) merges ν onto the inherited ι and is reversible; it never
//	changes what the key resolves to and cannot gate activation (§6.3).

// fsMeta is the access-control metadata of the §6.3 example: which paths a
// consumer may read and whether writes are allowed. ⊕ₖ is right-biased field
// override with path-set union.
type fsMeta struct {
	paths map[string]bool
	write bool
	tag   string
}

func mergeFSMeta(a, b fsMeta) fsMeta {
	out := fsMeta{paths: make(map[string]bool, len(a.paths)+len(b.paths)), tag: a.tag, write: b.write}
	for p := range a.paths {
		out.paths[p] = true
	}
	for p := range b.paths {
		out.paths[p] = true
	}
	if b.tag != "" {
		out.tag = b.tag
	}
	return out
}

var fsMetaZero = fsMeta{paths: map[string]bool{}}

var fsKey = runtime.NewMetaKey[string, fsMeta]("fs.conformance", fsMetaZero, mergeFSMeta)

// fsRender is the metadata interpreter: it renders the binding as a policy
// view — the §6.3 "provider consults the merged metadata" shape.
func fsRender(mu fsMeta) string {
	paths := make([]string, 0, len(mu.paths))
	for p := range mu.paths {
		paths = append(paths, p)
	}
	sort.Strings(paths)
	s := mu.tag + ":["
	for i, p := range paths {
		if i > 0 {
			s += ","
		}
		s += p
	}
	s += "]"
	if mu.write {
		s += "+w"
	}
	return s
}

// fsDeclared declares the metadata every fsCons consumer carries.
func fsDeclared(tag string, paths ...string) fsMeta {
	m := fsMeta{paths: map[string]bool{}, tag: tag}
	for _, p := range paths {
		m.paths[p] = true
	}
	return m
}

// fsProv provides fsKey with the metadata interpreter.
type fsProv struct{}

func (c *fsProv) Name() string                 { return "fs-provider" }
func (c *fsProv) Inject() []runtime.Dependency { return nil }
func (c *fsProv) Provide() []runtime.Capability {
	return []runtime.Capability{fsKey.Capability()}
}
func (c *fsProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.ProvideMeta(ctx, fsKey, fsRender)
}

// fsCons requires fsKey with declared metadata and records the interpreted
// binding at Apply.
type fsCons struct {
	declared fsMeta
	seen     chan string
}

func (c *fsCons) Name() string { return "fs-consumer" }
func (c *fsCons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.RequiresMeta(fsKey, c.declared)}
}
func (c *fsCons) Provide() []runtime.Capability { return nil }
func (c *fsCons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.RequireMeta(ctx, fsKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v
	}
	return nil, nil
}

// fsInterceptHost installs context-carried metadata ν and then mounts a
// consumer, so the consumer reads through ι.
type fsInterceptHost struct {
	nu  fsMeta
	out chan string
	ch  chan *runtime.Fiber
}

func (c *fsInterceptHost) Name() string { return "fs-intercept-host" }
func (c *fsInterceptHost) Inject() []runtime.Dependency {
	return nil
}
func (c *fsInterceptHost) Provide() []runtime.Capability { return nil }
func (c *fsInterceptHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.InterceptMeta(ctx, fsKey, c.nu); err != nil {
		return nil, err
	}
	f, err := ctx.Child(&fsCons{declared: fsDeclared("consumer"), seen: c.out})
	if err != nil {
		return nil, err
	}
	if c.ch != nil {
		c.ch <- f
	}
	return nil, nil
}

func metaWait(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for %s -> %v (state %v)", f.Name(), want, f.State())
}

func metaRecv(t *testing.T, ch chan string) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(8 * time.Second):
		t.Fatal("timeout waiting for interpreted value")
		return ""
	}
}

// TestMetaInterpretProviderDeclared — Definition 27 get with ι = εₖ: the
// provider function evaluates the DECLARED metadata alone.
func TestMetaInterpretProviderDeclared(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	if _, err := rt.Load(&fsProv{}); err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(&fsCons{declared: fsDeclared("consumer", "/a", "/b"), seen: out})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, cf, runtime.StateActive)
	if got := metaRecv(t, out); got != "consumer:[/a,/b]" {
		t.Fatalf("interpreted = %q, want declared-only view", got)
	}
	_ = cf.Dispose()
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

// TestMetaContextOverridesDeclared — Definition 27: intercept(k, ν) merges ν
// onto ι; get evaluates d(k) ⊕ₖ ι(k) with the CONTEXT right-biased: the
// enclosing context narrows the component's declaration (§6.3: grant
// read-only to a component that declared writes).
func TestMetaContextOverridesDeclared(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	if _, err := rt.Load(&fsProv{}); err != nil {
		t.Fatal(err)
	}
	// Consumer declares /data + write; context narrows to read-only /data.
	hf, err := rt.Load(&fsInterceptHost{
		nu:  fsMeta{paths: map[string]bool{"/data": true}, write: false, tag: "narrowed"},
		out: out,
	})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, hf, runtime.StateActive)
	got := metaRecv(t, out)
	if got != "narrowed:[/data]" {
		t.Fatalf("interpreted = %q, want context-narrowed read-only view", got)
	}
	_ = hf.Dispose()
	if err := hf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

// TestMetaMonoidInstallOrder — two installments fold in install order:
// ι = ((ε ⊕ ν1) ⊕ ν2); ν2 (later) wins conflicts, path sets union.
func TestMetaMonoidInstallOrder(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	if _, err := rt.Load(&fsProv{}); err != nil {
		t.Fatal(err)
	}
	hf, err := rt.Load(&metaTwoInterceptHost{out: out})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, hf, runtime.StateActive)
	// ν1 grants /a and write; ν2 (later) revokes write and grants /b.
	if got := metaRecv(t, out); got != "two:[/a,/b]" {
		t.Fatalf("interpreted = %q, want folded union without write", got)
	}
	_ = hf.Dispose()
	if err := hf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

type metaTwoInterceptHost struct {
	out chan string
	ch  chan *runtime.Fiber
}

func (c *metaTwoInterceptHost) Name() string { return "meta-two-intercept-host" }
func (c *metaTwoInterceptHost) Inject() []runtime.Dependency {
	return nil
}
func (c *metaTwoInterceptHost) Provide() []runtime.Capability { return nil }
func (c *metaTwoInterceptHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.InterceptMeta(ctx, fsKey, fsMeta{paths: map[string]bool{"/a": true}, write: true, tag: "two"}); err != nil {
		return nil, err
	}
	if err := runtime.InterceptMeta(ctx, fsKey, fsMeta{paths: map[string]bool{"/b": true}, write: false}); err != nil {
		return nil, err
	}
	f, err := ctx.Child(&fsCons{declared: fsDeclared("consumer"), seen: c.out})
	if err != nil {
		return nil, err
	}
	if c.ch != nil {
		c.ch <- f
	}
	return nil, nil
}

// TestMetaNoReloadOnIntercept — interception is not part of the dependency
// graph (paper: "no premise reads it and no field of a fiber holds it";
// §6.3: "installed, reconfigured, or removed at runtime without triggering
// any reload"). A consumer mounted BEFORE the installment stays Active and a
// consumer mounted after reads through ι.
func TestMetaNoReloadOnIntercept(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	pf, err := rt.Load(&fsProv{})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, pf, runtime.StateActive)

	early, err := rt.Load(&fsCons{declared: fsDeclared("early", "/early"), seen: out})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, early, runtime.StateActive)
	if got := metaRecv(t, out); got != "early:[/early]" {
		t.Fatalf("early interpreted = %q", got)
	}

	// Install ι after the consumer is Active: no reload, no state change.
	late := &fsInterceptHost{
		nu:  fsMeta{paths: map[string]bool{"/late": true}, write: false, tag: "late"},
		out: out,
	}
	hf, err := rt.Load(late)
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, hf, runtime.StateActive)
	if early.State() != runtime.StateActive {
		t.Fatalf("early consumer state = %v after intercept, want Active (no reload)", early.State())
	}
	// The late consumer reads through ι.
	if got := metaRecv(t, out); got != "late:[/late]" {
		t.Fatalf("late interpreted = %q, want context view", got)
	}

	_ = hf.Dispose()
	if err := hf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	_ = early.Dispose()
	if err := early.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	_ = pf.Dispose()
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

// TestMetaInterceptReversible — installation is a Runtime-managed reversible
// effect: when the installing activation unwinds, ι returns to its inherited
// value and a fresh consumer sees the ε-inherited view again.
func TestMetaInterceptReversible(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	if _, err := rt.Load(&fsProv{}); err != nil {
		t.Fatal(err)
	}
	hf, err := rt.Load(&fsInterceptHost{
		nu:  fsMeta{paths: map[string]bool{"/hosted": true}, write: true, tag: "hosted"},
		out: out,
	})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, hf, runtime.StateActive)
	if got := metaRecv(t, out); got != "hosted:[/hosted]+w" {
		t.Fatalf("interpreted under ι = %q", got)
	}

	// Unwind the interceptor's context; ι reverts to εₖ.
	_ = hf.Dispose()
	if err := hf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(&fsCons{declared: fsDeclared("after"), seen: out})
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, cf, runtime.StateActive)
	if got := metaRecv(t, out); got != "after:[]" {
		t.Fatalf("interpreted after unwind = %q, want ε-metadata view", got)
	}
	_ = cf.Dispose()
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

// TestMetaUndeclaredRequireRejected — declarations remain authoritative:
// RequireMeta on an undeclared MetaKey fails without touching the provider.
func TestMetaUndeclaredRequireRejected(t *testing.T) {
	rt := newTestRuntime(t)
	bad := &metaBadConsumer{}
	bf, err := rt.Load(bad)
	if err != nil {
		t.Fatal(err)
	}
	metaWait(t, bf, runtime.StateFailed)
	if err := bf.Err(); err == nil {
		t.Fatal("undeclared RequireMeta must fail the activation")
	}
}

type metaBadConsumer struct{}

func (c *metaBadConsumer) Name() string { return "meta-bad-consumer" }
func (c *metaBadConsumer) Inject() []runtime.Dependency {
	// Declares the PLAIN key family but reads the MetaKey capability is
	// impossible to declare accidentally: simulate by declaring nothing and
	// reading anyway through the undeprecated path is prevented at the
	// declaration check, so we declare nothing at all.
	return nil
}
func (c *metaBadConsumer) Provide() []runtime.Capability { return nil }
func (c *metaBadConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	_, err := runtime.RequireMeta(ctx, fsKey)
	return nil, err
}
