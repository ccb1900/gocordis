# Completion audit

Objective: finish the industrial execution/reliability chain and P1 test/release quality, then backfill passing evidence into the unified test report.

| Requirement | Evidence | Status |
|---|---|---|
| P0-01 run published version, not draft | `industrial/tests/Industrial.Integration.Tests/ElsaPocTests.cs`: publish v1, save broken v2 draft, run still uses v1; version-specific query test | Pass |
| P0-01 real Elsa Flowchart from published artifact, no custom topological executor | `ElsaDefinitionConverter.ToElsaJson` persists graph+connections; `BuildWorkflow` creates `Flowchart` + `Connection`; no `TopologicalOrder`/`PersistedOrder` remains | Pass |
| P0-01 distinct real Elsa Activity per node type | `IndustrialActivities.cs` defines 12 concrete activity classes + factory; `ElsaPocTests.IndustrialDefinitionConvertsToElsaDefinition` asserts persisted `elsaActivity` and distinct types | Pass |
| P0-01 plugin-contributed work and per-edge data flow | `PublishedNodeExecutor` reads predecessor outputs along published edges; source/reader/mapping/validation/sink/archive each perform their own work; no centralized `ApplyTransforms` | Pass |
| P0-01 persisted Elsa execution instance recoverable after restart | `ElsaPersistedExecution.cs` uses Elsa EF Core/SQLite + runner; `ElsaRestoresPinnedVersionAfterProcessRestart` inspects instance in a fresh process | Pass |
| P0-02 resource-scoped file identity | `FileIdentity.SourceResourceId`, `(ResourceId, IdentityKey)` unique index; `IdenticalFilesInTwoResourcesHaveIndependentLedgerEntries` | Pass |
| P0-02 target-level skip / new target backfill | `RunAsync` has no file-level skip; `NewlyConfiguredTargetReceivesAlreadySucceededHistoricalFile` | Pass |
| P0-02 CollectionRun state machine + FinishedAt | `CompleteRunAsync`, `ListRunsAsync`, terminal status assertions | Pass |
| P0-02 TargetId retry, claim guard, failure semantics | `RetryFailedAsync(targetId, ...)`, atomic `TryClaimDeliveryAsync`, retry tests | Pass |
| P0-03 per-node schemas + structured Mapping/Validation editors | `IndustrialNodesComponent.cs`; `KeyValueEditor.tsx`, `StringListEditor.tsx`, `config-editor-model.ts` and tests | Pass |
| P1 product management APIs | Resource/Credential CRUD + rotation, Machine registry, execution center, event trigger, backfill policy | Pass |
| P1 real browser designer E2E | `bash industrial/e2e-browser.sh` → `industrial-browser-e2e.log` | Pass |
| P1 real packaged-plugin E2E | `bash industrial/e2e-packaged.sh` → `industrial-packaged-e2e.log` | Pass |
| P1 non-SQLite real database integration | `bash industrial/e2e-database.sh` → `industrial-postgresql.log` (real PostgreSQL) | Pass |
| P1 unified test report fact source | `bash tools/release/test-report.sh` → `artifacts/test-report/report.json` / `report.md` | Pass |
| P1 Windows UNC/SMB real integration | `WindowsUncIntegrationTests` prepared; `windows-unc.yml` + `e2e-windows-unc.ps1` configured; current host cannot run Windows `net use` | **Blocked / incomplete** |

Unified report result: 871 passed, 2 explicitly skipped, 0 failed suites. Windows UNC is recorded as `blocked` with `passingResult: null`; no passing result exists.

Blocker details: `artifacts/windows-unc-blocker.md`.

Conclusion: objective not complete. A Windows runner with a real SMB share and `GOCORDIS_UNC_*` secrets must execute `WindowsUncIntegrationTests` and a passing log must be added to the unified report before this objective can be considered complete.
