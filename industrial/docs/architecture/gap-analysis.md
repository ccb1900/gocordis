# Gap analysis against docs/collector.md (new tree only)

Status legend: implemented / partial / not implemented / not applicable.

| Collector requirement | Status | Evidence or honest limit |
| --- | --- | --- |
| Cordis framework unchanged | implemented | All product code is under `industrial/`; only additive project references were added |
| Five dependency layers distinguished | implemented | shared `Industrial.Contracts`, reference-identity `Key<T>`, package `--provides/--dependency`; no plugin→plugin implementation imports |
| Plugin lifecycle owned by Cordis | implemented | Every capability, UI page/view/route/navigation/client module and API is a Fiber effect; disabling withdraws in LIFO order |
| Resource / Credential / Connection separated | implemented | `ResourceDefinition` stores ids and config; secrets live only in `CredentialVault`; `UncConnectionLease` keys sessions by share + credential hash |
| Windows UNC explicit credentials | implemented and accepted on Windows runner | `RoutingFileSource` + `UncConnectionLease`; real SMB share verified on `windows-latest` (see current-state evidence) |
| Absence of secrets in workflow/API/log | implemented | Node config carries `targetResourceId`; `SqlServerSinkFactory` reveals from vault; test asserts workflow JSON and node config contain no secret |
| Workflow Definition single editable source | implemented | JSON/Elsa derivative is published output only; the designer edits `WorkflowDefinition` |
| G6 editor with drag/connect | implemented | `industrial/web` module registers `industrial-workflow-canvas`; palette drop, port validation, property panel, save/validate/publish/run toolbar |
| 13 validation gates with node/edge paths | implemented | `WorkflowValidator`; located issues surfaced in the property panel |
| Elsa 3 PoC before full integration | implemented | Elsa runs the graph from the real `industrial/run-workflow` command; prior versions are queryable |
| Reader plugin extension | implemented | `.json` Reader plugin is packaged separately and appears in palette without Core/designer edits |
| Historical range + file-level backfill | implemented | Planner scans last N days; ledger identity prevents duplicates across repeated backfill runs |
| source_date / record_time / collected_at | implemented | filename/date parsing, `RecordEnvelope.RecordTime`, ledger `CollectedAt` |
| Idempotent DB writes / no duplicate retry | implemented | unique `record_id`, per-target delivery idempotency key |
| Multi-target independent delivery | implemented | `FileDelivery` per target, `industrial/deliveries` query, archive-only retry |
| Bounded retry and restart resume | implemented | `BoundedRetry`, token-fenced ledger claims, startup resumes the original Elsa instance from reusable stages; real OS process-kill test proves only incomplete targets are retried |
| Excel / MySQL / PostgreSQL sinks | implemented (no live-server test) | Mature drivers (`ExcelDataReader`, `Npgsql`, `MySqlConnector`); SQLite is the executable target in CI |
| Full Web UI pages and lifecycle | implemented | Nine page contributions, declarative views, routes and navigation owned by `industrial.ui`; the client module is shipped inside the `industrial.ui` package and bound by `UiPackageBinder` |
| E2E acceptance A–G | implemented in executable tests | `IndustrialReaderPluginTests`, `IndustrialResourceSharingTests`, `IndustrialUiPluginTests`, `CollectionPipelineTests`, `designer-block.test.tsx` |

## Post-review organization verdict (2026-11-25, after closing gaps)

**Framework / paper semantics: conforming.** One Runtime/Registry; no industrial
logic in Core; contracts separated; every business capability bound through
`Cordis Key<T>`; UI pages, views, routes, navigation, packaged client module, queries and
commands registered as Fiber effects and withdrawn in LIFO order; product host
uses real `.gocordis-plugin` packages with shared-contract sealing and declared
`--provides` / `--dependency`. No ALC sandbox claim, no second lifecycle.

**Collector spec: A–G have executable evidence.** The UI is a Cordis client
module: the backend contributes the page and its declarative `views`; the
browser module registers only the block renderer through the console facade and
talks to the hub through `hubQuery`/`hubCommand`. There is no iframe, no second
React/antd instance and no frontend-owned page registry.

**Remaining honest limits (environment or operational, not hidden gaps):**
- Managed packaged components use parameterless constructors, so declaration
  config is delivered through environment variables in the packaged path.
- UNC session lease is Windows-only; it is verified on a real `windows-latest`
  runner, not on this macOS/Linux host.
- SQL Server / PostgreSQL / MySQL sinks share one writeMode/businessKey contract
  and the one-delete-per-source replace rule, but only PostgreSQL and SQLite
  have live integration tests here; SQL Server/MySQL replace remains unverified
  against a real server.
- The G6 designer has a jsdom integration test using G6's SVG renderer plus a
  real Chromium E2E (`bash industrial/e2e-browser.sh`) covering resource edit
  preservation, triggered/backfill UIs, file diagnosis and a real failed-target
  repair+retry. A separate blank-environment suite (`bash industrial/e2e-user-flow.sh`)
  builds the whole graph in the browser and covers failure/repair/retry without
  API seeding. The designer also supports click-to-add nodes and an explicit
  edge form because G6's native drop gesture is not reliable in the packaged
  headless browser.
- `bash industrial/e2e-packaged.sh` builds and installs real `.gocordis-plugin`
  packages, starts `Industrial.PackagedHost`, and drives save/publish/run plus
  the execution-center query over HTTP.
- `bash industrial/e2e-database.sh` runs the PostgreSQL sink driver against a
  fresh local PostgreSQL database; SQLite is not substituted.
- Windows UNC/SMB acceptance now passes on a real `windows-latest` runner through
  `.github/workflows/windows-unc.yml` and `industrial/e2e-windows-unc.ps1`. When
  external `GOCORDIS_UNC_*` secrets are absent, the script provisions an ephemeral
  real SMB share and account on the runner and removes them after the test; see
  `artifacts/windows-unc-result.json` and the passing run
  https://github.com/ccb1900/gocordis/actions/runs/45799908846. The local
  macOS/Linux suite row still records an explicit skip. MySQL and SQL Server
  also still need external servers.
- `bash tools/release/test-report.sh` is the single fact source for all local
  suite counts (`artifacts/test-report/report.json` and `report.md`).
- Retry after restart is automatic: startup releases abandoned claims and resumes
  the persisted Elsa instance from committed stages, proven by
  `CrashRecoveryTests`; `industrial/recover-instances` remains available as an
  explicit diagnostic command.

Verification: `dotnet build Gocordis.sln` 0 warning/0 error; full solution tests
green; industrial C# 41 tests green; `industrial/web` 35 tests + build green;
packaged host re-verified through the UI composition and query endpoints. Runs
execute published Elsa Flowcharts with per-node real Activities and persisted
Elsa instances; ledger identity is resource-scoped, skips are per-target, runs
reach a terminal state, and retry is TargetId-scoped with an atomic claim.
Mapping/Validation have dedicated editors and no longer require raw JSON.
