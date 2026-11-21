# GoCordis v0.1 — Final Acceptance

Date: 2026-11-21. Baseline: commit `0eaf4eb` + 收尾整改批次。
Authority: [`论文.pdf`](./论文.pdf) 为语义唯一裁决来源;JS Cordis 为实现参考而非语义来源。

本文件是 **v0.1 Feature Freeze 的验收结论**。它取代 `ROADMAP.md` 中任何仍带
"待实现/PARTIAL" 字样的历史描述;后者已统一标记为历史记录。
结论:**PASS**(见 §9;证据边界见 §1 与 §7.5,不宣称形式化数学证明)。

---

## 1. Paper Semantic Coverage(论文语义覆盖)

以论文条目为锚,交叉核对论文原文、当前源码与测试证据:

| 论文条目 | 语义 | 源码落点 | 测试证据 | 状态 |
|---|---|---|---|---|
| Def 48/49 | Component / Fiber(实例 ≠ 定义) | `runtime/component.go`、`runtime/fiber.go` | `declarations_containment_test.go` D1–D8 | ✅ |
| Def 50/51 | registry(单决策域) | `Runtime.fibers` + orchestrator | `proof_invariants_test.go` | ✅ |
| Def 52 | 实例化 = 父的被追踪 effect | `ctx.Child` + `runtime/ownership.go` | `phase6_contract`、`deterministic_close_test.go` | ✅ |
| Def 53 | target view / quiet | `runtime/reconcile.go` | `thm73_thm80_test.go` | ✅ |
| Def 54 | relied 守卫(consumer 先于 provider 撤离) | `runtime/dependency_graph.go` | `c3_thm70_ordering_test.go` | ✅ |
| §3.1 Revertible effects | 槽位状态机 + Effect iterator | `runtime/context.go`、`runtime/component.go:IterComponent` | `effect_phase_test.go`、`iterator_test.go` | ✅ |
| §3.1.3 L-Iter / L-Divert | 逐 yield 激活 + 迭代边界 divert | `orchestrator.runApplyIter` | `iterator_test.go`(中途 Dispose / 依赖消失 / step raise) | ✅ |
| Def 24/25 Isolation | 逐 key realm 表 ρ,插入时固定,无祖先回退 | `runtime/providers.go:lookupOwn`、`fiber.keyRealms` | `def24_isolation_test.go`、`gap01_retiring_shadow_test.go` | ✅(ADR-0001 Accepted) |
| Def 26/27 Interception | 元数据 monoid d(k) ⊕ ι(k),provider 解释 | `MetaKey`、`ProvideMeta`、`RequireMeta`、`InterceptMeta`(ι 存于派生 `Context.metaView`,非共享 realm) | `meta_intercept_test.go`、`metakey_semantics_test.go` | ✅(ADR-0002 Accepted + 2026-11-21 Derived realization 修订) |
| Def 27 Derived realization | 拦截派生新 context,不原地改共享上下文 | `Context.Child` 快照继承 + `cmdSpawnChild.inheritedMeta` → `newContext`;独立根无继承 | `TestMetaInterceptDoesNotLeakAcrossIndependentRoots` + 变异验证 | ✅(审计 P-CO-01 已修复) |
| §6.3 Access Control | 按元数据裁决,拦截可逆且不触发 reload | `Context.InterceptMeta` 经 Effect 安装 | `meta_intercept_test.go` | ✅ |
| §4.4 Asynchrony | inertial host,L-Divert 取 landing 替代 | orchestrator 异步完成 | `det_admission`、`iterator_test.go` | ✅ |
| §4.4 Failure | raise → Unloading,outcome 阻止重入,不向父传播 | `finalizeActivation` | `TestApplyFailureEndsFailed` | ✅ |
| §4.4 Configuration | revision = 卸下→逆→移除→重插 | `Fiber.Revise` + HMR/loader | `revision_thm80_test.go`、`revise_race_test.go` | ✅ |
| §5.2.1 disable/re-enable | Enabled 开关走声明,entry 存活 fiber 是启停身份 | `config.Controller` + follows 依赖 | `config_test.go`、`e2e_plugin_unit_test.go` | ✅ |
| Thm 64 Preservation | 良构性不变量 | on-orchestrator 检查器 | `thm64_preservation_test.go`、`proof_invariants_test.go` | ✅(模型覆盖,非形式化证明) |
| Thm 68 Recovery | episode 累加器 ≃ 未开始 | LIFO + 恰好一次逆 | `thm68_recovery_test.go` | ✅(同上) |
| Thm 70 Ordering | L-Begin 门控 + provider episode 包住 consumer | 依赖门控 + 撤离顺序 | `thm70_ordering_test.go` | ✅(同上) |
| Thm 73 Progress | 有限步 + 无环 ⇒ quiesce | quiescence oracle(含 effect 在飞态) | `thm73_thm80_test.go` | ✅(同上) |
| Thm 80 Confluence | 规范形 + 合流 | det 交错 + ≃ 投影(忽略生成名) | `thm80_det_interleaving_test.go` | ✅(同上) |

**结论边界**:上述 ✅ 表示"当前实现与已有设计契约一致且有对应测试证据",
**不表示对任意合法调度空间的穷尽形式化证明**。Theorem 覆盖是有限模型 +
确定性交错 + fuzz,不是数学证明。

---

## 2. Kernel / Extension Boundary

架构边界(禁止 Kernel 演化为第二生命周期权威):

```text
Application ── Application UI ──┐
            └─ Application Plugins ─┤
                                   ├─ Public Runtime API ─┬─ Kernel
                                   │                      │   Fiber / Effect / Context
                                   │                      │   Realm / Dependency / Orchestrator
                                   │                      └─ Extensions
                                   │                          Loader / Config / HMR / Event
                                   │                          Registry / Watch / Scheduler / Console
                                   └─ Declarative reconciliation (config.Controller)
```

- Kernel **不含**数据库、采集、HTTP、Console 等业务能力。
- Extension **不直接操作** Fiber 内部生命周期;一致性经 Public API + 声明调和。
- 应用通过 Component / Dependency / Effect / 声明式调和组合能力。
- UI 插件与对应后端插件生命周期关联(`follows` = `console:plugin-alive/<id>`),
  后端启停经依赖门控带 UI 行同进退;降级不阻断(声明语义)。

`client/` 与 `web/console/` 职责界定(审计 G-04):
- `web/console/` — 唯一的 Console 前端(fusion: shell + views + module manager)。
- `client/` — 遗留前端 SDK,当前仅作 wire DTO 契约镜像(`client/src/types.ts` 被
  `extensions/console/webui/lifecycle_test.go`、`explorer/dto_test.go` 引用为 JSON 形状锚点),
  **不是第二个 Console 入口**。保留因其为测试契约来源;不新增功能。

---

## 3. Lifecycle / Concurrency / Resource Ownership

### 3.1 Effect / Unwind(论文 Temporal Composability)

- `Context.effect` 两阶段 Effect 槽位:Installing → Committed / Undoing → Undone。
- `installing` 计数 + `installDone` cond 屏障覆盖 install **及其补偿逆**;
  `beginUnwind()` 非阻塞(只翻状态 + cancel),`collectInverses()` 在 unwind goroutine
  等 `installing==0`,`runInverses()` 严格 LIFO。
- Gone 保证所有逆及 EffectCommitted/EffectUndoing/EffectUndone 事件已发布。
- 证据:`effect_phase_test.go`(两阶段握手、`TestEffectStrictLIFO`、
  `TestPhaseOrchestratorStaysLiveDuringSlowInstall`)。

### 3.2 Command Admission / Shutdown(RT-01)

- `sendMu` + `halted` 闸门将 submit 与 shutdown drain 线性化:
  `submit()==true ⇒ 命令必被应用`;`==false ⇒ 未入队`。
- 证据:`submit_drain_test.go`(灌满缓冲 + 阻塞 orchestrator 的确定性交错)。

### 3.3 Rehome / Revision(生命周期窗口闭合)

- Rehome 两阶段:phase 1 标记 retiring + gate consumers;phase 2 持有 `routeMu`
  换表 + 从 phase-1 快照迁移记录。路由表**合并语义**:只重指本 fiber 的 Provide 键,
  inject-only 键保持解析原处,继承键保留。
- 等待期注册的 provision 原子 retiring(`registerOwnRetiring`),新依赖无法绑定被撤离
  命名空间——consumer-first 覆盖整个 rehome 窗口。
- `WithResolvedRealms` 按**捕获的 ProviderIdentity** 跟随原 provider,不做全局扫描
  (多 realm 同 key 时不会换绑)。
- Revision:revising CAS 防重 + Gone 拒复活 + 拒绝 finalizing parent + 插入时读旧 fiber
  最终表;自依赖在装载边界拒绝。
- 证据:`rehome_test.go`、`revise_race_test.go`、`resolved_realms_binding_test.go`。

---

## 4. Loader / Config / HMR

### 4.1 Module Registry + ResourceHandle(WASM 生命周期,已冻结)

Module 生命周期统一经 attempt 范围的 `ResourceHandle` 协议:

| 出口 | 责任 |
|---|---|
| Backend.Load 失败 | Backend 自行清理未交付资源 |
| Backend 成功 → Registry 提交前失败 | Loader 调 `handle.Abandon()` |
| Registry 提交成功 | `handle.Commit()` 转移所有权给 Registry |
| Unload / CloseContext 批量清除 | Registry 调 `handle.Release()` |

wasm backend 内部以 `moduleGeneration`(ref + holders + committed)为身份:
- `LoadResource` 原子返回 (Module, handle) — 来自同一 `loadGeneration` 临界区。
- `Release` 以 generation **指针**为身份(不是 id),旧代际的迟到 Release 是新代际的 no-op。
- holders 归零才关闭编译资源;`commitGeneration` 不减 holders(claim 转移给 Registry)。
- 每个 handle 带状态机(Pending → Committed|Abandoned;Committed → Released),
  重复操作是 no-op。
- 证据:`generation_isolation_test.go`、`load_resource_race_test.go`(结构指针身份断言)、
  `end_to_end_test.go`(真实 Loader → factory.Create → fiber → Unload 全链路)、
  `loader_concurrency_test.go`。

**冻结声明**:Loader/WASM ResourceHandle 协议自本验收起冻结;后续只因**可复现的
正确性缺陷**重开,不因设计偏好重构。

### 4.2 Config / HMR

- `config.Controller` 声明调和:applied-diff 驱动 Load/Dispose,失败不部分提交。
- HMR 热替换:载荷不变不 reload,新版本按 revision 复合落地;替换终点等价于从头加载。
- `configwatch` 文件变更经 adapter 实时调和到 Controller。
- 证据:`config_test.go`、`configwatch/*_test.go`、`hmr/*_test.go`(H1–H20)。

---

## 5. UI Plugin Architecture

- 前端 `createModuleManager` / `reconcileClientModules`:模块按 `?v=<sha>` 内容修订
  导入,register 决定渲染器/面板/组件贡献;dispose 走 exact-once。
- keyed 注册表为**影子栈**:后注册遮蔽前者,遮蔽者 dispose 后恢复被遮蔽贡献;
  遮蔽项的失效 dispose 在影子期是 no-op,但真正移除条目以防复活死模块贡献。
- async register 被 await 在守卫窗口内,异常回滚部分注册。
- 后端 `follows` 依赖使 UI 行随后端插件启停;坏 manifest 产 `plugin-error` 行。
- shims 仅再导出宿主实际存在的绑定(React 18 无 `useOptimistic`;jsxDEV 降级 jsx)。
- 证据:`web/console/src/lib/client-modules.test.ts`、`src/views/registry.test.tsx`、
  `extensions/console/*_test.go`。

---

## 6. CI Verification

> 本仓库为私有仓库,本环境无法读取远程 Workflow Run 详情;以下以本地等价命令的实际
> 执行结果为验收依据,远程 CI 状态以仓库维护方最终确认为准。

本地验收命令(全部实际执行且通过):

| 命令 | 结果 |
|---|---|
| `gofmt -l .` | 干净 |
| `go vet ./...` | 通过 |
| `go test -count=1 ./...` | 通过 |
| `go test -race -count=1 ./...` | 通过 |
| `go test -race -count=10 ./extensions/loader/...` | 通过 |
| 前端 `npx vitest run` | 43/43 通过 |
| 前端 `npx tsc --noEmit` | 通过 |
| 前端 `npm run build` | 通过 |
| Windows 测试二进制交叉编译(全 28 包) | 通过 |

CI 配置:`.github/workflows/ci.yml` 含 verify(格式/vet/test/race + 敏感套件重复)、
windows-build(build/vet/test 交叉编译)、console-web(npm/vitest/tsc/build)三 job。

---

## 7. 已接受的差异(Accepted Differences)

1. **Effect Iterator 用回调式 `ApplyIter(ctx, yield)` 而非 `iter.Seq` range-over-func**:
   Go 自身惯用取舍,逐 yield 语义等价。
2. **ownership 树子先父后强于论文**:论文只要求 guard 沿 coeffect 排序;本仓库额外保证
   child cleanup 先于 parent,属 operational strengthening,不违背论文(§1.2 历史表)。
3. **Rehome 是短路由**:provider 在同一 activation 上迁移 realm,不 reload;旧命名空间
   依赖者保持 Pending,其自身 revision 才跟随(按捕获 binding,不换绑)。
4. **Console 默认 AccessPolicy 为全允许**:开发/本地姿态;工业部署必须配置监听地址、
   网络边界与授权策略,前端隐藏按钮不是安全控制。
5. **Deterministic driver 停靠 ApplyDone/UnwindDone,不逐 yield 调度**:测试工具限制,
   非语义缺陷(见 §8 延后事项)。

---

## 7.5 语义闭环（2026-11-21 收尾，原 P-CO-03 / P-CO-05 已关闭）

审计 PASS WITH LIMITATIONS 指出的三项语义闭环已按论文原文补齐：

| 闭环 | 论文依据 | 实现 | 验收测试 |
|---|---|---|---|
| 观察等价 | §3.3.2 Def 31-35:≃ₖ 由 key 的操作集诱导(最粗被尊重等价),≃_S = 同域 + 逐 key ≃ₖ;递归类型上为偏等价 | `runtime/observation_equivalence.go`:`ObservationSpec` 声明每 key 的可观察边界,`ObservationModel.EquivalentAt` 实现 Def 33,`RelevantKeys` 计算 dom∩S | `observation_equivalence_test.go`:等价三性质、域交、操作尊重、generation 可观察性 |
| Unified Context 递归 | §3.3.1 Def 28:Γ∞ = μΓ. Γ × (Γ→Γ) × Σ;子 Fiber 拥有新 Context,继承 Σ | `Context.Child` 派生 + `initialMetaView` 继承 + ownership 门控;三投影(Γ=activation, Γ→Γ=effect stack, Σ=keyRealms+metaView+graph) | `unified_context_test.go`:嵌套组合、逆序撤销、Revision 保留递归、投影不变量 |
| 独立性与五定理 | §3.4 Def 42/44/Thm43/45;§4.3 Thm64/68/70/73/80 | `observation_equivalence.go` + `theorem_evidence_test.go` 前提-检查点-测试对照账本;不同 key 天然独立、同 key 需 witness | `independence_test.go`(4 项:不同 key 交换、同 key witness、不扰动他 key、生成式 S 投影)、`theorem_evidence_test.go`(前提锚点、前提门、Thm73 quiescence、Thm80 重命名等价) |

**边界保持**:这些是有限模型/生成式 conformance 证据,不是对任意合法调度空间的形式化数学证明。
`FINAL-ACCEPTANCE.md` §1 的 Theorem ✅ 始终带 "模型覆盖,非形式化证明" 限定,不因本轮升级结论而抹去。

---

## 8. 延后事项及原因(不阻断 Feature Freeze)

| 编号 | 事项 | 不阻断原因 |
|---|---|---|
| G-05 | Deterministic driver 逐 yield 调度 | 迭代交错已由 `iterator_test.go` channel 屏障覆盖;为逐 yield 调度重构 Kernel 收益低、风险高 |
| G-06 | Windows 原生运行测试 | 当前交叉编译已守住编译兼容;工业 PC 原生运行测试归入产品发布验收,不属框架冻结条件 |
| P7 | WASM 沙箱能力面 = 声明集的形式化 | 属产品安全验收;Loader 生命周期协议已冻结,沙箱形式化不重开生命周期 |
| P7 | 依赖类型/版本(§6.6) | Key 元数据演进的自然延伸,待真实应用出现多版本依赖需求再启动 |

---

## 9. Feature Freeze 决策

**结论:PASS**(论文语义覆盖与可复现正确性层面)

依据:
- 论文核心语义(Effects / Coeffects / Isolation / Interception / Revision / 五定理)
  均有源码落点 + 测试证据,且无确定性阻断缺陷。
- 审计 PASS WITH LIMITATIONS 指出的三项语义闭环(P-CO-03 观察等价、P-CO-05 Unified Context
  递归组合、§3.4 独立性 + 五定理证据链)已按论文原文补齐并通过验收(§7.5)。
- 仍保留的"非形式化证明"限定是证据边界的说明,不是已知语义缺陷;Theorem 的测试证据是
  有限模型 conformance,不构成对任意合法调度空间的数学证明。
- Kernel / Extension 边界清晰,Loader/WASM 生命周期协议已冻结并经端到端验证。
- 全部本地验收命令通过;整改批次完成的 Makefile 严格门、仓库清理、文档一致性均已核实。
- 延后事项均为测试工具增强 / 产品发布验收 / 未来功能,不属框架语义缺陷。

**决策**:GoCordis v0.1 进入 Feature Freeze。除非出现**可复现的阻断性正确性缺陷**,
不再进行常规架构审查、Kernel 功能扩张或对已冻结协议的返工。
下一步重心转入 `industrial-data-collector` 的真实工业采集闭环验证。
