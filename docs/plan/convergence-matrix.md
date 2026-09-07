# gocordis Convergence Matrix v0.1

Authority: `docs/Dynamic Composable Runtime — Convergence & Completion Specification v0.1.md` (§5–§7, §23).
Status: **DRAFT v0.1 — audit evidence only, no production code changed.**
Date: 2026-09-07.

## Purpose

唯一的 Convergence Matrix。逐项记录 capability 的 gocordis implementation /
tests / Status / Priority / Gap / Action，作为 v0.1 Completion Gate (§25) 的判定依据。
本文档只做 audit，不修改 Kernel / Extension / Public API。

## Evidence conventions

- 每一行必须能由仓库内 evidence 支持。无法在仓库内验证的项，明确标注 `external-required`，不臆造引用。
- 权威层级 (spec §2)：Paper > stc-go / JS Cordis > gocordis spec > gocordis implementation。
  Paper / stc-go / Cordis 原文不在本仓库，本矩阵的 reference 锚点来自仓库内已固化的映射文档：
  `docs/plan/paper-mapping.md`、`docs/theorem-verification.md`、`docs/review/*.md`、各 extension spec。
  跨源 correspondence 尚未闭合的项记入 GAP-03。
- Status 定义按 spec §6；Priority 按 spec §7。

## Status Summary

| Bucket | PASS | PARTIAL | MISSING | N/A | Blocker |
|---|---|---|---|---|---|
| P0 paper semantics (§8.1–8.8, §10–§14) | 12 | 0 | 0 | 0 | — |
| Theorem closure (§9) | 5 | 0 | 0 | 0 | breadth items GAP-02 |
| Required P1 engineering (§7 P1, §15–§21) | 12 | 0 | 0 | 0 | — |
| P2 ecosystem (§3) | 0 | 0 | 0 | 5 | — |

Completion gate (§25) NOT yet satisfiable：GAP-02（P1 breadth）、GAP-03（external correspondence）未闭合。GAP-01（唯一 P0 PARTIAL）已闭合为 PASS（见文末 Closure Record）。

---

## A. P0 — Paper Semantics (§8.1–8.8)

| ID | Capability (spec §) | gocordis implementation | gocordis tests / evidence | Status |
|---|---|---|---|---|
| P0-01 | Component → Fiber identity separation (§8.1, UI-01 §5.1) | `runtime/component.go`, `runtime/fiber.go`, `runtime/id.go` (FiberID instance identity; Component 仅为定义/标签，无 ComponentID) | `runtime/ui02_snapshot_test.go` (FiberSnapshot ID vs Name), `runtime/declarations_containment_test.go` D1–D8 | PASS |
| P0-02 | Context = Effect + Coeffect 统一语义载体 (§8.2) | `runtime/context.go`（Effect / Provide / Require / Intercept）, `runtime/dependency.go`, `runtime/key.go` | D1–D8；`runtime/scoped_realm_test.go`；`runtime/phase5_contract_test.go` | PASS |
| P0-03 | Effect: Apply → Inverse → Unwind，inverse 生命周期明确 (§8.3) | `runtime/context.go` effectSlot 状态机 + `guardedInverse`；`runtime/events.go` EffectCommitted/Undoing/Undone | `runtime/t61_internal_test.go`；`runtime/phase4_contract_test.go` `TestEffectsUnwindLIFO`；D7/D8 panic containment | PASS |
| P0-04 | Temporal composability：cleanup ordering / LIFO / partial / failure / repeated activation (§8.4) | 同 P0-03 + `runtime/orchestrator.go` activation 生命周期 | `TestEffectsUnwindLIFO`；`TestPRecRecoveryExactnessRandomStacks`（failAt 注入）；`TestSameFiberReactivationIsNewProviderGeneration`；`TestT61LIFOExactlyOnceRecovery` | PASS |
| P0-05 | Coeffect / Dependency：unsatisfied→satisfied→Active；provider 消失→unsatisfied→dependent unwind (§8.5) | `runtime/dependency_graph.go`（resolveDependency / captureDependencies / graph 边），`runtime/reconcile.go` Pending 门控 | `TestDependencyPendingUntilProvider`；`TestDependencyRecoveryActivatesConsumer`；`TestDependencyLossConsumerFirst`；`TestDependencyLossDuringApplyPreventsActive`；`TestT63WithdrawalOrdering` | PASS |
| P0-06 | Spatial composability：组合由 Context/dependency satisfaction 驱动，非顺序执行 (§8.6) | realm 模型：`WithScope` 子 realm、按 identity 的依赖边、shadow、`runtime.Intercept` | `runtime/scoped_realm_test.go`；`runtime/f01_scope_test.go`；`runtime/phase3_contract_test.go` | PASS |
| P0-07 | Fiber identity ≠ Activation identity；stale async completion 隔离 (§8.7) | `runtime/activation.go`；orchestrator 按 `act.id` 判定 stale | `runtime/waitinactive_internal_test.go`；`runtime/c3_t63_ordering_test.go` C3-03 generations never mix；`runtime/ui02_snapshot_test.go` `TestUI02ActivationGeneration` | PASS |
| P0-08 | Ownership：child cleanup 先于 parent cleanup (§8.8) | `runtime/ownership.go` disposeChildren + wait gates；`finalizePending` 延迟终态 | C-04 ownership Close（`runtime/deterministic_close_test.go`, `runtime/det_admission_test.go`）；`runtime/phase6_contract_test.go`；`runtime/proof_invariants_test.go` P1 | PASS |
| P0-09 | Dynamic composition / Provider replacement barrier：old Gone → new Active，无 overlap (§11) | realm own-map 独占（`ErrDuplicateProvider`）；retire → remove；`resolveDependency` owner Active+同 act；`Fiber.Load` 在 Gone 后 | `TestDuplicateProviderRejected`；`TestProviderReplacementConsumerRebinds`；`TestSameFiberReactivationIsNewProviderGeneration`；`TestPropertyConfluenceReplacement`；hmr H9/H15/H18；http H16–H23 | PASS |
| P0-10 | Failed fiber contract：registry 成员、dependency visibility、Ready/Gone/replacement/Close 确定 (§12) | `finalizeActivation` Failed 分支（applyErr && Mounted，无自动重试）；Failed fiber 保留在 registry | `TestApplyFailureEndsFailed`；`TestFailedDisposeLoadRetryCycle`；`runtime/phase4_contract_test.go`；`TestUI02EffectMetadataAndEvents`（Failed row、providers 空、事件） | PASS |
| P0-11 | Kernel Registry 为权威状态来源；extension registry 不改 Kernel lifecycle (§14) | `runtime/runtime.go` fibers map + orchestrator 单决策域；observation snapshot 投影；`extensions/registry` 为独立 extension | T59 P1..P5（`runtime/t59_internal_test.go`）；C-2 every-step（`runtime/c2_t59_every_step_test.go`）；UI-02 snapshot；`extensions/registry/runtime_registry_test.go` | PASS |
| P0-12 | Retiring-record shadowing 语义边界：child realm 持 retiring record 时 ancestor 同 key provider 是否可回退 (§8.5/§8.6 + progress C-3 precondition) | 决策已冻结：nearest retiring record 阻断 ancestor fallback（`runtime/dependency_graph.go:38` `resolveDependency` 对 nearest retiring 返回不可满足，不继续 ancestor）；record 由 owner unwind inverse 物理移除（`runtime/context.go` provideCap inverse → `removeOwn`）；shadowing 为 realm-local | `runtime/gap01_retiring_shadow_test.go` `TestRetiringProviderShadowsAncestor`（deterministic driver：Active → Retiring → Gone；orchestrator probe resolve + Snapshot 断言 no-fallback / record-retiring-present / ancestor-valid-outside / Gone→eligible）；决策冻结记录见文末 Closure Record | **PASS** |

## B. Theorem Closure (§9)

| ID | Theorem | gocordis evidence | Status |
|---|---|---|---|
| P0-13 | T59 Preservation | `runtime/t59_internal_test.go` P1..P5（orchestrator 上运行）；C-2 every-step `runtime/c2_t59_every_step_test.go`（review verdict CONDITIONAL PASS，边界债务见 GAP-02）；`runtime/proof_invariants_test.go` | PASS (semantic)；C-2 harness boundary debt → GAP-02 |
| P0-14 | T61 Recovery Exactness | `runtime/t61_internal_test.go`；C-1 `runtime/c1_t61_min_test.go`（review PASS，`base != active` + `post == base`）；`docs/theorem-verification.md` | PASS |
| P0-15 | T63 Ordering | `runtime/t63_internal_test.go`；`runtime/c3_t63_ordering_test.go`（activation-level）；`TestT63WithdrawalOrdering`；`TestC3T63RandomizedSchedule` | PASS（C3 randomized 偶发 flake 为 pre-existing，基线对比已确认） |
| P0-16 | T66 Progress | `runtime/t66_t73_test.go` deterministic step driver（probe command + generation signals，无 sleep 证明）；`TestT66QuiescenceAllowsActive`；`TestT66AcyclicPrecedenceInvariant` | PASS (core scenarios) |
| P0-17 | T73 Confluence | `runtime/t66_t73_test.go`（24×4 mount + DAG topological）；`runtime/phase5_generators_test.go`；`runtime/property_contract_test.go`；`runtime/minimize_test.go`（AC-12 shrinker） | PASS（documented family；expansion surface → GAP-02） |

## C. Lifecycle Conformance (§10)

| ID | Capability | gocordis tests / evidence | Status |
|---|---|---|---|
| P0-18 | Load → Apply → Active → Unload → Cleanup → Gone | C-1 `c1_t61_min_test.go`；C-2 `c2_t59_every_step_test.go`；deterministic close C-01..C-04；`phase3/4/5/7_contract_test.go` | PASS |
| P0-19 | Apply Failure / Cleanup Failure / Dependency Loss / Dependency Recovery / Repeated Activation / Concurrent Lifecycle / Runtime Close | `TestApplyFailureEndsFailed`、`TestFailedDisposeLoadRetryCycle`、phase5 dependency tests、`TestSameFiberReactivationIsNewProviderGeneration`、`phase4_random_test.go`、UI-02 concurrency snapshot、`deterministic_close_test.go`、hmr/http E2E | PASS |

## D. Required P1 Engineering (§7 P1, §15–§21)

| ID | Capability (spec §) | gocordis implementation | tests / evidence | Status |
|---|---|---|---|---|
| P1-01 | Loader contract（Entry→Component→Fiber→Activation；经 Public API，不改 Kernel internal）(§15) | `extensions/loader` | L1–L19（L2 load failure registry unchanged；L14 snapshot immutability；L17 module identity；L19 isolation） | PASS |
| P1-02 | Config Reconciliation（External→Desired→Reconcile；parse failure 不破坏 applied state）(§16) | `extensions/config` + `extensions/configwatch`（adapter 链） | C1–C12；P2/P3/P6；TOML parser tests；configwatch A1–A4 + integration（dependency chain / ownership isolation） | PASS |
| P1-03 | Watch（watch→adapter→reconcile；禁止 adapter 直改 Fiber/lifecycle）(§16) | `extensions/watch` + `extensions/configwatch` | Watch engine tests（revision dedup / coalescing / fatal / subscription isolation）；integration full chain | PASS |
| P1-04 | HMR transactional（old → prepare → update，failure 保持 old；无 half-state / duplicate provider / stale activation / lost cleanup）(§17) | `extensions/hmr`（基于 runtime lifecycle 的替换） | H1–H20（H5/H7/H8/H20 failure；H9 warm；H15 concurrent；H18 provider replacement）；http H16–H23（same-port failure preservation） | PASS |
| P1-05 | WASM backend（等价于 native Component semantic model；经 loader/public API，不直改 Kernel）(§18) | `extensions/loader/wasm` | V01–V11（compile/reject/ABI/execution）+ wasm loader tests；runtime semantic 等价由同一 Load/Apply/Effect 路径承载 | PASS |
| P1-06 | Stable Registry extension（members dynamic；registration 可逆；旧 member inverse 不误伤新 member）(§19) | `extensions/registry`（kernel lifecycle 之上，非 Kernel registry） | R1R2 stable identity + consumer stability；R3 duplicate rejected；R5 replace atomicity + never-absent；R6 snapshot immutability；R7/R11 concurrency；`runtime_registry_test.go` | PASS |
| P1-07 | Observation boundary（Snapshot/RuntimeEvent read-only projection；console 不反向改 Kernel）(§20) | UI-02 `runtime/observation.go`, `runtime/events.go`, `runtime/runtime.go` `Snapshot()`（orchestrator 线性化） | UI-02 14 项验收：value-only/immutable、linearized-under-concurrency、snapshot↔observe 等价（`runtime/ui02_snapshot_test.go`） | PASS |
| P1-08 | Extension boundary（extension 只经 Public Runtime API；禁止直接 fiber/activation/scheduler mutation）(§21) | 全部 `extensions/*` 仅依赖 runtime public API；Kernel review（`docs/plan/baseline.md`、review specs） | extension 全量测试 + integration + `-race`；`go test -count=1 ./...` 与 `-race` 全绿 | PASS |
| P1-09 | Kernel 单生命周期决策域 / deterministic admission（Phase B 修复） | `runtime/orchestrator.go` + `runtime/deterministic.go` | `runtime/det_admission_test.go`（T-B-EXEC-01/02/03）；deterministic close C-01..C-04 | PASS |
| P1-10 | Event extension（extension-level event bus；sequence 单调） | `extensions/event` | E1–E14；P1 sequence uniqueness / monotonic across close | PASS |
| P1-11 | Scheduler extension（job 生命周期经 runtime fiber/activation） | `extensions/scheduler` | S1–S17（S15 activation disposal；S16 provider stable under churn；S17 provider replacement） | PASS |
| P1-12 | HTTP-RPC extension（P2 示例能力，已实现且边界一致） | `extensions/http` | HTTP01–HTTP30（dependency consumer、close、HMR/config interplay、property） | PASS (P2, 非 blocker) |

## E. N/A / Out-of-scope (§2, §3, §22)

| ID | Item | Reason |
|---|---|---|
| N/A-01 | gocordis-console / UI page / Timeline rendering | spec §3/§20：Console 是 consumer，不反向决定 Kernel；UI 阶梯另行治理（UI-01..UI-04 已冻结 roadmap） |
| N/A-02 | industrial collector / CAN / PLC / Modbus / CSV / Excel / Dashboard / Business workflow | spec §3：业务 consumer，非 Runtime scope |
| N/A-03 | Paper / stc-go / Cordis 原文逐字引用 | 原文不在仓库；correspondence 只能由映射文档部分锚定 → GAP-03 |
| N/A-04 | CI runner 执行 / Windows host / benchmarks | 环境与性能项，非语义（progress 已记录） |

## Gap Log（§24）

| Gap | Class | Evidence | Action | Blocking |
|---|---|---|---|---|
| GAP-01 | P0 semantic boundary | **CLOSED**：决策冻结 + 直接 conformance 测试（`runtime/gap01_retiring_shadow_test.go`，deterministic PASS + `-race` PASS）；矩阵 P0-12 → PASS；paper/stc-go 对照保留 external（GAP-03），详见文末 Closure Record | 1) 冻结决策（Closure Record §Rule）；2) conformance test 落地：retiring 期间 child consumer Withdrawn/Pending 且 resolve 不落到 ancestor；B 的 record 在 B 自身 Unloading（cleanup gated）期间仍物理存在且 retiring；owner Gone 后 record 移除、ancestor eligible；driver reconcile 显式 rebind child consumer → 绑定 ancestor；3) paper/stc-go 逐字对照仍待外部材料（转 GAP-03） | NO（已闭合） |
| GAP-02 | P1 theorem breadth（documented） | T73 生成器未覆盖 realm-sibling / nested-child / HMR-heavy interleavings（`docs/theorem-verification.md`）；C-2 的 orchestrator-boundary harness 债务（progress.md） | 1) C-2 增加 orchestrator 边界 checkpoint（沿用 T59 on-orchestrator 模式）消除 harness 债务；2) T73 生成器扩展或正式记录为有依据的 limitation | YES (per §25 Unverified Semantic Boundary = 0) |
| GAP-03 | P1 process / correspondence | §5 matrix 的 Paper / stc-go / Cordis reference 列无法在仓库内逐项验证 | 需要提供 paper / stc-go / Cordis 源材料后逐行补 correspondence 证据；在此之前不宣称跨源 PASS | YES (matrix completion) |
| GAP-04 | P2 non-semantic | CI workflow 未在 runner 上执行；Windows 未验证 | 环境项，不阻断语义 completion | NO |

## Recommended Closure Order

1. GAP-01（P0）：✅ 完成（conformance test + 决策冻结，见文末 Closure Record；P0 12 PASS / 0 PARTIAL）。
2. GAP-02（P1）：C-2 orchestrator-boundary checkpoint；T73 生成器扩展或正式记录 limitation。
3. GAP-03（P1）：等外部源材料后逐行补 correspondence。
4. 全部闭合后重跑 `go test ./...` + `go test -race ./...`（§25 gate）。

## F. GAP-01 Closure Record — Retiring Record Shadowing (frozen decision)

Status: **CLOSED → PASS** (was P0 PARTIAL in v0.1 audit). Freezing scope is
confined to the already-implemented semantics verified below; **zero
production code changed** by this closure.

### Rule

A nearest Retiring provider record shadows ancestor providers for the same
dependency key:

```text
resolve(K, realm) walks own -> ancestor.
Nearest record found but Retiring  =>  unavailable.
NO fallback to an Active ancestor record.
```

`Retiring != Absent`: the record remains in the realm own-map (physically
present, `providerRecord.retiring`) until the owning activation's unwind
inverse removes it, so a dependent in that realm can never resolve two
simultaneously-valid bindings (nearest retiring + ancestor active) for one key.

### Transition

```text
Retiring (record present, shadows ancestor)
    |
    | owner activation unwind completes; inverse removes the record
    v
Gone / record removed
    |
    v
ancestor provider may become eligible for the child realm again
```

### Scope

Shadowing is **realm-local**: only consumers whose realm path crosses the
retiring record are blocked. The ancestor record stays valid and resolvable
everywhere outside that child realm (root consumer unaffected).

### Rationale

Retiring is a decided withdrawal, not absence. Treating the nearest retiring
binding as absent and falling back to the ancestor would expose two valid
provider binding interpretations during the withdrawal window, break
nearest-binding determinism, and could rebind dependents to an ancestor while
the retiring provider is still in the process of unloading.

### Correspondence

- **Paper**: the Paper is not available in-repo for verbatim citation
  (N/A-03 / GAP-03). The Paper's nearest dependency binding / spatial
  composability principles do not literally spell out this operational rule.
  This is an **operational refinement required to preserve the Paper's
  nearest-binding / lifecycle semantics**, not a Paper-verbatim claim.
- **stc-go**: nearest provider binding is preserved across the lifecycle
  transition and a retiring nearest binding is not treated as absent for
  ancestor fallback. Exact stc-go identifiers cannot be verified in-repo
  (GAP-03, external-required) — no names are fabricated here.

### Evidence

- Conformance test: `runtime/gap01_retiring_shadow_test.go`
  `TestRetiringProviderShadowsAncestor` — deterministic driver timeline
  `Active → Retiring → Gone`; orchestrator-linearized probes assert:
  - no-fallback: `resolveDependency(childRealm, K)` unavailable while B
    Retiring (phases 1a/1b), never `A`;
  - record physically present + `Retiring` (Snapshot ProviderView) through
    B's own gated Unloading (deepest Retiring point, not `remove`-simulated);
  - ancestor valid outside shadow: root `resolve(K) == A`, root consumer D
    stays Active/Satisfied-A;
  - Gone permits visibility: after B Gone + record removal,
    `resolveDependency(childRealm, K) == A`; explicit driver reconcile
    (documented as harness-driven; the runtime sweeps Pending only on a new
    activation becoming Active) rebinds C to A and C activates on A.
- Verification: `go test ./runtime/...` PASS; `go test -race ./runtime/...`
  PASS.

## Explicit Non-Claims

- 本文档（v0.1 audit 本体）未修改任何 production code；GAP-01 closure 新增的是 conformance test（`runtime/gap01_retiring_shadow_test.go`）与决策记录，production code 修改为零。
- 所有 `external-required` 的 cross-source 判定均未臆造。
- UI-02 之后仍未实现：Runtime-level `Subscribe()`（UI-03）。按 §20，Observation contract 已由 read-only `Snapshot()` 满足；`Subscribe()` 属 UI-03 roadmap 的 Future Extension，不阻塞 v0.1 semantic completion（registry extension 已提供 extension 级 subscribe 先例）。
