package integration

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// P2-8 (integration audit): generation churn raced across a DEPENDENT CHAIN.
// The existing concurrent-churn test (E2E-17) churns a lone provider; the
// sequential churn tests (PC-20/E2E-28/P-01) have no concurrency. Consumer-
// first withdrawal violations that only manifest when generation turnover
// races over provider->A->B edges need this exact combination.
//
// Chain: chainP provides chainSvc(tag) -> chainA requires chainSvc and
// provides chainInner(tag) -> chainB requires chainInner and reports the
// resolved tag. Every generation moves all three rows together, so after the
// final reconcile B must resolve exactly the final tag.

var (
	chainSvcKey   = runtime.NewKey[chainVal]("chain.svc")
	chainInnerKey = runtime.NewKey[chainVal]("chain.inner")
)

type chainComp struct {
	kind string // "p" | "a" | "b"
	id   string
	tag  string
	kit  *kit
}

func (c *chainComp) Name() string { return "chain-" + c.kind + ":" + c.id }
func (c *chainComp) Inject() []runtime.Dependency {
	switch c.kind {
	case "a":
		return []runtime.Dependency{runtime.Requires(chainSvcKey)}
	case "b":
		return []runtime.Dependency{runtime.Requires(chainInnerKey)}
	}
	return nil
}
func (c *chainComp) Provide() []runtime.Capability {
	switch c.kind {
	case "p":
		return []runtime.Capability{chainSvcKey.Capability()}
	case "a":
		return []runtime.Capability{chainInnerKey.Capability()}
	}
	return nil
}
func (c *chainComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.kit != nil {
		c.kit.rec()
	}
	switch c.kind {
	case "p":
		if err := runtime.Provide(ctx, chainSvcKey, chainVal{tag: c.tag}); err != nil {
			return nil, err
		}
	case "a":
		if _, err := runtime.Require(ctx, chainSvcKey); err != nil {
			return nil, err
		}
		if err := runtime.Provide(ctx, chainInnerKey, chainVal{tag: c.tag}); err != nil {
			return nil, err
		}
	case "b":
		v, err := runtime.Require(ctx, chainInnerKey)
		if err != nil {
			return nil, err
		}
		if c.kit != nil && c.kit.seen != nil {
			select {
			case c.kit.seen <- v.tag:
			default:
			}
		}
	}
	return nil, nil
}

type chainVal struct{ tag string }

func TestConcurrentReconcileChurnWithDependents(t *testing.T) {
	kp, ka := &kit{}, &kit{}
	kb := &kit{seen: make(chan string, 512)}
	e := newEnv(t, func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		c := &chainComp{id: cc.ID, tag: tag}
		switch cc.Type {
		case "chainp":
			c.kind, c.kit = "p", kp
		case "chaina":
			c.kind, c.kit = "a", ka
		case "chainb":
			c.kind, c.kit = "b", kb
		default:
			return nil, fmt.Errorf("unknown type %q", cc.Type)
		}
		return c, nil
	}, "chainp", "chaina", "chainb")

	pv := func(tag string) config.Config {
		row := func(id, typ string) config.ComponentConfig {
			return config.ComponentConfig{ID: id, Type: typ, Config: map[string]any{"tag": tag}}
		}
		return config.Config{Components: []config.ComponentConfig{
			row("p", "chainp"), row("a", "chaina"), row("b", "chainb"),
		}}
	}

	// Seed once, so every churner races a live chain (not an empty runtime).
	if err := e.ctrl.Reconcile(ctxT(t), pv("seed")); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, e, "p"), fiberOf(t, e, "a"), fiberOf(t, e, "b"))
	select {
	case tag := <-kb.seen:
		if tag != "seed" {
			t.Fatalf("seed binding = %q", tag)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("chain never bound at seed")
	}

	const churners = 8
	const generations = 25
	var wg sync.WaitGroup
	wg.Add(churners)
	for g := 0; g < churners; g++ {
		go func(g int) {
			defer wg.Done()
			for i := 0; i < generations; i++ {
				tag := fmt.Sprintf("g%d-%d", g, i)
				ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
				_ = e.ctrl.Reconcile(ctx, pv(tag))
				cancel()
			}
		}(g)
	}
	wg.Wait()

	// Converge to the terminal generation.
	if err := e.ctrl.Reconcile(ctxT(t), pv("final")); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, e, "p"), fiberOf(t, e, "a"), fiberOf(t, e, "b"))

	// One owned entry per id, no duplicate generations (P2-7 lesson applied).
	owned := e.ctrl.Owned()
	if len(owned) != 3 {
		t.Fatalf("owned entries after churn = %d, want exactly 3 (one per id)", len(owned))
	}

	// B resolved the FINAL tag through both chain edges.
	deadline := time.Now().Add(8 * time.Second)
	resolved := ""
	for resolved == "" && time.Now().Before(deadline) {
		select {
		case tag := <-kb.seen:
			if tag == "final" {
				resolved = tag
			}
		default:
			time.Sleep(5 * time.Millisecond)
		}
	}
	if resolved == "" {
		t.Fatal("B never resolved the terminal generation through the chain")
	}

	// Preservation over the churned composition.
	snap, err := e.rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatal(err)
	}
	if msg := pcThm64Check(snap); msg != "" {
		t.Fatalf("preservation violated after chained concurrent churn: %s", msg)
	}
}
