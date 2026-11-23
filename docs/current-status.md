# Current Status — Gocordis .NET

This is the **only** place that records current branch/HEAD, entry commands, verified counts and known gaps. Everything under `docs/migration/` is a historical migration archive; do not read its branch names, SHAs or test counts as current state. `README.md` is the front door and points here.

- **Branch**: `dotnet` (tracks `origin/dotnet`).
- **Baseline for review**: the `dotnet` branch HEAD at the time of the review; always refer to the actual `git rev-parse HEAD`, not a remembered commit.
- **Engineering constitution**: `AGENTS.md` (root). Engineering manual: `docs/guide/csharp-developer-manual.md`. User manual: `docs/guide/user-manual.md`.

## Product entry

```bash
dotnet run --project src/Gocordis.Platform -- \
  --manifest <gocordis.app.json> --store <plugins-dir> [--address http://127.0.0.1:8170] [--host web|winforms|avalonia] [--validate-only]
```

Console: `http://127.0.0.1:8170/` by default when an explicit address is given. WinForms/Avalonia are render-plan profiles, **not** native window hosts.

## Runnable full loop

```bash
bash samples/SpatiotemporalShowcase/build-and-run.sh /tmp/gocordis-showcase
# add --run to start the platform; see samples/SpatiotemporalShowcase/README.md for verification steps
```

It builds 5 projects + a real TSX frontend (`@gocordis/plugin-kit`), packs 4 real `.gocordis-plugin` packages (`--shared-assembly`, `--provides`, `--dependency`), installs them, writes the declaration, and starts the host.

## Verification commands

```bash
dotnet build Gocordis.sln --nologo -m:1
dotnet test  Gocordis.sln --nologo --no-build -m:1
(cd web/console    && npm ci && npm test && npm run build)
(cd web/plugin-kit && npm test)
bash docs/migration/verify-baseline.sh   # Go baseline drift check
```

**Last locally verified** (2026-11-23, branch `dotnet`): C# **481** tests green, frontend **102** tests green, plugin-kit tests green, solution build 0 warning/0 error. These counts are a record of that run, not a claim about any earlier SHA.

Task 01 的有限校准基线见 `docs/migration/acceptance-baseline.md`；它区分三个验收域与 E0–E4 证据等级，P0-01…P0-04 均已关闭（真实 Process↔WASM 调用与包闭环由 `CrossBackendCapabilityTests`、`BackendDispatchProductTests` 覆盖）。

Local caveat: host-header ACL tests (`WebUiPolicyTests`, `PlatformApplicationTests`) fail with `502 BadGateway` when `HTTP_PROXY`/`HTTPS_PROXY` are set in the environment; run the suite with those variables unset to test the actual server.

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

## Known boundaries (not "done" claims)

- Managed ALC is **not** a security sandbox; Process/WASM are the isolation boundaries; managed plugins are trusted code. Shared contract assemblies are host-approved, full-identity checked, and process-lifetime (contract upgrades require a controlled host restart).
- Package signatures are verifiable provenance, **not** an install-trust chain.
- WinForms/Avalonia are render-plan profiles, not full native hosts.
- Koishi plugin market / remote distribution / browser-side reconcile loop are product references, not ported runtime features.
- Paper C38 (`VersionedKey<T>` + NuGet ranges instead of nominal `K×P`) remains the single accepted difference; C34 is covered via `Runtime.RehomeAsync`.

## Doc layers

1. **Current engineering spec**: root `AGENTS.md`, `docs/guide/csharp-developer-manual.md`.
2. **Current status**: this file (and `README.md` pointer).
3. **Historical migration record**: `docs/migration/` — archive only, including `README.md`, round/review documents and their old branch names, SHAs and counts.
