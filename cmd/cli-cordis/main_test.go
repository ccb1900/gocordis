package main

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/cli"
	"dynamic-runtime/runtime"
)

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type harness struct {
	rt   *runtime.Runtime
	cliF *runtime.Fiber
	regF *runtime.Fiber
	svc  cli.Service
	out  *bytes.Buffer
}

func newHarness(t *testing.T, ctx context.Context, ver string) *harness {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	cliF, err := rt.Load(&cli.Component{})
	if err != nil {
		t.Fatalf("load cli component: %v", err)
	}
	if err := cliF.Ready(ctx); err != nil {
		t.Fatalf("cli Ready: %v", err)
	}
	out := &bytes.Buffer{}
	reg := &registrar{ver: ver, out: out, svcCh: make(chan cli.Service, 1)}
	regF, err := rt.Load(reg)
	if err != nil {
		t.Fatalf("load registrar: %v", err)
	}
	if err := regF.Ready(ctx); err != nil {
		t.Fatalf("registrar Ready: %v", err)
	}
	select {
	case svc := <-reg.svcCh:
		return &harness{rt: rt, cliF: cliF, regF: regF, svc: svc, out: out}
	case <-ctx.Done():
		t.Fatalf("service never delivered: %v", ctx.Err())
		return nil
	}
}

func TestRegistrarEffectIsReversible(t *testing.T) {
	ctx := testCtx(t)
	h := newHarness(t, ctx, "3.3.3")
	defer func() { _ = h.rt.Close(ctx) }()

	if err := h.svc.Execute(ctx, "run --version", nil, nil); err != nil {
		t.Fatalf("execute run --version: %v", err)
	}
	if err := h.svc.Execute(ctx, "version", nil, nil); err != nil {
		t.Fatalf("execute version: %v", err)
	}
	if got := h.out.String(); strings.Count(got, "3.3.3") != 2 {
		t.Fatalf("output = %q, want two version prints", got)
	}

	if err := h.regF.Dispose(); err != nil {
		t.Fatalf("Dispose registrar: %v", err)
	}
	if err := h.regF.Gone(ctx); err != nil {
		t.Fatalf("registrar Gone: %v", err)
	}
	for _, input := range []string{"run", "version"} {
		if err := h.svc.Execute(ctx, input, nil, nil); err == nil {
			t.Fatalf("%q still dispatches after registrar disposal", input)
		}
	}
}

func TestRuntimeCloseLeavesFibersGone(t *testing.T) {
	ctx := testCtx(t)
	h := newHarness(t, ctx, "4.4.4")

	if err := h.rt.Close(ctx); err != nil {
		t.Fatalf("rt.Close: %v", err)
	}
	for _, f := range []*runtime.Fiber{h.cliF, h.regF} {
		if err := f.Gone(ctx); err != nil {
			t.Fatalf("fiber Gone after Close: %v", err)
		}
		if st := f.State(); st != runtime.StateGone {
			t.Fatalf("fiber state after Close = %v, want Gone", st)
		}
	}
}

func TestRunHelperDispatchesArgs(t *testing.T) {
	for _, args := range [][]string{
		{"run", "--version"},
		{"--version"},
		{"version"},
		{"v"},
	} {
		var out, errOut bytes.Buffer
		if err := run(args, &out, &errOut); err != nil {
			t.Errorf("run(%v): %v", args, err)
			continue
		}
		if !strings.Contains(out.String(), versionValue) || errOut.Len() != 0 {
			t.Errorf("run(%v): out=%q err=%q", args, out.String(), errOut.String())
		}
	}

	var out, errOut bytes.Buffer
	if err := run([]string{"missing"}, &out, &errOut); err == nil {
		t.Fatal("run(missing): want unknown-command error")
	}
}
