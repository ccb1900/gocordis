package runtime

import (
	"context"
	"fmt"
	"sort"
	"sync"
)

// Deterministic Kernel Control (Phase B). Deterministic mode is scheduling
// instrumentation, not lifecycle semantics: producers only produce
// completions; Command Admission decides whether a completion enters the
// orchestrator command stream; the Driver controls admission order. All
// parking/admission paths are non-blocking.

type RuntimeMode int

const (
	RuntimeNormal RuntimeMode = iota
	RuntimeDeterministic
)

func WithRuntimeMode(mode RuntimeMode) Option {
	return func(o *options) { o.mode = mode }
}

type deterministicState struct {
	mu      sync.Mutex
	pending map[Step]command

	// parked is a non-blocking wake-up for Close's shutdown drain (buffered 1):
	// it fires whenever a completion is parked while we are draining.
	parked chan struct{}
}

func newDeterministicState() *deterministicState {
	return &deterministicState{pending: make(map[Step]command), parked: make(chan struct{}, 1)}
}

type StepKind uint8

const (
	StepApplyDone StepKind = iota
	StepUnwindDone
)

type Step struct {
	Kind         StepKind
	FiberID      FiberID
	ActivationID ActivationID
}

func (s Step) String() string {
	k := "ApplyDone"
	if s.Kind == StepUnwindDone {
		k = "UnwindDone"
	}
	return fmt.Sprintf("%s(%d/%d)", k, s.FiberID, s.ActivationID)
}

func stepOf(cmd command) (Step, bool) {
	switch c := cmd.(type) {
	case *cmdApplyDone:
		return Step{Kind: StepApplyDone, FiberID: c.fiberID, ActivationID: c.activationID}, true
	case *cmdUnwindDone:
		return Step{Kind: StepUnwindDone, FiberID: c.fiberID, ActivationID: c.activationID}, true
	default:
		return Step{}, false
	}
}

func (r *Runtime) admitCommand(cmd command) {
	step, isCompletion := stepOf(cmd)
	if r.mode != RuntimeDeterministic || !isCompletion {
		r.submit(cmd)
		return
	}
	r.det.mu.Lock()
	if _, dup := r.det.pending[step]; !dup {
		r.det.pending[step] = cmd
		select {
		case r.det.parked <- struct{}{}:
		default:
		}
	}
	r.det.mu.Unlock()
}

func (r *Runtime) enqueueNonBlocking(cmd command) bool {
	o := r.orch
	o.sendMu.Lock()
	defer o.sendMu.Unlock()
	select {
	case <-o.stop:
		return false
	default:
	}
	select {
	case o.commands <- cmd:
		return true
	default:
		return false
	}
}

func stepStillValid(rt *Runtime, step Step) bool {
	rt.mu.RLock()
	f := rt.fibers[step.FiberID]
	rt.mu.RUnlock()
	if f == nil {
		return false
	}
	f.mu.RLock()
	defer f.mu.RUnlock()
	act := f.activation
	if act == nil || act.id != step.ActivationID {
		return false
	}
	switch step.Kind {
	case StepApplyDone:
		return f.state == StateLoading
	case StepUnwindDone:
		return f.state == StateUnloading
	}
	return false
}

func (r *Runtime) detEnabledSteps() []Step {
	if r.mode != RuntimeDeterministic {
		return nil
	}
	r.det.mu.Lock()
	steps := make([]Step, 0, len(r.det.pending))
	for s := range r.det.pending {
		steps = append(steps, s)
	}
	r.det.mu.Unlock()
	var out []Step
	for _, s := range steps {
		if stepStillValid(r, s) {
			out = append(out, s)
		}
	}
	sort.Slice(out, func(i, j int) bool {
		if out[i].Kind != out[j].Kind {
			return out[i].Kind < out[j].Kind
		}
		if out[i].FiberID != out[j].FiberID {
			return out[i].FiberID < out[j].FiberID
		}
		return out[i].ActivationID < out[j].ActivationID
	})
	return out
}

func (r *Runtime) detExecute(step Step) error {
	if r.mode != RuntimeDeterministic {
		return fmt.Errorf("runtime: detExecute requires RuntimeDeterministic")
	}
	if !stepStillValid(r, step) {
		return fmt.Errorf("runtime: stale completion %v", step)
	}
	r.det.mu.Lock()
	cmd, ok := r.det.pending[step]
	if ok {
		delete(r.det.pending, step)
	}
	r.det.mu.Unlock()
	if !ok {
		return fmt.Errorf("runtime: no parked completion for %v", step)
	}
	if !r.enqueueNonBlocking(cmd) {
		return fmt.Errorf("runtime: queue full/stopped admitting %v", step)
	}
	return nil
}

func (r *Runtime) detPending() int {
	r.det.mu.Lock()
	defer r.det.mu.Unlock()
	return len(r.det.pending)
}

// drainShutdownCompletions is Close's Deterministic-mode shutdown admission
// drain. It releases exactly the lifecycle completions required for the
// Runtime to reach Closed (ApplyDone/UnwindDone produced by cmdClose's
// disposeAll, including ownership-cascade unwinds). It NEVER drives arbitrary
// lifecycle steps (no new activations, no normal scheduling); it waits on
// internal notification (det.parked) or orch.done — no polling, no sleeps.
func (r *Runtime) drainShutdownCompletions(ctx context.Context) error {
	for {
		// Admit every currently-valid parked completion (idempotent: each step
		// is removed once; stale steps are rejected and left out).
		for {
			en := r.detEnabledSteps()
			if len(en) == 0 {
				break
			}
			for _, s := range en {
				_ = r.detExecute(s) // stale -> error; shutdown-required -> admitted
			}
		}
		select {
		case <-r.orch.done:
			return nil
		case <-r.det.parked:
			// A new shutdown completion was parked; loop to admit it.
		case <-ctx.Done():
			return ctx.Err()
		}
	}
}
