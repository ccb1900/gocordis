> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

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
- T73 generators cover: single-root-realm provider/consumer mount permutations
  (24×4), op-level dependency-precedence DAG topological schedules (provider
  chain, 24×4), and two independent subsystems + effect component (16×2).
  Realm-sibling, nested-child and HMR/WASM-heavy interleavings remain the next
  expansion surface.
- T73 effect independence is a generator precondition (asserted by
  `t73Precondition`); duplicate-provider traces are not generated.
- AC-12 shrinker: deterministic delta-debugging `Minimize` (op-list removal,
  deterministic) shipped and self-tested (`runtime/minimize_test.go`
  `TestMinimizeDeterministic`): 40-op trace → minimal `[open commit]`, each
  single removal non-reproducing, repeatable.
- T59 invariant checks run on the orchestrator goroutine (no cross-goroutine
  reads) and require quiescent checkpoints.

## Reviewer-gap closures (next review round)

- Quiescence ≠ all Gone: `TestT66QuiescenceAllowsActive` — provider+consumer
  Active is quiescent (no pending transition); dispose completion kept as a
  separate convergence test (`TestPropertyProgressToQuiescence` doc updated).
- Withdrawal ordering (T63): `TestT63WithdrawalOrdering` — consumer cleanup
  strictly precedes provider cleanup.
- Dependency-precedence acyclicity: enforced at Load/Child
  (`runtime/dependency_cycle.go`, `ErrDependencyCycle`) and checked as an
  invariant oracle across randomized reloads (`TestT66AcyclicPrecedenceInvariant`).
- T66 uses a deterministic orchestrator step driver (probe command + generation
  signals); no `Ready(timeout)` proof.
- T73: legal mount-permutation schedules over the same logical operation set
  with explicit precondition; full op-level DAG topological oracle remains the
  next expansion.
