# Gap analysis against docs/collector.md (new tree only)

Status legend: implemented / partial / not implemented / not applicable.

| Collector requirement | Status | Evidence or honest limit |
| --- | --- | --- |
| Cordis framework unchanged | implemented | All product code is under `industrial/`; only additive project references were added |
| Five dependency layers distinguished | implemented | shared `Industrial.Contracts`, reference-identity `Key<T>`, package `--provides/--dependency`; no plugin→plugin implementation imports |
| Plugin lifecycle owned by Cordis | implemented | Every capability, UI page/view/route/navigation/client module and API is a Fiber effect; disabling withdraws in LIFO order |
| Resource / Credential / Connection separated | implemented | `ResourceDefinition` stores ids and config; secrets live only in `CredentialVault`; `UncConnectionLease` keys sessions by share + credential hash |
| Windows UNC explicit credentials | implemented (Windows runtime only) | `RoutingFileSource` + `UncConnectionLease`; Linux host cannot exercise SMB 1219, so no Windows integration test here |
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
| Bounded retry and restart resume | implemented | `BoundedRetry`, ledger failure rows, fresh coordinator resumes via `RetryFailedAsync`; automatic resume-on-startup is not wired (manual retry command remains) |
| Excel / MySQL / PostgreSQL sinks | implemented (no live-server test) | Mature drivers (`ExcelDataReader`, `Npgsql`, `MySqlConnector`); SQLite is the executable target in CI |
| Full Web UI pages and lifecycle | implemented | Nine page contributions, declarative views, routes, navigation and one client module, all owned by `industrial.ui` |
| E2E acceptance A–G | implemented in executable tests | `IndustrialReaderPluginTests`, `IndustrialResourceSharingTests`, `IndustrialUiPluginTests`, `CollectionPipelineTests`, `designer-block.test.tsx` |

## Post-review organization verdict (2026-11-25, after closing gaps)

**Framework / paper semantics: conforming.** One Runtime/Registry; no industrial
logic in Core; contracts separated; every business capability bound through
`Cordis Key<T>`; UI pages, views, routes, navigation, client module, queries and
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
- UNC session lease is Windows-only and cannot be exercised on this Linux host.
- SQL Server / PostgreSQL / MySQL sinks are implemented with mature drivers but
  have no live-server integration test; SQLite is the tested target.
- The G6 designer has a jsdom integration test for facade registration, palette
  rendering and drop interaction, but not a real-browser screenshot test.
- Retry after restart is proven by a fresh coordinator reading the ledger; an
  automatic startup sweep is intentionally not added (retry stays an explicit
  command, avoiding a self-spinning loop).

Verification: `dotnet build Gocordis.sln` 0 warning/0 error; full solution tests
green; industrial C# 31 tests green; `industrial/web` 21 tests + build green;
packaged host re-verified through the UI composition and query endpoints.
