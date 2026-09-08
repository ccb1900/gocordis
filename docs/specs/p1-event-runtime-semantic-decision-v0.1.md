> ⚠️ **ARCHIVED（已归档）— 2026-09-08**
>
> 本文档已整体归档，仅作历史参考，**不再作为规范来源或引用依据**。
> 本仓库现以论文原文为唯一规范来源：`docs/论文.pdf`
> （A Programming Paradigm for Spatiotemporal Composability）。
> 当前路线图与论文↔代码对照见 `docs/ROADMAP.md`。
>
> This document is archived for historical reference only and is no longer cited
> as authority. The paper is the sole normative source; see `docs/ROADMAP.md`.

# P1 Event Runtime — Semantic Decision Record v0.2

Status: DECISION RECORD v0.2 — **CONDITIONAL PASS** (architect review 2026-09-07).
Zero production code, zero tests. Nothing in this document is implemented yet.
Reference: `docs/gocordis Plugin Runtime Platform Completion Specification v0.1.md`
§5–§12, §31-A, §34.
Audit anchor: commit `87169d3` (HEAD at audit time).

## 0. v0.1 → v0.2 Changelog

Architect review verdict: v0.1 大部分语义冻结正确（ownership / snapshot /
scope path / Effect 生命周期），**Conditional PASS**，需先完成四处修改：

| Change | v0.1 | v0.2 |
|---|---|---|
| D5-ordering | global seq 冻结为 Kernel semantic requirement | 降级为“当前 deterministic ordering implementation strategy”；Kernel 只要求“ordering 有定义且不依赖 map iteration” |
| D6 | handler 无 Context（不能 Require/Emit） | Kernel 只规定 handler 不持有 Runtime internal authority；handler-facing Context/API 延迟到 **P2 决策**，本记录不冻结 |
| D8 | Emit 描述疑似 “同步 + await-all” | 重新澄清：Emit = **同步 broadcast**，同步函数调用完成 ≠ await semantics；不收集返回值；error 可聚合（Go error 通道） |
| D11 | Waterfall `next(value)` forward-algebra 冻结 | **不再冻结任何 next-algebra**；Waterfall 语义移交 **P1.4 semantic review**（Cordis 是 around-middleware algebra，与本记录的 forward algebra 是两种组合代数） |

Approved unchanged: D1 EventKey[T]、D2 Effect-owned、D3 owner identity、
D4 emitter-path scope、D5 dispatch domain（handler 锁外/非 orchestrator）、
D7 cancellation、D12 Event ≠ Dependency、D13 canonical stream、
D14 `extensions/event` 分离。Serial/Parallel 的精确 result/error 约定相应
推迟到 P1.2 / P1.3 各自 review（见 §10/§11），不在本记录提前冻结。

## 1. Authority & Correspondence

Authority: Paper > stc-go > JS Cordis > DeepSeek Harness（platform spec §0）。

v0.2 外部证据状态（本环境可核验的部分已核验，未核验的一律标注）：

| Anchor | Source | Status in this record |
|---|---|---|
| [DSH-EVENTS] deepseek-harness `docs/cordis-tutorial/04-events.md`（dispatch-mode 表 + ctx.on-as-effect + waterfall around-middleware） | https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/cordis-tutorial/04-events.md | ✅ fetched & verified（2026-09-07） |
| [DSH-FW] deepseek-harness `docs/user/develop/framework/events.md`（listener 随 plugin unload 自动移除） | 同仓库 user docs | snippet-verified（search），未全量 fetch |
| [CORDIS-THISARG] cordiverse/cordis `events.ts`（`thisArg` + `Context.filter`） | commit `e99a344`（search snippet） | snippet-verified；**events.ts 全量 fetch 超时**，逐行引用 external-required |
| [PAPER-MAP] 论文 metatheory（revertible effect / LIFO recovery） | 仓库内 `docs/theorem-verification.md`、`docs/plan/paper-mapping.md` | ✅ in-repo |
| stc-go 仓库原文 | reviewer 提供 | fetch 失败（环境无网络），**external-required**；仅用 [PAPER-MAP] 的论文级结论 |

规则：凡未在本环境核验的外部表述，不写入“已核验 correspondence”，只作
“待核验的决策输入”；不臆造外部 API/测试名称。仓库内语义资产（platform spec
§3 冻结清单）是本文所有推导的硬约束，一律不改。

## 2. Decision Summary

| # | Topic | Decision (short) |
|---|---|---|
| D1 | Event identity | typed `EventKey[T]` = (payload Go type, name)，unforgeable、可比较；无通配符；无字符串 namespace 冲突 — **PASS** |
| D2 | Registration | `ctx.On` ≡ Effect registration（[DSH-EVENTS]/[DSH-FW]：listener 随 plugin 消失），inverse 自动 unregister — **PASS** |
| D3 | Handler ownership | owner = 注册 activation generation (fiber, activation)；条目 realm-scoped + owner identity + 序键 — **PASS** |
| D4 | Scope model | **emitter-path 匹配**；ancestor handler 参与 child dispatch；sibling 隔离；无 shadowing — **PASS** |
| D5a | Dispatch domain | handler 永不在 orchestrator goroutine 执行、锁外执行、dispatch 快照一致性 — **PASS** |
| D5b | Ordering | Kernel 只要求“有序且不依赖 map iteration”；**当前实现策略** = 全局单调注册 seq（可换，非语义要求） |
| D6 | Handler authority | Kernel：handler 不持有 Runtime/Orchestrator/Registry internal authority；handler-facing Context/API → **P2 决策**（[CORDIS-THISARG] 作输入） |
| D7 | Cancellation | dispatch 绑定 emitter Context；逐 handler 检查 — **PASS**（随 D6 澄清不变） |
| D8 | Emit | 同步 broadcast；同步调用全部命中 handler；不收集返回值；error 可聚合（Go error 通道）；≠ await — 已澄清 |
| D9 | Serial | 有序 + await previous before next 冻结；result/error 精确约定 → **P1.2 review**（Cordis 参考：first non-null/false/undefined wins & stops） |
| D10 | Parallel | await all 冻结；error 聚合 → P1.3 review；共享状态规则 v0.1 保持 |
| D11 | Waterfall | **不冻结 algebra**；around-middleware vs forward-algebra 二选一 → **P1.4 review**；W-05/W-07/确定性/取消 先行冻结 |
| D12 | Event ≠ Dependency | dispatch 不提供 Require — **PASS** |
| D13 | Event ≠ Kernel stream | P1 dispatch 不进 `runtime/events.go` canonical stream — **PASS** |
| D14 | `extensions/event` | 保持 extension 现状；不迁移不废弃 — **PASS** |

## 3. D1 — Event Identity

### Decision

```text
EventKey[T] = (payload Go type T, name string)
事件实例携带 T 类型 payload。
handler 只能注册到精确匹配的 EventKey；无通配符、无前缀匹配。
```

### Rationale

- 镜像 `CapabilityKey`/`Key[T]`（`runtime/key.go`）：typed、stable、unforgeable。
  Event 与 Capability 共用同一 identity 设计语言，避免 Event 沦为
  `map[string]any`。
- 同一 name + 不同 payload 类型 = 不同事件（类型即 namespace 的一部分）。
- [DSH-EVENTS]：flat event namespace 由 “namespace/action” 命名约定保持可读；
  该约定属于 API 层，不改变 identity 的类型化规则。

### Formal Semantics

- `E == E'` ⇔ `typeOf(T) == typeOf(T') && name == name'`。
- dispatch(E) 只命中注册了 E 的 handler；注册了 E'（E' ≠ E）的 handler 永不
  收到 E。
- v0.1 无 wildcard / predicate / filter（与 `extensions/event` exact-match
  原则一致）。

### 关系：ancestor / child / sibling（identity 层面）

Event name **不独占、不 shadow**：root 与 child realm 可为同一事件各注册
handler；child 注册不遮蔽 root 注册（见 D4）。

## 4. D2/D3 — Handler Ownership（Registration ≡ Effect，owner identity）

### Decision

```text
ctx.On(E, h)
    ↓
ctx.Effect(kind=Event, key=E)          // 复用现有 Effect 通道
    ├─ install: 向 注册 realm 的 handler chain 追加
    │           {owner=(fiber, activation), seq=序键, h}
    └─ inverse: 精确删除该 (owner, seq) 条目
    ↓
owner Context unwind（effect LIFO inverse）
    ↓
handler 从 registry 移除
```

- 条目归属：注册时所在 realm（realm-scoped own handler chain，与
  `intercept` chain 同层，`runtime/providers.go`）。
- 每条目 tag：`(event key, owner ProviderIdentity{fiberID, activationID}, 序键)`。
- 删除条件：owner identity + 序键精确匹配（防止旧 activation inverse 误删新
  activation handler——与 provider `removeOwn` identity 校验同一原则）。
- 同一 owner 多次 `On` = 多条 handler，按序键有序，各自独立删除。

### Rationale

- [DSH-EVENTS]：`ctx.on()` is an effect, listener disappears with the plugin —
  no manual removeListener bookkeeping。
- [DSH-FW]：listener registered with `ctx.on()` is removed automatically when
  its plugin unloads。
- [PAPER-MAP]：revertible effect 携带 inverse、卸载按 LIFO 回滚（论文 §5 /
  metatheory LIFO recovery；仓库映射见 `docs/theorem-verification.md`）。
- 结论：**不需要第二套 registration 生命周期**。

### Lifecycle Semantics（可测试不变式）

- owner activation Active：handler 可被 dispatch。
- owner activation 进入 Unloading：新 dispatch 通过 owner 有效性检查排除
  （见 D5a / §12）；inverse 完成后条目物理消失。
- owner activation 结束（Gone/Pending）：条目已移除；任何新 dispatch 看不到。
- **No-handler-leak oracle**：owner 结束后 `registered(owner)` 为空，且每个
  可见 handler 都有存活的 owner activation。

## 5. D4 — Scope / Realm Semantics（PASS，评审批准）

### 先区分三个概念

```text
Service lookup          : nearest-wins，exclusive（provider record per key）
Event propagation       : dispatch 时事件如何到达 handler
Event registration visibility : 一个 handler 对哪些 emit 可见
```

**不得**因为 Service nearest-wins 就假设 Event nearest-wins。Event 无
exclusivity，无 nearest-wins，无 shadowing。

### Decision：emitter-path 匹配模型

```text
emit(E, payload) 由 realm path = P(emitter) = [R_emit, ..., Root] 的 fiber 发出
⇒ dispatch 收集的 handler = 所有注册 realm HR ∈ P(emitter) 且 owner 有效 的 E-handler
⇒ 全部 handler 按序执行（当前实现策略：全局注册序；见 D5b）
```

### 逐项回答

场景：`Root ├── A（A 注册 E-handler）└── B`。

| 问题 | 回答 |
|---|---|
| Root 是否可见？ | A 注册于 Root ⇒ 对所有 emit 可见（Global Event）。 |
| A 是否可见？ | A 注册于 RA ⇒ 只对 RA 自身及 RA 子孙的 emit 可见；Root 层其它成员（不在 RA path 上）的 emit 不可见。 |
| B 是否可见？ | B 是 sibling ⇒ B 的 emit path 不含 RA ⇒ 不可见（构造性隔离）。 |
| child 是否继承？ | A 注册于 RA 的 handler 对 RA 的 child scope emit 可见（child path 含 RA）。 |
| ancestor handler 是否参与 child dispatch？ | 是（Root/父 scope handler 参与一切 child dispatch）。 |
| sibling 是否隔离？ | 是（sibling path 互不含彼此 realm）。 |
| 是否存在 shadowing？ | 否（handler additive；child 不遮蔽 ancestor）。 |

平台 spec §12 例子（Root + Scope X{B,C}）下：X 内 handler 对 X 成员 emit
可见 ✅；对 sibling 不可见 ✅；Root 注册的 handler 全局可见 ✅。

### Scope Semantics（正式）

- `S(emit) = { HR : HR ∈ path(emitter) }`；注册 realm 的可达集合 = 其子树。
  命中 = 交集。无独立 capture/bubble/shadow 概念。
- ordering：见 D5b（实现策略，非语义要求）。

## 6. D5a — Dispatch Execution Domain（PASS）

- `ctx.Emit/Serial/Parallel/Waterfall` 由持有活 Context 的 plugin/application
  代码在调用方 goroutine 发起。
- handler（plugin code）**永不在 orchestrator goroutine 执行**、永不在
  orchestrator 决策锁内执行（§3/§5 硬约束；与 `extensions/event` “user code
  never under the lock” 同原则）。
- dispatch 流程：在 realm 锁下收集**一致性快照**（path 命中 + owner 有效性
  检查），释放锁后在调用方 goroutine 执行 handler。
- **快照一致性**：dispatch 期间的新注册/注销不改变当前批次；下个 dispatch
  才可见。每个 dispatch 在其快照点线性化。

## 7. D5b — Ordering（v0.2：从语义要求降级为实现策略）

### Kernel semantic requirement（唯一冻结项）

> Dispatch ordering 必须有精确定义，且**不依赖 map iteration order**。

### 当前实现策略（可替换，非语义）

> 全局单调注册 seq（单计数器）作为序键；跨 realm 命中 handler 按该 seq
> 升序执行。

为什么降级：ordering 是 dispatch 的可观测性质，但“用什么键”属于实现选择。
若未来需要 scoped ordering / composition generation 等，可在不改变本记录
语义的前提下替换策略（P1.x 实现文档需记录所采用的键与不变量）。

## 8. D6 — Handler Authority（v0.2：移交 P2，不冻结）

### v0.1 问题

v0.1 把 “handler 无 Context，因此不能 Require/Emit” 冻结为 Kernel invariant。
[CORDIS-THISARG] 显示 Cordis listener 可拥有 `this: Context`（`events.ts`
提供 `thisArg`，并支持 `Context.filter` 过滤 listener）；DeepSeek Harness 的
listener 也会围绕 Context/service 能力工作。把“无 Context”过早冻结会把
P2 Plugin Platform 的能力封死。

### v0.2 Decision

```text
Kernel Event dispatch 不向 handler 暴露 Runtime internals：
handler 不得直接持有 Runtime / Orchestrator / Registry / realm 内部对象，
不得由此获得 lifecycle authority（Load/Dispose）或 registry 写入能力。

Handler 是否获得受限 Context / EventContext（以及能否 Require / Emit /
读取 service），属于 Public Plugin API 层决策 → 移交 P2，本记录不冻结。
```

### Rationale

- 真正的红线不是“有没有 Context”，而是 **authority 边界**：任何 handler-facing
  API 都不得让 plugin 绕过 Runtime 单决策域（platform spec §3）。
- P1 Kernel 只保证 dispatch 不泄漏 internal authority；P2 再决定受限
  Context 的形态（也可选择不给）。
- [CORDIS-THISARG]（snippet）：`ctx.emit(thisArg, ...)` + `Context.filter` —
  作为 P2 决策输入，非本阶段冻结项。

## 9. D7 — Cancellation Semantics（PASS，随 D6 澄清）

- dispatch 绑定 **emitter Context**（`runtime/context.go:133`，activation-
  scoped，unwind 时 cancel）。
- 逐 handler 执行前检查取消：
  - Emit/Serial：已取消 ⇒ 停止剩余，返回 `ctx.Err()`（与已聚合 error 以
    `errors.Join` 返回）。
  - Parallel：不再启动新 handler；已启动 handler 全部等待结束（无 leak）。
  - Waterfall：下一阶段前/每次 `next` 前检查（具体代数见 P1.4）。
- handler-facing 形态若在 P2 变化（如 handler 获得受限 Context），cancellation
  绑定 emitter 的原则不变。

## 10. D8 — Emit（v0.2 澄清：同步 broadcast ≠ await）

### Decision

```text
Emit = synchronous broadcast（[DSH-EVENTS] dispatch-mode 表）
```

| 维度 | 语义 |
|---|---|
| 是否等待 handler | handler 是**同步函数**（Go 无 Promise）；“完成”= 同步函数返回。**不存在 await semantics** |
| 调用范围 | 同步调用全部命中 handler |
| handler 顺序 | 按 D5b 序键调用（确定性实现策略） |
| 是否收集返回值 | **不收集**（fire-and-observe；[DSH-EVENTS]：returned promises/values are not awaited or collected） |
| handler error | Go 特有 error 通道：调用全部 handler 后以 `errors.Join` 返回给 emitter；这是同步 error 上报，**不是**把 Emit 变成 await-all 的 dispatch mode |
| cancellation | 逐 handler 检查；已取消停止剩余并返回 ctx.Err() |
| dispatch 中增删 | 不影响当前批次（快照）；下个 dispatch 生效 |

### 关键澄清（评审要求）

- “Emit 返回前所有命中 handler 已执行完” = 同步函数调用完成，**不是 await**。
- 不得把 Emit 描述成“同步 + await-all”——那会把 Emit 与 Parallel（await-all
  dispatch mode）的边界压缩。Emit 与 Parallel 是**两个不同 dispatch mode**
  （[DSH-EVENTS] mode 表），语义边界必须保持。

## 11. D9 — Serial（P1.2 review；仅冻结框架）

### 冻结（不变）

```text
A → B → C：严格按序；前一个完成才启动下一个（await previous before next）。
```

### 参考语义（[DSH-EVENTS]，P1.2 需转成 Go 约定）

```text
Listeners run in order, awaited; the first non-null/false/undefined return
wins and stops the rest.（bail = synchronous version of serial）
```

### 未冻结（P1.2 review）

- Go 中 “first non-null/false/undefined wins” 如何落地（error？value？
  哨兵？）—— 由 P1.2 implementation spec 决定，本记录不提前冻结。
- 取消（随 D7）、dispatch 中增删（快照）已冻结，不重审。

## 12. D10 — Parallel（P1.3 review；仅冻结框架）

### 冻结（不变）

```text
       A
Event ┼ B   并发执行，await all
       C
```

- await all：每 handler 一个 goroutine；调用返回前全部结束（[DSH-EVENTS]：
  all listeners run concurrently; awaited together）。
- 无顺序保证、无启动顺序保证（显式 out of contract）。
- cancellation：停止新启、等待已启（无 leak）。
- dispatch 中增删：快照批次不变。

### 未冻结（P1.3 review）

- error / value 聚合的确切 Go 形态。
- 共享状态规则：v0.1 保持“handler 之间不得通过共享可变对象传递结果”，
  P1.3 复核是否放宽。

## 13. D11 — Waterfall（v0.2：algebra 不冻结，移交 P1.4 semantic review）

### v0.1 问题

v0.1 冻结了 forward algebra：`next(w)` 把新值传给下游。评审指出这不是
Cordis 的 waterfall 语义，两者是**不同的组合代数**。

### 已核验参考（[DSH-EVENTS]）

```text
waterfall = around-middleware：
listener(input, next)
  ├─ result := await next()          // 取得 downstream result
  └─ return transform(result)        // 在“回来”的路上包装
  或 return ...（不调 next）= short-circuit / veto
```

与 v0.1 的 forward algebra（`next(newInput)` 传值下去）是两种模型；P1.4 必须
在参考 [DSH-EVENTS]/[PAPER-MAP]/stc-go 后二选一并冻结，**本记录不提前决定**。

### 先行冻结（与 algebra 无关，评审同意保留）

- W-05 / W-07：handler 注册是 effect-owned；无 detached listener。
- 确定性 ordering 与“不依赖 map iteration”（D5b）。
- cancellation 绑定 emitter Context（D7）。
- dispatch 中增删 = 快照批次不变。

### 未冻结（P1.4 review 清单）

1. around-middleware vs forward-algebra（二选一）。
2. `next` 的确切签名与 exactly-once 规则。
3. short-circuit 的最终结果取值规则。
4. error / cancellation 在链中的精确传播。
5. nested waterfall（handler 内 dispatch）—— 与 D6/P2 联动。
6. 是否需要分层序（若 P1.4 采用 wrap 语义，D5b 实现策略可能需调整——已
   降级为策略，允许替换，无需改语义层）。

### P1.1 不受影响

Emit（P1.1）完全不依赖 Waterfall algebra；P1.1 可在本记录未决前独立实现。

## 14. D12 — Event 与 Dependency（PASS）

- Event dispatch **不提供依赖解析**。
- Handler 依赖 Service 的唯一合法路径 = owner activation 在 Apply 期通过
  Kernel Dependency 解析（[DSH-EVENTS] 同款：reporter 插件 `inject: ['stats']`
  声明依赖，listener 使用已注入的 service）——具体以 P2 的 handler API 落地：

```text
Handler
   ↓（owner Apply 期经 Kernel Dependency 解析/注入）
Service
   ↓
gocordis Dependency（realm nearest-wins，provider generation identity）
```

- Event 永不管理 service 生命周期/可见性；provider 消失仍由 Kernel
  withdrawal 处理（与 `extensions/event` “Subscription 不是 Dependency” 边界
  一致）。禁止第二套 dependency runtime（platform spec §18）。

## 15. D13 — Event 与 Kernel canonical stream（PASS）

- `runtime/events.go` canonical stream 是 lifecycle fact 流（UI-02）。
  P1 dispatch 不是 lifecycle fact，不进 canonical stream。
- handler 注册/注销经 Effect kind 元数据（key = event name）在 UI-02
  EffectView 可见（与 Provider/Intercept 同级可观测）。
- dispatch 执行本身对 Kernel 观测面不可见（plugin 业务行为）。

## 16. D14 — `extensions/event` 审计（PASS，评审批准不迁移不废弃）

基于 `extensions/event/event.go`（217 行）与其测试 E1–E14：

| 类别 | 结论 |
|---|---|
| 可复用（原则） | exact-match registry；单锁线性化；user code 永不锁内执行；单调 sequence；幂等 Close 无 recover 纪律；“Subscription 不是 Dependency”边界 |
| 不可复用（原因） | channel 投递（fire-and-forget、有界缓冲、慢订阅丢事件）；无 owner（手动 Close、无 auto-GC）；无 await；无 scope；裸 string EventType |
| 需要迁移 | 无（保持 extension 身份与现语义，E1–E14 继续 PASS） |
| 必须废弃 | 无 |

评审结论：“something happened”通知（extension）与 “Plugin composition
point”（P1 Event）是两个不同抽象；未来需要 `P1 Event → extension bus`
桥接时再做显式 adapter（P2+ 决策）。

## 17. Formal Semantics（汇总）

### Event 语义（统一）

```text
Reg        = 形如 {HR, owner(f,a), 序键, E, h} 的注册集合
Dispatch   = emit(E, payload) by emitter fiber f_e in realm R_e
Snapshot   = { r ∈ Reg : r.E == E, r.HR ∈ path(R_e),
              owner(r) valid(Active + activation match) }
Order      = 按 r.序键（D5b 实现策略；语义只要求有定义、不依赖 map 序）
执行       = 调用方 goroutine，锁外，逐 handler，见各类型策略
```

### Lifecycle Semantics

- 注册只发生在 owner activation Apply 期；unwind 只经 effect inverse 删除；
  owner 有效性随 activation 状态变化（Active 才参与新 dispatch；Unloading 起
  对新 dispatch 不可见；inverse 后物理消失）。
- LIFO：同 owner handler 逆注册序删除（共享 `beginUnwind`/`runInverses` LIFO）。
- 不 bypass Quiescence/Reconciliation：注册/注销是 effect；dispatch 是调用方
  行为，不产生 lifecycle 决策。

### Scope Semantics

见 §5：emitter-path 匹配；ancestor handler 参与 child dispatch；sibling
隔离；无 shadowing；无 exclusivity。

### Concurrency Semantics

- 快照点一致性；dispatch 中增删不影响当前批次。
- handler 与 owner unwind 并发：在途 dispatch 可执行“快照时仍有效”的 handler；
  受 panic 隔离与 emitter-cancellation 约束；owner unwind 不等待在途 dispatch，
  在途 dispatch 也不延长 owner 生命周期（无反向 gate）。
- Parallel：无 goroutine leak。
- 所有 registry 变更在 realm 锁下线性化；所有 handler 执行在锁外。

### Error / Cancellation Semantics（冻结状态矩阵）

| Item | 冻结状态 |
|---|---|
| Emit：全执行 + errors.Join；≠ await | ✅ 冻结（D8） |
| Serial：ordering + await previous | ✅ 冻结；result/error 约定 → P1.2 |
| Parallel：await all + error 聚合 | ✅ await all 冻结；聚合形态 → P1.3 |
| Waterfall：error 传播 | ❌ → P1.4 |
| panic 隔离（handler panic = handler error） | ✅ 冻结（所有类型） |
| cancellation（emitter ctx；逐 handler/每次 next） | ✅ 冻结（D7） |

## 18. Existing Implementation Gap

| Gap | 现状 | 需要新增（P1.x，未实现） |
|---|---|---|
| Typed Event identity | 无 | `EventKey[T]`（key.go 模式） |
| Handler registry | 无 | realm-scoped own handler chain + owner identity + 序键 |
| Effect kind=Event | 只有 Provider/Cleanup（`runtime/context.go:33`） | 新 EffectKind（仅元数据） |
| `ctx.On` | 无 | Effect 封装注册/注销（Kernel 面；listener 签名形态属 P2/D6） |
| Dispatch executors | 无 | Emit（P1.1）；Serial/Parallel/Waterfall（P1.2–P1.4） |
| Snapshot 收集 | 有 realm lookup 先例 | path 命中 + owner 有效性检查 |
| Scope 命中 | 有 realm path 先例（lookup / interceptsForKey） | 同一 walk 反向使用 |

P1.1 可直接复用：Effect slot/LIFO inverse（D2 核心）、typed key（D1）、realm
path walk（D4）、panic 隔离纪律、确定性 driver 测试设施。

## 19. Deferred Decisions

1. **Handler-facing Context/EventContext**（D6）：P2 决策，输入
   [CORDIS-THISARG]（`thisArg`/`Context.filter`）。P1 只保证不泄漏 internal
   authority。
2. **Waterfall algebra**（D11）：P1.4 semantic review；around-middleware vs
   forward-algebra 二选一；nested waterfall 与该决策联动。
3. **Serial result/error 约定**（D9）：P1.2 review（Go 落地
   first-non-null/false/undefined-wins）。
4. **Parallel 聚合与共享状态**（D10）：P1.3 review。
5. **Async handler**：handler 一律同步函数；promise/future 桥需独立 typed
   抽象（P2 评估）。
6. **`ctx.Off` 显式注销**：v0.1 只经 effect unwind 删除；真实用例出现再评估。
7. **ordering 键选择**（D5b）：实现策略，P1.x 文档记录所选键与不变量。
8. **Event 桥接 `extensions/event`**：P2+ 显式 adapter。
9. **DeepSeek Harness 其余文档 / Cordis events.ts 全量 / stc-go**：逐行
   correspondence 待外部材料可 fetch 后补齐（当前 external-required）。

## 20. P1.1 Emit Implementation Contract（不实现，仅契约）

### Scope of P1.1

```text
EventKey[T]
  + realm handler registry（owner identity + 序键）
  + ctx.On（Effect 封装，inverse unregister）
  + ctx.Emit（同步 broadcast）
  + snapshot + scope matching + owner validity
  + deterministic registration ordering（D5b 策略）
```

明确 **不做**：Serial / Parallel / Waterfall / Plugin API / Service API /
Loader / HMR / `extensions/event` 改动 / Kernel 其它改动。

### Contract

1. `On(E, h)`：经 `ctx.Effect(kind=Event, key=E)` 注册；inverse 精确移除
   (owner, 序键) 条目。
2. `Emit(E, payload)`：调用方 goroutine 收集 path 命中 + owner 有效快照 →
   按序键执行（同步）；逐 handler 检查 emitter ctx；全部执行后
   `errors.Join` 返回（handler error 上报；≠ await）。
3. Kernel 面 handler 签名最小形态 `func(T) error`（测试用）；最终
   listener 签名形态属 P2/D6，不在 P1.1 冻结。
4. handler 不持有 Runtime internals；panic 隔离转 handler error，不中断其它
   handler（Emit 语义）。
5. 语义保证（测试 oracle）：
   - 注册序确定性执行（不依赖 map 序）；
   - owner unwind 后 handler 不可见且 registry 无残留（no-handler-leak）；
   - sibling scope 隔离；root handler 全局可见；父 handler 见 child emit；
   - dispatch 中注册/注销不影响当前批次；
   - emitter ctx 取消 ⇒ 剩余 handler 跳过并返回 ctx.Err()。
6. Kernel/extensions 生产代码仅允许新增 P1.1 自身文件与 conformance 测试
   （测试在 P1.1 Implementation Spec 批准后编写）。

### Boundary

本记录 zero code / zero test。P1.1 Implementation Spec 在本记录 v0.2 评审
通过后另行下发。
