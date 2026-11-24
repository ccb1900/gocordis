# Current Status — Gocordis .NET

This is the **only** place that records current branch/HEAD, entry commands, verified counts and known gaps. Everything under `docs/migration/` is a historical migration archive except `existence-audit.md`, which is maintained as the current per-capability classification; never read old branch names, SHAs or test counts from the archive as current state. `README.md` is the front door and points here.

- **Branch**: `dotnet` (tracks `origin/dotnet`).
- **Baseline for review**: the `dotnet` branch HEAD at the time of the review; always refer to the actual `git rev-parse HEAD`, not a remembered commit.
- **Engineering constitution**: `AGENTS.md` (root). Engineering manual: `docs/guide/csharp-developer-manual.md`. User manual: `docs/guide/user-manual.md`.

## Product entry

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest <gocordis.app.json> --store <plugins-dir> [--address http://127.0.0.1:8170] [--host web]
  [--allow-remote] [--allowed-host <host>] [--trust development|trusted-only] [--public-key <pem>] [--validate-only]
```

Console: no fixed default port (`http://127.0.0.1:0` picks an ephemeral port); `8170` is only what the examples pass explicitly via `--address`. Web is the only runnable host; the CLI rejects `winforms`/`avalonia` because they are render-plan profiles, not native window hosts.

## Runnable full loop

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase
# add --run to start the platform; see samples/SpatiotemporalShowcase/README.md for verification steps
```

It builds 5 projects + a real TSX frontend (`@gocordis/plugin-kit`), packs 4 real `.gocordis-plugin` packages (`--shared-assembly`, `--provides`, `--dependency`), installs them and writes the declaration; add `--run` to start the host. Last run also verified the control-plane loop: all 4 fibers Active, queries/pages/client JS served, disable produced the strict LIFO undo log (`client-module → dashboard → worker → writer → store`), re-enable restored Active and healthy queries.

The empty-directory developer loop (`bash samples/PluginDeveloperWorkflow/e2e.sh <out>`) was re-run green. It now packs a per-run SDK prerelease version: reusing `1.0.0` let NuGet's global cache serve an older Core and caused `MissingMethodException` at plugin activation. E2E test helpers pass `-nodeReuse:false` so reused MSBuild nodes cannot hold redirected stdout pipes open.

## Verification commands

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
(cd web/console    && npm ci && npm test && npm run build)
(cd web/plugin-kit && npm test)
bash docs/migration/verify-baseline.sh   # Go baseline drift check
```

**Last locally verified** (2026-11-24, branch `dotnet`): C# **562 passed / 1 skipped / 0 failed** across 8 test assemblies (Core 48, Conformance 154, Runtime 158+1 skipped, Extensions 62, Packaging 79, UI 3, Client 2, Product 56; PosixOnly/WindowsOnly tests report Skipped explicitly — never silent passes), frontend **103** tests green, plugin-kit tests green, solution build 0 warning/0 error. These counts are a record of that run, not a claim about any earlier SHA.

Task 01 的有限校准基线见 `docs/migration/acceptance-baseline.md`；它区分三个验收域与 E0–E4 证据等级，P0-01…P0-04 均已关闭（真实 Process↔WASM 调用与包闭环由 `CrossBackendCapabilityTests`、`BackendDispatchProductTests` 覆盖）。

Loopback HTTP tests now construct clients with `UseProxy = false`, so the full suite is reproducible with the ambient `HTTP_PROXY`/`HTTPS_PROXY` left set (verified 549/1 skipped/0 failed that way).

**CI**: no confirmed public CI result for the current HEAD; do not reuse historical 443/443 or other round results as proof for this commit. The Go baseline verification is handled by `verify-baseline.sh`, with drift reported rather than silently accepted.

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
- Package signatures are verifiable provenance, **not** an install-trust chain.
- WinForms/Avalonia are render-plan profiles, not full native hosts. Native window hosts for these profiles are an explicitly DEFERRED product capability (P2-03): do not count `UiHostKind` values as completed hosts.
- Koishi plugin market / remote distribution / browser-side reconcile loop are product references, not ported runtime features.
- Process/WASM packages without capability declarations are rejected at the product factory: the old echo/value demo path is isolated from the product. Only `endpoint` is implemented for cross-backend value kinds.
- Backend common protocol and generation fence live in `src/Gocordis.Runtime.Contracts`; Process and WASM no longer depend on `Runtime.Managed`.
- Official extension capabilities bind `Key<I…>` from contracts-only `Gocordis.Extensions.Contracts`, are published by Runtime provider fibers, and are re-exported by the SDK (no ASP.NET Core pulled into plugins). Env is opt-in (`EnvExtensionComponent`): it mutates process environment, is not host-supplied, and must be explicitly loaded by an embedding host. Pattern libraries without a Go `ServiceKey` (Broker, registry, event, observe, scheduler, HMR, package-manager, CliDispatcher, ServerHost) stay library-only; see `docs/migration/existence-audit.md` for the per-capability arbitration.
- Paper C38 (`VersionedKey<T>` + NuGet ranges instead of nominal `K×P`) remains the single accepted difference; C34 is covered via `Runtime.RehomeAsync`.

## Doc layers

1. **Current engineering spec**: root `AGENTS.md`, `docs/guide/csharp-developer-manual.md`.
2. **Current status**: this file (and `README.md` pointer).
3. **Current classification**: `docs/migration/existence-audit.md` and `docs/migration/completeness-ledger.md` (explicit carve-outs from the archive, maintained with current state).
4. **Historical migration record**: the rest of `docs/migration/` — archive only, including `README.md`, round/review documents and their old branch names, SHAs and counts.
