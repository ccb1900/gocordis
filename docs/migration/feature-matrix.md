# Full Migration Feature Matrix (Go → C#)

Baseline (re-confirmed 2026-11-22):

- Go `origin/main`: `ed2ef724a24331ecc3fa21974539315fd90212bf`
# Historical C# baseline (matrix first written): `488e959d750a87cf9a091bf80e6a553bdace5d7c`
# Calibrated target: current `migration/dotnet-complete` HEAD; product host added after ledger calibration
- Working branch: `migration/dotnet-complete`

Sources are first-hand: Go `runtime/ extensions/ cmd/ integration/`, C# `src/ tests/ samples/`, JS Cordis `cordiverse/cordis@5e2e6c1` (4.0.0-rc.13), Koishi `koishijs/koishi@97d5c39` + `koishijs/webui@1dc5c0e`.
Paths are repo-relative unless prefixed `/tmp/...`. Status values: **Equivalent / Adapted / Divergent / Missing** (`Partial` removed; see `final-calibration.md`).
`Acceptance` names are existing tests; `(new)` markers are removed after verification.

## 1. Runtime semantic domains (P0/P1)

| Feature | Go Source | Go Tests | Paper Semantics | JS Cordis | Koishi | C# Source | Status | Gap | Target | Acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Runtime command loop | runtime/runtime.go:14,147; orchestrator.go:16 | runtime/phase3_contract_test.go:11 | C12/C19–C21 | Context/Fiber loop `core/src/context.ts:19` | App=Context alias `core/src/context.ts:217` | src/Gocordis.Core/Runtime.cs:46-85,168-227 | Equivalent | Context writes use short lock, not decision loop | Keep Channel decisions + documented short-lock writes | CoreSemanticsTests.ConcurrentLoadAndDisposeStayLinearizable |
| Component/typed Key/MetaKey | runtime/component.go:17; key.go:17 | runtime/declarations_containment_test.go:1 | C07/C08/C11/C15 | reflect.ts:65-249 | Schema keyed by plugin | src/Gocordis.Core/Capabilities.cs:4-37; Realms.cs:24-39 | Equivalent | No JS Proxy `ctx.foo` (accepted) | Runtime-enforced declaration check | CoreSemanticsTests.DeclaredRequireAndProvideActivateBothFibers |
| Fiber state/Revise | runtime/fiber.go:19,231; state.go:9 | runtime/revise_race_test.go:1 | C16/C20/C28/C34 | fiber.ts:90-97,386-406 | ForkScope (3.x, not copied) | src/Gocordis.Core/Runtime.cs:251-377; Fiber.cs | Equivalent | Retire→reinsert, no identity-preserving replace | Stale completion cannot affect new generation | PlatformConvergenceTests.PC19_StaleActivationCannotAffectANewGeneration |
| Effect/LIFO/divert | runtime/context.go:258,789,824 | runtime/thm68_recovery_test.go:85 | C02–C06/C26 | fiber.ts:261-378 | effect scope (reference only) | src/Gocordis.Core/Activation.cs:40-68; Context.cs:175-212 | Equivalent | Strict serial LIFO vs JS concurrent unwind | Keep serial LIFO and record difference | PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue |
| Iterator effect | runtime/component.go:99; context.go:860 | runtime/effect_phase_test.go:1 | C02/C04/C28 | generator effect `fiber.ts:296-305` | HMR accepted/declined | src/Gocordis.Core/Runtime.cs:772-847 | Equivalent | Test-only deterministic driver | Per-step divert check | PaperGapClosureTests.C28_IterativeStepsShareOneResolutionAndRecoverOnDivert |
| Provider realm/isolation | runtime/providers.go:21,187; rehome.go:70 | runtime/rehome_test.go:1 | C09/C10/C17/C34 | isolate.ts:16-119 | namespace plugin keys | src/Gocordis.Core/Runtime.cs:958-999; Context.cs:126-136 | Divergent | C34 accepted: no Rehome API; per-key realm routing implemented | Per-key realm routing + explicit accepted diff | AuditRegressionTests.IsolateCannotHijackAKeyOwnedByAnotherRealm |
| Dependency reconcile/relied/guard | runtime/reconcile.go:20; dependency_graph.go:64 | runtime/thm70_ordering_test.go:87 | C19–C22/C27/C29 | fiber.ts:422-469 | fork.update/unload | src/Gocordis.Core/Runtime.cs:658-754,849-907 | Equivalent | — | Missing dep → Pending, auto-reactivate | PaperConformanceTests.C22_DependencyLossIsWithdrawalNotFailure |
| Ownership cascade | runtime/ownership.go:10,150 | runtime/phase6_contract_test.go:11 | C18/C19/C31 | fiber children | group passive update | src/Gocordis.Core/Runtime.cs:876-904; Fiber.cs:41-60 | Equivalent | — | Child Gone before parent; no stale child refs | CoreSemanticsTests.ParentFailureCascadesToOwnedChildren |
| Event registration | runtime/event_registry.go:35,449 | runtime/event_value_test.go:1 | C12/C32–C35 | events.ts:163-206 | before/after hooks | src/Gocordis.Core/Runtime.cs:1113-1154 | Equivalent | Dispatch modes in Extensions | Owner-scoped reversible registration | CoreSemanticsTests.EventHandlersAreRemovedWhenOwnerIsDisposed |
| Observation/equivalence | runtime/observation.go:30; observation_equivalence.go:32 | runtime/observation_equivalence_test.go:1 | C13/C24/C29/C30/UI-02 | Snapshot + internal events | Console refresh/patch | src/Gocordis.Core/Observation.cs:8-62; Runtime.cs:1260-1268 | Divergent | C# uses Snapshot + HTTP polling; no public EventSink object | Linearizable read-only Snapshot + observation equivalence | PaperConformanceTests.C13_ObservationEquivalenceIgnoresGenerativeFiberIds |

## 2. Go extensions (P2/P3, all 24)

| Feature | Go Source | Go Tests | Paper Semantics | JS Cordis | Koishi | C# Source | Status | Gap | Target | Acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| broker | extensions/broker/broker.go:49 | extensions/broker/broker_test.go:141 | C17/§6.2 | Service + registry | multi-impl services | src/Gocordis.Extensions/Patterns.cs:13-154 | Equivalent | — | Named routing + reversible registration | BrokerObserveGapTests.BrokerNamedProvidersSupportSelectionAndReversibleRegistration |
| bundle | extensions/bundle/bundle.go:27 | extensions/bundle/bundle_test.go:10 | C40 | include/config preset | plugin group | src/Gocordis.Plugins/DeclarationPipeline.cs (BundleLibrary) | Equivalent | — | TOML/JSON bundle load + expand test | DeclarationGapTests.BundleFileExpandsAndRejectsUnknown |
| cli | extensions/cli/cli.go:26,305 | extensions/cli/cli_test.go:71 | §5 effect | command extension | CLI commands | src/Gocordis.Extensions/Patterns.cs (CliDispatcherSchemaExtensions) | Equivalent | — | Typed command registration + help | DeclarationGapTests.CliHelpAndSchema |
| config | extensions/config/config.go:64,115 | extensions/config/config_test.go:60 | C19/C40 | loader Entry | loader.reload | src/Gocordis.Plugins/ConfigSchema.cs; PluginRegistry.cs | Equivalent | — | Schema validation before reconcile | DeclarationGapTests.ConfigSchemaRejectsInvalidDeclarationWithNoFiberChange |
| configwatch | extensions/configwatch/adapter.go:60 | extensions/configwatch/tx_guard_test.go:1 | C19/C40 | include journal | atomic config write | src/Gocordis.Plugins/DeclarationPipeline.cs (TxGuard) | Equivalent | — | Existing bad/delete/close behavior + tx boundary | DeclarationGapTests.ConfigWatcherTxGuardCoversReconcileAndDoesNotAdvanceRevisionOnFailure |
| console | extensions/console/{host,hub,registry,webui,explorer,insight,runtime,logstore,procplugin} | registry/p32_conformance_test.go:1; webui/lifecycle_test.go:1 | UI-01..03 | Console service | Koishi webui | src/Gocordis.Plugins/ContributionApi.cs; src/Gocordis.Console.WebUi/WebUiServer.cs | Equivalent | — | Explorer/Inspector/Dashboard data and assets served | ProductGateTests.ExplorerDashboardConformance_* + PluginControlPlaneTests (6) |
| devtools | extensions/devtools/diagnose.go:60 | extensions/devtools/diagnose_test.go:181 | C39 | no direct | plugin doctor | src/Gocordis.Extensions/DevtoolsDiagnostics.cs | Equivalent | — | Deterministic Diagnose(modules, plugins, version) | RuntimeGapTests.DiagnoseReportsVersionMismatchAndUnknownProvider |
| env | extensions/env/env.go:29 | extensions/env/env_test.go:113 | C07/C12 | dotenv | .env + interpolation | src/Gocordis.Extensions/Infrastructure.cs | Equivalent | — | Layered dotenv with reversible populate | InfrastructureGapTests.EnvFourFilePriorityAndRevert |
| event | extensions/event/dispatch.go:61 | dispatch_emit_conformance_test.go:204 | §3.4.2 | events.ts:108-206 | before/after | src/Gocordis.Extensions/EventDispatcher.cs | Equivalent | no OnOnce alias | 5 modes + value/chain + cancellation | EventDispatchGapTests.* |
| fs | extensions/fs/fs.go:77,386 | fs_conformance_test.go:1 | C35/R4 | fs extension | file mount | src/Gocordis.Extensions/Infrastructure.cs | Equivalent | — | Full provider surface + memory provider + Range | InfrastructureGapTests.FsFullConformance |
| hmr | extensions/hmr/hmr.go:74; hmr_impl.go:401 | extensions/hmr/hmr_test.go:325 | C40/Thm80 | hmr.ts:521-749 | plugin HMR | src/Gocordis.Extensions/GenericHmr.cs; src/Gocordis.Runtime.Wasm/WasmHotSwapper.cs | Divergent | Generic HMR migrated; WASM adapter warm-swap divergence accepted | Generic HMR target+usage+validation | RuntimeGapTests.GenericHmrStaleGenerationCannotReleaseNewTarget |
| http | extensions/http/http.go:49,621 | http_test.go:111 | §5/C35 | http extension | HttpClient service | src/Gocordis.Extensions/Infrastructure.cs | Equivalent | — | Core HTTP surface + close semantics | InfrastructureGapTests.HttpStateStreamCloseConformance |
| http-socks | extensions/http-socks/proxy.go:18 | proxy_test.go:1 | §6.2/C35 | proxy option | proxy env | src/Gocordis.Extensions/HttpSocks.cs | Equivalent | — | SOCKS proxy registration as reversible effect | InfrastructureGapTests.SocksProxyRegistrationAndUndo |
| loader | extensions/loader/loader.go:73,112 | loader_test.go:78 | C40 | loader Entry/tree | loader plugin | src/Gocordis.Runtime.Managed/PluginLoadContext.cs; src/Gocordis.Plugins/PluginRegistry.cs | Equivalent | no include/group module graph | Artifact/Module/usage generation fence | BackendLifecycleTests + PluginSystemTests |
| market | extensions/market/service.go:70,171 | service_test.go:371 | C40 | registry service | market UI | src/Gocordis.Extensions/Patterns.cs | Equivalent | — | Catalog/install/upgrade/uninstall over one declaration API | DeclarationGapTests.MarketInstallUpgradeUninstallAndPackageLockDiff |
| observe | extensions/observe/observer.go:52 | observer_test.go:87 | UI-02/03 | ObserveStream | Console data service | src/Gocordis.Extensions/Observation.cs:40-278 | Equivalent | ObserveStream drops old vs log terminates (documented) | Sequence-anchored replay + overflow | ObservationBoundaryTests.* |
| package-manager | extensions/package-manager/package_manager.go:61,119 | package_manager_test.go:25 | C40/§6.6 | PackageManager | plugin package | src/Gocordis.Extensions/Patterns.cs | Equivalent | — | Package apply with backup/rollback + diff state | DeclarationGapTests.MarketInstallUpgradeUninstallAndPackageLockDiff |
| patch | extensions/patch/patch.go:34,139 | patch_test.go:1; atomic_test.go:47 | C40 | include patch | config patch | src/Gocordis.Plugins/DeclarationPipeline.cs (PatchFile) | Equivalent | — | File-backed atomic patch apply | DeclarationGapTests.PatchFileAtomicFailureLeavesNoChange |
| pluginmeta | extensions/pluginmeta/manifest.go:47,124 | parse_test.go:106 | C40 | EntryOptions | plugin manifest | src/Gocordis.Plugins/PluginManifest.cs | Equivalent | — | Strict parse + content checksum verification | DeclarationGapTests.ChecksumMismatchRejected |
| registry | extensions/registry/registry.go:56,162 | registry_test.go:1 | C17/C31 | RegistryService | service registry | src/Gocordis.Extensions/Patterns.cs:156-253 | Equivalent | two registries with distinct roles | Immutable snapshot + watch + replace | ExtensionTests.BrokerAndRegistryManageMembers |
| scheduler | extensions/scheduler/scheduler.go:57,146 | scheduler_test.go:234 | R4 | TimerService | cron/scheduler | src/Gocordis.Extensions/Timing.cs:148-278 | Equivalent | explicit pump vs wall clock | No overlap, no drift | ExtensionTests.SchedulerAdvancesJobsDeterministically |
| server | extensions/server/server.go:31,487; acl/static/proxy/echo | server_test.go:63; acl_test.go:1 | §5/C35 | ServerHost | webui transport | src/Gocordis.Extensions/ServerGuards.cs | Equivalent | — | ASP.NET Core host with ACL+proxy+WS | InfrastructureGapTests.ServerAclProxyWsConformance |
| timer | extensions/timer/timer.go (package present) | timer tests | R4 | TimerService | timing extensions | src/Gocordis.Extensions/Timing.cs | Equivalent | — | Owned reversible timers + deterministic ticks | RuntimeGapTests.TimerOwnedEntryStopsWhenOwnerUnwinds |
| watch | extensions/watch | watch tests | C19/C40 | include refresh | configwatch | src/Gocordis.Extensions/ReversibleWatch.cs | Equivalent | — | Recursive watch with filters and reversible provider | RuntimeGapTests.WatchRecursiveFilterReadyErrorAndRevoke |

## 3. Product/CLI/front-end/validation domains

| Feature | Go Source | Go Tests | Paper Semantics | JS Cordis | Koishi | C# Source | Status | Gap | Target | Acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| platform host | cmd/platform/main.go:63,174 | cmd/platform/main_test.go:106 | C19/C40 | loader entry | App/CLI start | src/Gocordis.Platform/ | Equivalent | — | Product host with manifest/store/host-profile + CLI run | PlatformApplicationTests.WebHostStartsServesConsoleAndStopsCleanly |
| cli-cordis | cmd/cli-cordis/main.go:18 | main_test.go:60 | §5 | CLI command | CLI plugin | tools/Gocordis.Cli/Program.cs | Equivalent | — | CLI install/run/enable/disable + product host | CliProductTests.AppEnableAndDisableToggleManifestFlagsWithoutChangingOtherFields |
| plugin CLI (create/doctor/package/sandbox) | cmd/plugin/{create,doctor,package,sandbox}.go | cmd/plugin/package_test.go:103 | C39/C40 | hmr/package | plugin dev tools | tools/Gocordis.Cli/Program.cs | Equivalent | — | Full plugin tooling via Gocordis.Cli | PluginToolingTests + CliAndSourceTests + CliProductTests |
| create-cordis scaffold | cmd/create-cordis/create.go:13 | create_test.go:16 | C40 | create-cordis | create-koi | tools/Gocordis.Cli/Program.cs (`create`) | Equivalent | — | Generate minimal .NET app + manifest | CliAndSourceTests.CliCreateGeneratesBuildableAppScaffold |
| httpd sample | cmd/httpd/main.go:39 | main_test.go:46 | §5 | server example | simple app | WebUiServer sample | Equivalent | — | Minimal HTTP host sample | HttpdProbeTests.HttpdProbeRespondsAndStops |
| backfill sample | cmd/backfill/core.go:46 | core_test.go:62 | journal/replay | no direct | business plugin | — | Equivalent | — | Business journal + idempotent replay sample | BackfillJournalTests.BackfillJournalReplaysOnlyMissingDatesAndIsIdempotent |
| example/host/collector/procplugindemo/wasmhmr | cmd/{example,host,collector,procplugindemo,wasmhmr} | few | C39 | examples | examples | samples/SamplePlugin, EchoProcPlugin, UiPlugin | Equivalent | — | One C# sample per backend/composition | SampleParityTests.SampleParityListsExpectedBackendsAndComposition |
| web/console SPA | web/console/src/** | web/console/src/**/*.test.ts | UI-01..03 | client extension | Koishi webui | web/console + src/Gocordis.Console.WebUi | Equivalent | React SPA is C#-hosted; TS front end is product asset | Same SPA served by product host | ProductGateTests.ExplorerDashboardConformance_BuiltExplorerAndDashboardAssetsAreServed |
| web/plugin-kit/client SDK | web/plugin-kit; client/src/api.ts | TS tests | C39 | plugin kit | @koishijs/client | src/Gocordis.Client/ConsoleClient.cs | Equivalent | .NET-native client SDK replaces JS package; envelope/API contract aligned | Typed Console client for meta/pages/panels/modules/query/command/stream | ConsoleClientTests.ClientReadsMetaPagesPanelsModulesAndNamedQueries |
| integration gates | integration/e2e_*; platform_convergence_* | 21 files | Thm64/68/70/73/80 | no direct | E2E | tests/Gocordis.Conformance.Tests | Adapted | Only representative PC gates (01/12/19/21/27/30) + theorem gates ported | Representative PC gates + theorem matrix | ProductGateTests.PCGateParity_* + TheoremMatrixTests |
| deterministic theorem facility | runtime/deterministic.go:17; theorem_evidence_test.go | fuzz/property tests | Thm64/68/70/73/80 | no direct | no direct | tests/Gocordis.Conformance.Tests/DeterministicDriver.cs | Equivalent | — | Keep test-only; Thm73 quiescence covered | ProductGateTests.Thm73InstallingQuiescence_InstallingEffectBlocksActiveUntilRelease |

## 3b. Runtime backend domains (batch 4)

| Feature | Go Source | Go Tests | Paper Semantics | JS Cordis | Koishi | C# Source | Status | Gap | Target | Acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Managed runtime | extensions/loader/builtin.go:12 | loader_test.go:78 | C39/C40 | in-process modules | in-process plugins | src/Gocordis.Runtime.Managed/* | Divergent | ALC is not a sandbox and has no Go equivalent | Collectible ALC + generation fence + documented boundary | BackendLifecycleTests + ManagedRuntimeTests |
| Process runtime | extensions/loader/proc/backend.go:71 | transport/transport_test.go:1 | §6.2/C35 | no direct | external plugin process | src/Gocordis.Runtime.Process/* | Equivalent | plugin→host call unsupported in both Go/C# | Handshake/RPC/shutdown/reap/error policy | BackendLifecycleTests + ProcessRuntimeTests |
| WASM runtime | extensions/loader/wasm/backend.go:57 | wasm/backend_test.go:1 | §6.3/C39 | no direct | no direct | src/Gocordis.Runtime.Wasm/* | Equivalent | 64 MiB limit now enforced; HMR warm-swap divergence tracked in hmr row | Wasmtime ABI + custom section + size guard + host bridge | WasmRuntimeTests.ModuleLargerThanSixtyFourMiBIsRejectedBeforeCompilation |
| Go Native runtime | Go native backend (compile-time registry) | integration wasm/proc gates | C39 | no direct | no direct | Process backend substitute | Divergent | .NET cannot load Go native plugins without interop; no equivalent required | Document Process backend as isolation substitute; do not fake equivalence | docs/migration/runtime-integrity.md |

## 3c. UI host domains (batch 5)

| Feature | Go Source | Go Tests | Paper Semantics | JS Cordis | Koishi | C# Source | Status | Gap | Target | Acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Web UI host | extensions/console/webui/server.go:101 | webui/lifecycle_test.go:1 | UI-01..03 | client extension | Koishi webui | src/Gocordis.UI.Hosts/UiHostProfiles.cs; src/Gocordis.Console.WebUi/* | Equivalent | Explorer/Inspector productization still separate | Same plugin renders pages/panels/modules | MultiHostUiContractTests + WebUiCompositionTests |
| WinForms UI host | no Go equivalent | — | C12 contribution | no direct | native client (product only) | src/Gocordis.UI.Hosts/UiHostProfiles.cs | Adapted | Render-plan contract + web-module degradation; no native control host package | Same plugin renders or explicitly degrades | MultiHostUiContractTests.SamePluginRendersOnEachHostAndDegradesWebOnlyResources |
| Avalonia UI host | no Go equivalent | — | C12 contribution | no direct | native client (product only) | src/Gocordis.UI.Hosts/UiHostProfiles.cs | Adapted | Render-plan contract + web-module degradation; no native control host package | Same plugin renders or explicitly degrades | MultiHostUiContractTests.ProfilesFollowOnePluginLifecycleWithoutExtraRegistries |

## 4. Explicit decisions and unresolved rules

1. **Accepted differences** (must stay documented, not hidden): C34 Rehome, C38 nominal K×P namespace, strict serial LIFO vs JS concurrent unwind, no JS Proxy `ctx.foo`, retire→reinsert instead of identity-preserving replace.
2. **No Missing rows remain**. All batch 6 targets were implemented as .NET-native mechanisms or explicitly documented divergences.
3. **Adapted rows are not Equivalent**: each has a concrete Gap and evidence; no row may be silently promoted.
4. **Koishi is product reference only**: Koishi uses Cordis 3.x ForkScope; C# follows Go/Cordis-4 Fiber. Do not port `ctx.plugin`, `ns-require`, `require.cache`, or vite toolchain into C# runtime.
5. **JS Cordis 4 conflicts resolved by paper/Go**: committed-after-Apply, strict LIFO, declaration enforcement, and no automatic retry on Failed are kept in C#.
6. **ALC is not a sandbox**: Process/WASM backends remain the isolation boundary; ALC only provides collectible load/unload.
7. **Runtime state is never persisted**: only manifests, app declarations, package lock state, and business journals persist.

## 5. Status counts (source-driven)

| Status | Count |
| --- | ---: |
| Equivalent | 45 |
| Adapted | 2 |
| Divergent | 5 |
| Missing | 0 |
| **Total acceptance rows** | **52** |

Row-level Missing is 0; remaining sub-capability gaps (native UI controls, Koishi ecosystem, package signing) are listed in `final-calibration.md`. Divergent rows retain explicit semantic reasons.