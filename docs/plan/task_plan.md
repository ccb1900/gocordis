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

- [ ] Add activation-local, immutable copies of declared Inject and Provide capability sets.
- [ ] Reject `Require(key)` unless `key` is declared in the activation's Inject set; return a typed error.
- [ ] Reject `Provide(key, value)` unless `key` is declared in the activation's Provide set; return a typed error.
- [ ] Decide and document whether declarations are exact (`actual == declared`) or upper bounds (`actual subset of declared`); implement the selected rule at activation completion.
- [ ] Recover panics at Apply, effect install, cleanup, and inverse boundaries; convert them to lifecycle errors, continue the required unwind, and never strand a Fiber in Loading/Unloading.
- [ ] Add tests for undeclared read/write, duplicate declaration, panic during each lifecycle boundary, and cleanup after failure.
- **Status:** pending
- **Acceptance:** a consumer with an undeclared dependency cannot become Active or retain a stale provider reference; injected panics cannot crash the process and leave no Runtime-managed resource/provider behind.

### Phase 3: Define and implement scoped Context semantics

- [ ] Write an ADR that defines Go equivalents of paper `get`, `set`, `isolate`, and `intercept`, including typed-key identity, realm identity, metadata composition, inheritance, and visibility rules.
- [ ] Replace the single Runtime-global provider lookup with a context-scoped store plus per-context realm resolution.
- [ ] Implement Context derivation for owned children so a child inherits its parent's store/realm/interception view and can override it without mutating the parent.
- [ ] Implement reversible contextual set/provide and access-time interception with explicit metadata merge contracts.
- [ ] Keep provider generation identity and consumer-first withdrawal operating on the resolved realm, not only a global key.
- [ ] Add tests for sibling isolation, nested override/recovery, same key in separate realms, interceptor composition/order, and isolation-aware dependency notification.
- **Status:** pending
- **Acceptance:** two subtrees can provide the same logical key independently; removing one subtree restores exactly its parent view and does not reload unrelated consumers.

### Phase 4: Strengthen orchestration and progress guarantees

- [ ] Build a declared dependency graph at mount/reconciliation boundaries and detect cycles with actionable diagnostics.
- [ ] Choose a policy for cycles (reject on Load/Child is recommended) and keep the graph valid across reload and HMR replacement.
- [ ] Audit every lifecycle transition for a single linearization point and every external read for synchronization correctness.
- [ ] Verify cancellation, late effect completion, owned-child withdrawal, and provider retirement cannot bypass declaration/realm checks.
- [ ] Add bounded randomized operation tests for load, dispose, dependency churn, child creation, and close.
- **Status:** pending
- **Acceptance:** cyclic declarations fail deterministically; finite acyclic scenarios quiesce within a bounded, testable number of lifecycle steps.

### Phase 5: Rebuild the theorem-to-test evidence suite

- [ ] Define executable invariants for preservation: valid ownership tree, unique provider per resolved realm, complete dependency bindings, active provider for each active consumer.
- [ ] Generalize recovery-exactness tests to randomized stacks of effects, providers, child contexts, failures, and cancellation races.
- [ ] Test ordering under concurrent provider/consumer mounting and replacement, including realm-specific providers.
- [ ] Replace the fixed three-order confluence test with generated independent operation schedules; compare canonical Runtime observables at quiescence.
- [ ] Add fuzz targets with deterministic seeds, a CI smoke duration, and a longer scheduled fuzz job.
- **Status:** pending
- **Acceptance:** every claim records its assumptions, invariant, generator, oracle, seed on failure, and minimal reproducer; race mode passes all generated tests.

### Phase 6: Revalidate extensions against the corrected kernel

- [ ] Update Loader, Config, HMR, WASM, Registry, Event, Scheduler, Watch, and HTTP contracts to use declared/scoped Context APIs only.
- [ ] Add compatibility adapters or versioned APIs where an extension currently performs undeclared Require/Provide.
- [ ] Verify HMR replacement preserves old behavior on candidate failure, honors isolation realms, and never mutates Fiber/provider internals directly.
- [ ] Verify WASM guests are activation-owned resources and host calls cannot bypass capability declarations or scope boundaries.
- [ ] Run end-to-end scenarios that compose Config + Loader + HMR + WASM with dependency cascades and cleanup checks.
- **Status:** pending
- **Acceptance:** all extension and E2E tests pass; architecture audit finds one Fiber lifecycle authority and no extension-to-kernel internal mutation.

### Phase 7: Release readiness and evidence handoff

- [ ] Update README, package docs, architecture diagram, and a paper-to-implementation mapping table.
- [ ] Publish supported semantics, non-goals, migration notes, and the Go-specific trust boundary for unmanaged side effects.
- [ ] Add benchmark/regression thresholds for reconciliation and unload cascades if performance-sensitive paths changed.
- [ ] Execute the full CI matrix from a clean checkout and retain results/coverage/fuzz seeds as release evidence.
- [ ] Perform final paper-first review and record any remaining divergence as an explicit limitation, not an implied guarantee.
- **Status:** pending
- **Acceptance:** all completion-definition gates pass and the final review has no P0/P1 open items.

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
