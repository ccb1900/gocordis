# Gap analysis against docs/collector.md (new tree only)

Status legend: implemented / partial / not implemented / not applicable.

| Collector requirement | Status | Evidence or gap |
| --- | --- | --- |
| Cordis framework unchanged | implemented | `git status` shows only new `industrial/` files; existing solution/projects untouched |
| Five dependency layers distinguished | implemented | Contracts assembly + Cordis `Key<T>` keys; no plugin-to-plugin implementation references |
| Plugin lifecycle owned by Cordis | implemented | Domain/nodes/UI are ordinary `Component`s; UI registrations use `Context.EffectAsync` |
| Resource / Credential / Connection separated | implemented | `ResourceDefinition`, `CredentialVault`, `ConnectionLease` contract |
| Windows UNC explicit credentials | partial | Need Windows-host SMB session coordinator; error 1219 case not reproduced on Linux |
| Absence of secrets in workflow/API/log | implemented (dev key opt-in; production requires env key) | Workflow stores only `resourceId`; `CredentialView` has no secret; tests assert no leak |
| Workflow Definition single editable source | implemented | `WorkflowDefinition` persisted in EF Core; Elsa JSON is a published derivative |
| G6 editor with drag/connect | not implemented | Node descriptors and validation API ready; React designer still needed |
| 13 validation gates with node/edge paths | implemented | `WorkflowValidator`; errors located to `nodes.*`, `edges[n]`, `resourceReferences.*` |
| Elsa 3 PoC before full integration | implemented | Activity executes through Elsa; Activity catalog is a Cordis capability; Definition conversion verified; persistence/recovery still a gap |
| Historical range + file-level backfill | implemented | `HistoricalRangePlanner`, ledger identity, tests |
| source_date / record_time / collected_at | implemented | File name `yyyyMMdd`, `RecordEnvelope.RecordTime`, `CollectionFile.CollectedAt` |
| Idempotent DB writes / no duplicate retry | implemented | Unique `record_id`, `SqliteRecordSink`, repeated-run test |
| Multi-target independent delivery | implemented | `FileDelivery` per target, archive-only retry test |
| Bounded retry | implemented | `BoundedRetry` with 3 attempts and exponential backoff |
| Crash/restart recovery runner | partial | `RetryFailedAsync` retries only failed ledger targets; automatic resume-on-startup not yet wired |
| Excel / MySQL / PostgreSQL / event trigger | not implemented | Phase 6 of collector; intentionally deferred |
| Full Web UI pages (8) | partial | Six page contributions and five queries; no React-specific designer page yet |
| E2E acceptance A–G | partial | A, B (resource sharing shape), C (API-driven creation), D (local→CSV→SQLite→archive), E (archive failure), F (historical range) have tests; G has UI lifecycle test |
