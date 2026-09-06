# Paper → Implementation Mapping (Phase 7 evidence)

Assumptions stated with every claim: finite fibers/effects; cooperative
Apply/Cleanup; acyclic declared dependency graph; only Runtime-managed effects
are reversible (Go host side effects outside Context.Effect are the documented
trust boundary).

## Claim → Invariant/Test evidence

| Paper claim | Executable evidence | Where |
|---|---|---|
| Preservation (valid ownership tree, unique provider per resolved realm, complete dependency bindings, active consumer has active provider) | `checkPreservationInvariants` (on-orchestrator) | `runtime/proof_invariants_test.go` |
| Recovery exactness (S2 == S0 after unwind incl. partial failure) | `TestPRecRecoveryExactnessRandomStacks` (random effect stacks, failAt injection) | `runtime/phase5_generators_test.go` |
| Ordering (concurrent provider/consumer mounting; realm resolution) | `TestPOrdConcurrentMountOrdering`; sibling-realm isolation | `runtime/phase5_generators_test.go`, `runtime/scoped_realm_test.go` |
| Progress (finite/acyclic/cooperative → quiesce) | bounded random schedules + cycle rejection at Load/Child | `runtime/phase4_*_test.go`, `runtime/proof_invariants_test.go` |
| Conditional confluence (independent orderings → same canonical observable) | `TestPConfConfluenceGeneratedSchedules` (mount-only schedules; disposal-of-identity is documented out of scope) | `runtime/phase5_generators_test.go` |
| Declarations authoritative | `Require/Provide` membership; D1–D8 | `runtime/declarations_containment_test.go` |
| Scoped isolation/interception | sibling realms, shadow, `runtime.Intercept` order/panic | `runtime/scoped_realm_test.go` |

## Kernel semantics ↔ Go API

| Concept | Go surface |
|---|---|
| get(k) / set(k,v) | `runtime.Require/Provide(ctx, Key[T], ...)` — resolved per realm |
| isolate | explicit `Context.Child(comp, runtime.WithScope())` child realm |
| intercept(k,f) | `runtime.Intercept[T](ctx, key, fn)` read-time chain |
| reversible effect | `ctx.Effect(install → inverse)` exactly-once LIFO |
| dependency loss | withdrawal to Pending (never arbitrary failure) |
| provider identity | `ProviderIdentity{FiberID, ActivationID}` |
| ownership | `ctx.Child` ownership tree, distinct from dependency |

## Trust boundary & migration notes

- Only Runtime-managed effects are reversible; arbitrary Go side effects must be
  wrapped via `ctx.Effect` (documented in code; no automatic reversal).
- Breaking changes vs earlier Kernel: Phase 2 declaration enforcement
  (undeclared Require/Provide rejected), Phase 3 realm-aware resolution
  (backward compatible when no explicit scope), Phase 4 mount-boundary cycle
  rejection. Extensions required no source changes (they already declared).
- Migration: components must declare Inject/Provide exactly what they
  Require/Provide; use `WithScope()` only when sibling isolation is intended.
- Fuzz: `FuzzPreservation` with deterministic seed corpus; CI smoke
  `-fuzztime=5s`. Failures reproduce from the seed; corpus is retained by Go.
- Benchmarks/regression thresholds: not added in v0.1 (documented non-semantic
  limitation); reconciliation/unload paths unchanged in complexity.
