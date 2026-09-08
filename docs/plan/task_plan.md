> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Task Plan: Bring Go Cordis to a Verifiable Paper-Aligned v1

## Goal

Deliver a buildable Go implementation whose documented semantics and automated tests establish the paper's core spatiotemporal-composability guarantees: reversible effects, declared reactive coeffects, scoped isolation/interception, lifecycle progress, and conditional confluence.

## Scope and non-goals

- The paper is normative. `stc-go` and Cordis are scenario/API references only.
- Preserve the public Runtime/Fiber lifecycle where possible; make declaration enforcement and scoped context deliberate breaking changes if needed.
- Do not claim an unrestricted theorem proof for arbitrary Go code. State the required assumptions: finite fibers/effects, cooperative Apply/Cleanup, acyclic declared dependency graph, and Runtime-managed effects only.
- HMR, Loader, WASM, Config, HTTP, Event, Registry, Watch, and Scheduler remain extensions; none may create a second Fiber lifecycle authority.

## Current Phase

Plan complete — awaiting implementation authorization.

## Completion definition

- `go test -race ./...` passes in a pinned, reproducible Go toolchain.
- Property/fuzz tests exercise the five paper-aligned claims under their stated assumptions: preservation, recovery exactness, ordering, progress, and confluence.
- Public docs map each supported paper construct to Go API behavior and state all limitations.
- CI executes the required static checks, unit/integration/property tests, fuzz smoke test, and race suite.

## Phases

### Phase 1: Establish a reproducible verification baseline

- [x] Pin the Go version used by local development and CI (including the toolchain download/selection policy).
- [x] Eliminate the current Go 1.25 tool / Go 1.26 cached-object mismatch without relying on a developer-global cache.
- [x] Add a CI workflow that runs format, vet/static analysis, `go test ./...`, and `go test -race ./...`.
- [x] Record package-by-package baseline results and existing known failures.
- **Status:** done (see `docs/plan/baseline.md`)
- **Acceptance:** a clean checkout passes the complete test command twice; the race suite has no skips caused by environment setup. — MET: two fresh `-count=1` runs + full `-race -count=1` green on Go 1.26.0 with isolated GOCACHE; zero environment-induced skips.

### Phase 2: Make declarations authoritative and failures contained

- [x] Add activation-local, immutable copies of declared Inject and Provide capability sets.
- [x] Reject `Require(key)` unless `key` is declared in the activation's Inject set; return a typed error.
- [x] Reject `Provide(key, value)` unless `key` is declared in the activation's Provide set; return a typed error.
- [x] Decide and document whether declarations are exact or upper bounds; implement the selected rule (UPPER BOUND / subset) — see `docs/plan/phase2-decision.md`.
- [x] Recover panics at Apply, effect install, cleanup, and inverse boundaries; convert them to lifecycle errors, continue the required unwind, and never strand a Fiber in Loading/Unloading.
- [x] Add tests for undeclared read/write, duplicate declaration, panic during each lifecycle boundary, and cleanup after failure (`runtime/declarations_containment_test.go`, D1–D8).
- **Status:** done
- **Acceptance:** MET — undeclared consumer fails (never Active); undeclared provide unwinds prior effects; panics contained with committed/unwind cleanup; full suite + race green.

### Phase 3: Define and implement scoped Context semantics

- [x] Write an ADR that defines Go equivalents of paper `get`, `set`, `isolate`, and `intercept`, including typed-key identity, realm identity, metadata composition, inheritance, and visibility rules. (`docs/plan/phase3-adr.md` — v2, revised per architect feedback).
- [x] Replace the single Runtime-global provider lookup with a realm-aware store (root-realm default; explicit child realms).
- [x] Implement Context derivation for owned children so a child inherits its parent's scope; explicit `WithScope()` derives a child realm (override/shadow without mutating the parent).
- [x] Implement reversible contextual set/provide (realm-scoped) and access-time interception (`runtime.Intercept` free function, install-order chain, Effect-reversible).
- [x] Keep provider generation identity and consumer-first withdrawal operating on the resolved realm (identity-resolved dependency edges).
- [x] Add tests for sibling isolation, nested override/recovery, same key in separate realms, interceptor composition/order, panic containment (`runtime/scoped_realm_test.go`).
- **Status:** done (v0.1 scope; deeper randomized coverage folds into Phase 5)
- **Acceptance:** MET for v0.1 — sibling explicit realms provide the same key independently (concurrent Active, mutually invisible); removing one subtree leaves the other Active and does not reload it; an unscoped parent-realm consumer cannot resolve sibling providers.

### Phase 4: Strengthen orchestration and progress guarantees

- [x] Build a declared dependency graph at mount/reconciliation boundaries and detect cycles with actionable diagnostics (`runtime/dependency_cycle.go`).
- [x] Choose a policy for cycles: reject on Load/Child (ErrDependencyCycle); keep graph valid across reload/HMR (edges are per-activation, identity-resolved; no stale edges).
- [x] Audit lifecycle transitions for single linearization point & external reads (documented; serialized orchestrator + mutex-guarded snapshots; race suite green).
- [x] Verify cancellation, late effect completion, owned-child withdrawal, provider retirement cannot bypass declaration/realm checks (existing phase2–7 + new realm tests + race).
- [x] Add bounded randomized operation tests for load, dispose, dependency churn, child creation, and close (`runtime/phase4_random_test.go`, `runtime/phase4_cycle_test.go`).
- **Status:** done
- **Acceptance:** MET — cyclic declarations fail deterministically at Load/Child (Load-cycle + child-boundary-cycle tests); finite acyclic scenarios quiesce; race/vet/test green.

### Phase 5: Rebuild the theorem-to-test evidence suite

- [x] Define executable invariants for preservation: valid ownership tree, unique provider per resolved realm (own-map), complete dependency bindings, active consumer has satisfiable provider (`runtime/proof_invariants_test.go`; checked on-orchestrator).
- [x] Generalize recovery-exactness tests to randomized stacks of effects/providers/failures (`TestPRecRecoveryExactnessRandomStacks`).
- [x] Test ordering under concurrent provider/consumer mounting and replacement, incl. realm-specific providers (`TestPOrdConcurrentMountOrdering` + scoped sibling tests).
- [x] Replace the fixed three-order confluence test with generated independent operation schedules vs canonical observables (`TestPConfConfluenceGeneratedSchedules`).
- [x] Add fuzz targets with deterministic seeds + CI smoke (`FuzzPreservation`; verified `-fuzztime=3s`); longer scheduled job pending CI runner.
- **Status:** done
- **Acceptance:** every claim records its assumptions, invariant, generator, oracle, seed on failure, and minimal reproducer; race mode passes all generated tests.

### Phase 6: Revalidate extensions against the corrected kernel

- [x] Update extension contracts to use declared/scoped Context APIs only — no source changes required (all extensions already declared); full suite green.
- [x] Compatibility adapters/versioned APIs — not needed (backward compatible root realm; no undeclared Require/Provide found).
- [x] Verify HMR honors isolation realms & never mutates internals (public API only; E2E-09/E2E-TYPE-09/HTTP-16..20 green).
- [x] Verify WASM guests activation-owned, no host bypass (wasm instance bound via ctx.Effect; no host API; V/W tests green).
- [x] E2E Config+Loader+HMR+WASM cascades (integration suite incl. wasm_e2e/type_e2e/wasm_hmr_e2e green).
- **Status:** done
- **Acceptance:** MET — extension/E2E all pass under corrected kernel; audit: single Fiber lifecycle authority; no extension→Kernel internal mutation.

### Phase 7: Release readiness and evidence handoff

- [x] Paper-to-implementation mapping table + supported semantics/limitations/trust boundary (`docs/plan/paper-mapping.md`).
- [x] Non-goals, migration notes, trust boundary published (mapping doc + per-phase decision docs).
- [~] Benchmark/regression thresholds — documented non-semantic limitation (paths unchanged in complexity); deferred.
- [~] Full CI matrix from a clean checkout — workflow added (`ci.yml`); execution requires a GitHub runner (not available here); local equivalents executed twice + race.
- [x] Final paper-first review — remaining divergences recorded as explicit limitations (identity-aware disposal schedules; CI runner execution; Windows runtime verification; benchmarks).
- **Status:** done (v0.1; documented limitations above)
- **Acceptance:** completion-definition gates pass locally; final review has no P0/P1 open items (P2/deferred documented).

## Decisions made

| Decision | Rationale |
|---|---|
| Paper defines correctness; reference repos do not. | The user explicitly requested a paper-first evaluation. |
| Repair verifiability before semantics expansion. | A broken toolchain prevents trustworthy regression evidence. |
| Treat scoped isolation/interception as required for a “paper-aligned” claim. | They are core Context Paradigm operations, not optional extensions. |
| Prefer rejecting dependency cycles. | It makes the paper's acyclic-progress premise explicit and gives users an actionable error. |

## Risks and controls

| Risk | Control |
|---|---|
| Scoped Context is a substantial API/data-model change. | ADR first; introduce compatibility layer or v2 API and migrate extensions one by one. |
| Go cannot automatically reverse arbitrary side effects. | Document Runtime-managed-effect boundary; provide ergonomic Effect helpers and panic containment. |
| Fuzz tests become flaky. | Use deterministic scheduling hooks, fixed seeds, bounded timeouts, and failure reproducers. |
| HMR conflicts with exclusive/scope-local providers. | Test candidate-first when possible, then sanctioned withdraw-then-load fallback per resolved realm. |

## Errors encountered

| Error | Attempt | Resolution |
|---|---:|---|
| `go test -race ./...` cannot compile because Go 1.25 toolchain reads Go 1.26 cached objects. | 1 | Phase 1 makes the toolchain/cache isolated and reproducible before code changes. |
