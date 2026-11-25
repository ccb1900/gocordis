# Industrial application — current state (new tree only)

Baseline: current `dotnet` branch HEAD; this document intentionally records no
SHA (a historical SHA would be stale after the next commit). The Cordis
framework was not modified for industrial logic; all product code lives under
`industrial/`.

| Capability | State | Evidence |
| --- | --- | --- |
| Additive solution and mature dependencies | Implemented | `Industrial.slnx`; CsvHelper, EF Core/SQLite, Microsoft.Data.SqlClient, ExcelDataReader, Npgsql, MySqlConnector, Elsa 3, OpenTelemetry |
| Resource registry / credential vault / connection lease | Implemented | `ResourceRegistry`, `CredentialVault`, `UncConnectionLease`, `CollectionPipelineTests` |
| Workflow Definition single source + 13 validation gates | Implemented | `WorkflowDefinition`, `WorkflowValidator`, `WorkflowValidationTests` |
| Publish conversion to Elsa definition + graph execution | Implemented | `ElsaDefinitionConverter`, `ElsaWorkflowExecutor`, `ElsaPocTests` |
| Cordis-native G6 designer (console facade + block renderer) | Implemented | `industrial/web/src/module.ts` registers `industrial-workflow-canvas`; backend page declares declarative `views`; `designer-block.test.tsx` renders palette and drop interaction; no iframe/second app shell |
| Reader plugin extension (scenario A) | Implemented | `IndustrialJsonReaderComponent` adds palette node + `.json` broker route; `IndustrialReaderPluginTests` saves/publishes/runs through product commands and withdraws the node on disable |
| Shared resource/credential (scenario B) | Implemented | `IRecordSinkFactory` broker resolves target resources; `SqlServerSinkFactory.BuildConnectionStringAsync` reads the secret from the vault; `IndustrialResourceSharingTests` runs two workflows with no secret in workflow JSON |
| Web UI workflow creation (scenario C) | Implemented | Drag/connect/configure/save/validate/publish/run are exercised by frontend block tests plus C# command tests; transforms run in the product reader pipeline |
| CSV → mapping/validation → sink → archive (scenario D) | Implemented | `WorkflowTransformReader`, resource-aware sinks, `FileArchiveSink`; `industrial/deliveries` query exposes per-target status |
| Failure, retry, restart recovery (scenario E) | Implemented | `BoundedRetry`, ledger-only failure rows, `RetryFailedAsync`; new coordinator instance resumes from ledger in tests |
| Historical backfill (scenario F) | Implemented | Corrected `HistoricalRangePlanner` scans the last N days; ledger identity prevents duplicate business rows, covered by offline/backfill test |
| UI plugin lifecycle incl. routes/navigation (scenario G) | Implemented | One `industrial.ui` plugin contributes page, views, route, navigation, client module and APIs; enable/disable withdraws all through Fiber effects |
| Packaged product loop | Implemented | `industrial/pack.sh` builds real `.gocordis-plugin` packages, installs into a fresh store and starts `Industrial.PackagedHost` |

Verification: `dotnet build Gocordis.sln` 0 warning/0 error; full solution tests
green; industrial C# tests (Contract 2, Unit 8, Integration 25, E2E 6) green;
`industrial/web` 35 frontend tests + production build green. Runs execute the
published Elsa Flowchart with a distinct real Activity per node type and an
Elsa-persisted workflow instance that can be inspected after restart; each node
reads its inputs from predecessor nodes along the published edges. The ledger
scopes identity by source resource, decides skips per target, and closes
CollectionRun state. Developer/packaged hosts share one component map. The last packaged
host was verified through `/api/ui/pages` (declarative designer view),
`/api/ui/client-modules`, `/api/query/industrial/nodes` (string port types) and
the served client-module asset. Remaining environment limits are listed in
`gap-analysis.md`.

Production `GOCORDIS_INDUSTRIAL_KEY` is mandatory; local scripts opt into the
documented development fallback only for evaluation.
