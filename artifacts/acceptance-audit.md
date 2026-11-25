# Completion audit

Objective: harden the industrial execution/reliability chain, then close P1 test/release quality with a unified fact source.

| Requirement | Evidence | Status |
|---|---|---|
| Published Elsa Flowchart execution, pinned versions, restart-inspectable instance | `ElsaPocTests` + `ElsaPersistedExecution.cs` | Pass |
| Per-node real Elsa activities and graph-edge data flow | `IndustrialActivities.cs`, `PublishedNodeExecutor` | Pass |
| Atomic delivery claims with token, lease and conditional completion/failure | `CollectionLedger.cs`, `DeliveryLeaseTests.cs` | Pass |
| Expired lease recovery and guarded run state machine | `RecoverExpiredLeasesAsync`, `FinalizeRunAsync`, `DeliveryLeaseTests.cs` | Pass |
| Cross-run delivery semantics and TargetId retry | `CollectionRunDeliveryEntity`, `CollectionCoordinator.RetryFailedAsync` | Pass |
| Resource-scoped file identity and retry relocation | `FileIdentityFactory`, `LocateAsync` | Pass |
| Persisted stage data plane (FileBatchRef/DatasetRef/TransformResultRef) | `StageOutputStore.cs`, `StageOutputStoreTests.cs` | Pass |
| Sink/archive stage cache only after successful delivery | `PublishedNodeExecutor.ExecuteSinkAsync/ExecuteArchiveAsync` | Pass |
| Node configuration schemas and typed validation | `IndustrialNodesComponent.cs`, `WorkflowValidator.cs`, `NodeConfigReader.cs` | Pass |
| Validation strategies and ProcessingResult statistics | `ValidationTransform.cs`, `ProcessingResult`, ledger statistics columns | Pass |
| Ref-counted WNet UNC leases (native seam) | `UncConnectionLease.cs`, `UncLeaseTests.cs` | Pass (unit); Windows real share still blocked |
| EF migrations for all industrial stores | `Migrations/Ledger`, `Migrations/Target`, `Migrations/Stages`, design-time factories | Pass |
| Disconnect/large-file recovery tests | `RecoveryTests.cs` | Pass |
| Unified test report fact source | `tools/release/test-report.sh`, `report.json`, `report.md` | Pass |
| Windows UNC/SMB real-share acceptance | `.github/workflows/windows-unc.yml`, `e2e-windows-unc.ps1`, blocker evidence | Blocked on this macOS host; no passing result |

Unified report: 883 passed, 3 explicitly skipped, 0 failed suites. Windows UNC remains `blocked` with `passingResult: null`.

Blocker details: `artifacts/windows-unc-blocker.md`.
