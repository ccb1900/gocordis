package integration

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// cfgKit is a per-config-test kit of components by type.
type cfgKit struct {
	provider    *kit
	consumer    *kit
	independent *kit
	regowner    *kit
	regconsumer *kit
}

func newCfgEnv(t *testing.T) (*env, *cfgKit) {
	k := &cfgKit{
		provider:    &kit{seen: make(chan string, 16)},
		consumer:    &kit{seen: make(chan string, 16)},
		independent: &kit{},
		regowner:    &kit{},
		regconsumer: &kit{},
	}
	factory := func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		if tag == "" {
			tag = cc.ID
		}
		fail, _ := cc.Config["fail"].(bool)
		switch cc.Type {
		case "provider":
			return providerComp(k.provider, cc.ID, tag, fail), nil
		case "consumer":
			return consumerComp(k.consumer, cc.ID), nil
		case "independent":
			return independentComp(k.independent, cc.ID, fail), nil
		case "regowner":
			return regOwnerComp(k.regowner, cc.ID), nil
		case "regconsumer":
			return regConsumerComp(k.regconsumer, cc.ID), nil
		}
		return nil, fmt.Errorf("unknown type %q", cc.Type)
	}
	e := newEnv(t, factory, "provider", "consumer", "independent", "regowner", "regconsumer")
	return e, k
}

func tomlOf(comps ...config.ComponentConfig) string {
	var b string
	for _, c := range comps {
		b += "[[components]]\nid = \"" + c.ID + "\"\ntype = \"" + c.Type + "\"\n"
		if len(c.Config) > 0 {
			b += "\n[components.config]\n"
			for k, v := range c.Config {
				switch val := v.(type) {
				case string:
					b += k + " = \"" + val + "\"\n"
				case int:
					b += fmt.Sprintf("%s = %d\n", k, val)
				case bool:
					b += fmt.Sprintf("%s = %v\n", k, val)
				}
			}
		}
		b += "\n"
	}
	return b
}

// E2E-01 Configuration Lifecycle: TOML -> Watch -> ConfigWatch -> Config ->
// Factory -> Runtime -> Fiber Active.
func TestE2E01ConfigurationLifecycle(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "app.toml")
	if err := os.WriteFile(path, []byte(tomlOf(config.ComponentConfig{ID: "p1", Type: "provider", Config: map[string]any{"tag": "v1"}})), 0o644); err != nil {
		t.Fatal(err)
	}
	e, k := newCfgEnv(t)
	fw := watch.NewFileWatcher()
	defer fw.Close()

	adapter, err := configwatch.New(configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}, e.ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer adapter.CloseContext(ctxT(t))

	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, e, "p1"))
	if got := k.provider.applies.Load(); got < 1 {
		t.Fatalf("provider applies = %d", got)
	}
}

// E2E-02 Configuration Replacement: config change creates a NEW fiber.
func TestE2E02ConfigurationReplacement(t *testing.T) {
	e, _ := newCfgEnv(t)
	cc := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("v1")}}); err != nil {
		t.Fatal(err)
	}
	f1 := fiberOf(t, e, "p")
	waitActive(t, f1)
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("v2")}}); err != nil {
		t.Fatal(err)
	}
	f2 := fiberOf(t, e, "p")
	waitActive(t, f2)
	if f1.ID() == f2.ID() {
		t.Fatal("replacement reused the old fiber")
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %s, want Gone", f1.State())
	}
}

// E2E-03 Invalid Configuration Preservation: invalid config leaves Applied A.
func TestE2E03InvalidConfigurationPreservation(t *testing.T) {
	e, _ := newCfgEnv(t)
	cc := config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": "a"}}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc}}); err != nil {
		t.Fatal(err)
	}
	f := fiberOf(t, e, "p")
	waitActive(t, f)
	// Duplicate ID -> whole config invalid, nothing changes.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc, cc}}); err == nil {
		t.Fatal("invalid config accepted")
	}
	if len(e.ctrl.Owned()) != 1 || fiberOf(t, e, "p").ID() != f.ID() {
		t.Fatal("applied state changed by invalid config")
	}
	if f.State() != runtime.StateActive {
		t.Fatal("fiber no longer active")
	}
}

// E2E-04 Provider Replacement: consumer withdraws then recovers (kernel).
func TestE2E04ProviderReplacement(t *testing.T) {
	e, k := newCfgEnv(t)
	pv := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	cv := config.ComponentConfig{ID: "c", Type: "consumer"}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv("v1"), cv}}); err != nil {
		t.Fatal(err)
	}
	p1 := fiberOf(t, e, "p")
	c1 := fiberOf(t, e, "c")
	waitActive(t, p1, c1)
	// Mark consumer-first: replace provider; consumer must withdraw before the
	// provider is Gone (kernel invariant), then recover.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv("v2"), cv}}); err != nil {
		t.Fatal(err)
	}
	p2 := fiberOf(t, e, "p")
	waitActive(t, p2, c1)
	if p1.ID() == p2.ID() {
		t.Fatal("provider not replaced")
	}
	if p1.State() != runtime.StateGone {
		t.Fatal("old provider not gone")
	}
	// Consumer stayed active on the new provider generation.
	eventually(t, 5*time.Second, "consumer saw v2", func() bool {
		select {
		case tag := <-k.consumer.seen:
			return tag == "v2"
		default:
			return false
		}
	})
	_ = c1
}

// E2E-05 Multi-Level Dependency: P <- A <- B withdraw and recover in order.
func TestE2E05MultiLevelDependency(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	kitP := &kit{}
	kitA := &kit{}
	kitB := &kit{}

	// P provides capKey; A requires capKey and provides capAKey; B requires capAKey.
	p := providerComp(kitP, "P", "v1", false)
	a := &aComp{kit: kitA, id: "A"}
	b := &bComp{kit: kitB, id: "B"}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	af, err := rt.Load(a)
	if err != nil {
		t.Fatal(err)
	}
	bf, err := rt.Load(b)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, pf, af, bf)
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	// Consumer-first: B and A withdraw (inactive) before P can be gone.
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := bf.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	if err := af.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	// Recovery: remount P -> A -> B reactivate.
	if err := pf.Load(); err != nil {
		t.Fatal(err)
	}
	waitActive(t, pf, af, bf)
}

// P-03 Configuration Conservation: valid A then invalid B keeps A.
func TestP03ConfigurationConservation(t *testing.T) {
	e, _ := newCfgEnv(t)
	cc := config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": "a"}}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc}}); err != nil {
		t.Fatal(err)
	}
	f := fiberOf(t, e, "p")
	waitActive(t, f)
	// Invalid B (duplicate id) leaves A applied.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc, cc}}); err == nil {
		t.Fatal("expected validation error")
	}
	if got := fiberOf(t, e, "p"); got.ID() != f.ID() || len(e.ctrl.Owned()) != 1 {
		t.Fatal("applied diverged from A")
	}
}

// P-12 Convergence: repeated config churn converges to the last valid state.
func TestP12Convergence(t *testing.T) {
	e, _ := newCfgEnv(t)
	pv := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	for _, tag := range []string{"a", "b", "c", "d"} {
		if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv(tag)}}); err != nil {
			t.Fatal(err)
		}
	}
	waitActive(t, fiberOf(t, e, "p"))
	if len(e.ctrl.Owned()) != 1 {
		t.Fatalf("owned = %d, want 1", len(e.ctrl.Owned()))
	}
}
