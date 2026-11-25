# Observation contract (paper §3.3.2 / Def 31–37, Thm 80)

`ObservationEquivalence` is an **engineering projection** of the paper's `≃K`,
not a claim to have implemented the full mathematical relation. This document
fixes exactly which dimensions are observed and why that is sufficient for the
recovery/confluence claims the repository makes.

| Dimension | Observed? | Where | Why |
| --- | --- | --- | --- |
| Component name | yes | `FiberSnapshot.Name` | Stable identity of the declared component across schedules. |
| Fiber state | yes | `FiberSnapshot.State` | Pending/Loading/Active/Unloading/Failed/Gone are the lifecycle observations. |
| Declared requires/provides | yes | `Requires/Provides` names plus `RequiredKeys/ProvidedKeys` reference identities | The coeffect declaration is part of the observable contract (Def 51–56). A name alone is NOT a capability: Key instance identity, `ValueType` and key kind are compared too, so same-name/different-instance or same-name/different-type never compare equal. |
| Home realm + per-key visibility | yes | `HomeRealm`, `Routes` (key → realm, both by reference identity) | ADR-0001: no ancestor fallback. Two fibers with different realms or different per-key routes are not observationally equal. |
| Committed provider bindings | yes | `SemanticBindings`: key identity + provider name/state/home realm/declared key identities | Binding identity matters (Thm70) but generative fiber ids do not. Rewriting a provider id to its name only is insufficient when several fibers share a component name. |
| Failure type + message | yes | `Failure`, `FailureType` | Two failures with the same text but different causes must not compare equal. |
| Failed inverse (cleanup) records | yes | `CleanupIncomplete`, per-fiber `CleanupFailures` | A failed unwind is never clean recovery (P0.5). |
| Effect failure attributes | yes | per-fiber `EffectFailures` projected to slot/label/install/inverse/type/message | Attribution is observable; exception object identity is not. |
| Runtime-wide failure ledgers | yes | `RuntimeSnapshot.CleanupFailures` and `.EffectFailures` | Failure evidence survives fiber removal: a Gone fiber leaves no live row but its cleanup/effect failures remain observable and must break equivalence with a clean run. |
| Fiber Id, Generation, Revision, ParentId, ActivationId | no | deliberately ignored | Generative episode counters; two equivalent schedules may retry a consumer a different number of times. |
| Provided values | no | deliberately ignored | Value equality belongs to the capability contract (`Key<T>` / `VersionedKey<T>`), not to `≃K` over control state. |

Fibers are sorted by their **full semantic projection** (name, state, declarations with key identities, realm/routes, bindings with provider identity, failures), never by name alone. Key/realm references are tokenized in a deterministic pre-walk of each snapshot, so repeated component names and different input order leave the comparison unchanged.

## Declaration authority in `Context` (paper §5.1.4)

`Context` is the single accessor surface, and declaration checks are part of its contract, not a fallback:

- `Require` and `OnAsync` require the `EventKey`/`Key` in `Inject()`. `EmitAsync` requires the `EventKey` in `Inject()` **or** `Provide()`: using another provider's event is a coeffect, while emitting on a key this component owns is an authorized use of its declared channel and must not create a false self-dependency.
- `DeclareMeta` requires the `MetaKey` in `Inject()`. `Meta` reads and `OverrideMeta` writes require the `MetaKey` in `Inject()` **or** `Provide()`; metadata is read-time interception policy, but there is no blanket "runtime metadata is exempt" back door.
- Every undeclared access throws `UndeclaredCapabilityException`. Captured declarations at Fiber creation are authoritative (a component cannot widen them mid-life).

A compensation that fails while an effect is being diverted is recorded twice: as an `EffectFailureRecord` (`isInstall = false`) and as a global `CleanupFailureRecord`; the slot becomes `Failed` and the later LIFO unwind skips it. The fiber therefore never presents a failed compensation as a clean `Undone`/`Pending` outcome.

## Evidence that the projection is total and honest

- `ObservationEquivalenceTests.EquivalentIsTotalReflexiveSymmetricAndTransitiveOverAllFiberStates`
- `ObservationEquivalenceTests.GenerativeIdentityAndRevisionAreNotObservable`
- `ObservationEquivalenceTests.FailedFiberIsNotEquivalentToCleanFiberEvenWithTheSameMessage`
- `ObservationEquivalenceTests.FailedUnwindIsNeverEquivalentToCleanRecovery`
- `ObservationEquivalenceTests.EffectFailureAttributesArePartOfTheProjection`
- `ObservationEquivalenceTests.Thm80NormalFormComparisonUsesDef76ProjectionAndExcludesFailedFibers`
- `ObservationEquivalenceTests.GlobalFailuresSurviveFiberRemovalAndBreakEquivalence`
- `ObservationEquivalenceTests.SameNameDifferentKeyInstanceIsNotEquivalent`
- `ObservationEquivalenceTests.SameNameDifferentValueTypeIsNotEquivalent`
- `ObservationEquivalenceTests.DuplicateComponentNamesAreOrderIndependentButKeepTheirSemanticProjection`
- `ObservationEquivalenceTests.DifferentRealmAndPerKeyVisibilityAreNotEquivalent`
- `ObservationEquivalenceTests.BoundProvidersCompareFullProviderSemanticIdentityNotJustName`
- `SmallStateExplorationTests.ConcurrentApplyAndDisposeInterleavingsAreRepeatableAndResidueFree`
- `InverseFailureTests.DivertInverseFailureRunsOnceRecordsBothFailuresAndNeverLooksClean`
- `InverseFailureTests.DivertInverseFailureOnDependencyLossDoesNotPretendCleanRecovery`
- `CoreSemanticsTests.UndeclaredMetaReadAndOverrideFailWhileDeclaredAccessKeepsWorking`
- `CoreSemanticsTests.UndeclaredEmitFailsWhileDeclaredInjectStillDispatches`

## Limits we do not paper over

- No proof that this projection equals the paper's full `≃K` for arbitrary
  user-defined key semantics; the paper itself scopes equivalence to the keys
  in `K` that the system can reify.
- Thm80 normal forms do not drop failed fibers: `State`, `FailureType`, `Failure`
  and the failure ledgers are compared, so a failed endpoint cannot be sawn off
  to make two schedules look equal. There is no separate "exclude failed" routine.
- Runtime-derived state is never persisted (`Snapshot()` is read-only), so the
  equivalence is evaluated over live observations only.

## Sequence-anchored observation stream (`ObservationLog<T>`)

This is the consumer-facing half of the observation contract (the .NET
counterpart of Go's `observe.Observer`, feeding console/fleet/tooling clients).
`docs/paper/observation-contract.md` fixes the anchor semantics so a
snapshot→subscribe resume cannot silently skip or duplicate an event:

- `Subscribe(from)` treats `from` as **the last sequence the consumer has
  already consumed** — normally a runtime snapshot's `EventSequence`. It
  replays retained events with `Sequence > from` in order, then streams live
  events. The anchor itself is never delivered: with a retained ring of `2,3`,
  `Subscribe(1)` replays `2,3`, `Subscribe(2)` replays `3`, and
  `Subscribe(LatestSequence)` is live-only.
- `from = 0` is the "oldest retained" sentinel and replays the entire ring. It
  is never a stale anchor.
- An anchor older than the event immediately preceding the ring
  (`from < OldestSequence - 1`) fails with `SequenceTooOldException`: the
  dropped prefix left a gap that cannot be replayed, so the consumer must
  re-snapshot and re-anchor at `LatestSequence`. A one-behind anchor is valid
  (the missing event was already consumed).
- Delivery inside one subscription episode is at most once and in Sequence
  order. The ring and each subscription queue are bounded; a subscriber that
  falls behind its live queue limit is terminated with
  `SubscriptionOverflowException` instead of blocking the emitter. Replay is
  spooled in full before live fan-out and is never subject to overflow.
- Async delivery (`EventsAsync`) is notification-driven by a bounded
  `System.Threading.Channels` channel; there is no polling delay. Draining via
  `TryRead`, overflow termination, error and closed/end-of-stream semantics are
  unchanged. Disposing the log completes its active subscriptions normally.

Evidence: `ObservationBoundaryTests` (anchor contract, replay larger than the
live queue, log disposal).
