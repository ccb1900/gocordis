# AGENTS.md — Gocordis .NET Repository Engineering Constitution

This file is the **binding decision framework for AI agents working in this repository**. It is not a Go manual, not an architecture encyclopedia, and not a migration log. It constrains *decisions*: what this project is, how competing references are arbitrated, where layer boundaries sit, which runtime semantics are inviolable, how a plugin is developed/packaged/composed/executed, how a real product loop is verified, when not to reinvent the wheel, and when a task is complete.

Normative words: **MUST** (required), **SHOULD** (expected), **MAY** (permitted). Historical material lives under `docs/migration/` and is **archive**, not current state. Current state lives in `docs/current-status.md`; the front door is `README.md`.

---

## 1. What this repository is

A **.NET 10 plugin runtime and application platform** implementing the paper *A Programming Paradigm for Spatiotemporal Composability* (`docs/论文.pdf`, C01–C40). Time dimension = reversible effects with LIFO inverses; space dimension = declarative, reactively resolved capabilities. It is no longer a Go project: the Go tree is a frozen baseline, kept only as reference and verification input.

- **Product entry**: `dotnet run --project src/Gocordis.Platform -- --manifest <file> --store <dir> [--address <url>] [--host web] [--validate-only]`. Web is the only runnable UI host; WinForms/Avalonia are render-plan profiles kept for future native hosts and are rejected by the CLI.
- **Runnable full loop**: `bash samples/SpatiotemporalShowcase/build-and-run.sh <out>` (build → real `.gocordis-plugin` packages → install → manifest → platform), see its README.
- **Current state / counts / CI caveats**: `docs/current-status.md` (and `README.md` as pointer). Never treat a historical SHA or CI result as proof for the current HEAD.

---

## 2. Reference dimensions and arbitration

Four references, but only **three in the semantic authority chain**:

1. **The paper** (`docs/论文.pdf`) — sole semantic arbiter; C01–C40 are stable item IDs.
2. **JS Cordis 4.0.0-rc.13** — capability-coverage reference (what a mature plugin runtime exposes).
3. **Go Gocordis baseline** — implementation experience and executable-spec reference.

**Koishi** is a **product-composition reference**, not part of the authority chain: study its plugin/config/console composition shape; do **not** port `ctx.plugin`, `ns-require`, `require.cache`, or its Vite/TS toolchain into the runtime.

Rules:

- Conflicts arbitrate **top-down** (paper → JS → Go). A Go reinforcement may be kept, **MUST NOT** be presented as a paper requirement.
- **Cooperation is an engineering constraint, not a theorem premise.** Paper Thm73 assumes only acyclic `≺`, finite iterator, finite names, per-step lifecycle rules. Finite/cancellable/acyclic Apply/Cleanup is our testability discipline (C29).
- Frozen accepted differences **MUST NOT** be reopened or quietly changed: no identity-preserving `registry.replace()` (revision = retire→reinsert), no `ctx.foo` Proxy, no `Promise.all` concurrent unwind (strict serial LIFO), `EffectAsync`/`IIterativeComponent`/`CancellationToken` instead of JS generator close protocol, `Bail`+`BailValue`, .NET-native substitutions for JS-only mechanisms, manifest descriptive-only. C38 (`VersionedKey<T>` + NuGet ranges instead of nominal `K×P`) is the only paper item still accepted-different. C34 is **covered** via `Runtime.RehomeAsync` — do not call it an open difference.
- Layer boundaries are a **product boundary statement**, not a claim that every platform capability is complete: Managed = real .NET execution; Process/WASM = isolation boundaries; ALC is **not** a sandbox; package signatures are provenance, not an install-trust chain; WinForms/Avalonia = render profiles.

---

## 3. Layer boundaries (allowed dependencies)

Actual `ProjectReference` graph (enforced where noted by `BoundaryTests.cs` / PC gates):

| Layer | Project | May reference | Role |
|---|---|---|---|
| Kernel | `Gocordis.Core` | **none** (NuGet libs OK) | Runtime, Fiber, Context, effects, events, keys, realms. State setters internal. |
| Semantics | `Gocordis.Plugins` | Core | Component/Manifest/Registry, declaration reconciliation, UI/API contribution contracts. |
| Extension contracts | `Gocordis.Extensions.Contracts` | Core, Plugins | Stable `ExtensionKeys` (Key<I…>), public service interfaces and DTOs; no ASP.NET Core, no implementations. |
| Platform services | `Gocordis.Extensions` | Core, Plugins, Extension contracts | broker, event dispatch modes, timers, fs/http/socks, diagnostics. Official capabilities (Timer/HTTP/FS/Env/SOCKS/Diagnostics/Watch/RouteGuard) are published by ordinary Provider Components via `ExtensionKeys` and loaded into the one Runtime; pattern libraries without a Go ServiceKey stay library-only. |
| Backend contracts | `Gocordis.Runtime.Contracts` | Core | Physical home of `IPluginBackend`, `BackendProtocol`, `CoreBackendContext`, `GenerationTracker`. No backend implementation. |
| Backends | `Runtime.Managed` / `Runtime.Process` / `Runtime.Wasm` | Core, Plugins, Runtime.Contracts | Execution backends behind one Core contract. **Process/WASM MUST NOT reference Managed.** Backend selection belongs to the composition root. |
| Packaging | `Gocordis.Packaging` | Core, Extensions, Plugins | `.gocordis-plugin` format, store, control plane, `gocordis-ui.json`, `UiPackageBinder`. |
| Console | `Gocordis.Console.WebUi` | Core, Extensions, Plugins, Packaging | HTTP/SSE hub, SPA hosting, client-module serving, plugin control endpoints. |
| UI profiles | `Gocordis.UI.Hosts` | Core, Plugins | Web/WinForms/Avalonia render plans only. |
| SDK | `Gocordis.Client` | none | Typed console client; does not enter the runtime graph. |
| Product host | `Gocordis.Platform` | all of the above (not Client) | One Runtime, one Registry, control plane, package store, UI host. **MUST NOT** bypass `UiPackageBinder` by pre-registering raw factories. |
| Tooling | `tools/Gocordis.Cli` | Core, Packaging, Plugins, Console.WebUi, Platform | `plugin create/pack/install/...`, `app run`, `doctor`. |
| Tests | `tests/*` | their subject + allowed samples | Conformance/theorem matrix, boundary gates, product E2E. |
| Samples | `samples/*`, `samples/SpatiotemporalShowcase` | Core (+Plugins) + own contracts | Plugs must not import each other; share typed contracts via `--shared-assembly`. |

Enforced today: `CoreDeclaresNoProjectReferences`, `DeclaredGraphPointsToCore`, `ProductionProjectsDoNotReferenceTests`, `ProcessAndWasmDoNotDependOnTheManagedImplementation` (`tests/Gocordis.Runtime.Tests/BoundaryTests.cs`); `PC12_PluginRegistryNeverCreatesASecondLifecycle` and PC gates in Conformance/ProductGateTests. The rest are design rules — uphold them in review, do not assume a test catches them.

ADRs are binding design records: `docs/adr/0001` per-key realms (no ancestor fallback), `0002` derived metadata interception (right-biased monoid, no cross-root leakage), `0003` effect iterators + inertial divert, `0004` kernel event demotion (dispatch modes in Extensions). `docs/migration/adr-channel-orchestrator.md` freezes the single `Channel<RuntimeCommand>` decision domain + short-lock `Mutate` writes. `docs/plan/phase3-adr.md` is a **superseded** hierarchical-shadowing bias — never reintroduce it.

---

## 4. Runtime semantics that MUST NOT break

Twelve rules, each with executable evidence (do not weaken the test to make a change pass):

1. **Declaration authority** (C07/C08): `Inject()/Provide()` bound; `Context.Require/Provide` enforce per-key; undeclared throws. Evidence: `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`.
2. **No startup orchestration** (C19–C22): dependency satisfaction is a trigger, not code you write; no `Task.Delay`/sleep waiting, no global singleton resource; `LoadAsync` only submits declarations. Evidence: `TheoremMatrixTests.Thm73_Matrix`, `PaperConformanceTests.C22_…`.
3. **Reversible effects** (C02–C06/C26): every retractable consequence goes through `EffectAsync`/`Provide`/`OnAsync`/`LoadChildAsync`; inverses run strict **serial LIFO**. Evidence: `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`, `CoreSemanticsTests.EffectsUnwindInStrictLifoOrder`.
4. **Active ⇔ ready** (C20/C21): resource is truly usable before Apply returns. Evidence: `TheoremMatrixTests.Thm70_Matrix`.
5. **Failure = raise** (C33): Apply error → fiber `Failed` + unwind; no propagation to parent, no auto-retry; retry = corrected declaration/revision. Evidence: `CoreSemanticsTests.ApplyFailureUnwindsInstalledEffectsAndNeverRetries`.
6. **Dependency loss → Pending** (C20–C22): withdraw effects and return to Pending, then auto-reactivate; **never** Failed/panic. Evidence: `PaperConformanceTests.C22_…`, `Thm64_Matrix["dependency-withdrawal-recovery"]`.
7. **Single lifecycle authority** (C19/C21): Fiber state writable only by the decision loop; plugins submit declarations, never touch fiber state or reflect into runtime internals. Evidence: `BoundaryTests.*`, `PC12_*`.
8. **Entry survives; fiber = one enablement** (C40): `PluginManifest(id/type/enabled)`; toggle = Load/Dispose; disabled plugin remains a validated entry. Evidence: `PluginSystemTests.EnabledSwitch…`.
9. **Persistence trichotomy** (C40/§4.4): declarations (switches/config) → durable store; business results → component journal + idempotent replay; runtime-derived state/fiber table **never** persisted; `Snapshot()` read-only. Evidence: `AuditRegressionTests.SnapshotIsSafeUnderConcurrentLoadDispose`.
10. **Flush while deps alive** (C22): `Context.Require` stays readable during Cleanup until Unwound; checkpoints belong in Cleanup. Evidence: `CoreSemanticsTests.CleanupCanStillRequireItsDependency`.
11. **One provider per key** (C17): duplicates rejected in a realm; multi-implementation via `Broker<T>` / realms, never a private registry. Evidence: `CoreSemanticsTests.DuplicateProviderIsRejectedAtLoad`, `ExtensionTests.BrokerAndRegistryManageMembers`.
12. **Cooperation** (engineering, C29): finite, cancellable steps; observe `Context.CancellationToken`; cycles rejected at mount with a chain diagnostic. Evidence: `CoreSemanticsTests.DependencyCycleIsRejectedAtMount`, `DeterministicDriver.SameSeedReplaysTheSameInterleaving`.

Theorem anchors: Thm64↔C25, Thm68↔C26, Thm70↔C27, Thm73↔C29, Thm80↔C30 — each a 3-scenario matrix in `TheoremMatrixTests.cs`; C14 commuting inverses and C35 reversibility boundary have dedicated tests. Full C-item matrix: `docs/migration/paper-coverage-matrix.md` (machine-checked by `PaperItemMatrixTests`).

---

## 5. Plugins: develop, package, compose, execute

### The three dependency layers (MUST keep distinct)

Never collapse these into one mechanism — an assembly resolving says nothing about a capability being Active:

| Layer | Expresses | Mechanism | Failure mode |
|---|---|---|---|
| **Package dependency** | plugin B needs plugin A's package/version installed | `PluginDependency` + version range; install/upgrade/uninstall reverse checks (`PluginDependencyPlanner`, `PluginStore`) | install rejected / upgrade blocked |

**Assembly is declaration-led.** A package dependency only constrains the STORE; it never auto-declares or auto-activates the dependency. The application manifest is the sole assembly set; activation is decided solely by typed `Inject()/Provide()`. If a declared plugin's package requires an id that is installed but NOT in the manifest, the runtime reports `PACKAGE_DEPENDENCY_NOT_DECLARED` (a read-only warning) — never silently "satisfied". Use `PluginDependencyPlanner` as the single reverse-dependency algorithm for install/upgrade/uninstall. Do not convert package dependencies into typed `Inject()` automatically — the two are not equivalent.
| **Contract (assembly) dependency** | B compiles against A's interfaces, types, DTOs, keys | shared contracts assembly (`--shared-assembly`) loaded once into Default ALC | type-identity mismatch / load conflict |
| **Capability dependency** | B needs A's runtime capability before it may activate | `Component.Inject()/Provide()` over strong `Key<T>`; Core reconciliation | B stays `Pending` until a provider is Active; withdraws on loss |

**Shared contracts, isolated implementations.** The correct ALC placement is a product rule, not an accident:

- `Gocordis.Core`, `Gocordis.Plugins`, shared contracts → **Default ALC** (one type identity, host-owned)
- each plugin's implementation assembly and private third-party deps → **that plugin's own collectible ALC** (independent unload; different plugins may use different private versions)

Do **not** promote every plugin assembly to Default ALC, and do **not** assume plugins can share no types at all.

**Contract ownership**: `SharedAssemblyRegistry.Seal` is the ONLY public host entry for approving and sealing contracts. It verifies *all* candidate copies across installed packages are byte-identical, then loads one canonical copy into the Default ALC. Approval helpers are private/internal; a plugin cannot widen the shared-assembly whitelist through a normal API. Plugins may only reuse the sealed identity; a differing copy is rejected, a contract upgrade needs a host restart. Ordinary plugins cannot decide the process-wide contract version by loading first.

**Cross-backend capability identity does NOT use a shared assembly.** For Managed↔WASM↔Process, the host owns one `CapabilityCatalog` that allocates a single authoritative `ICapabilityKey` per `CapabilityId = (Namespace, Name)`. Managed plugins receive it via an `ICapabilityCatalog` constructor parameter; WASM declares it in the `gocordis.capabilities` custom section; Process declares it in the package manifest. All backends hand those keys to Core before activation, so the one Runtime still owns matching/binding/withdrawal/reactivation and no backend keeps a Provider/Consumer table. Wire values are `CapabilityEnvelope`/`ICapabilityEndpoint`; version is compatibility metadata (NuGet ranges, C38), never identity. `SharedAssemblyRegistry` remains ONLY for managed-only compile-time typed contracts — it is not on the cross-backend critical path.

- Implement `Component`: `Name`, `Inject()`, `Provide()`, `ApplyAsync` (single-step) or `IIterativeComponent.IterateAsync` (per-step divert). Declare exactly what you use; keys/`EventKey`s live in a shared contracts assembly (`static readonly`, reference identity) for managed-only typed contracts, or come from the host catalog for cross-backend capabilities.
- In-process composition: app/plugin projects reference Core (+Plugins) and the contracts project; never plugin→plugin or plugin→app.
- **Packaged form** (the real deployment shape): one `.gocordis-plugin` per plugin, declared with `plugin pack … --backend managed --entry-assembly <dll> --entry-type <type>`; cross-plugin typed keys **MUST** use `--shared-assembly <ContractsName>` — the host seals that DLL into `AssemblyLoadContext.Default` so every plugin sees one `Key<T>` identity; without it gating silently fails. Declare `--provides <name>` / `--dependency <id>@<range>` so the control plane's dependency view is real; a package dependency is not a capability guarantee — the consumer still waits on `Inject()`.
- Trust boundary: **Managed plugins are trusted code, not a sandbox.** ALC is collectible, not isolating; use Process/WASM for untrusted code. Only host-approved contract assemblies may be shared (see `SharedAssemblyRegistry`), full identity is checked, and shared assemblies have **process lifetime** — upgrading a shared contract requires a controlled host restart.
- UI belongs to the package: pages/panels/client modules in `gocordis-ui.json` (or inline `manifest.ui`), bound as effects of the plugin's fiber by `UiPackageBinder`; install brings UI, disable/uninstall reverses it. C# contributes data capabilities (API queries/commands), not hardcoded console UI. Pages may declare `group` (`/`-separated) for hierarchical menus and `order` for sibling sorting. A TSX frontend is built by `@gocordis/plugin-kit` (`web/plugin-kit`) into one ESM served same-origin; react/antd come from the console facade (one React instance).
- Composition root selects backends and registers the factory; control plane decorates it with UI binding. Do **not** pre-register raw factories in `PlatformApplication` (that bypasses UI binding).
- Official extension capabilities MUST be composed as plugins: stable `Key<T>` in `ExtensionKeys`, a Provider Component using `Context.Provide`, consumers using `Inject()`/`Require()`, and a disposal effect installed BEFORE the capability so LIFO unwind withdraws first. The composition root loads those providers into the one Runtime; it MUST NOT construct a private service instance and pass it directly to WebUi/console. Packaged plugins exchanging these keys declare `--shared-assembly Gocordis.Extensions.Contracts`.
- Reuse before reinventing: `Broker<T>` for multi-implementation; `Context.OnAsync/EmitAsync` is the only event registration authority (no second dispatcher registry); `Observations`/domain versions for UI invalidation; `DiagnosticsService` for declaration findings; package store + control service for persistence. No second registry, runtime, effect stack, or lifecycle authority.

---

## 6. Verifying a real product loop

A unit test is not enough. For any change touching loading/UI/console semantics, prove the chain:

**declaration → package load → backend execution → Core fiber → effect/capability/UI/API → reversible teardown**

- Commands: `dotnet build Gocordis.sln --nologo -m:1`; `dotnet test Gocordis.sln --nologo --no-build -m:1`; `cd web/console && npm ci && npm test && npm run build`; `cd web/plugin-kit && npm test`; `bash docs/migration/verify-baseline.sh` (Go baseline; drift must be reported, not silently accepted).
- Product E2E: `samples/SpatiotemporalShowcase/build-and-run.sh` + its README verification (enable/disable, strict reverse LIFO effect log, dependency view non-empty).
- Backend↔frontend contract: when a query's shape matters to the console, assert the **canonical DTO** on the C# side **and** feed the same payload through the real frontend parser (existing pattern: `framework-dashboard.test.ts`, `schema-form.test.ts`). An API 200 with an unparsable shape is a defect.
- Tests are behavior tests: call the code as its consumer does and assert the result the consumer sees. Write the failing test before the fix; see it fail for the expected reason. Concurrency claims need `-race`-equivalent rigor on the .NET side (deterministic driver / concurrent snapshot tests). Untrusted observation data is parsed at the boundary and trusted inside.

---

## 7. Prohibitions (violation = wrong implementation)

1. Manual startup order, sleep/delay dependency waits, global singleton resources, self-spinning retries after `DependencyMissingException`.
2. Touching fiber state outside the decision loop / Public API, or reflection/type assertion into runtime internals; holding another component's `Fiber` to drive it.
3. Persisting fiber tables or using `Snapshot()` as a recovery source; business state outside component journal.
4. Undeclared `Require`/`Provide`, relying on the runtime exception as a fallback.
5. Plugin→plugin or plugin→app imports; keys not in a shared contracts assembly; packaged plugins omitting `--shared-assembly`.
6. Infinite blocking or ignoring `Context.CancellationToken`; declaring dependency cycles; retry storms; loading a new component inside Cleanup as "compensation".
7. Untracked background tasks/files outside `EffectAsync`; claiming reversibility for externally emitted HTTP/log/process side effects (C35).
8. A second registry/routing layer, a second lifecycle authority, or a second `Runtime`/effect stack; pre-registering raw factories that bypass package-UI binding.
9. Treating ALC as a sandbox; promoting arbitrary assemblies into `Default` ALC; presenting WinForms/Avalonia profiles as native hosts or signatures as an install-trust chain.
10. Reopening frozen accepted differences; promoting Adapted/Divergent ledger rows to Equivalent; reusing historical SHA/CI/test counts for the current HEAD.
11. **Claiming a capability "migrated/done" because it exists at another layer.** You MUST prove the full call chain — plugin declaration, package load, backend execution, Core fiber, effect/capability/UI/API, and reversible teardown — before declaring completion. Existence in the Go baseline, in Core, or as a type definition is not completion.

---

## 8. Completion checklist (MUST pass before stopping)

- [ ] `dotnet build Gocordis.sln` clean (0 warning / 0 error); `dotnet test Gocordis.sln` green.
- [ ] `web/console`: `npm test` + `npm run build` green; `web/plugin-kit`: `npm test` green.
- [ ] Changed semantics anchored to paper item IDs in `docs/migration/paper-coverage-matrix.md`; no frozen difference reopened.
- [ ] Each component: `Inject/Provide` match actual `Require/Provide`; external resources via `EffectAsync`; readiness before Active.
- [ ] State persistence limited to declarations + journals; zero runtime-state persistence; no second lifecycle authority.
- [ ] Packaged plugins: shared contracts declared; provides/dependencies declared; UI (if any) in `gocordis-ui.json`, not in console code.
- [ ] Backend↔frontend query shapes asserted on both sides when the console consumes them; no new 404s or shape mismatches.
- [ ] Full product loop exercised (showcase enable/disable + LIFO log + dependency view) when loading/UI/console semantics changed.
- [ ] Docs: current state only in `docs/current-status.md` / `README.md`; historical claims stay under `docs/migration/`; no stale branch/test-count text in current docs.

When the checklist is green, **stop**. Do not continue into open-ended source fixing; report the result and any remaining known gaps.

---

## 9. References

- Current state: `README.md`, `docs/current-status.md`.
- Engineering manual: `docs/guide/csharp-developer-manual.md`; user manual: `docs/guide/user-manual.md`.
- Semantic authority: `docs/论文.pdf`, `docs/migration/semantic-mapping.md` (conflict rulings §4, accepted differences §5, platform boundary §7).
- Coverage ledger: `docs/migration/feature-matrix.md`, `docs/migration/paper-coverage-matrix.md`, `docs/migration/theorem-matrix.md`.
- ADRs: `docs/adr/0001`–`0004`; channel orchestrator: `docs/migration/adr-channel-orchestrator.md`.
- Product chain: `docs/migration/product-execution-chain.md`; runtime boundary: `docs/migration/runtime-integrity.md`.
- Runnable showcase: `samples/SpatiotemporalShowcase/`; frontend kit: `web/plugin-kit/README.md`.
- Historical migration record (archive only): `docs/migration/README.md` and its round/review documents.
