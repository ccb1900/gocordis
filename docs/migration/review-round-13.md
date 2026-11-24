# Review Round 13 — Paper-Level Semantic Closure

Review baseline: `dotnet` branch `02d0205..a3b6d8c`; code commit `559403eb`.
This round targets paper semantics, not feature count or matrix labels.

## Closed findings

### Revertible effects

- `Context.EffectAsync` divert path now runs the inverse at most once. An inverse
  failure is recorded in both `EffectFailure` and `CompensationFailure`, the slot
  becomes `Failed`, and later unwind skips it. Failed compensation is never
  represented as clean recovery.
- A `DivertedException` is treated as the paper's normal target-change signal,
  not as an install failure, so deterministic interleaving does not invent error
  records.

### Observation equivalence

- `RuntimeSnapshot` now carries a non-serialized semantic projection: captured
  inject/provide key instances, home realm, per-key routes, and committed
  bindings by provider semantic identity.
- `ObservationEquivalence.Equivalent` compares global `CleanupFailures` and
  `EffectFailures`, so a removed fiber cannot hide a failed inverse.
- Capability identity uses Key instance + value type + name + kind; same-name
  different instances and different value types are no longer equivalent.
- Sorting uses the full semantic projection, so duplicate component names are
  order-independent but distinct states remain distinct.
- `docs/paper/observation-contract.md` records the projection and its limits.

### Unified Context declaration authority

- `Meta` and `OverrideMeta` require the `MetaKey` in `Inject`/`Provide`.
- `EmitAsync` requires the `EventKey` in `Inject`.
- Showcase `StoreProviderComponent` now injects `K.StoreChanged` because its
  background writer emits it; declaration checks are real product rules, not
  test-only gates.

### HMR atomicity and recoverability

- `PluginRegistry` has a monotonic `DeclarationVersion` and an
  `ApplyAsync(declarations, expectedVersion)` overload. Version validation occurs
  inside the registry gate before staging, so stale HMR plans cannot touch a fiber.
- `PlatformApplication.HmrAsync` reads declarations and version atomically via
  `Registry.ReadDeclarations()` and carries the version through plan→commit.
- Added true commit-stage rollback evidence: first fiber activates, second Apply
  fails, third fiber activates; rollback removes all new fibers/effects/providers
  and restores the previous declaration without duplicate providers.

### Configuration tree

- Include expansion already had path normalization, cycle detection and depth
  limits. It now also rejects duplicate declaring Entry IDs with
  `DuplicateEntryException` (id + source path). Explicit flat plugin declarations
  still override an expanded entry with the same id.

### Async interleaving evidence

- `SmallStateExplorationTests.ConcurrentApplyAndDisposeInterleavingsAreRepeatableAndResidueFree`
  uses `TaskCompletionSource` gates to control two Apply and two Dispose tasks,
  with no sleep or scheduler-order assumption. It asserts observation equivalence
  across release orders, repeatability, no fiber residue and install→undo order.

### Final-state assertion

- `PaperContributionsTests` waits for the consumer's observable `Pending` state
  via `Runtime.WaitForFiberStateAsync`, not a permissive `Pending/Loading` check.

## Local verification

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 663 passed / 1 skipped / 0 failed, 3 consecutive runs
plugin-kit npm test       → 2 passed
console npm test          → 103 passed
console npm run build     → success
```

No CI result was queried. Remaining explicit boundaries: WinForms/Avalonia are
render-plan profiles, automatic file-watch HMR is not implemented, package
signatures are provenance rather than an install trust chain, and observation
identity is process-local (cross-backend identity uses the host `CapabilityCatalog`).
