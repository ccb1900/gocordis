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
| Browser user configures triggers and cron runs automatically | `TriggerEditor.tsx`, `industrial/trigger-status`, Timer-backed scheduler, `user-stories.spec.ts` | Pass |
| Browser user previews backfill impact before execution | `BackfillPreview.tsx`, `industrial/preview-backfill`, `industrial/backfill-preview`, `user-stories.spec.ts` | Pass |
| File-level processing detail with classified failure diagnosis | `FileDetails.tsx`, `industrial/file-details`, `FailureDiagnoses`, `user-stories.spec.ts` | Pass |
| Resource edit preserves connection config and credential | `industrial/resources` query, `SaveResourceAsync`, `ResourceConsole`, browser edit test | Pass |
| Delivery retry reuses published mapping/validation and delivery scope | `RetryDeliveryAsync`, `PublishedNodeExecutor` retry scope, `CrashRecoveryTests` | Pass |
| Shared sink write options reach SQL Server/PostgreSQL/MySQL | `SinkWriteOptions`, `SinkFactories`, `ExtendedIo`, `SinkWriteOptionsTests` | Pass |
| UNC connection test uses the credentialed WNet session | `ResourceConnectionTester`, `UncConnectionLease`, fake-native unit test | Pass |
| Schedule fires are durable and timezone-aware | `ScheduleFires`, `TryClaimScheduleFireAsync`, `TriggerEditor`, `DeliveryLeaseTests` | Pass |
| Backfill preview uses the published sink target set | `PreviewBackfillAsync`, `ExpectedTargetsAsync`, `ElsaPocTests` | Pass |
| Blank-environment browser-only full user flow | `industrial/e2e-user-flow.sh`, `full-flow.spec.ts` | Pass |
| Multi-run retry resolves delivery→run→Elsa instance | `CollectionRunEntity.InstanceId`, `RetryDeliveryAsync`, `CrashRecoveryTests.DeliveryRetryResumesTheRunThatOwnsTheFailedDelivery` | Pass |
| Schedule fire lifecycle: claim/complete separation, bounded missed-fire compensation, failed-fire recovery | `ScheduleFires` status/attempts/token, `CollectionLedger`, `RunDueSchedulesAsync`, `DeliveryLeaseTests` | Pass |
| G6 native gestures in packaged browser | `industrial/web/e2e/designer-gestures.spec.ts`, `industrial/e2e-browser.sh` | Pass |
| New Sink backfills historical files to only the added target | `CrashRecoveryTests.PublishANewSinkBackfillsOnlyThatTargetForHistoricalFiles` | Pass |
| Node configuration schemas and typed validation | `IndustrialNodesComponent.cs`, `WorkflowValidator.cs`, `NodeConfigReader.cs` | Pass |
| Validation strategies and ProcessingResult statistics | `ValidationTransform.cs`, `ProcessingResult`, ledger statistics columns | Pass |
| Normalized ref-counted WNet UNC leases with credential identity (native seam) | `UncConnectionLease.cs`, `UncLeaseTests.cs`, real Windows runner row below | Pass |
| EF migrations for all industrial stores | `Migrations/Ledger`, `Migrations/Target`, `Migrations/Stages`, design-time factories | Pass |
| Disconnect/large-file recovery tests | `RecoveryTests.cs` | Pass |
| Unified test report fact source | `tools/release/test-report.sh`, `report.json`, `report.md` | Pass |
| Windows UNC/SMB real-share acceptance | `.github/workflows/windows-unc.yml`, `e2e-windows-unc.ps1`, `artifacts/windows-unc-result.json`, passing run https://github.com/ccb1900/gocordis/actions/runs/45799908846 | Pass on real `windows-latest` runner with a real SMB share |

| Same Delivery across two failing runs retries with the last attempt's instance | `FileDelivery.LastAttemptRunId`, `RetryDeliveryAsync`, `CrashRecoveryTests.RetryUsesTheLastRunThatAttemptedTheSameDelivery` | Pass |
| Low-frequency cron and superseded-version fires recover from persisted checkpoints | `ScheduleCheckpoints`, `RunDueSchedulesAsync`, `SchedulerRecoveryTests.LowFrequencyAndSupersededVersionFiresAreRecovered` | Pass |
| G6 save/reload preserves node positions and palette-added nodes | `industrial/web/e2e/designer-gestures.spec.ts` | Pass |

Unified report: 937 passed, 3 explicitly skipped, 0 failed suites, generated for tested code commit `75ce6b2cb5fda2c72694a8034f64285b638bf757` (`artifacts/test-report/report.json` records the SHA). Windows UNC acceptance is `passed` with the external `windows-latest` run above; the local macOS suite row remains explicitly skipped and is not used as acceptance evidence.

External runner evidence: `artifacts/windows-unc-result.json`; historic probe notes: `artifacts/windows-unc-blocker.md`.
