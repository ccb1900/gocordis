# Current Status — Gocordis .NET

This is the **only** place that records current branch/HEAD, entry commands, verified counts and known gaps. Everything under `docs/migration/` is a historical migration archive except `existence-audit.md`, which is maintained as the current per-capability classification; never read old branch names, SHAs or test counts from the archive as current state. `README.md` is the front door and points here.

- **Branch**: `dotnet` (tracks `origin/dotnet`).
- **Code state**: built-in Console plugin and config-equality fix are committed on `dotnet`; use `git rev-parse HEAD` / `git log -1` for the exact SHA rather than a remembered value.
- **Engineering constitution**: `AGENTS.md` (root). Engineering manual: `docs/guide/csharp-developer-manual.md`. User manual: `docs/guide/user-manual.md`.

## Product entry

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest <gocordis.app.json> --store <plugins-dir> [--address http://127.0.0.1:8170] [--host web]
  [--allow-remote] [--allowed-host <host>] [--trust development|trusted-only] [--public-key <pem>] [--validate-only]
```

Console: no fixed default port (`http://127.0.0.1:0` picks an ephemeral port); `8170` is only what the examples pass explicitly via `--address`. Web is the only runnable host; the CLI rejects `winforms`/`avalonia` because they are render-plan profiles, not native window hosts.

A fresh checkout run with `dotnet run` gets the embedded zero-build console: dashboard counts, contributed pages, dependency graph (declared/runtime toggle), runtime snapshot and recent events. The frontend is split by Cordis ownership: `web/console-runtime` is the reusable library, `web/console` is a thin shell, and three independent builtin plugin frontends each own only their own contributions — `src/Gocordis.Builtin.WebConsole/frontend` (transport overview), `src/Gocordis.Builtin.ControlPlaneUi/frontend` (control surface + event feed), `src/Gocordis.Builtin.InsightUi/frontend` (dashboard, graph, inspector, inventory). Every frontend builds one ESM into its own protected component's `client-modules/<id>/`. `web/console/dist` and those `ui.js` files are tracked so `dotnet build` copies them without an npm step on a fresh checkout; the embedded fallback above is only a safety net.

## Runnable full loop

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase
# add --run to start the platform; see samples/SpatiotemporalShowcase/README.md for verification steps
```

It builds 5 showcase projects + one real TSX frontend (`@gocordis/plugin-kit`), packs 4 business `.gocordis-plugin` packages (`--shared-assembly`, `--provides`, `--dependency`) and installs them. The application declaration contains ONLY those user plugins: Web Console is loaded by Platform bootstrap as a protected builtin Fiber, not as a sample package and not as a manifest entry.

There are now three protected builtin UI Components in the one Registry: `Gocordis.Builtin.WebConsole` (HTTP transport + overview/status + `gocordis-console-ui` ESM), `Gocordis.Builtin.ControlPlaneUi` (control-plane page + event-feed panel + `gocordis-control-plane-ui` ESM), and `Gocordis.Builtin.InsightUi` (framework/dashboard, dependency/runtime graph, runtime inspector, local inventory + `gocordis-insight-ui` ESM). Each declares its own pages/routes/navigation/client module with owner-bound effects; each renderer lives in that plugin's own ESM. `PlatformApplication` only registers the three factories and applies one builtin declaration batch. Control-plane SERVICE remains a separate protected Fiber. Builtin pages exist in native render profiles without an HTTP listener. `PlatformApplication` no longer constructs `WebUiServer`; its only direct HTTP ownership is the minimal degraded recovery host. Tests assert owner ids, module package ids, rejected uninstall/disable, shell/runtime boundaries and degraded recovery.

The empty-directory developer loop (`bash samples/PluginDeveloperWorkflow/e2e.sh <out>`) was re-run green. It now packs a per-run SDK prerelease version: reusing `1.0.0` let NuGet's global cache serve an older Core and caused `MissingMethodException` at plugin activation. E2E test helpers pass `-nodeReuse:false` so reused MSBuild nodes cannot hold redirected stdout pipes open.

The paper §5.3 case-study loop is `bash samples/KoishiShowcase/build-and-run.sh <out>` (add `--run` to start the host). It packs/installs four real `.gocordis-plugin` packages with one host-sealed shared contract, then composes vocabulary → database → adapter → feature. `KoishiPackagedProductTests` drives the real packages: all four fibers Active, disabling the database leaves adapter/feature Pending and withdraws their query, re-enabling restores them, and an independent console application (own Runtime/Registry/fibers, HTTP-only client) serves its own page and server query. The paper §5 behavior-by-behavior evidence matrix is `docs/paper/chapter-5-coverage.md`.

## Verification commands

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
dotnet build industrial/Industrial.slnx --nologo -m:1 && dotnet test industrial/Industrial.slnx --nologo --no-build -m:1
(cd industrial/web && npm ci && npm test && npm run build)
(cd web/console-runtime && npm ci && npm test)
(cd web/console        && npm ci && npm test && npm run build)
(cd src/Gocordis.Builtin.WebConsole/frontend        && npm ci && npm test && npm run build)
(cd src/Gocordis.Builtin.ControlPlaneUi/frontend  && npm ci && npm test && npm run build)
(cd src/Gocordis.Builtin.InsightUi/frontend        && npm ci && npm test && npm run build)
(cd web/plugin-kit             && npm test)
bash docs/migration/verify-baseline.sh   # Go baseline drift check
```

**Last locally verified** (2026-11-26, branch `dotnet`, reliable-collection worktree): C# **685 passed / 1 skipped / 0 failed** (Core 53, Conformance 179, Runtime 206+1 skipped, Extensions 73, Packaging 88, UI 3, Client 2, Product 81; PosixOnly/WindowsOnly tests report Skipped explicitly — never silent passes), industrial solution **82 passed / 0 failed**, industrial designer frontend **38** tests green plus client-module build, real-browser designer E2E and real packaged-plugin E2E green, real PostgreSQL sink integration **1** test green, console-runtime **63** tests green, thin shell **3** tests green plus production build, builtin console frontend **1** test green plus ESM build, builtin control-plane frontend **1** test green plus ESM build, builtin insight frontend **40** tests green plus ESM build, plugin-kit **3** tests green, solution build 0 warning/0 error. Conformance matrices are serialized, the gated-interleaving fixture publishes its barrier only after the inverse is registered, and fiber-state waiters are now released at the state transition instead of only on Snapshot, and a declared provider withdrawn between Pending gating and `Require` during Apply is treated as dependency loss (Pending/reactivate), never a Failed fiber. The industrial Workflow Designer now uses draw.io-like direct gestures with no mode switching: drag a node body to move it, drag from a port to create a typed edge, drag the background to pan, or hold Space/middle-button to pan over a node. The palette is collapsible, node properties open in a Drawer instead of a permanent column, the product opens on the workflow list with links into a specific designer, and shell route changes have a short transition. Its scoped styles are injected by the client module instead of relying on an unloaded CSS asset. The industrial product loop now runs the published Elsa Flowchart from persisted connections with a distinct real Elsa Activity per node type and an Elsa-persisted workflow instance that can be inspected after a process restart; each node reads its inputs from predecessor nodes along the published edges. Published versions stay pinned against later draft edits. The ledger now uses database-level atomic claims with claim tokens, leases, conditional completion/failure, expired-lease recovery and a guarded Created/Running/Succeeded/PartialFailure/Failed/Cancelled/Interrupted state machine aggregated from run-scoped delivery rows. Stage outputs (FileBatchRef, DatasetRef, TransformResultRef) are persisted in a Cordis data plane, so a resumed workflow reuses completed stages instead of re-reading data; sink/archive stages are only committed after successful delivery. Startup reload now resumes the original Elsa instance from its root (stage cache skips finished stages), releases abandoned claims even before lease expiry, and retries only incomplete deliveries; a real OS process kill after a partial second-target write is followed by automatic restart recovery: the CSV through Reader and Mapping ends complete and duplicate-free in both targets. Delivery claims are fenced by token, support true concurrent acquisition and lease renewal during long writes, and staged file identity is revalidated before reuse. Execution diagnostics now expose per-node status, processed counts, errors and bounded samples in the designer. Node schemas cover CSV/Excel/Mapping/Validation/SQL/Archive semantics with typed validation and forbidden secret keys; validation supports drop/warn/fail/quarantine with persisted statistics. UNC sessions use WNetAddConnection2 with reference-counted leases instead of password-bearing `net use`. EF migrations cover all three industrial stores; disconnect, large-file and lease tests are part of the unified report. Developer and packaged hosts share one component/declaration map; the retired aggregate industrial component is gone. `.gocordis-plugin` packages were rebuilt and installed locally, including a generated registry index. A tag-triggered release workflow now archives the platform and industrial plugin packages, but no CI result was queried. All local suite counts come from the single executable fact source `bash tools/release/test-report.sh` → `artifacts/test-report/report.json` / `report.md` (last run: 937 passed / 3 explicitly skipped / 0 failed suites for code commit 75ce6b2cb5fda2c72694a8034f64285b638bf757). Real-browser designer, real packaged-plugin and real PostgreSQL integration now run locally; Windows UNC/SMB is now verified on a real `windows-latest` runner (passing run https://github.com/ccb1900/gocordis/actions/runs/45799908846, evidence `artifacts/windows-unc-result.json`); the local macOS skip is no longer the acceptance evidence. The industrial product operation loop is now user-owned in the browser: the workflow list creates a named draft and opens the designer, the resource page creates/edits sources and targets, binds credentials and runs a read-only connection test with actionable messages, the credential page rotates secrets without displaying them, and the execution center renders ledger runs and per-target deliveries with a target-level retry action. Real-browser E2E covers those four stories (`industrial/web/e2e/user-stories.spec.ts`). The long-running user path is now closed too: the designer edits manual/schedule/event triggers with cron presets and shows next/last run; a real Timer-backed scheduler runs published cron workflows without manual action; backfill preview reports discovered files, already-complete files, missing targets and duplicate risk before execution; the file-details page shows per-file/per-target records with classified failure messages and actions. Real-browser E2E covers all four stories. The correctness-convergence round removed the data-damaging paths found in review: resource edits keep their connection config and credential binding; SQL Server replace deletes once per source inside a transaction; PostgreSQL/MySQL share the writeMode/businessKey contract with SQL Server; UNC connection tests establish the credentialed WNet session; delivery retry resumes the original published workflow with stage reuse and a delivery-level scope; schedule fires are claimed durably with an explicit timezone; and backfill preview derives missing targets from the published sink set. A blank-environment, browser-only E2E now creates credentials/resources/workflows, builds the graph through palette clicks and the edge form, publishes/runs, injects a real target failure, repairs the resource and retries the failed delivery without API seeding. An integration test also proves that publishing a new Sink after a successful historical delivery backfills only that new target. Go baseline verification reports the known frozen `docs/guide/user-manual.md` checksum drift and is recorded, not hidden. These counts are a record of that run, not a claim about any earlier SHA.

Task 01 的有限校准基线见 `docs/migration/acceptance-baseline.md`；它区分三个验收域与 E0–E4 证据等级，P0-01…P0-04 均已关闭（真实 Process↔WASM 调用与包闭环由 `CrossBackendCapabilityTests`、`BackendDispatchProductTests` 覆盖）。

Loopback HTTP tests now construct clients with `UseProxy = false`, so the full suite is reproducible with the ambient `HTTP_PROXY`/`HTTPS_PROXY` left set (verified 549/1 skipped/0 failed that way).

**CI**: no confirmed public CI result for the current HEAD; do not reuse historical 443/443 or other round results as proof for this commit. The Go baseline verification is handled by `verify-baseline.sh`, with drift reported rather than silently accepted. That script currently reports one drifted frozen overlay file, `docs/guide/user-manual.md` (changed by the earlier contracts refactor `181c56e` without updating `docs/migration/baseline.sha256`); the Go commit pin and all other overlay files still verify. This is recorded here, not papered over by rewriting the frozen checksum.

## Dependency semantics (current, settled)

Three layers stay distinct: **package dependency** (PluginDependency + version range; store install/upgrade/uninstall via `PluginDependencyPlanner`), **contract dependency** (host-sealed shared assembly in Default ALC; only `SharedAssemblyRegistry.Seal` is the public host entry), **capability dependency** (typed `Inject()/Provide()`; Core reconciliation). Assembly is declaration-led: a package dependency never auto-declares/activates its provider — the application manifest is the sole assembly set. An installed-but-undeclared dependency yields a read-only `PACKAGE_DEPENDENCY_NOT_DECLARED` warning, never a silent "satisfied". `Allow()` is private; plugins cannot widen the shared-assembly whitelist.

## Cross-backend capability contract (current)

One host-owned `CapabilityCatalog` (`Gocordis.Core`) allocates a single `ICapabilityKey` instance per `CapabilityId = (Namespace, Name)`. All three backends translate declarations into those same keys **before** Core loads the component; Core's matching/binding/withdrawal/reactivation is unchanged.

- **Managed**: a plugin that needs a cross-backend capability accepts `ICapabilityCatalog` in its constructor. It shares only `Gocordis.Core` with the host — **no shared contracts DLL and no `SharedAssemblyRegistry`** are needed for cross-backend identity. Plugins that keep using static `Key<T>` are unaffected.
- **WASM**: declarations live in the `gocordis.capabilities` custom section (JSON). A provider exports `capability_invoke(ptr,len)→(ptr,len)`; a consumer imports `gocordis.capability_buffer/require/invoke`. The host bridges both directions through the catalog keys and the Core `Context`.
- **Process**: declarations come from the package manifest; the v1 handshake is kept and only `capability/require` and `capability/invoke` are allowlisted. Undeclared plugin→host calls still return `-32601`.
- **Wire contract**: `CapabilityId`, `CapabilityContract` (value kind, SemVer version, media type, schema), `CapabilityDeclaration` (canonical `|`-separated wire form), `CapabilityEnvelope`, `ICapabilityEndpoint`, `DelegatingCapabilityEndpoint`. Version is compatibility metadata, never part of identity; ranges use the existing `NuGet.Versioning` dependency (C38).
- **Lifecycle**: endpoint registration and handle release are installed as Core effects, so unwind stays strict serial LIFO. Only bindings are reversible; calls to external systems remain subject to C35.

## Platform productization (Task 01–04)

- **Task 01 developer workflow**: `Gocordis.Plugin.Sdk` package + `plugin create`
  template (no runtime-repo path in generated project), `plugin validate`
  preflight, `app add` declaration, runnable `samples/PluginDeveloperWorkflow/e2e.sh`
  from an empty directory; author docs in `docs/guide/plugin-developer-workflow.md`.
  Deferred: hot reload/debug without packing. Effect-slot attribution is
  implemented (`EffectFailureRecord` in `runtime/snapshot`; see
  `tests/Gocordis.Core.Tests/CoreSemanticsTests.cs`).
- **Task 02 UI hosts**: unified contribution contract and scope in
  `docs/guide/ui-host-contract.md`. Web is the only real host; WinForms/Avalonia
  are explicitly deferred render-plan profiles, NOT completed native hosts.
  The `runtime/snapshot` query now projects the fields the inspector consumes
  (component, intent, activation ID, parent/child fibers, providers and
  dependencies); scopes/effects emit honest empty projections until Core
  exposes those read models.
- **Task 03 trust/version**: `ApplicationPlugin` carries VersionRange,
  LockedVersion, PackageHash, Source; `ApplicationLoader` enforces lock+hash.
  `PluginTrustPolicy` (Development / TrustedOnly with pinned PEM keys) enforced
  by `PluginStore.InstallAsync`/`UpgradeAsync`, parsed once by
  `PluginTrustPolicy.FromPemFiles` and wired into `plugin install|upgrade`,
  `app run`, `PlatformRunner` and Console uploads. `PlatformHostOptions.Trust`
  is now assigned (the earlier parameter shadowing left it null). Default is
  local development (unsigned accepted); no remote marketplace.
  `--allow-remote` only opens the listener; without a host-supplied
  `ConsoleAccessPolicy` every remote API scope is denied (query stays open).
  The `plugin preflight` command replaced the misleading
  `plugin sandbox` name (old name kept as alias).
- **Task 04 stability/scale**: BenchmarkDotNet hot-path benchmarks plus repeated
  enable/disable ledger-stability test. Cold 10k apply after batch snapshots is
  still O(N²)-ish (FindCycle/Reconcile per command). Re-measured at current HEAD: 3.545 ms / 241.436 ms / 32.583 s for 100/1,000/10,000 — see benchmark README.

## Known boundaries (not "done" claims)

- Managed ALC is **not** a security sandbox; Process/WASM are the isolation boundaries; managed plugins are trusted code. Shared contract assemblies are host-approved, full-identity checked, and process-lifetime (contract upgrades require a controlled host restart).
- §6.3 threat model (explicit): **Managed** = trusted in-process code, ALC is collectible not isolating. **Process** = OS address-space isolation plus a JSON-RPC method allowlist and `ProcessPermissionPolicy` applied at launch (absolute/PATH-resolved executable, no shell, empty child environment by default with only runtime-necessary `DOTNET_ROOT`/`DOTNET_ROLL_FORWARD` and host-authorized variables, explicit working directory); there is NO credential drop, so it must not be called a secured sandbox. **WASM** = Wasmtime engine limits (memory ceiling, stack ceiling, epoch interruption) plus a declared-capability import allowlist (`WasmCapabilityBridge.TryAuthorize` wired from the custom section); guest modules still trust the host bridge. These are different boundaries, not interchangeable "sandbox" labels.
- Package signatures are verifiable provenance, **not** an install-trust chain.
- WinForms/Avalonia are render-plan profiles, not full native hosts. Native window hosts for these profiles are an explicitly DEFERRED product capability (P2-03): do not count `UiHostKind` values as completed hosts.
- Koishi plugin market / remote distribution / browser-side reconcile loop are product references, not ported runtime features.
- Process/WASM packages without capability declarations are rejected at the product factory: the old echo/value demo path is isolated from the product. Only `endpoint` is implemented for cross-backend value kinds.
- Backend common protocol and generation fence live in `src/Gocordis.Runtime.Contracts`; Process and WASM no longer depend on `Runtime.Managed`.
- Official extension capabilities bind `Key<I…>` from their own `Gocordis.Extensions.Contracts.<Name>` assembly, are published by Runtime provider fibers; `plugin create --contract <Name>` adds only the contract package the consumer needs (no ASP.NET Core pulled into plugins). Env is opt-in (`EnvExtensionComponent`): it mutates process environment, is not host-supplied, and must be explicitly loaded by an embedding host. Pattern libraries without a Go `ServiceKey` (Broker, registry, event, observe, scheduler, HMR, package-manager, CliDispatcher, ServerHost) stay library-only; see `docs/migration/existence-audit.md` for the per-capability arbitration.
- A fiber whose unwind had a failed inverse is **not** clean recovery: it stays `CleanupIncomplete`, its records survive on `RuntimeSnapshot`, and `ObservationEquivalence` never treats it as equivalent to a clean unwind. `DisposeAsync` still completes the fiber to `Gone` (terminal lifecycle) and reports failures through diagnostics rather than throwing from teardown.
- Observation streams use a **last-consumed sequence anchor**: `Subscribe(from)` replays `Sequence > from`, while `0` means oldest retained. Delivery uses bounded `System.Threading.Channels`; replay gaps still terminate with `SequenceTooOldException`. Observation equivalence compares global failure ledgers and Key instance/value-type identity; identity is process-local, while cross-backend identity uses the host `CapabilityCatalog`. See `docs/paper/observation-contract.md`.
- Context declaration authority is strict: `Meta`/`OverrideMeta` require the MetaKey in `Inject`/`Provide`, and `EmitAsync` requires the EventKey in `Inject` or `Provide`: an owner may emit its own declared event without creating a false self-dependency.
- Theorem 64/68/70/73/80 have executable scenario evidence, not mathematical proofs; Thm73's S(n) ≤ (K+3)(V(n)+1) upper bound is explicitly **not proved**. See `docs/migration/theorem-matrix.md`.
- Paper §5.2.2 HMR is reachable in the product through `POST /api/hmr` → `PlatformApplication.HmrAsync` → `HmrEngine`; the three-phase plan (accepted/declined/stale/restart-required) is returned over HTTP. Cancellation is **preflight-only**: once the single Registry transaction starts it is not interrupted. The read→plan→commit window is guarded by `DeclarationVersion`: a stale expected version fails before staging or touching any fiber. Replacement sources are resolved segment by segment and must land inside the real store root; changed modules/imports/externals are supplied by the caller, so this is an HMR coordination engine with a product entry, not automatic file-watch/build HMR. Package upgrade via `plugin upgrade` remains a distinct revision/packaging operation.
- Paper C38 (`VersionedKey<T>` + NuGet ranges instead of nominal `K×P`) remains the single accepted difference; C34 is covered via `Runtime.RehomeAsync`.

## Doc layers

1. **Current engineering spec**: root `AGENTS.md`, `docs/guide/csharp-developer-manual.md`.
2. **Current status**: this file (and `README.md` pointer).
3. **Main paper chapter matrix**: `docs/paper/chapter-coverage-matrix.md` — per-section four-dimension matrix built from the 92-page PDF outline (unique IDs, pages, C# implementation, product integration, verification evidence, status, difference/acceptance). Supporting docs: `docs/paper/chapter-2-theory-mapping.md`, `docs/paper/observation-contract.md`, `docs/paper/chapter-5-coverage.md`, `docs/paper/chapter-6-boundaries.md`, `docs/paper/chapter-7-8-positioning.md`. C01–C40 remain the separate semantic-item/test ledger and never imply whole-chapter completion.
4. **Current classification**: `docs/migration/existence-audit.md` and `docs/migration/completeness-ledger.md` (explicit carve-outs from the archive, maintained with current state).
5. **Historical migration record**: the rest of `docs/migration/` — archive only, including `README.md`, round/review documents and their old branch names, SHAs and counts.
