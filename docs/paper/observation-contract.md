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
