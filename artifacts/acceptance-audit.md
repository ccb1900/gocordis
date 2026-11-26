# Completion audit

Objective: harden the industrial execution/reliability chain, then close P1 test/release quality with a unified fact source.

| Requirement | Evidence | Status |
|---|---|---|
| Published Elsa Flowchart execution, pinned versions, restart-inspectable instance | `ElsaPocTests` + `ElsaPersistedExecution.cs` | Pass |
| Per-node real Elsa activities and graph-edge data flow | `IndustrialActivities.cs`, `PublishedNodeExecutor` | Pass |
| Atomic delivery claims with token, lease and conditional completion/failure | `CollectionLedger.cs`, `DeliveryLeaseTests.cs` | Pass |
| Expired/abandoned lease recovery, concurrent claims and lease renewal | `RecoverExpiredLeasesAsync`, `RecoverAbandonedLeasesAsync`, `RenewDeliveryLeaseAsync`, `DeliveryLeaseTests.cs` | Pass |
| Real OS process kill mid-write → restart → only incomplete delivery retried | `CrashRecoveryTests.RealProcessKillRecoversOnlyTheIncompleteDelivery`, `Industrial.ProcessCrashFixture`, `ElsaPersistedExecution.RunAsync` | Pass |
| Two-target mid-write crash → restart → only incomplete delivery retried | `CrashRecoveryTests.cs`, `ElsaPersistedExecution.RunAsync`, startup recovery effect | Pass |
| Staged file identity revalidated before reuse | `StagedFileIdentity.cs`, `RecoveryTests.cs` | Pass |
| Node execution diagnostics in designer | `NodeExecutionView`, `industrial/execution`, `dto.ts`, `CordisDesigner.tsx` | Pass |
| Cross-run delivery semantics and TargetId retry | `CollectionRunDeliveryEntity`, `CollectionCoordinator.RetryFailedAsync` | Pass |
| Resource-scoped file identity and retry relocation | `FileIdentityFactory`, `LocateAsync` | Pass |
| Persisted stage data plane (FileBatchRef/DatasetRef/TransformResultRef) | `StageOutputStore.cs`, `StageOutputStoreTests.cs` | Pass |
| Sink/archive stage cache only after successful delivery | `PublishedNodeExecutor.ExecuteSinkAsync/ExecuteArchiveAsync` | Pass |
| Node configuration reaches runtime (CSV/Excel/Transform/Sink) | `NodeConfigReader`, `CsvRecordReaderConfigTests`, `PublishedNodeExecutor`, `SqlServerRecordSink` | Pass |
| Browser user creates named workflow from the list | `WorkflowList.tsx`, `industrial/workflows` block, `user-stories.spec.ts` | Pass |
| Browser user configures/tests source and target resources | `ResourceConsole.tsx`, `ResourceConnectionTester`, `industrial/test-resource`, `user-stories.spec.ts` | Pass |
| Browser user manages credentials without exposing secrets | `CredentialConsole.tsx`, `industrial/save-credential`, `industrial/rotate-credential`, `user-stories.spec.ts` | Pass |
| Execution center shows ledger runs/deliveries with target-level retry | `ExecutionCenter.tsx`, `industrial/execution-center`, `industrial/retry-failed`, `user-stories.spec.ts` | Pass |
| Node configuration schemas and typed validation | `IndustrialNodesComponent.cs`, `WorkflowValidator.cs`, `NodeConfigReader.cs` | Pass |
| Validation strategies and ProcessingResult statistics | `ValidationTransform.cs`, `ProcessingResult`, ledger statistics columns | Pass |
| Normalized ref-counted WNet UNC leases with credential identity (native seam) | `UncConnectionLease.cs`, `UncLeaseTests.cs`, real Windows runner row below | Pass |
| EF migrations for all industrial stores | `Migrations/Ledger`, `Migrations/Target`, `Migrations/Stages`, design-time factories | Pass |
| Disconnect/large-file recovery tests | `RecoveryTests.cs` | Pass |
| Unified test report fact source | `tools/release/test-report.sh`, `report.json`, `report.md` | Pass |
| Windows UNC/SMB real-share acceptance | `.github/workflows/windows-unc.yml`, `e2e-windows-unc.ps1`, `artifacts/windows-unc-result.json`, passing run https://github.com/ccb1900/gocordis/actions/runs/45739397237 | Pass on real `windows-latest` runner with a real SMB share |

Unified report: 900 passed, 3 explicitly skipped, 0 failed suites. Windows UNC acceptance is `passed` with the external `windows-latest` run above; the local macOS suite row remains explicitly skipped and is not used as acceptance evidence.

External runner evidence: `artifacts/windows-unc-result.json`; historic probe notes: `artifacts/windows-unc-blocker.md`.
