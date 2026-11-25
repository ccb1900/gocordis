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

## Post-review organization verdict (2026-11-25, after closing gaps)

**Framework / paper semantics: conforming.** One Runtime/Registry; no industrial
logic in Core; contracts separated; every business capability bound through
`Cordis Key<T>`; UI pages, client module, queries and commands registered as
Fiber effects and withdrawn in LIFO order; product host uses real
`.gocordis-plugin` packages with shared-contract sealing and declared
`--provides` / `--dependency`. No ALC sandbox claim, no second lifecycle.

**Collector spec: the implementation gaps were closed.**
- New independent React + TypeScript + Ant Design + AntV G6 designer under
  `industrial/web/` (19 frontend tests, production build) contributed as a
  Cordis client module by `IndustrialClientModuleComponent`.
- Elsa now executes the workflow graph from the product command
  `industrial/run-workflow`, which the G6 designer calls; the designer saves
  through `industrial/save-workflow`. Node data work is delegated to Cordis
  capabilities, publish retains definition/edge provenance and prior versions
  are queryable through `industrial/workflow-versions`, which the G6 designer
  also displays.
- Platform plugins are split into resource / credential / workflow / execution /
  collection, plus nodes / elsa / ui / clientmodule, each independently packaged.
- `UncConnectionLease` is integrated through `RoutingFileSource` with a
  per-share session cache (password-aware key, effect-bound release) and real
  CSV/XLSX discovery on the UNC path. `RoutingRecordReader` makes ExcelDataReader reachable
  for `.xlsx`. Npgsql / MySqlConnector sinks select by configuration.
  OpenTelemetry is instrumented with `ActivitySource` and covered by a listener
  test.
- Published workflow rows are retained as history (only the active flag moves)
  and surfaced through a query.
- Tests are split into Contract / Unit / Integration / E2E projects.

**Remaining honest limits (not implementation gaps):**
- Managed plugin loading uses parameterless constructors, so packaged declaration
  config is delivered through environment variables, not live control-plane
  config edits.
- UNC session lease is Windows-only and cannot be exercised on this Linux host.
- SQL Server / PostgreSQL / MySQL sinks are implemented with mature drivers but
  have no live-server integration test; SQLite is the tested target.
- The G6 designer has DTO/graph-model unit tests and a production build, not a
  browser-driven E2E test.

Verification at this point: `dotnet build industrial/Industrial.slnx` 0 warning/0 error;
25 C# tests across four projects pass; `industrial/web` 19 tests + build pass;
full packaged product re-verified via `pack.sh`.
