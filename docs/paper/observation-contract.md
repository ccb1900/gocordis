# Observation contract (paper §3.3.2 / Def 31–37, Thm 80)

`ObservationEquivalence` is an **engineering projection** of the paper's `≃K`,
not a claim to have implemented the full mathematical relation. This document
fixes exactly which dimensions are observed and why that is sufficient for the
recovery/confluence claims the repository makes.

| Dimension | Observed? | Where | Why |
| --- | --- | --- | --- |
| Component name | yes | `FiberSnapshot.Name` | Stable identity of the declared component across schedules. |
| Fiber state | yes | `FiberSnapshot.State` | Pending/Loading/Active/Unloading/Failed/Gone are the lifecycle observations. |
| Declared requires/provides | yes | `CapturedRequires/CapturedProvides` | The coeffect declaration is part of the observable contract (Def 51–56). |
| Committed provider bindings | yes | `BoundProviders`, provider ids rewritten to names | Binding identity matters (Thm70) but generative fiber ids do not. |
| Failure type + message | yes | `Failure`, `FailureType` | Two failures with the same text but different causes must not compare equal. |
| Failed inverse (cleanup) records | yes | `CleanupIncomplete`, `CleanupFailures` | A failed unwind is never clean recovery (P0.5). |
| Effect failure attributes | yes | `EffectFailures` projected to slot/label/install/inverse/type/message | Attribution is observable; exception object identity is not. |
| Fiber Id, Generation, Revision, ParentId, ActivationId | no | deliberately ignored | Generative episode counters; two equivalent schedules may retry a consumer a different number of times. |
| Provided values | no | deliberately ignored | Value equality belongs to the capability contract (`Key<T>` / `VersionedKey<T>`), not to `≃K` over control state. |

## Evidence that the projection is total and honest

- `ObservationEquivalenceTests.EquivalentIsTotalReflexiveSymmetricAndTransitiveOverAllFiberStates`
- `ObservationEquivalenceTests.GenerativeIdentityAndRevisionAreNotObservable`
- `ObservationEquivalenceTests.FailedFiberIsNotEquivalentToCleanFiberEvenWithTheSameMessage`
- `ObservationEquivalenceTests.FailedUnwindIsNeverEquivalentToCleanRecovery`
- `ObservationEquivalenceTests.EffectFailureAttributesArePartOfTheProjection`
- `ObservationEquivalenceTests.Thm80NormalFormComparisonUsesDef76ProjectionAndExcludesFailedFibers`

## Limits we do not paper over

- No proof that this projection equals the paper's full `≃K` for arbitrary
  user-defined key semantics; the paper itself scopes equivalence to the keys
  in `K` that the system can reify.
- Failed fibers are excluded from Thm80 normal-form comparison by the projection;
  there is no separate "exclude failed" routine.
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
