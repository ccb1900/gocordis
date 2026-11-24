# Industrial application — current state (new tree only)

Baseline: `dotnet` branch, HEAD at the time of this work `d9043cf`. The Cordis
framework was not modified; all files in `industrial/` are new.

| Capability | State | Evidence |
| --- | --- | --- |
| Additive solution and mature dependencies | Implemented | `Industrial.slnx`; CsvHelper, EF Core/SQLite, Microsoft.Data.SqlClient, Elsa 3; patched transitive NU1902/NU1903 versions |
| Resource registry (versioned, provider-disabled safe) | Implemented | `ResourceRegistry`, `CollectionPipelineTests` |
| Credential vault (AES-GCM, rotate, views never leak secret) | Implemented (prod requires GOCORDIS_INDUSTRIAL_KEY; dev fallback opt-in) | `CredentialVault`, `CredentialVaultNeverExposesSecretsThroughViews` |
| Workflow Definition single source + validation (13 gates) | Implemented | `WorkflowDefinition`, `WorkflowValidator`, `WorkflowValidationTests` |
| Publish conversion to Elsa definition | Implemented (PoC) | `ElsaDefinitionConverter`, `ElsaPocTests.IndustrialDefinitionConvertsToElsaDefinition` |
| Elsa 3 executes a plugin-contributed Activity | Implemented (PoC) | `CollectFileActivity`, `ElsaPocTests.ElsaExecutesAPluginContributedActivity` |
| File identity, stability, historical range | Implemented | `FileIdentityFactory`, `LocalFileSource`, `HistoricalRangePlanner` |
| File-level + target-level ledger, idempotent delivery | Implemented | `CollectionLedger`, `CollectionPipelineTests` |
| Archive failure retried without database rewrite | Implemented | `ArchiveFailureIsRetriedWithoutRepeatingDatabaseDelivery` |
| UI pages/queries/command via Cordis plugin effects | Implemented | `IndustrialUiComponent`, `IndustrialUiPluginTests` |
| Adding a node without core changes | Implemented | `IndustrialNodesComponent`, `NewNodePluginIsDiscoveredWithoutChangingCore` |
| UNC shared resource with explicit credentials | Partial | Resource shape and vault exist; Windows SMB session coordination not yet implemented on Linux host |
| SQL Server / MySQL / PostgreSQL sinks | Partial | SQL Server sink implemented; MySQL/PostgreSQL deferred to Phase 6 |
| Full G6 designer drag/connect UI | Not implemented | Node descriptors + API + plain page contributions exist; React designer not yet built |
| Long downtime recovery across process restart | Partial | Ledger state is durable; restart replay runner not yet implemented |

Latest verification: `dotnet build industrial/Industrial.slnx` 0 warning/0 error;
`dotnet test industrial/tests/Industrial.Tests` 20 passed.
Production `GOCORDIS_INDUSTRIAL_KEY` is mandatory; the local host opts into the
documented development fallback only for evaluation.
