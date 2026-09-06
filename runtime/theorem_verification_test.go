package runtime_test

// Paper-level theorem verification harness — Phase 1 (Model / ObservableState /
// Trace / deterministic RNG) and Phase 2 (oracle skeleton + self checks).
// Spec: docs/review/GOCORDIS — Paper-Level Theorem Verification Specification
// v0.1.md. This file contains NO production semantics change; it extends the
// existing runtime property-test structure (property_contract_test.go /
// property_internal_test.go) rather than creating a duplicate system.

import (
	"math/rand/v2"
	"strings"
	"testing"

	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Independent data model (NOT runtime internal state).
// ---------------------------------------------------------------------------

// ModelFiberState mirrors the legal fiber states for scenario reasoning.
type ModelFiberState string

const (
	ModelPending   ModelFiberState = "Pending"
	ModelLoading   ModelFiberState = "Loading"
	ModelActive    ModelFiberState = "Active"
	ModelUnloading ModelFiberState = "Unloading"
	ModelGone      ModelFiberState = "Gone"
)

// ModelFiber is a scenario-level fiber in the independent model.
type ModelFiber struct {
	ID         string
	Parent     string // "" for root
	State      ModelFiberState
	Activation uint64
}

// Model is the independent, deterministic scenario model used as the oracle
// basis. It is intentionally NOT runtime internal state, so a divergence
// between model and runtime can reveal a runtime bug.
type Model struct {
	Fibers    map[string]*ModelFiber
	Providers map[runtime.CapabilityKey]string // capability -> owning fiber id
	Deps      map[string][]runtime.CapabilityKey
	Effects   map[string][]string
}

func newModel() *Model {
	return &Model{
		Fibers:    make(map[string]*ModelFiber),
		Providers: make(map[runtime.CapabilityKey]string),
		Deps:      make(map[string][]runtime.CapabilityKey),
		Effects:   make(map[string][]string),
	}
}

func (m *Model) fiber(id string) *ModelFiber {
	f := m.Fibers[id]
	if f == nil {
		f = &ModelFiber{ID: id, State: ModelPending}
		m.Fibers[id] = f
	}
	return f
}

// ---------------------------------------------------------------------------
// ObservableState — user-observable semantic snapshot (no pointers/goroutines).
// ---------------------------------------------------------------------------

type ObservableState struct {
	ActiveFibers  []string
	Providers     map[string]string // provider fiber id -> value tag (scenario)
	Dependencies  map[string][]string
	EffectResidue []string // remaining committed-effect markers (must be empty after full unwind)
}

// ---------------------------------------------------------------------------
// Trace / deterministic RNG
// ---------------------------------------------------------------------------

// Trace is a replayable operation log: seed + step descriptions.
type Trace struct {
	Seed uint64
	Ops  []string
}

func (t *Trace) add(f string, args ...any) {
	t.Ops = append(t.Ops, f)
	_ = args
}

func (t *Trace) replay() string {
	var b strings.Builder
	b.WriteString("seed: ")
	b.WriteString(uint64String(t.Seed))
	for i, op := range t.Ops {
		b.WriteString("\n  step ")
		b.WriteString(intString(i))
		b.WriteString(": ")
		b.WriteString(op)
	}
	return b.String()
}

func uint64String(v uint64) string { return itoa(v) }
func intString(v int) string       { return itoa(uint64(v)) }

// itoa avoids importing strconv for a tiny helper; keep formatting minimal.
func itoa(v uint64) string {
	if v == 0 {
		return "0"
	}
	var buf [24]byte
	i := len(buf)
	for v > 0 {
		i--
		buf[i] = byte('0' + v%10)
		v /= 10
	}
	return string(buf[i:])
}

// deterministicRand returns a seeded RNG (math/rand/v2, no globals).
func deterministicRand(seed uint64) *rand.Rand {
	return rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
}

// ---------------------------------------------------------------------------
// Failure taxonomy (spec §35)
// ---------------------------------------------------------------------------

const (
	failT59Preservation = "T59_PRESERVATION"
	failT61Recovery     = "T61_RECOVERY"
	failT61LIFO         = "T61_LIFO"
	failT61ExactlyOnce  = "T61_EXACTLY_ONCE"
	failT63Ordering     = "T63_ORDERING"
	failT66Progress     = "T66_PROGRESS"
	failT66Deadlock     = "T66_DEADLOCK"
	failT66Livelock     = "T66_LIVELOCK"
	failT73Confluence   = "T73_CONFLUENCE"
	failT73Precond      = "T73_PRECONDITION"
)

func theoremFailure(tb testing.TB, theorem, seed string, detail ...string) {
	tb.Helper()
	tb.Fatalf("=== THEOREM FAILURE ===\ntheorem: %s\nseed: %s\n%s", theorem, seed, strings.Join(detail, "\n"))
}

// ---------------------------------------------------------------------------
// Phase 2 skeleton self-checks: the harness itself must pass fixed cases.
// ---------------------------------------------------------------------------

// Model well-formedness used by the T59 checker skeleton.
func checkModelWellFormed(m *Model) error {
	for id, f := range m.Fibers {
		if f.Parent != "" {
			if _, ok := m.Fibers[f.Parent]; !ok {
				return errf("model: fiber %s parent %s missing", id, f.Parent)
			}
		}
	}
	for key, owner := range m.Providers {
		f, ok := m.Fibers[owner]
		if !ok || f.State != ModelActive {
			return errf("model: provider %v owned by non-active fiber %s", key, owner)
		}
	}
	return nil
}

func errf(format string, args ...any) error {
	return &modelError{msg: sprintf(format, args...)}
}

type modelError struct{ msg string }

func (e *modelError) Error() string { return e.msg }

func sprintf(format string, args ...any) string {
	// minimal formatting for model errors (test-only)
	b := []byte(format)
	_ = b
	var out []byte
	argi := 0
	for i := 0; i < len(format); i++ {
		if format[i] == '%' && i+1 < len(format) && format[i+1] == 'v' && argi < len(args) {
			out = append(out, sprintArg(args[argi])...)
			argi++
			i++
			continue
		}
		if format[i] == '%' && i+1 < len(format) && format[i+1] == 's' && argi < len(args) {
			out = append(out, sprintArg(args[argi])...)
			argi++
			i++
			continue
		}
		out = append(out, format[i])
	}
	return string(out)
}

func sprintArg(a any) string {
	switch v := a.(type) {
	case string:
		return v
	case int:
		return itoa(uint64(v))
	case uint64:
		return itoa(v)
	case runtime.CapabilityKey:
		return v.String()
	default:
		return "?"
	}
}

// TestTheoremModelWellFormed — fixed self-checks for the model oracle.
func TestTheoremModelWellFormed(t *testing.T) {
	m := newModel()
	if err := checkModelWellFormed(m); err != nil {
		t.Fatalf("empty model not well formed: %v", err)
	}

	// Parent validity violation must be detected.
	m2 := newModel()
	child := m2.fiber("child")
	child.Parent = "missing"
	if err := checkModelWellFormed(m2); err == nil {
		t.Fatal("expected parent-validity violation")
	}

	// Provider owned by a non-active fiber must be detected.
	m3 := newModel()
	key := runtime.NewKey[string]("t59.provider").Capability()
	p := m3.fiber("P")
	p.State = ModelPending
	m3.Providers[key] = "P"
	if err := checkModelWellFormed(m3); err == nil {
		t.Fatal("expected provider-state violation")
	}
}

// TestTheoremTraceReplay — seeds are deterministic and replayable.
func TestTheoremTraceReplay(t *testing.T) {
	tr := Trace{Seed: 42}
	tr.add("create A")
	tr.add("create B")
	replay := tr.replay()
	if !strings.Contains(replay, "seed: 42") || !strings.Contains(replay, "step 1") {
		t.Fatalf("replay malformed: %s", replay)
	}

	r1 := deterministicRand(7)
	r2 := deterministicRand(7)
	a := r1.IntN(1000000)
	b := r2.IntN(1000000)
	if a != b {
		t.Fatalf("deterministic rng diverged: %d != %d", a, b)
	}
}
