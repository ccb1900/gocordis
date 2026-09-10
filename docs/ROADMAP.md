# Paper-First Roadmap — gocordis v2.0

Date: 2026-09-08。Authority: [`论文.pdf`](./论文.pdf)(唯一规范来源,见 [`README.md`](./README.md))。
本文档取代全部已归档文档中的路线图/映射/矩阵;旧定理编号(T59/T61/T63/T66/T73,沿袭自
stc-go)自本文起停用,改用论文编号。

## 0. 论文结构速览(锚点索引)

| 论文章节 | 内容 | 与本仓库的关系 |
|---|---|---|
| §3.1 | Revertible effects(effect context、effect functions、**effect iterators**) | 已实现(槽位形态);迭代器形态未实现 → P4 |
| §3.2 | Reactive coeffects(get/set、specification/notification、**isolation、interception**) | 已实现但机制与论文不同 → P2/P3 |
| §3.3 | Context paradigm(统一 context、observational equivalence) | 已实现 |
| §4.1–4.2 | Components/fibers、orchestration、lifecycle(六规则 L-Begin/L-Iter/L-Finish/L-Divert/L-Leave/L-Unload)、confinement | 已实现 |
| §4.3 | 元理论:**Theorem 64** (Preservation)、**68** (Recovery exactness)、**70** (Ordering)、**73** (Progress)、**80** (Confluence) | 五套定理测试已实现,编号待重锚 → P1 |
| §4.4 | 四个扩展:Asynchrony、Failure、Isolation(K×R 编码)、Configuration/revision | Asynchrony/Failure 已实现;Isolation 机制不同;revision 部分 → P5 |
| §5 | 实现:Cordis core library、declarative loader(配置调和 + HMR) | loader/HMR/config 已有扩展形态 → P5 统一 |
| §6 | 讨论:service multiplexing、access control、sandboxing、语言独立性、依赖类型/版本 | 扩展层 partially → P7 |

## 1. 论文 ↔ 代码对照表(经原文复核,2026-09-08)

### 1.1 已对齐(论文编号为锚)

| 论文条目 | 语义 | Go 落点 | 测试证据 |
|---|---|---|---|
| Def 48/49 | Component / Fiber(实例 ≠ 定义) | `runtime/component.go`、`runtime/fiber.go` | declarations_containment D1–D8 |
| Def 50/51 | registry(单决策域) | `Runtime.fibers` + orchestrator | proof_invariants |
| Def 52 | 实例化 = 父的被追踪 effect(父卸载级联子) | `ctx.Child` + `runtime/ownership.go` | phase6_contract、deterministic_close |
| Def 53 | target view / quiet | `runtime/reconcile.go` | thm73_thm80_test |
| Def 54 | relied 守卫(consumer 先于 provider 撤离,按绑定) | `runtime/dependency_graph.go` | TestThm70WithdrawalOrdering |
| **Theorem 64** | Preservation(良构性 4 条款) | P1..P5 不变量(on-orchestrator) | proof_invariants_test、thm64_preservation_test |
| **Theorem 68** | Recovery exactness(episode 累加器 ≃ 未开始) | LIFO/恰好一次 + 基线等价 | thm68_recovery_test |
| **Theorem 70** | Ordering(L-Begin 门控;provider episode 严格包住 consumer) | 依赖门控 + 撤离顺序 | thm70/c3 |
| **Theorem 73** | Progress(≺ 无环 + 有限 + 合作 ⇒ quiesce,含终止界) | deterministic step driver | thm73_thm80_test |
| **Theorem 80** | Confluence(规范形 + 合流;**排除 failed fiber**——与论文一致) | 合流生成器 + 前置条件 | thm73_thm80 |
| §4.4 Asynchrony | inertial host(L-Divert 只取 landing 替代)仍 quiesce | orchestrator 异步完成 + deterministic mode | det_admission、deterministic_close |
| §4.4 Failure | raise → Unloading → Inactive,Installed nothing;outcome 阻止重入;**不向父传播**;failed 留在 registry | `finalizeActivation` Failed 分支 | TestApplyFailureEndsFailed、TestFailedDisposeLoadRetryCycle |
| §4.4 Configuration | revision = 卸下→逆→移除(子先父后)→同名重插;re-enable 走复合而非改 τ | HMR/loader 的替换路径(partial) | hmr H1–H20 |

### 1.2 已实现但机制与论文不同(需要 ADR 裁决,见 P2/P3)

| 论文条目 | 论文语义 | 现实现 | 偏差性质 |
|---|---|---|---|
| **Def 24/25 + §4.4 Isolation** | **逐 key** 的 realm 表 ρ:K⇀R,**插入时固定**;isolate 运行时重指派 = 接口变更 = revision;经 K×R 配对编码,元定理在多 realm 下原样成立("provisions disjoint **within a realm**");无层级、无祖先回退、无 shadow | 层级 realm 树:WithScope 子 realm、继承、shadow、祖先回退(含 GAP-01 的 retiring-shadow 规则) | 重新解释 + 超结构。论文根本没有 ancestor fallback 概念,GAP-01 规则是本仓库独有操作规则 |
| **Def 26/27** | interception = **元数据 monoid 合并**(声明 d(k) ⊕ 上下文 ι(k),right-biased、context 优先),provider 是 ℳₖ→𝒱ₖ 的**函数**,读取时解释元数据;承载 §6.3 capability 访问控制 | `runtime.Intercept[T]` 函数变换链,直接改值 | 同目的、异机制。现实现表达不了"provider 依元数据做策略裁决" |
| **§3.1.3 + L-Iter/L-Divert** | 激活 = **效果迭代器**逐 yield(L-Iter 一步一迭代,L-Divert 可落在迭代之间);异步 host 取 landing 替代(inertial) | Apply 一次到位 + ctx.Effect 槽位注册,激活不可中断(仅完成时检查 stale) | 宿主化简化(stc-go 同);per-iteration divert 粒度缺失 |
| §4.2.2 末段 | guard 沿 coeffect 排序,**不沿 fiber 树**("a parent may run its inverse while a child is still Unloading");仅 O-Remove(entry 移除)要求子先父后 | ownership 树强制 child cleanup 严格先于 parent(P0-08) | 比论文更强的保证(不违背,但表述应改为 operational strengthening) |

### 1.3 扩展层分类(非内核语义,不得成为第二生命周期权威)

R4 评审(2026-09-09)按"是否服务论文实现"将扩展定类:

| 类 | 判据 | 成员 |
|---|---|---|
| **A — 论文 §5.2 实现章** | 直接实现 declarative loader / config reconciliation / HMR(含 §5.2.1 disable/re-enable:ComponentConfig.Enabled 开关,R5 落地) | loader、config、configwatch、watch、hmr |
| **B — 论文 §6 讨论项落地** | P7 逐项实现 | broker(§6.2)、loader/wasm(§6.3 沙箱) |
| **C — 平台能力(非论文语义,有消费方与测试约束)** | runtime 的通用补充 | event(派发模式之家,P6 起)、registry(稳定成员,broker 底座)、scheduler(周期任务)、UI-03 事件流(console/events,2026-09-09:Observer 环形保留 + 序号锚点续传订阅;内核只留单调序号计数器 + WithEventSink 汇点钩子,paper-neutral 观测面进一步收缩) |
| **D — 实例/演示(转移)** | 某绑定模式的可运行样例,非语义、非通用能力 | ~~extensions/http~~ → **cmd/httpd**(2026-09-09:外部资源绑定模式参考——Active ⇔ serving、撤销先于终态、同地址重绑;零生产消费方,聚焦测试随迁) |

- `RuntimeDeterministic` 调度插桩:定理验证基础设施(已内部化,R3),保留。
- `cmd/` 演示程序(backfill/collector/host/example/wasmhmr/httpd):论文 §5.3 case study 的角色。
- kernel 内事件派发已按 ADR-0004 移入 extensions/event(P6 完成);Bus 已移除(R3)。

## 2. 演进阶段

每个阶段:先 ADR/规格,后实现,定理级 conformance 收尾;门禁恒为
`go test -count=1 ./...` + `go test -race -count=1 ./...` + `go vet` + gofmt(即 `make verify`),
新增机制必须配 fuzz target(确定性种子,失败可复现)。

### P1 — 证据重锚定(纯文档 + 测试改名,零语义变更)✅ 2026-09-08 完成

- [x] 定理标识符与测试文件全量改用论文编号(`TestThm64*`/`thm64_*` 等;采用整体改名
      而非双标签——旧名在 git 历史与本文 §4 速查表中保留)。文件:
      `thm64_preservation_test.go`、`thm68_recovery_test.go`、`thm70_ordering_test.go`、
      `thm73_thm80_test.go`、`c1_thm68_min_test.go`、`c2_thm64_every_step_test.go`、
      `c3_thm70_ordering_test.go`。
- [x] 论文语义偏差已由原文复核固化于本文 §1.2(取代旧映射表述;P0-08 强化为
      operational strengthening、GAP-01 无论文对应物的结论见 ADR-0001)。
- [x] ADR 草案:`docs/adr/0001-isolation-per-key-realms.md`(P2)、
      `0002-interception-metadata-monoid.md`(P3)、`0003-effect-iterators.md`(P4)、
      `0004-kernel-event-demotion.md`(P6 预研)。
- 验收:`go test -count=1 ./...`、`go vet`、gofmt、`go test -race ./...` 全绿。

### P2 — ADR-I:Isolation 回归论文语义(Def 24/25)✅ 2026-09-08 完成(ADR-0001 Accepted,实施记录见 ADR)

- [ ] 决策:以**逐 key realm 表(插入时固定)**为原语;运行时 isolate(k, r) 落为 revision
      (卸下→重插,§4.4 Configuration 复合)。
- [ ] 现有层级 scope 的处置:降级为派生语法糖(编译到逐 key 表)或废弃;祖先回退/shadow
      若保留,必须作为显式 ADR 扩展并配 conformance,不得冒称论文语义。
- [ ] 利用论文 §4.4 的 K×R 配对论证:多 realm 并存与 Theorem 64/68/70/73/80 兼容,
      定理生成器扩展到 realm 维度。
- 验收:Def 24/25 逐条 conformance(get/set/isolate 运输预条件、重指派语义);
  Theorem 80 合流生成器含 realm 交错。

### P3 — ADR-II:Interception 回归论文语义(Def 26/27)✅ 2026-09-08 完成(ADR-0002 Accepted)

- [ ] Key[T] 携带元数据类型 ℳₖ 与 monoid(⊕ₖ, εₖ);provider 可声明为解释元数据的函数。
- [ ] `runtime.Intercept` 演进为 `intercept(k, ν)` 元数据合并(right-biased),旧函数链适配或废弃。
- [ ] 解锁 §6.3:capability 式访问控制(provider 按元数据裁决),拦截可在运行时增删而不触发 reload
      (论文:"it affects only how a dependency is invoked, not whether it is satisfied")。
- 验收:Def 26/27 conformance;访问控制示例进入 case study;fuzz 覆盖合并序。

### P4 — 效果迭代器(§3.1.3 / L-Iter / L-Divert)✅ 2026-09-08 完成(ADR-0003 Accepted)

- [ ] 组件激活可选表达为 `iter.Seq`(Go 1.23 range-over-func)效果迭代器:每 yield 一个
      (context 变换, inverse);Apply-once 形态保留为单迭代特例(源兼容)。
- [ ] orchestrator 支持 **L-Divert 落在迭代边界**(inertial host:只取 landing 替代——
      论文 §4.4 明确该替代下全部元定理仍成立,Theorem 73 不依赖 aborting 替代)。
- [ ] deterministic driver 扩展到迭代粒度(divert 时刻成为可调度步骤)。
- 验收:Theorem 68/70 在迭代粒度交错下保持;T66(T73 Progress)驱动器覆盖 divert 路径;
  新增 FuzzIteratorInterleaving。

### P5 — 声明式 revision 统一(§4.4 Configuration + §5.2)✅ 2026-09-08 内核原语完成

- [x] 内核 revision 原语:`Fiber.Revise(ctx, component)` — 论文复合的严格实现
      (retire → deactivate(relied 守卫排序)→ O-Remove(子先父后)→ 同位重插:
      同 parent / 同 namespace / 同逐 key 表 + 新定义);依赖者经 target-view 比较
      自发重激活;Failed fiber 的 Revise = 有据重试(重插无 outcome)。
- [x] Theorem 80 终点性质:`TestThm80RevisionEndpointEquivalence` — revision 终点的
      无身份 canonical observable(逐名状态/绑定/提供集)与从头加载修订配置逐行相等。
- [x] (R1 评审补齐,2026-09-08)`WithFreshIsolation()` — revision 重指派 realm 对
      (论文 §4.4:"the new realm pairs");`TestReviseReassignsIsolation` 固化。
- [ ] loader/HMR 短路径(候选先行、载荷不变不 reload)对齐同一终点 — 扩展层改造,随后续阶段。
- 验收(已完成部分):全量 + race + vet + gofmt 绿;Thm80 终点等价测试 -count=3 稳定。

### P6 — ADR-III:内核瘦身至论文面 ✅ 2026-09-08 完成(ADR-0004 Accepted)

- [x] 事件派发(emit/serial/parallel/waterfall + 新增 bail)移出 kernel →
      extensions/event;kernel 仅保留注册 = 可逆 effect + EventBindings 读模型。
- [x] P1 事件一致性套件与 gap 套件迁至 extensions/event(断言不变,内部访问点改公开面)。
- [x] 审计 kernel 其余非论文物(Snapshot/事件日志为观测投影,保留并标注平台层;
      deterministic 插桩为定理基础设施,保留)。
- 验收:kernel 公开 API 逐项可锚定论文条目或 ADR;全量测试 + race 绿。

### P7 — §6 讨论项的扩展层落地(按需排序)◐ 2026-09-08 首项落地

- [x] Service broker / 多路复用(§6.2):`extensions/broker` — 可逆 effect 注册、
      RoundRobin/First 策略、滚动升级消费者无感(broker_test.go 两项一致性)。
      与 registry extension(独占绑定)互补,覆盖 §6.2 的两种形态。
- [x] 跨进程调用(§6.2):`extensions/loader/proc` — 进程外插件后端
      (stdio JSON-RPC 2.0;每激活一进程;握手即就绪;卸载先于终态;崩溃不自动重启;
      进程边界非沙箱的边界声明)。2026-09-09。
- [ ] WASM 沙箱一致性(§6.3):guest 能力面 = 声明集的形式化。
- [ ] 依赖类型/版本(§6.6):Key 元数据演进的自然延伸。

## 3. Go 最佳实践约定

- **效果迭代器用 `iter.Seq`**(Go 1.23+;go.mod 已 1.24):与语言惯用迭代协议对齐,
  range-over-func 让组件作者以顺序代码表达逐 yield 激活。
- **泛型 typed key**(已有 `Key[T]`)继续作为 capability 请求的载体(§6.3:
  "inject declaration acts as a capability request")。
- **单一 orchestrator 协程**保持不变——这是论文 §4.2.2 "reactive only, no scheduler"
  (规则 nondeterministic、定理对所有合法步序成立)在宿主中的直译。
- 错误:sentinel + `%w` 包装(已有);公开 API 方法集最小化;新接口在消费侧定义。
- 测试文化保持:确定性种子、信号驱动零 sleep、on-orchestrator 不变量检查、每机制一个 fuzz target。

## 4. 与旧编号的对照(过渡期速查)

| 旧(stc-go 沿袭) | 论文 |
|---|---|
| T59 | Theorem 64(Preservation) |
| T61 | Theorem 68(Recovery exactness) |
| T63 | Theorem 70(Ordering) |
| T66 | Theorem 73(Progress) |
| T73 | Theorem 80(Confluence)⚠ 注意:论文 Theorem 73 是 Progress,勿混淆 |
