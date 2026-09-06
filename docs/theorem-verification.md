# Paper-Level Theorem Verification (gocordis)

Status: **CONDITIONAL PASS** — all five theorem suites implemented and green;
remaining gaps are documented (no automated shrinker; generators cover the core
scenario families; full generality requires the randomized expansion noted in
GOCORDIS §42/§36).

Authority: `docs/review/GOCORDIS — Paper-Level Theorem Verification Specification v0.1.md`
Method: independent model + runtime execution + executable invariant/oracle +
deterministic seeds; no sleep-based proofs (T66 uses a test-only orchestrator
step driver built on the public command queue and generation signals).

| Theorem | Definition | Runtime mechanism | Property | Status |
|---|---|---|---|---|
| T59 Preservation | registry/dependency/provider invariants | root-realm providers, identity edges, snapshots | randomized invariant check P1..P5 (white-box, run on orchestrator) | PASS |
| T61 Recovery | effect inverse / LIFO / exactly-once | ctx.Effect slots, guarded inverses | baseline equivalence R0 vs R1 + ordered trace | PASS |
| T63 Ordering | dependency gating | Loading gated on satisfied deps | randomized provider up/down event oracle | PASS |
| T66 Progress | orchestrator bounded quiescence | test-only deterministic step driver | bounded transition count, no deadlock/livelock | PASS (core scenarios) |
| T73 Confluence | legal interleavings | same op set, many legal schedules | observational equivalence (24 schedules × 4 seeds) + explicit precondition | PASS |

## Evidence files

- `runtime/theorem_verification_test.go` — model/observable/trace/RNG/failure taxonomy.
- `runtime/t59_internal_test.go` — P1..P5 checker + randomized cycles + orchestrator-run check.
- `runtime/t61_internal_test.go` — LIFO/exactly-once + baseline equivalence.
- `runtime/t63_internal_test.go` — randomized ordering oracle.
- `runtime/t66_t73_test.go` — deterministic quiescence driver; bounded progress; confluence schedules.
- `runtime/fuzz_interleaving_test.go` — `FuzzInterleaving` + corpus (T59/T63 checks, signal-driven).

## Gates

- `go test ./...` — PASS
- `go test -race ./...` — PASS
- `go test -run Property ./...` — PASS (AC-03)
- `go test -run Property -fuzz FuzzInterleaving -fuzztime 10s ./runtime/` — PASS (AC-04; Go forbids `-fuzz` across multiple packages, so the single package containing `FuzzInterleaving` is targeted)

## Assumptions / limitations (explicit, never implied guarantees)

- Finite fibers/effects, cooperative Apply/Cleanup, acyclic declared dependency
  graph, Runtime-managed effects only.
- T66/T73 generators currently cover: single-root-realm provider/consumer
  chains, effect stacks, provider withdraw/reload, mount-order interleavings.
  Realm-sibling, nested-child and HMR/WASM-heavy interleavings are the next
  expansion surface.
- T73 effect independence is a generator precondition (asserted by
  `t73Precondition`); duplicate-provider traces are not generated.
- No automated shrinker yet (AC-12): failures carry theorem+seed+operation
  trace; minimization is manual.
- T59 invariant checks run on the orchestrator goroutine (no cross-goroutine
  reads) and require quiescent checkpoints.
