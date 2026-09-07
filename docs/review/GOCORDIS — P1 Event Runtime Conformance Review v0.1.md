# P1 EVENT RUNTIME CONFORMANCE REVIEW

Result:
CONDITIONAL PASS

P1.1 Emit:       PASS
P1.2 Serial:     PASS
P1.3 Parallel:   PASS
P1.4 Waterfall:  PASS

Shared Registry:     PASS
Shared Snapshot:     PASS
Shared Scope:        PASS
Shared Ownership:    PASS
Shared Cancellation: PASS
Shared Panic:        PASS
Reentrancy:          PASS
Concurrency:         PASS
Generation:          PASS (kernel mechanism verified; event-level combined test missing — see Issues G-1)
Lifecycle Isolation: PASS

Tests:
go vet:       PASS
go test:      PASS
go test -race: PASS (single full run); known pre-existing flake reproduced on repeat runs (see Flakes)

Issues:
- G-1 test gap: no P1 test drives a dispose -> reload (generation B) cycle on a
  Component that registered On/OnWaterfall and proves generation A handlers are
  not visible in generation B dispatches (event-level generation correctness).
- G-2 test gap: no P1 test combines event registration with dependency
  withdrawal (Provider X withdraw -> dependent consumer unload -> its Effect
  unwind -> event handler unregistered, stale handler == 0).

Required Changes:
- Add the two combined conformance tests for G-1 and G-2 (test-only; no
  runtime change expected — the kernel mechanisms are already in place and
  verified statically below).

Final Verdict:
CONDITIONAL PASS — no lifecycle violation, ownership leak, scope violation,
generation bug, race, or semantic contradiction was found. The only
shortfalls are two test-coverage gaps (G-1, G-2); closing them is expected to
require test code only.

---

## 1. Audit Object

Baseline commit: `28eece4` (P1.4 Waterfall; the P1 Event Runtime state).
Current HEAD at audit time: `6e7edc5` (P2.1 conformance tests). The runtime
production tree is identical between the two commits (the P2.1 delta is two
test files only), so the audit below covers the P1 Event Runtime exactly at
its completion state.

Audit scope: the whole `runtime/` package — event code AND its Kernel
integration points (Context, Activation, Fiber, Effect, Realm, Provider,
Orchestrator, Reconciliation) — to confirm the Event Runtime did not break or
bypass Kernel semantics.

## 2. Method

Static audit (source reading + targeted greps) and dynamic audit (required
command battery). No production code was modified during this review.

## 3. Architecture Audit (A-01 .. A-13)

| Gate | Claim | Evidence | Verdict |
| ---- | ----- | -------- | ------- |
| A-01 | One Event Registry | single `eventRegistry` type in `runtime/event_registry.go`; `Runtime.eventReg` is the only registry field (`runtime/runtime.go`); all four dispatches read it via `r.eventSnapshot(...)` | PASS |
| A-02 | Same EventKey | one `EventKey[T]`/`eventKeyID` identity = (payload type, name) in `runtime/eventkey.go`; no Fiber/Activation/seq/pointer/realm participates in identity | PASS |
| A-03 | Same Scope resolution | all four dispatches call the same `r.eventSnapshot(key, c.realm)` and the same `realmOnPath` (ancestor + current, no sibling/descendant, additive, no shadowing) | PASS |
| A-04 | Same Snapshot semantics | one snapshot function captures owner-valid regs under `eventRegistry.mu.RLock`, releases the lock, then executes; register/unregister during dispatch never mutates the current batch | PASS |
| A-05 | Same Activation ownership | every registration is Effect-owned by the activation (owner `ProviderIdentity`); dispatch filters by `r.eventOwnerActive` (fiber StateActive AND `f.activation.id == owner.ActivationID`) | PASS |
| A-06 | Registration via Effect | `On`/`OnWaterfall` both go through `onEvent` -> `Context.effect(EffectKindEvent, ...)` with an exactly-once inverse that unregisters | PASS |
| A-07 | Dispatch does not modify Fiber lifecycle | dispatch code contains no state writes; `eventOwnerActive` only reads fiber state under lock; no `Dispose`/`Load`/state transitions reachable from dispatch | PASS |
| A-08 | Dispatch does not touch Orchestrator | no `r.submit`/orchestrator reference in `event_registry.go`; dispatch is synchronous on the caller goroutine | PASS |
| A-09 | Dispatch does not touch Dependency Graph | no dependency read/write/reconcile call in event code | PASS |
| A-10 | Dispatch does not touch Provider Registry | no provider record mutation in event code (Provider publish/withdraw stays in the Provider effect path) | PASS |
| A-11 | No Event-specific lifecycle | no event-owned Fiber/Activation/state machine types; Event only rides the activation Effect lifecycle | PASS |
| A-12 | No Event-specific ownership | one ownership model: activation-owned Effects; no detached listener mechanism | PASS |
| A-13 | No Event-specific Scope system | Event reuses `realm` from the activation Context; no capture/bubble/shadow/priority scope | PASS |

Kernel isolation: `runtime/` imports no `dynamic-runtime/extensions/*` package;
the pre-existing `extensions/event` Event Bus is a separate extension layer and
is not part of the Kernel Event System audited here.

## 4. Dispatch Conformance

- Emit: synchronous broadcast; registration order; errors and contained panics
  aggregate via `errors.Join`, remaining handlers continue; cancellation stops
  future handlers, never force-terminates a running one. Matches §9.
- Serial: synchronous sequential in registration order; not fail-fast; errors
  aggregate; cancellation checkpoint between handlers. Matches §10.
- Parallel: true concurrency with a start barrier test (a serial implementation
  cannot pass `TestP1ParallelConcurrentBarrier`); returns only after all
  started handlers complete; deterministic snapshot membership; error
  aggregation in snapshot registration order, not completion order; started
  handlers always awaited (no fire-and-forget). Matches §11.
- Waterfall: synchronous middleware chain with before/after around `next()`;
  `next()` is synchronous (downstream completed when it returns); second
  `next()` returns `ErrWaterfallNextTwice`, downstream never runs twice, and
  the violation surfaces even when the handler swallows the error; short
  circuit (nil without `next()`) is never conflated with cancellation
  (`ctx.Err()`); errors propagate along the chain (no `errors.Join`);
  panic contained by the unified `ErrEventHandlerPanic` policy, chain
  terminates, error propagates; no global waterfall stack (chain state is
  pure call-stack state). Matches §12–§17.
- Waterfall + ordinary On: plain `On` registrations run as transparent nodes
  (auto-next after nil) and chain-aware handlers act as terminal nodes under
  Emit/Serial/Parallel (trivial `next()`); locked by
  `TestP1WaterfallPlainHandlersTransparent` and
  `TestP1WaterfallHandlerSharedAcrossDispatchModes`. Matches §16.
- Panic: one shared `guardedEventHandler` used by all four dispatches; panic
  never escapes the Runtime. Matches §17.
- Reentrancy: nested dispatches take a fresh snapshot and never reuse outer
  execution state; `TestP1SerialReentrantDispatch`,
  `TestP1ParallelReentrantAndNested`, `TestP1WaterfallReentrant/Nested`.
  Matches §18.
- Concurrency safety: registry lock is never held during user handlers
  (snapshot under lock, execution outside); concurrent register/unregister/
  dispatch tests per suite; race detector clean. Matches §19.
- Lifecycle interaction: dispose removes handlers; post-dispose dispatches
  never invoke them; residue assertions `rt.eventReg.count() == 0`.
  Matches §20.

## 5. Test Coverage Matrix (existing suites)

| Suite | Spec items | Tests | Covered categories |
| ----- | ---------- | ----- | ------------------ |
| P1.1 Emit | E-01..E-13 | 10 | identity/matching, effect-owned registration + unwind removal, deterministic ordering, snapshot isolation (reg + unreg), error aggregation + panic isolation, synchronous sequential, cancellation, no lifecycle-authority leak, guard rails, scope matching |
| P1.2 Serial | S-01..S-18 | 14 | ordered deterministic, await-previous, error aggregation (not fail-fast), cancellation (pre/ between/ handler-observed), snapshot reg/unreg, scope visibility + sibling isolation, effect disposal, concurrent registration/disposal, reentrant dispatch, panic containment |
| P1.3 Parallel | P-01..P-20 | 15 | concurrency barrier, waits-for-completion, membership determinism, error ordering, panic isolation, cancellation before/after start, snapshot reg/unreg, scope, disposal/no-leak, concurrent reg/disposal, reentrant+nested, payload identity |
| P1.4 Waterfall | W-01..W-20, T-01..T-09 | 24 | basic chain, before/after, registration ordering, synchronous next, short circuit, error propagation/short-circuit/transformation, duplicate next, cancellation, snapshot reg/unreg, scope, effect disposal, reentrant/nested, panic, 1000x determinism, no leak, race-safe concurrent reg/disposal, mixed plain+chain-aware, guard rails |

Category audit result: basic / ordering / scope / snapshot / registration /
unregistration / ownership / dispose / cancellation / error / panic /
reentrancy / nested dispatch / concurrency / race are covered. Two combined
categories are NOT covered by any P1 test: (G-1) event registration across
activation generations (dispose -> reload), and (G-2) dependency withdrawal
driving event-handler unregistration.

## 6. Generation Correctness (kernel mechanism, §22)

Static verification only: `eventOwnerActive` requires the owning fiber to be
StateActive AND `f.activation.id == owner.ActivationID`, so a handler whose
owner activation has been replaced by a later generation is never included in
a dispatch snapshot; the physical unregistration runs at effect unwind of the
old activation. The EventKey/registry store no identity tied to a generation.
No semantic bug found; the missing piece is an event-level combined test
(G-1).

## 7. Dependency Interaction (kernel mechanism, §21)

Event registrations are ordinary activation Effects. A provider withdrawal
causes the dependent consumer's activation to unwind through the existing
Reconciliation/Effect machinery, which runs the registration inverse; an
event handler therefore cannot survive provider loss. Static path verified;
combined test missing (G-2).

## 8. Required Commands

| Command | Result |
| ------- | ------ |
| `go vet ./...` | PASS |
| `go test ./...` | PASS (all packages, including `runtime`, `integration`, extensions) |
| `go test -race ./runtime -run TestP1 -count=1` | PASS (Emit/Serial/Parallel/Waterfall suites) |
| `go test -race ./... -count=1` | PASS (single full run) |

## 9. Flake Documentation (§27)

- Name: `TestC3T63RandomizedSchedule` (subtest `seed-3`, occasionally other
  seeds) — reproduced under `go test -race ./runtime -run 'TestUI02ScopeHierarchyIsolation|TestC3T63RandomizedSchedule' -count=5`:
  failure inside the C3 deterministic driver helper (`c3: no enabled step 0
  for fiber 2; enabled=[...] pending=3`).
- Relation to P1: NONE. The scenario (`t66Comp`) registers provider/consumer
  fibers and `ctx.Effect` (kind Custom) only; it never registers or dispatches
  Events. P1 changes (Event registry + `EffectKindEvent` metadata + dispatch
  code) do not touch the orchestrator, fiber state machine, reconciliation, or
  the numeric values of existing effect kinds (`Custom=0`, `Provider=1`
  unchanged; `Event=2` appended).
- Proof not introduced by this stage: the flaky file
  (`c3_t63_ordering_test.go`, first added in `07306e4`) and its sibling
  (`ui02_snapshot_test.go`, `ed92fd8`) both predate P1.1 (`d1c7ccf`); the
  same flake mode was baseline-reproduced and accepted in earlier review
  rounds. It is a pre-existing timing/seed-sensitive test, not a P1
  regression.
- `TestUI02ScopeHierarchyIsolation` did not reproduce in this run; it is the
  second previously documented baseline flake (UI-02 snapshot suite, also
  predating P1).
- No test was deleted or weakened to pass the gate.

## 10. Prohibited-Items Check (§31 / FAIL list)

No second event registry, no stale handler (residue == 0 asserted), no scope
leak, no activation-ownership violation, no race introduced, no
fire-and-forget Parallel, no asynchronous/duplicated Waterfall next, no panic
escaping the Runtime, no direct lifecycle modification by Event dispatch:
none of the FAIL conditions were observed.

## 11. Required Changes (revisit after review sign-off)

1. G-1: add `P1` test — Component registers `On`/`OnWaterfall`, dispose,
   reload (generation B): generation A handlers never fire in B; generation B
   handler fires; old registration residue == 0.
2. G-2: add `P1` test — consumer Component with `Inject X` registers an Event
   handler; provider X withdraws; consumer unloads; handler no longer fires;
   `rt.eventReg.count()` returns to baseline (stale handler == 0).

Both are expected to be test-only additions (no production change), consistent
with the audited kernel mechanisms in §6/§7.
