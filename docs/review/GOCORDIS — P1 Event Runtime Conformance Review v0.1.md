# P1 EVENT RUNTIME CONFORMANCE REVIEW

Result:
PASS

Status:
- v0.1 initial audit (commit `f630c26`): CONDITIONAL PASS — two test gaps
  (G-1, G-2) with no semantic findings.
- Gap closure (commit `b087db2`): G-1 and G-2 closed by
  `runtime/p1_conformance_gap_test.go` (test-only).
- This revision upgrades the verdict to PASS.

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
Generation:          PASS (kernel mechanism + test G-1)
Lifecycle Isolation: PASS

Tests:
go vet:       PASS
go test:      PASS
go test -race: PASS (single full run); known pre-existing flake documented
                below (reproduced on repeat runs, unrelated to P1)

Issues:
- (resolved) G-1: no P1 test proved event registration isolation across a
  dispose -> reload generation cycle. Closed by
  TestP1ReviewGap1GenerationEventRegistration.
- (resolved) G-2: no P1 test proved provider withdrawal unregisters the
  dependent consumer's event handler. Closed by
  TestP1ReviewGap2DependencyLossUnregistersHandler.

Required Changes:
- (done) G-1/G-2 combined tests added in `b087db2`; test-only, no runtime
  change was needed — matching the static audit (kernel mechanisms already
  correct).

Final Verdict:
PASS — the initial audit found no lifecycle violation, ownership leak, scope
violation, generation bug, race, or semantic contradiction; the only
shortfalls were two test-coverage gaps, both now closed with passing tests
and zero production change.

---

## 1. Audit Object

Baseline: `28eece4` (P1 Event Runtime completion state). The audit covers the
whole `runtime/` package — event code AND Kernel integration (Context,
Activation, Fiber, Effect, Realm, Provider, Orchestrator, Reconciliation).
Runtime production tree is identical from `28eece4` through the current HEAD;
the subsequent commits touch tests/docs only (`6e7edc5` P2.1 tests, `f630c26`
review, `b087db2` gap tests).

## 2. Architecture Audit (A-01 .. A-13)

| Gate | Claim | Evidence | Verdict |
| ---- | ----- | -------- | ------- |
| A-01 | One Event Registry | single `eventRegistry` in `runtime/event_registry.go`; `Runtime.eventReg` is the only registry (`runtime/runtime.go`); all four dispatches read it via `r.eventSnapshot` | PASS |
| A-02 | Same EventKey | one `EventKey[T]`/`eventKeyID` identity = (payload type, name) in `eventkey.go`; no Fiber/Activation/seq/pointer/realm in identity | PASS |
| A-03 | Same Scope resolution | all dispatches share `eventSnapshot(key, c.realm)` + `realmOnPath` (ancestor+current; additive; no sibling/descendant/shadowing) | PASS |
| A-04 | Same Snapshot semantics | one snapshot: owner-valid regs captured under `eventRegistry.mu.RLock`, lock released before execution; dispatch-time reg/unreg never mutates the current batch | PASS |
| A-05 | Same Activation ownership | registrations Effect-owned by activation; dispatch filters `eventOwnerActive` (StateActive AND `f.activation.id == owner.ActivationID`) | PASS |
| A-06 | Registration via Effect | `On`/`OnWaterfall` -> `onEvent` -> `Context.effect(EffectKindEvent)` with exactly-once unregister inverse | PASS |
| A-07 | No Fiber-lifecycle mutation | dispatch code has no state writes; `eventOwnerActive` only reads fiber state under lock | PASS |
| A-08 | No Orchestrator access | no `r.submit`/orchestrator reference in event code; dispatch runs synchronously on caller goroutine | PASS |
| A-09 | No Dependency Graph access | no dependency read/write/reconcile in event code | PASS |
| A-10 | No Provider Registry access | no provider record mutation in event code | PASS |
| A-11 | No Event-specific lifecycle | no event-owned Fiber/Activation/state machine | PASS |
| A-12 | No Event-specific ownership | one ownership model (activation Effects); no detached listeners | PASS |
| A-13 | No Event-specific scope | reuses activation `realm`; no capture/bubble/shadow/priority scope | PASS |

Kernel isolation: `runtime/` imports no `dynamic-runtime/extensions/*`;
`extensions/event` is a separate extension layer, not part of the Kernel
Event System.

## 3. Dispatch Conformance (spec §9–§17)

- Emit §9: synchronous broadcast; registration order; errors + contained
  panics aggregate via `errors.Join`, remaining handlers continue;
  cancellation stops future handlers, never force-terminates a running one.
- Serial §10: synchronous sequential registration order; not fail-fast;
  error aggregation; cancellation checkpoint between handlers.
- Parallel §11: true concurrency (barrier test that a serial implementation
  cannot pass); returns only after ALL started handlers complete; snapshot
  membership deterministic; error aggregation in snapshot order; started
  handlers always awaited (no fire-and-forget); cancellation blocks only
  not-yet-started handlers and still waits running ones.
- Waterfall §12–§15: synchronous middleware chain with before/after around
  `next()`; `next()` synchronous (downstream completed on return); second
  `next()` returns `ErrWaterfallNextTwice`, downstream never runs twice, and
  the violation surfaces even when swallowed; short circuit (nil, no `next()`)
  is distinct from cancellation (`ctx.Err()`); errors propagate along the
  chain (no aggregation); panic contained by the shared
  `ErrEventHandlerPanic` policy, chain terminates, error propagates; no
  global waterfall state (pure call-stack).
- Waterfall + ordinary On §16: plain `On` = transparent node (auto-next on
  nil); chain-aware handlers = terminal nodes under Emit/Serial/Parallel
  (trivial `next()`); locked by
  `TestP1WaterfallPlainHandlersTransparent` and
  `TestP1WaterfallHandlerSharedAcrossDispatchModes`.
- Panic §17: single shared `guardedEventHandler` for all four dispatches;
  panic never escapes the Runtime.
- Reentrancy §18: nested dispatches take a fresh snapshot; outer chain state
  never reused.
- Concurrency §19: registry lock never held during user handlers; concurrent
  register/unregister/dispatch covered per suite; race detector clean.
- Lifecycle §20: dispose removes handlers; post-dispose dispatch never
  invokes them; `rt.eventReg.count() == 0` residue assertions.

## 4. Error Semantics Matrix (spec §23)

| Dispatch  | Error model    | Continue? | Cancellation                       |
| --------- | -------------- | --------- | ---------------------------------- |
| Emit      | Aggregate      | Yes       | Stop future handlers               |
| Serial    | Aggregate      | Yes       | Stop future handlers               |
| Parallel  | Aggregate      | Yes       | Stop not-yet-started handlers      |
| Waterfall | Propagate      | No        | Stop downstream                    |

Panic: unified containment for all four. Matches implementation and tests.

## 5. Fine-Grained Test Coverage Matrix (function level)

### P1.1 Emit (10 tests; spec E-01..E-13)

| Test function | Covers |
| ------------- | ------ |
| TestP1EmitTypedIdentityAndMatching | identity: (payload type, name); same name + different type != same event |
| TestP1EmitEffectOwnedRegistrationAndUnwindRemoval | ownership: On -> Effect; dispose -> unregister; residue 0 |
| TestP1EmitDeterministicOrdering | ordering: registration sequence |
| TestP1EmitDispatchSnapshotIsolation | snapshot: register/unregister during dispatch; current batch unchanged, next dispatch changed |
| TestP1EmitErrorAggregationAndPanicIsolation | error aggregation; panic containment; handlers continue |
| TestP1EmitSynchronousSequentialDispatch | synchronous broadcast: all started handlers complete on return |
| TestP1EmitCancellationStopsFurtherDispatch | cancellation: stop future handlers; running handler not killed |
| TestP1EmitNoLifecycleAuthorityLeak | lifecycle isolation: handler Context carries no registry/lifecycle authority |
| TestP1EmitAndOnGuardRails | rails: nil ctx / zero key / nil handler rejected |
| TestP1EmitScopeMatching | scope: ancestor+current visible; sibling isolated; additive |

### P1.2 Serial (14 tests; spec S-01..S-18)

| Test function | Covers |
| ------------- | ------ |
| TestP1SerialOrderedDeterministic | ordering: A->B->C sequential |
| TestP1SerialAwaitsPreviousHandler | await-previous (strictly sequential) |
| TestP1SerialErrorAggregationNotFailFast | error aggregation; errors never stop later handlers |
| TestP1SerialCancellationStopsFutureHandlers | cancellation before handler start |
| TestP1SerialCancellationBetweenHandlers | cancellation at handler boundary |
| TestP1SerialHandlerObservesCancellation | running handler observes ctx; not force-terminated |
| TestP1SerialSnapshotRegistration | snapshot: mid-dispatch registration -> next dispatch only |
| TestP1SerialSnapshotUnregistration | snapshot: mid-dispatch unregister; member still runs; next dispatch excludes |
| TestP1SerialScopeVisibilityAndSiblingIsolation | scope: ancestor/current/sibling |
| TestP1SerialEffectDisposalRemovesHandler | dispose; no handler leak |
| TestP1SerialConcurrentRegistrationSafe | concurrency: register + dispatch race-free |
| TestP1SerialConcurrentDisposalSafe | concurrency: dispose + dispatch race-free |
| TestP1SerialReentrantDispatch | reentrancy: nested dispatch with own snapshot |
| TestP1SerialPanicContained | panic containment; registry intact; runtime keeps working |

### P1.3 Parallel (15 tests; spec P-01..P-20)

| Test function | Covers |
| ------------- | ------ |
| TestP1ParallelConcurrentBarrier | true concurrency (start barrier) |
| TestP1ParallelWaitsForCompletion | awaits ALL started handlers |
| TestP1ParallelRegistrationOrderingIndependent | deterministic snapshot membership; completion order free |
| TestP1ParallelErrorsAndDeterministicOrder | error aggregation in snapshot order (not completion order) |
| TestP1ParallelPanicIsolation | panic containment per handler |
| TestP1ParallelCancellationBeforeStart | cancellation skips not-yet-started |
| TestP1ParallelCancellationAfterStart | started handlers still awaited and complete |
| TestP1ParallelSnapshotRegistration | snapshot: mid-dispatch registration |
| TestP1ParallelSnapshotUnregistration | snapshot: mid-dispatch unregistration |
| TestP1ParallelScopeVisibility | scope: ancestor/current/sibling |
| TestP1ParallelEffectDisposalNoHandlerLeak | dispose; no handler leak |
| TestP1ParallelConcurrentRegistrationSafe | concurrency: register + dispatch race-free |
| TestP1ParallelConcurrentDisposalSafe | concurrency: dispose + dispatch race-free |
| TestP1ParallelReentrantAndNested | reentrancy: nested dispatches, own snapshots |
| TestP1ParallelPayloadIdentity | payload passed through without copying |

### P1.4 Waterfall (24 tests; spec W-01..W-20 + T-01..T-09)

| Test function | Covers |
| ------------- | ------ |
| TestP1WaterfallBasicChain | W-01 basic A->next->B->next->C |
| TestP1WaterfallBeforeAfter | W-02 around-middleware before/after order |
| TestP1WaterfallRegistrationOrdering | W-03 chain order = snapshot registration order |
| TestP1WaterfallNextIsSynchronous | W-04/T-06 next() synchronous (downstream completed) |
| TestP1WaterfallShortCircuit | W-05 short circuit; nil result, not error |
| TestP1WaterfallErrorPropagation | W-06 error propagates C->B->A->caller |
| TestP1WaterfallErrorShortCircuit | W-07 handler error without next(): downstream never starts |
| TestP1WaterfallErrorTransformation | W-08 handler may wrap/transform downstream error |
| TestP1WaterfallDuplicateNext | W-09/T-07 next() twice: downstream runs once; ErrWaterfallNextTwice; violation surfaced even if swallowed |
| TestP1WaterfallCancellation | W-10 cancel before next(): no downstream; ctx.Err distinct from short circuit |
| TestP1WaterfallSnapshotRegistration | W-11/T-04 mid-dispatch registration -> next dispatch only |
| TestP1WaterfallSnapshotUnregistration | W-12 mid-dispatch dispose; member still runs; next dispatch excludes |
| TestP1WaterfallScopeVisibility | W-13/T-03 scope ancestor/current/sibling |
| TestP1WaterfallEffectDisposal | W-14/T-01/T-02 dispose; handler gone; no residue |
| TestP1WaterfallReentrant | W-15 reentrant Waterfall completes inner dispatch first |
| TestP1WaterfallNested | W-16 nested one level deeper; outer chain resumes after inner |
| TestP1WaterfallPanicContained | W-17 unified containment; chain terminates; runtime survives |
| TestP1WaterfallDeterministicChain | W-18/T-05 1000 dispatches identical A->B->C |
| TestP1WaterfallNoHandlerLeak | W-19 repeated activate/waterfall/dispose; residue 0 |
| TestP1WaterfallPlainHandlersTransparent | W-16-mixed §16: ordinary On = transparent node (auto-next) |
| TestP1WaterfallHandlerSharedAcrossDispatchModes | §16: chain-aware handler = terminal node under Emit/Serial/Parallel |
| TestP1WaterfallGuardRails | rails: nil ctx / zero key / nil handler rejected |
| TestP1WaterfallConcurrentRegistrationSafe | W-20 concurrency: register + Waterfall race-free |
| TestP1WaterfallConcurrentDisposalSafe | W-20 concurrency: dispose + Waterfall race-free |

### Gap closure (2 tests; review G-1/G-2)

| Test function | Covers |
| ------------- | ------ |
| TestP1ReviewGap1GenerationEventRegistration | G-1 generation: dispose->reload; A handlers (On+OnWaterfall) never fire in B (Emit+Waterfall); residue 0 after each generation |
| TestP1ReviewGap2DependencyLossUnregistersHandler | G-2 dependency: provider withdrawal unloads consumer; event handler gone (stale == 0, residue 0); not Failed |

Category audit: basic / ordering / scope / snapshot / registration /
unregistration / ownership / dispose / cancellation / error / panic /
reentrancy / nested dispatch / concurrency / race / generation / lifecycle
are all covered at the function level above.

## 6. Generation Correctness (spec §22)

Mechanism: `eventOwnerActive` requires StateActive AND
`f.activation.id == owner.ActivationID`, so handlers of a replaced activation
generation are never in a dispatch snapshot; physical unregistration runs at
effect unwind. Covered dynamically by G-1 (event level) and by the C3
activation-generation tests (Kernel level).

## 7. Dependency Interaction (spec §21)

Event registrations are ordinary activation Effects; provider withdrawal
unwinds the dependent consumer's activation through existing
Reconciliation/Effect machinery, running the registration inverse. Covered
dynamically by G-2. No "provider gone but handler remains" path exists.

## 8. Required Commands

| Command | Result |
| ------- | ------ |
| `go vet ./...` | PASS |
| `go test ./...` | PASS (all packages) |
| `go test -race ./runtime -run TestP1 -count=1` | PASS (includes the 2 gap tests) |
| `go test -race ./... -count=1` | PASS (single full run) |

## 9. Flake Documentation (spec §27)

- Name: `TestC3T63RandomizedSchedule` (subtest `seed-3`, occasionally other
  seeds) — reproduced under
  `go test -race ./runtime -run 'TestUI02ScopeHierarchyIsolation|TestC3T63RandomizedSchedule' -count=5`
  (failure inside the C3 deterministic driver helper; `enabled=[...]
  pending=3`).
- Relation to P1: NONE. The scenario registers provider/consumer fibers and
  `ctx.Effect` (kind Custom) only; it never uses Events. P1 changes do not
  touch the orchestrator/fiber state machine/reconciliation, and existing
  EffectKind numeric values are unchanged (`Custom=0`, `Provider=1`;
  `Event=2` appended).
- Proof not introduced by this stage: `c3_t63_ordering_test.go` (first added
  `07306e4`) and `ui02_snapshot_test.go` (`ed92fd8`) both predate P1.1
  (`d1c7ccf`); the same flake mode was baseline-reproduced and accepted in
  earlier review rounds.
- `TestUI02ScopeHierarchyIsolation` did not reproduce in the most recent run;
  it is the second previously documented baseline flake (UI-02 suite,
  predating P1).
- No test was deleted or weakened to pass the gate.

## 10. FAIL-List Check (spec §28)

No second event registry, no stale handler, no scope leak, no
activation-ownership violation, no new race, no fire-and-forget Parallel, no
asynchronous/duplicated Waterfall next, no panic escaping the Runtime, no
direct lifecycle modification by Event dispatch: none observed.

## 11. Change Traceability

- `28eece4` — P1.4 Waterfall (audit baseline).
- `6e7edc5` — P2.1 plugin boundary conformance tests (no runtime delta).
- `f630c26` — this review, initial CONDITIONAL PASS.
- `b087db2` — G-1/G-2 gap-closure tests (test-only).
- This revision — verdict upgraded to PASS.
