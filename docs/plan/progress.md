> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# Progress Log

## Session: 2026-09-06

### Phase: Planning

- **Status:** complete
- Actions taken:
  - Converted the paper-first assessment into a seven-phase implementation plan with acceptance gates.
  - Recorded current architectural strengths, semantic gaps, and the blocked test baseline.
- Files created:
  - `task_plan.md`
  - `findings.md`
  - `progress.md`

## Test Results

| Test | Input | Expected | Actual | Status |
|---|---|---|---|---|
| Baseline race suite | `go test -race ./...` | Compile and test all packages | Fails before tests: Go 1.25 toolchain vs Go 1.26 cached objects, plus inaccessible global cache paths | Blocked |

## 5-Question Reboot Check

| Question | Answer |
|---|---|
| Where am I? | Planning is complete; implementation has not started. |
| Where am I going? | Phases 1–7 in `task_plan.md`. |
| What's the goal? | A buildable, paper-aligned, test-evidenced Go implementation. |
| What have I learned? | See `findings.md`. |
| What have I done? | Created the persistent execution plan and recorded the baseline. |

## Session: 2026-09-06 (cont.) — Phase 1 executed

- **Status:** Phase 1 complete (implementation authorized scope: Phase 1 only).
- Actions taken:
  - Added `.go-version` (1.26.0) + `.github/workflows/ci.yml` (fmt/vet/test/race + repeated race for runtime/integration/hmr/http).
  - Recorded toolchain/cache policy and package baseline in `docs/plan/baseline.md`.
  - Verified on Go 1.26.0 (version-fox) with isolated GOCACHE: `go test -count=1 ./...` twice + `go test -race -count=1 ./...` all PASS; no environment-induced skips.
- Corrected prior record: the "Go 1.25 vs 1.26 cache mismatch blocks tests" finding no longer applies with the pinned-toolchain + isolated-cache policy (baseline was already green this session).
- Not started (awaiting authorization): Phase 2 (Kernel declaration enforcement), Phases 3–7.

## Session: 2026-09-06 (cont.) — Phase 2 executed

- **Status:** Phase 2 complete.
- Kernel changes (`runtime/`): activation-local immutable Inject/Provide declaration sets; `Require`/`Provide` membership enforcement (upper-bound/subset rule, see `docs/plan/phase2-decision.md`); panic containment at Apply / Effect install / inverse & Cleanup boundaries (`ErrComponentApplyPanic`, `ErrEffectInstallPanic`, `ErrInversePanic`).
- Tests: `runtime/declarations_containment_test.go` (D1–D8) added; all existing suites pass unchanged (declarations were already honest).
- Not started: Phase 3 (scoped Context) — awaits ADR + authorization per plan.

## Session: 2026-09-06 (cont.) — Phase 3 ADR

- **Status:** ADR written (`docs/plan/phase3-adr.md`); Kernel implementation NOT started (needs authorization + 3 decisions in ADR §11).
- Realm model: activation-owned realms; child derivation (inherit + override, no parent mutation); read-through resolution; per-realm exclusivity; interceptors = read-time transform chains; root-realm = today's single-realm special case (source compatible).

## Session: 2026-09-06 (cont.) — Phase 3 implementation (incremental)

- **Status:** increment 1 done.
- ADR revised per architect feedback (`docs/plan/phase3-adr.md` v2): realm = explicit scope/ownership domain; Runtime.Load → root realm; ctx.Child → inherit; explicit scope creates child realm; child may shadow ancestors; per-realm own-map exclusivity; identity-resolved dependency edges; `runtime.Intercept` generic free function (Go methods can't be generic).
- Kernel increment 1 (backward compatible): `realm` type (`runtime/providers.go`), Runtime.rootRealm, Fiber/Context realm, Provide→own realm, Require→realm-path lookup, orchestrator dependency edges keyed by resolved provider identity; internal registry tests migrated.
- Gates: `go test ./...`, `go test -race ./...`, `go vet ./...` green (incl. fix of pre-existing E2E-11 timing flake: wait for replaced config fiber Active).
- Remaining (increment 2+): explicit scope derivation (`ctx.Derive`/Child scope option → child realm + shadow), `runtime.Intercept`, sibling-isolation/override/interception acceptance tests, full Phase-3 gate re-run.

## Session: 2026-09-06 (cont.) — Phase 3 increment 2 done

- Explicit scope derivation: `Context.Child(comp, runtime.WithScope())` creates a child realm (parent = current realm); default inherits; child realms may shadow ancestors (`ownership.go`/`command.go`).
- Interception: generic free function `runtime.Intercept[T](ctx, key, fn)`; per-realm install-order chain applied on Require in ancestor->descendant order; Effect-reversible; panic contained. Realm stores/removes chain (`providers.go`, `context.go`).
- Acceptance tests: sibling isolation + parent-realm invisibility; intercept order (5 -> 6 -> 60); interceptor panic contained (`runtime/scoped_realm_test.go`).
- Gates green: `go test ./...`, `go test -race ./...`, `go vet ./...`.
- Next: Phase 4 (dependency-graph cycle detection + progress/linearization audits + bounded randomized tests).

## Session: 2026-09-06 (cont.) — Paper-Level Theorem Verification (docs/review) Phase 1–3

- Phase 1/2 (harness): `runtime/theorem_verification_test.go` — independent Model/ObservableState/Trace (seed replay) + deterministic `math/rand/v2` + failure taxonomy + oracle self-checks. No production change.
- Phase 3 (T59 Preservation): `runtime/t59_internal_test.go` — white-box P1..P5 invariant checker (parent validity, per-realm provider uniqueness + identity validity, active-fiber dependency validity, snapshot consistency), deterministic provider dispose/reload/consumer-drop cycles over 5 seeds × 60 steps; no sleep-based proof (signal waits only).
- Gates: runtime tests + `-race` green.
- Next: Phase 4 (T61 baseline observational equivalence), then T63/T66/T73 + FuzzInterleaving + docs/theorem-verification.md (§44 final report only when all pass).

## Session: 2026-09-06 (cont.) — Theorem Verification Phase 4–5

- Phase 4 (T61): `runtime/t61_internal_test.go` — LIFO/exactly-once effect recovery (open:1,2,3 → close:3,2,1), baseline equivalence (R1 loads+disposes transient F ⇒ same active set as R0, no provider/effect residue). Signal-driven, no sleep.
- Phase 5 (T63): `runtime/t63_internal_test.go` — randomized provider up/down cycles (5 seeds × 40) with user-level event oracle: every consumer Apply must lie between P:up and P:down; consumers also assert dep present at Apply.
- Gates: runtime tests + `-race` green (T59/T61/T63).
- Next: Phase 6 T66 (deterministic bounded-progress driver), Phase 7 T73 (DAG legal schedules), Phase 8 FuzzInterleaving, Phase 10 docs/theorem-verification.md + final §44 report.

## Session: 2026-09-06 (cont.) — Theorem Verification Phase 6–8 + docs

- T66: test-only deterministic quiescence driver (scan runs on orchestrator via probe command; waits via generation signals, no sleeps) + bounded-progress randomized test; fixed transient false deadlock with settling probe.
- T73: 24 legal mount permutations × 4 seeds observational-equivalence; explicit `t73Precondition`; synchronized recorder.
- Fuzz: `FuzzInterleaving` (seed corpus; signal-driven scenario; checks T59/T63); 10s fuzz PASS (~700k execs). Go forbids `-fuzz` across multiple packages → AC-04 targets `./runtime/`.
- Docs: `docs/theorem-verification.md` (matrix + limitations).
- Overall: **CONDITIONAL PASS** (remaining: automated shrinker, broader generators). Gates: `go test ./...`, `go test -race ./...`, AC-03, AC-04 all PASS.

## Session: 2026-09-06 (cont.) — Phase 4 done

- Declared-dependency cycle detection (`runtime/dependency_cycle.go`), realm-aware (provider reachable only along consumer realm path; sibling scopes can't false-positive), self-loops ignored; rejects at `Runtime.Load` and `ctx.Child` with ErrDependencyCycle + actionable chain diagnostics.
- Tests: `runtime/phase4_cycle_test.go` (root two-node cycle rejected both orders; child-boundary same-realm cycle fails activator), `runtime/phase4_random_test.go` (seeded bounded random load/dispose churn).
- Linearization/sync audit: single serialized orchestrator decision domain; all external reads snapshot-guarded; no new gaps found.
- Gates green: `go test ./...`, `go test -race ./...`, `go vet ./...`.
- Next: Phase 5 (theorem-to-test evidence suite: invariants, randomized effect/provider/context schedules, ordering & confluence generators, fuzz smoke).

## Session: 2026-09-06 (cont.) — Phase 5 (partial)

- Executable preservation invariants (`runtime/proof_invariants_test.go`), checked synchronously inside the orchestrator decision domain (no mid-transition sampling): ownership tree valid/acyclic; provider records never outlive their owning activation (Loading/Active/Unloading same activation); every Active consumer's declared deps satisfiable on its realm path.
- Bounded random schedules over providers/consumers with per-step invariant checks (seeds 1,7,42,2026, count=5 stable) + fuzz smoke `FuzzPreservation` (1416 execs, 3s, PASS) + `-race ./runtime/` green.
- Remaining: recovery-exactness randomized effect stacks; ordering incl. realm-specific providers; generated confluence schedules with canonical observables.

## Session: 2026-09-06 (cont.) — Phases 5–7 closed

- Phase 5 complete: recovery-exactness random stacks, concurrent ordering (incl. realm providers), generated confluence schedules (mount-only; identity-aware disposal documented out of scope), preservation invariants, fuzz smoke — race `-count=3` stable, full `go test -race ./...` green.
- Phase 6 complete: extensions revalidated under corrected Kernel with zero source changes; HMR/WASM/E2E evidence green; single lifecycle authority confirmed.
- Phase 7 complete (documented limitations): paper mapping + trust boundary/migration in `docs/plan/paper-mapping.md`; CI workflow added but not executed on a runner; benchmarks deferred (non-semantic); Windows runtime verification pending a Windows host.

## Session: 2026-09-06 (cont.) — Reviewer-gap closures

- Quiescence-with-Active, T63 withdrawal ordering, acyclic precedence invariant (oracle) added; runtime + race green. Still CONDITIONAL (automated shrink + op-level DAG schedule oracle remain).

## Session: 2026-09-06 (cont.) — Code updates per review

- T73 op-level DAG topological schedules: `t73ProviderConsumerDAG` (precedence edges incl. "consumers mount before provider disposal"), `topoSchedules` (bounded Kahn, deterministic seeds), `runT73DAGSchedule` (per-step Ready/Gone so schedules are truly realized), `TestT73DAGTopologicalSchedules` — PASS.
- Earlier closures (quiescence-with-Active, T63 withdrawal ordering, T66 acyclic-precedence oracle) kept.
- Gates: `go test ./runtime`, `-race` targeted PASS.

## Session: 2026-09-06 (cont.) — "unimplemented → implemented" round

- AC-12 shrinker: `runtime/minimize_test.go` `Minimize` (deterministic delta-debugging) + `TestMinimizeDeterministic` (40→2, minimality + repeatability).
- T73 expansion: `t73TwoSubsystemDAG` + `TestT73TwoIndependentSubsystems` (two independent providers+consumers+effect, independence encoded; 16×2 schedules converge to [P1 P1 P2]).
- All T73/T66/minimize tests + `-race` PASS; gofmt clean.

## Session: 2026-09-07 — Theorem review arc C-1/C-2 (deterministic single-fiber)

- C-1 (T61 single-fiber baseline, `runtime/c1_t61_min_test.go`): reviewer verdict **PASS** at `4a5e8fc`. Active-phase observation sanity (`base != active`, canonical diagnostics) + `post == base`; observation oracle proven non-degenerate.
- C-2 (T59 every-step, `runtime/c2_t59_every_step_test.go`): reviewer verdict **CONDITIONAL PASS** at `6f1f1b4` (this session's review, no rework requested):
  - PASS: oracle implementation (registry → fiber snapshot → realm snapshot → provider registry → dependency snapshot → provider→consumer graph → P1–P5), P1–P5 coverage, T59/quiescence separation (T59 PASS at Loading/Unloading while quiescence FAIL; T66 ≠ T59), single-fiber every-step, production isolation (test-only).
  - CONDITIONAL: **orchestrator boundary** — `checkT59()` reads orchestrator-owned `o.graph`; the C-2 driver only establishes `Fiber.State()`/pending based boundaries, not an explicit "orchestrator idle / current command fully applied" proof. Classified as **theorem harness boundary debt**, not oracle correctness failure (single-fiber scenario has no real graph edges).
- Recorded debt (C-3 precondition):
  1. First C-3 spec rule: **T63 test observation boundary = orchestrator probe / orchestrator-owned semantic checkpoint** (same pattern as `t59CheckOnOrchestrator`), so the C-2 boundary gap is not replicated when `o.graph` becomes a real oracle input.
  2. Open spec question to settle before C-3 (do NOT reverse-derive from runtime code): realm resolution when a child realm holds a retiring record while an ancestor realm holds an active provider for the same key — does the retiring child record block parent fallback, or is it treated as unavailable and resolution continues to the ancestor?
- Next (not started, awaiting authorization): C-3 (T63 ordering, multi-fiber P→C scenarios), then C-2 full-oracle arms get committed multi-fiber exercise.

## Session: 2026-09-07 — Developer Console UI-01 (Observation Model, design-only)

- New spec: Developer Console Specification v0.1 (UI-01 → UI-09 ladder).
- Audit: Runtime has NO public Observation API; only test-internal semantic projection (`observe()` in `runtime/obs_test.go`, quiescence-gated). Extension precedent: `registry.Snapshot()/Subscribe()`.
- Deliverable: `docs/plan/devconsole-ui01-observation-model.md` — frozen UI-01 model (RuntimeSnapshot/FiberSnapshot/ProviderView/DependencyView/EffectView/ScopeSnapshot/RuntimeEvent), expressiveness matrix vs kernel internals, and 6 decisions required before UI-02 (component identity, stable ScopeID, effect Type/Key metadata, timestamps, snapshot consistency semantics, RuntimeID source). No production code, no UI, no Snapshot/Subscribe (per §41/§42).
- Next (not started, awaiting authorization): UI-02 Snapshot API producer per §5 decisions.

## Session: 2026-09-07 — GAP-01 Retiring Record Shadowing conformance closure

- Closed the sole P0 PARTIAL from the v0.1 convergence audit (`b09462c`) without touching production code.
- Frozen decision (convergence-matrix.md §F): a nearest Retiring provider record shadows ancestor providers for the same key; `Retiring != Absent`; shadowing is realm-local; owner Gone / record removed re-opens ancestor eligibility. Recorded as operational refinement preserving the Paper's nearest-binding/lifecycle semantics (not a verbatim Paper rule); stc-go correspondence external-required (GAP-03).
- Conformance test: `runtime/gap01_retiring_shadow_test.go` `TestRetiringProviderShadowsAncestor` — deterministic kernel driver (det mode) timeline Active → Retiring → Gone; orchestrator-linearized resolve probes + Snapshot assert no-ancestor-fallback while the retiring record is physically present (incl. through B's own gated Unloading), ancestor valid at root throughout, Gone → ancestor eligible, explicit driver reconcile rebinds the child consumer onto the ancestor.
- C-2/C-3 open spec question (realm resolution over a retiring child record vs ancestor fallback) resolved in favor of block-no-fallback, matching the existing `resolveDependency` implementation.
- Gates: `go test ./runtime/...` PASS; `go test -race ./runtime/...` PASS; production diff zero (test + docs only).
- Matrix: P0-12 → PASS; Status Summary P0 12 PASS / 0 PARTIAL; GAP-01 gap log CLOSED.

## Session: 2026-11-20 — Console UI-01 Lifecycle & Composition (client module reconcile)

- Closed the frontend half of the plugin UI lifecycle: the Go side was already composition-governed (`ui-client` ctx.Effect register/unregister, manifest 404s on disable), but the browser never unloaded — `loadedModules` was add-only (failed loads poisoned forever), register dispose handles were dropped, and a disabled plugin's renderers stayed live until a page reload.
- Active Module Registry: `web/console/src/lib/client-modules.ts` rebuilt around `createModuleManager` — reconcile diffs the manifest against active activations; activate = import (cached ESM) + `register(facade)`; dispose = module-returned dispose FIRST, then facade-collected inverses LIFO (exact mirror of `runtime/component.go` Cleanup ordering). Failed import/register rolls back and retries (never remembered); failing manifest fetch keeps activations (unknown ≠ empty); reconciles serialize through a promise queue; `reconcileClientModules()` driven from `main.tsx` bootstrap + `App.tsx` composition domain.
- Go manifest revision: `GET /api/ui/client-modules` URLs now carry `?v=<sha256[:4] hex>` content hash (`extensions/console/webui/server.go` clientModuleURL) — in-session plugin file updates re-import instead of serving the stale ESM cache; unreadable files degrade to the bare URL.
- Docs: `docs/guide/ui-plugin-lifecycle.md` (lifecycle + register contract + runtime mirror), `web/console/README.md` module lifecycle section.
- Tests: `web/console/src/lib/client-modules.test.ts` rewritten — 14 cases incl. 100-cycle leak test (importer 1×, register 100×, dispose 100×, zero registry residue), rollback/retry isolation, dispose idempotence, serialized overlap; `extensions/console/webui/client_modules_test.go` pins the `?v=` wire contract + revision-tracks-content. Gates: `go build/vet/gofmt` clean, `go test -race ./extensions/console/...` PASS, `tsc --noEmit` clean, vitest 30/30 PASS.
- Deferred to UI-02: Widget/Settings/Action extension-point contracts (`api.pages.register` style facade); `ui-page` factory for DiscoverPlugins rows.

## Session: 2026-11-20 — UI-01 paper-conformance review round (twelve-law audit + anchoring)

- Audited the client-module lifecycle work against AGENTS.md §1 (12 iron laws) with first-source evidence. Verdict: semantics conform; two ANCHORING gaps fixed, zero behavior change.
- F1 retry anchoring: "retry only on reconcile triggers" is the mirror of the config Controller's applied-diff reconcile (extensions/config/config.go:18-19, 221, 486-497 — a failed add never enters `applied`, every Reconcile re-attempts by diff; "no automatic retry" = no self-loop). Iron law 5's no-auto-retry binds the Runtime fiber (Failed outcome blocks re-entry); the frontend module load is not a fiber — the ui-client component stays Active. Anchored in client-modules.ts header comment + docs/guide/ui-plugin-lifecycle.md §失败与重试的论文锚定.
- F2 ROADMAP §1.3 class A membership added: console 前端模块调和 (§5.2.1 disable→dispose / re-enable→re-register, `?v=` revision = mini-HMR).
- Considered and rejected (logged): explicit frontend Failed table (redundant with not-landing-in-activations, matches Controller applied semantics); moduleCache old-revision pruning (growth bounded by operator actions; browser ESM cache holds the same objects anyway).
- `?v=` compatibility verified: Go serves on `r.URL.Path` only (query excluded), frontend compares URL strings opaquely, plugin-kit parses nothing.
- Next: browser E2E smoke (real ESM import / EventSource / React dispose), then ordered commits.

## Session: 2026-11-20 — UI-01 browser E2E + late-registration reactivity fix

- Browser E2E (real Chromium via playwright-core + a temporary .e2e-smoke harness — runtime + configwatch + webui + fixture ui-client, removed after the run; script kept in /tmp): 18/18 assertions pass — enable→page+marker render (SSE 观察流在线), disable→page/marker vanish + dispose exactly once, re-enable→register re-runs on the cached module body (registers=2, bodies=1), content revision (?v= change)→module body re-executes + hot re-render, three fast toggles→counters exact (registers=6/disposes=5), zero duplicate markers/menu items, zero page errors. Screenshots verified.
- The E2E caught one real defect unit tests could not: a page whose renderer registers LATE (module import finishing after the pages fetch) rendered the "renderer not registered" fallback forever — the registry Maps were not reactive. Fix (TDD): registry.tsx now bumps a version on every real register/dispose (identity-guarded stale disposals don't bump) and exports subscribeRenderers/rendererRegistryVersion; App reads it via useSyncExternalStore and pins it into the ViewContext memo — renderer availability is a dependency that re-triggers, not a race (paper: dependency satisfaction is the trigger, 铁律 2).
- Gates after fix: vitest 32/32 (new registry.test.tsx), tsc clean, go build/vet/gofmt clean, `go test -race ./...` full repo PASS, E2E 18/18 ALL PASS.

## Session: 2026-11-20 — UI-02 five extension points + plugin UI API + ui-page factory

- Completed the remaining GoCordis UI-framework items from the original plan: Widget/Settings/Action extension points (Page/Panel existed), the grouped plugin UI API, and the missing ui-page factory (DiscoverPlugins emitted ui-page rows with no factory — manifest [[pages]] had no runtime effect).
- Contract ruling (paper-anchored, documented): `api.pages.register` registers a page RENDERER, not a page definition — definitions stay in Go composition (ui-page component / RegisterPage); a frontend-declared page would be a second lifecycle authority (铁律 1/7).
- Frontend: registry gains widgets/actions/settings maps with the reactive version semantics (TDD); built-in `widget` block kind composes registered widgets declaratively ({kind:"widget", params} with $focus resolution, explicit fallback); SettingsSlot in the explorer renders a plugin's registered settings UI (fetch/save owned by the slot) with ConfigEditor fallback; ActionBar surfaces context actions above the page card; ClientModuleAPI split into BaseModuleAPI (globalThis shim) + grouped facade (pages/panels/blocks/widgets/settings/actions, all manager-tracked, settings bound to the module name).
- Go: host.NewUIPageComponent — page_id/title/route/renderer/views/actions config, validation at construction, Effect-owned RegisterPage, dispose/re-enable lifecycle tested.
- Browser E2E (real Chromium, 17/17): widget renders via declarative block; action runs from the shell dropdown; registered settings UI replaces ConfigEditor and the save round-trip persists (threshold=42); disable withdraws all three (settings falls back); re-enable restores all. Screenshots verified.
- E2E caught a PRE-EXISTING crash: PluginLifecycle.Removed returning a nil slice marshals as JSON null and Explorer crashed on removed.length. Fixed at the api boundary (plugins/removedPlugins coerce null→[]).
- Gates: vitest 36/36, tsc clean, go build/vet/gofmt clean, `go test -race ./extensions/console/...` PASS, full-repo -race PASS.

## Session: 2026-11-20 — UI-03 framework contract hardening (four review areas)

- Source review first (two explore passes), then fixes per area. Paper discipline: no kernel semantics touched; §6.3 capability control stays at P3/ADR-0002.
- P0 authorization: webui `AccessPolicy` seam (`SetAccessPolicy`; kinds query/command/lifecycle/config; deny → 403 forbidden; nil policy = default allow = dev posture unchanged, documented as NOT a security boundary by itself). Frontend `when` doc upgraded: display filter, never a boundary. webui tests pin all four gated surfaces + handler-not-called + Kind/Name/request seen.
- P1 consistency: fixed a REAL defect — config.go replace left the still-live old fiber orphaned when removeOne failed (comment said "do not overlay" but code overlaid anyway): failed-replacement ids now skip the addOne; C21 regression (Gone-failure via blocked cleanup + tight ctx; retry completes). Frontend: ModuleManager exposes reconciling state (subscribeState/state); App distinguishes "插件模块加载中" from a settled "未注册" error (not-ready ≠ failed, 铁律 4 as display corollary).
- P1 isolation: activation facade warns on cross-module overrides of any extension-point key (page/panel/block/widget/action/settings), silent for self re-register (vitest both cases); proc backend unexpected death now publishes `plugin.exited` observation (client.Exited()/Closed(), watcher goroutine, expected teardown silent; liveness test via PROCPLUGIN_CHILD re-exec proves observe + fiber-stays-Active degrade-not-block + dispose silence).
- P1 version contracts: FACADE_VERSION="1" on BaseModuleAPI.frameworkVersion (globalThis + per-module facade); plugin-kit shims guard missing facade with a readable error (was undefined property-access crash); kit README compatibility rules (major alignment; `?v=` ≠ contract version).
- Corrected one plan-stage premise honestly: row-action command errors are NOT swallowed (App's ctx.hubCommand catches into shell hubError) — verified by reading App.tsx:97-107; no change needed, E2E now proves the forbidden message reaches the page.
- E2E (browser, 8/8): two display-identical actions, server denies one — UI click → shell hubError "forbidden: ..."; direct curl probes → 202 vs 403 {"code":"forbidden"}; screenshot evidence.
- Gates: vitest 38/38; tsc clean; go build/vet/gofmt clean; `go test -race ./...` full repo PASS.
- Deferred explicitly: Framework UI shell/nav/theme convergence + Plugin Kit full DX (UI-04); request-ctx threading into Host.Query/Command (Wails signature impact, separate iteration); meta inventory filtered by policy.

## Session: 2026-11-20 — Joint-review framework side: follows (plugin unit) + plugin-error (loud bad manifests)

- Joint review of gocordis + industrial-data-collector; framework-scoped findings fixed here first so the app can pin them.
- Plugin-unit co-lifecycle WITHOUT a second lifecycle authority (铁律 7): proc-plugin now Provides `console:plugin-alive/<id>` (host.PluginAliveKey, declared in Provide(), provided at top of Apply — semantics = declaration active, NOT process health: degrade paths keep the marker, so UI survives a broken backend exactly per the existing degrade-not-block contract; process death is not a lifecycle event, 铁律 12). DiscoverPlugins attaches `follows=<name>` to ui-client/ui-page rows when the manifest has a backend (frontend-only rows get none — a dangling provider would wait Pending forever, pinned by TestDependencyPendingUntilProvider). Both UI components parse optional follows and add the Requires per-instance (key identity is (type,name), construction-site-independent — domain_contract_test).
  - Cascade proven by TestBackendSwitchCascadesUIClient (shown RED first: state stayed Active; green after): backend Dispose+Gone → UI fiber Active→Pending (never Failed), ListClientModules empty; re-enable (new Load) → Runtime re-activates the consumer automatically.
- Bad manifests surface (铁律 6, restores discover.go's own doc claim "must surface, not vanish" that contradicted the skip): unparsable / unreadable-but-present / name-mismatch expand to ONE `plugin-error` row (id = directory name — the declared name is untrusted) whose Apply raises → Failed with reason in the explorer; repairing the manifest changes the row → replace → auto-recovery (fix-the-declaration re-enablement, 铁律 5). Missing manifest.toml still = not a plugin directory. warnOncePerDir deleted (its map also leaked entries per broken dir forever). NewErrorComponent + tests (activation raise carries reason, default reason, rows incl. error-row assertions + follows assertions).
- Docs: user-manual §13 (follows semantics, plugin-error contract, factory registration now lists plugin-error), ui-plugin-lifecycle.md new 插件业务单元 section (incl. the explicit non-cascade ruling for degrade), ROADMAP §1.3 class A updated.
- Gates: full `go test -race ./...` PASS (zero FAIL lines), build/vet/gofmt clean. Applications enabling discovery must register the plugin-error factory — documented loudly; config validation rejects unknown types for the whole document, which is the intended (visible) failure if they forget.
