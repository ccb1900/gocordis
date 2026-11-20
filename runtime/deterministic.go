package runtime

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"sync"
)

// Deterministic Kernel Control (Phase B). Deterministic mode is scheduling
// instrumentation, not lifecycle semantics: producers only produce
// completions; Command Admission decides whether a completion enters the
// orchestrator command stream; the Driver controls admission order. All
// parking/admission paths are non-blocking.

type runtimeMode int

const (
	runtimeNormal runtimeMode = iota
	runtimeDeterministic
)

func withRuntimeMode(mode runtimeMode) Option {
	return func(o *options) { o.mode = mode }
}

// ---------------------------------------------------------------------------
// Public deterministic-mode surface (P2-4, integration audit). The driver is
// scheduling INSTRUMENTATION for composition tests and tools: it lets a test
// admit parked completions (ApplyDone/UnwindDone) in a chosen order instead
// of free-running scheduling. It changes NOTHING about lifecycle semantics —
// the same commands are admitted, only their order is controlled — so any
// composition that works in normal mode must quiesce to the same state here
// (Thm 80 conformance at composition level).
// ---------------------------------------------------------------------------

// WithDeterministicMode runs the runtime under the deterministic driver:
// Apply/Unwind completions are PARKED instead of free-running, and a DetDriver
// admits them one step at a time in the order the test chooses. Close still
// drains the shutdown completions it needs to reach Closed.
func WithDeterministicMode() Option {
	return withRuntimeMode(runtimeDeterministic)
}

// DetStepKind names one parked completion kind.
type DetStepKind string

const (
	// DetApplyDone is a parked Apply completion.
	DetApplyDone DetStepKind = "ApplyDone"
	// DetUnwindDone is a parked Unwind completion.
	DetUnwindDone DetStepKind = "UnwindDone"
)

// DetStep identifies one parked completion: the kind plus the fiber/activation
// generation it belongs to. Stale steps (the fiber moved past the step) are
// rejected by Execute.
type DetStep struct {
	Kind         DetStepKind
	FiberID      FiberID
	ActivationID ActivationID
}

func (s DetStep) String() string {
	return fmt.Sprintf("%s(%d/%d)", s.Kind, s.FiberID, s.ActivationID)
}

// DetDriver admits parked completions in deterministic mode. Obtain it from
// Runtime.DetDriver; the methods are safe for concurrent use (the underlying
// machinery is mutex-guarded), though a test normally drives it from one
// goroutine.
type DetDriver struct {
	rt *Runtime
}

// DetDriver returns the deterministic-mode driver. On a normal-mode runtime
// the driver's methods are no-ops / errors — deterministic mode is chosen at
// construction with WithDeterministicMode.
func (r *Runtime) DetDriver() *DetDriver { return &DetDriver{rt: r} }

// EnabledSteps lists the currently valid parked steps in a deterministic
// (kind, fiber, activation) order.
func (d *DetDriver) EnabledSteps() []DetStep {
	r := d.rt
	if r.mode != runtimeDeterministic {
		return nil
	}
	var out []DetStep
	for _, s := range r.detEnabledSteps() {
		out = append(out, detStepToPublic(s))
	}
	return out
}

// Execute admits one parked completion. It returns an ErrDetStale-wrapped
// error when the fiber has moved past the step, ErrDetAdmission when
// the orchestrator rejected the command (queue full / halted), and
// ErrDetWrongMode on a normal-mode runtime.
func (d *DetDriver) Execute(step DetStep) error {
	r := d.rt
	if r.mode != runtimeDeterministic {
		return ErrDetWrongMode
	}
	return r.detExecute(detStepFromPublic(step))
}

// Pending reports how many completions are currently parked.
func (d *DetDriver) Pending() int {
	r := d.rt
	if r.mode != runtimeDeterministic {
		return 0
	}
	return r.detPending()
}

func detStepToPublic(s detStep) DetStep {
	k := DetApplyDone
	if s.Kind == detStepUnwindDone {
		k = DetUnwindDone
	}
	return DetStep{Kind: k, FiberID: s.FiberID, ActivationID: s.ActivationID}
}

func detStepFromPublic(s DetStep) detStep {
	k := detStepApplyDone
	if s.Kind == DetUnwindDone {
		k = detStepUnwindDone
	}
	return detStep{Kind: k, FiberID: s.FiberID, ActivationID: s.ActivationID}
}

type deterministicState struct {
	mu      sync.Mutex
	pending map[detStep]command

	// admitOverride, when non-nil, replaces enqueueNonBlocking for detExecute.
	// It exists so deterministic-driver tests can stage transient admission
	// failures (queue full / orchestrator stopped) deterministically. nil means
	// the production admission path (enqueueNonBlocking).
	admitOverride func(command) bool

	// parked is a non-blocking wake-up for Close's shutdown drain (buffered 1):
	// it fires whenever a completion is parked while we are draining.
	parked chan struct{}
}

func newDeterministicState() *deterministicState {
	return &deterministicState{pending: make(map[detStep]command), parked: make(chan struct{}, 1)}
}

var (
	// errDetStale reports that a parked completion is no longer valid: the
	// fiber/activation already moved past the step, so the completion is
	// obsolete and will never be admitted.
	errDetStale = errors.New("runtime: stale completion")

	// ErrDetStale reports that a parked completion is no longer valid: the
	// fiber/activation already moved past the step (compare with errors.Is).
	ErrDetStale = errors.New("runtime: stale deterministic step")

	// ErrDetWrongMode reports that a deterministic-driver operation ran on a
	// normal-mode runtime.
	ErrDetWrongMode = errors.New("runtime: deterministic driver requires WithDeterministicMode")

	// errDetAdmission reports that the orchestrator rejected a deterministic
	// completion (command queue full or stopped). The completion REMAINS
	// parked: enqueue-before-delete guarantees a failed admission never loses
	// it, and the driver may retry.
	errDetAdmission = errors.New("runtime: deterministic completion admission rejected")
)

type detStepKind uint8

const (
	detStepApplyDone detStepKind = iota
	detStepUnwindDone
)

type detStep struct {
	Kind         detStepKind
	FiberID      FiberID
	ActivationID ActivationID
}

func (s detStep) String() string {
	k := "ApplyDone"
	if s.Kind == detStepUnwindDone {
		k = "UnwindDone"
	}
	return fmt.Sprintf("%s(%d/%d)", k, s.FiberID, s.ActivationID)
}

func stepOf(cmd command) (detStep, bool) {
	switch c := cmd.(type) {
	case *cmdApplyDone:
		return detStep{Kind: detStepApplyDone, FiberID: c.fiberID, ActivationID: c.activationID}, true
	case *cmdUnwindDone:
		return detStep{Kind: detStepUnwindDone, FiberID: c.fiberID, ActivationID: c.activationID}, true
	default:
		return detStep{}, false
	}
}

func (r *Runtime) admitCommand(cmd command) {
	step, isCompletion := stepOf(cmd)
	if r.mode != runtimeDeterministic || !isCompletion {
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
	// Same admission gate as submit(): halt (not stop-close) is the authority,
	// so a deterministic completion is admitted before the gate or rejected.
	if o.halted {
		return false
	}
	select {
	case o.commands <- cmd:
		return true
	default:
		return false
	}
}

func stepStillValid(rt *Runtime, step detStep) bool {
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
	case detStepApplyDone:
		return f.state == StateLoading
	case detStepUnwindDone:
		return f.state == StateUnloading
	}
	return false
}

func (r *Runtime) detEnabledSteps() []detStep {
	if r.mode != runtimeDeterministic {
		return nil
	}
	r.det.mu.Lock()
	steps := make([]detStep, 0, len(r.det.pending))
	for s := range r.det.pending {
		steps = append(steps, s)
	}
	r.det.mu.Unlock()
	var out []detStep
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

func (r *Runtime) detExecute(step detStep) error {
	if r.mode != runtimeDeterministic {
		return fmt.Errorf("runtime: detExecute requires runtimeDeterministic")
	}
	if !stepStillValid(r, step) {
		return fmt.Errorf("%w: %v", ErrDetStale, step)
	}

	// Enqueue-before-delete: a completion is admitted to the orchestrator
	// FIRST and only removed from pending after the admission succeeded. If the
	// orchestrator rejects the command (queue full / stopped), the parked
	// completion must remain so a later attempt can admit it. The whole
	// lookup/admit/delete is one critical section so a step can never be
	// enqueued twice, even under a concurrent retry.
	r.det.mu.Lock()
	cmd, ok := r.det.pending[step]
	if !ok {
		r.det.mu.Unlock()
		return nil // nothing parked for this step; nothing to admit
	}

	admit := r.det.admitOverride
	if admit == nil {
		admit = r.enqueueNonBlocking
	}
	if !admit(cmd) {
		r.det.mu.Unlock()
		return fmt.Errorf("%w: %v", errDetAdmission, step)
	}
	delete(r.det.pending, step)
	r.det.mu.Unlock()
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
		// is removed on successful admission). Stale completions (the fiber
		// moved past the step) are obsolete and drop out of detEnabledSteps.
		for {
			en := r.detEnabledSteps()
			if len(en) == 0 {
				break
			}
			blocked := false
			for _, s := range en {
				if err := r.detExecute(s); err != nil && !errors.Is(err, ErrDetStale) {
					// Admission rejected (queue full/stopped): the completion
					// stays parked (never lost). Do NOT report it as drained;
					// back off and retry below.
					blocked = true
					break
				}
			}
			if !blocked {
				continue
			}
			break
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
