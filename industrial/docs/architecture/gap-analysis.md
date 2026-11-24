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

## Post-review organization verdict (multi-round, 2026-11-25)

**Framework / paper semantics:** the layer organization is correct after fixes —
no industrial code in Cordis Core, one Runtime/Registry, contracts separated,
all business capabilities bound through `Key<T>`, UI pages/API registered as
Fiber effects, product host uses real `.gocordis-plugin` packages, and
`pack.sh` now declares `--provides` / `--dependency` so package dependencies
are no longer collapsed into capability declarations. Contribution owners are
bound to the actual Fiber/Activation, and the Elsa catalog inverse restores
the previous catalog rather than replacing it with an empty one.

**Collector spec:** still only **partially** conforming. Honest gaps:

- No React/Ant Design/AntV G6 frontend source under `industrial/`. Pages now use
  declaratively correct view-block arrays with matching columns and registered
  `table` renderers with matching columns, dashboard rows use an array query,
  default resource is seeded by the domain plugin, and page actions have configured defaults; the workflow
  designer is still not a G6 canvas, so collector §7.2–7.5, §9.1–9.3 are not met.
- Elsa is wired into the product publish chain (`industrial/publish-workflow`
  command → `ElsaDefinitionConverter` → `WorkflowRegistry.PublishAsync`), the
  published artifact retains definition/edge provenance, and an Activity really
  executes in the PoC. The collection run command still invokes
  `CollectionCoordinator` directly; Elsa does not execute the workflow graph in
  production, so collector §7.8 is only PoC-level.
- Plugin taxonomy is coarser than collector §5.1: `industrial.domain` provides
  twelve capabilities in one Fiber; there are no separate persistence /
  resource / credential / workflow / execution plugins.
- `ConnectionLease` has no implementation; UNC shares reuse the local file
  source, with no Windows SMB session/credential coordinator (§6.3–6.4).
- One EF Core context owns all business tables; published versions replace the
  previous published row instead of retaining history (§10, §7.8(4)).
- Tests are one project without unit/integration/contract/E2E separation and
  without browser/G6 tests (§12.4).
- `ExcelDataReader`, `Npgsql`, `MySqlConnector` and OpenTelemetry are not used;
  SQL Server sink is selectable via `GOCORDIS_INDUSTRIAL_SINK` /
  `GOCORDIS_INDUSTRIAL_CONNECTION_STRING` (packaged managed loading uses the
  parameterless constructor, so control-plane config edits are not delivered to
  components) and has no live-server test (§4.1).

These gaps are recorded, not claimed as complete. The organization is sound at
the framework level; the full industrial product surface defined by the
collector is larger than the delivered vertical slice.
