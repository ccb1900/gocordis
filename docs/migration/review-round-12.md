# Review Round 12 — Registry Transaction and Product Boundary Closure

Review baseline: `dotnet` branch commits `02d0205..a3b6d8c`; current code commit `623b9991` (`d89b549c` review fixes, `1deb91a0` repeated start/stop, deterministic final-state fixes).

This round addresses the source-level findings from the six-commit review, not a
new feature batch. Every item below has a red test before the fix and a local
full-suite run after integration.

## P1 — Registry field reconciliation and transaction

- `EntryDiff.Classify` now returns a compositional `EntryDelta` instead of one
  priority label. All changed fields are recorded independently; `Enabled=false`
  can no longer be swallowed by `Config`, `Rehome`, `Intercept` or `Parent`.
- `PluginRegistry.ApplyAsync` prepares/validates the whole batch before touching
  any live fiber. One `RegistryUndo` records every touched entry, meta edit and
  created/rehome fiber. Rollback disposes in-place-edited originals and restores
  only touched entries; untouched fibers are preserved.
- Disabled entries no longer retain the retired managed component in
  `PluginEntry.Component`, so the collectible ALC can be reclaimed.
- Evidence: `EntryReconciliationTests.EntryDeltaReportsEveryChangedFieldInsteadOfOnePriorityLabel`,
  `ConfigAndDisabledRetiresTheFiberInsteadOfApplyingConfigInPlace`,
  `RehomeAndConfigReinsertsOnceWithTheNewlyDeclaredConfig`,
  `InterceptAndConfigAreAppliedTogetherWithoutReplacingTheFiber`,
  `FailureDuringInPlaceConfigRollsBackAlreadyAppliedEdits`,
  `CommitFailureAfterInPlaceProviderRestoresCapabilityWithoutDuplicateOrOrphan`,
  `ConfigurationTreeTests.DisableWithConfigChangeThroughTheLoaderRetiresTheFiber`.

## P1 — HMR and Process security boundaries

- HMR replacement sources are resolved segment by segment (symlinks and Windows
  reparse points), compared to the real store root, re-checked for drift, and
  handed to the engine as the resolved path. `external -> /outside` inside the
  store can no longer satisfy containment.
- Process children start with an empty environment by default. Only
  `DOTNET_ROOT`, `DOTNET_ROLL_FORWARD`, Windows `SystemRoot`, host-allowlisted
  parent variables and explicit injected values are admitted. A secret deny-list
  is no longer the boundary.
- Evidence: `PlatformApplicationTests.HmrRejectsSourceBehindASymlinkedIntermediateDirectory`,
  `HmrRejectsDotDotTraversalThroughASymlinkedDirectory`,
  `HmrRejectsSourceWhoseFinalEntryIsASymlinkToOutside`,
  `ProcessPermissionPolicyTests.SpawnedChildDoesNotInheritParentSecrets`.

## P1/P2 — Observation, Include and HMR cancellation

- Observation `from` is explicitly the last-consumed sequence; replay is
  `Sequence > from`. Delivery uses bounded `System.Threading.Channels` instead
  of `Task.Delay(1)` polling.
- `ApplicationLoader.EffectivePluginsAsync` normalizes Include paths, detects
  cycles with a readable chain, enforces a depth limit, expands duplicates once
  and keeps explicit flat declarations authoritative.
- HMR cancellation is preflight-only: it decides whether the single registry
  transaction is entered, never how that transaction proceeds.
- Evidence: `ObservationBoundaryTests`, `IncludeCycleTests`, `HmrCancellationTests`,
  `BrokerObserveGapTests` updated to the last-consumed anchor contract.

## Product-level regressions kept green

- `ManagedPluginProductE2ETests`: payload-root fingerprints keep staged ALC
  identities distinct when HMR replaces bytes at the same id/version.
- `KoishiPackagedProductTests`, `SharedContractPackagingTests`,
  `TransitiveContractProductTests`: shared-contract and HMR product paths.
- `PlatformApplicationTests.RepeatedStartStopLeavesNoFiberOrListenerResidue`:
  four start/stop/dispose rounds with no Fiber residue.
- `PaperContributionsTests` now awaits `Runtime.QuiesceAsync()` and asserts the
  exact final `Pending` state instead of accepting `Pending` or `Loading`.
- `global.json` now uses `latestMajor` so a local SDK 11 can run net10.0 tests
  while CI still installs the pinned 10.0.115 SDK via `global-json-file`.

## Local verification

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 646 passed / 1 skipped / 0 failed, 3 consecutive runs
plugin-kit npm test       → 2 passed
console npm test          → 103 passed
console npm run build     → success
```

No CI result was queried. Remaining product boundaries (native desktop hosts,
automatic file-watch HMR, install-time trust chain) remain explicitly deferred;
they are not reclassified as done by this round.
