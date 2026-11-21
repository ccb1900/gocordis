# Paper-First Roadmap — gocordis v2.0

Date: 2026-09-08。Authority: [`论文.pdf`](./论文.pdf)(唯一规范来源,见 [`README.md`](./README.md))。
本文档取代全部已归档文档中的路线图/映射/矩阵;旧定理编号(T59/T61/T63/T66/T73,沿袭自
stc-go)自本文起停用,改用论文编号。

## 0. 论文结构速览(锚点索引)

| 论文章节 | 内容 | 与本仓库的关系 |
|---|---|---|
| §3.1 | Revertible effects(effect context、effect functions、**effect iterators**) | 已实现:Effect 槽位状态机 + **IterComponent 逐 yield 迭代器**(ADR-0003 Accepted,P4 完成) |
| §3.2 | Reactive coeffects(get/set、specification/notification、**isolation、interception**) | 已按论文实现:逐 key realm 表(ADR-0001)+ 元数据 monoid(ADR-0002),均 Accepted |
| §3.3 | Context paradigm(统一 context、observational equivalence) | 已实现 |
| §4.1–4.2 | Components/fibers、orchestration、lifecycle(六规则 L-Begin/L-Iter/L-Finish/L-Divert/L-Leave/L-Unload)、confinement | 已实现(含迭代边界 divert) |
| §4.3 | 元理论:**Theorem 64** (Preservation)、**68** (Recovery exactness)、**70** (Ordering)、**73** (Progress)、**80** (Confluence) | 五套定理测试已实现并按论文编号重锚(P1 完成) |
| §4.4 | 四个扩展:Asynchrony、Failure、Isolation(K×R 编码)、Configuration/revision | 四项均已实现:Isolation=逐 key realm(ADR-0001),revision=Fiber.Revise 复合(P5) |
| §5 | 实现:Cordis core library、declarative loader(配置调和 + HMR) | loader/HMR/config 扩展形态已就位,P5 内核原语完成 |
| §6 | 讨论:service multiplexing、access control、sandboxing、语言独立性、依赖类型/版本 | 扩展层逐项落地(broker/proc/wasm);WASM 沙箱形式化与依赖版本为 P7 延后项 |

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
| **Theorem 73** | Progress(paper p.49: ≺ 无环 + 有限 N + 有界 K + 每步为 lifecycle rule ⇒ no-deadlock + S(n)≤(K+3)(V(n)+1) ⇒ quiesce)。**"合作"不是论文前提**（仅 archived 验证假设）；quiescent=eq.49 "no transition left in progress"——Active+Installing/Undoing effect **不是** quiescent（effect barrier 后 conformance oracle 已同步） | deterministic step driver + 双 quiescence oracle(含 effect 状态) | thm73_thm80_test、obs_test/TestThm73QuiescenceSeesInstallingEffect |
| **Theorem 80** | Confluence(规范形 + 合流;**排除 failed fiber**——与论文一致)。**deterministic interleaving conformance coverage（非完整定理证明）**:同一操作集的 ApplyDone/UnwindDone 不同提交顺序 → 终态在 ≃ 投影(忽略 Lemma 61 生成名重命名)下等价;明确断言 guard 链(consumer unwind 先于 provider unwind)与 Revise canonical order(旧 consumer unwind → 旧 provider unwind → 新 provider apply → consumer reattach);非法交错被 guard 变成不可达 = Thm80(1) canonical form 的结构性证据 | 合流生成器 + 前置条件 + det 交错 + ≃ 投影 | thm73_thm80、thm80_det_interleaving_test、revision_thm80 |
| **Def 27 (Derived realization)** | Interception 必须派生新 context;不得原地修改共享上下文,否则元数据跨根泄漏 | `Context.metaView` + `Child()` 派生快照 + `newContext` 注入;独立根无继承(ADR-0002 修订 2026-11-21) | `meta_intercept_test.go:TestMetaInterceptDoesNotLeakAcrossIndependentRoots` |
| §4.4 Asynchrony | inertial host(L-Divert 只取 landing 替代)仍 quiesce | orchestrator 异步完成 + deterministic mode | det_admission、deterministic_close |
| §4.4 Failure | raise → Unloading → Inactive,Installed nothing;outcome 阻止重入;**不向父传播**;failed 留在 registry | `finalizeActivation` Failed 分支 | TestApplyFailureEndsFailed、TestFailedDisposeLoadRetryCycle |
| §4.4 Configuration | revision = 卸下→逆→移除(子先父后)→同名重插;re-enable 走复合而非改 τ | `runtime/revise.go`/`fiber.go:Revise` + HMR/loader 替换路径 | thm80_revision、revise_race_test、hmr H1–H20 |

### 1.2 历史偏差记录(已关闭,勿当作当前待办)

> **历史记录**:下表记的是 2026-09-08 复核时的**旧状态**,四项偏差均已由 P2/P3/P4 关闭。
> 当前实现与裁决见 §1.1、ADR-0001/0002/0003 与各阶段实施记录。保留此表仅供追溯,
> 不得据此重新引入祖先回退或推翻已接受的架构决策。

| 论文条目 | 历史偏差(已关闭) | 关闭方式 | 当前状态 |
|---|---|---|---|
| Def 24/25 + §4.4 Isolation | 旧实现为层级 realm 树 + 祖先回退 + shadow | ADR-0001 Accepted:逐 key realm 表 ρ,插入时固定,**无层级/无祖先回退/无 shadow**;`providers.go:lookupOwn` 严格逐 realm | ✅ 已对齐 |
| Def 26/27 | 旧 `Intercept[T]` 为值变换链,表达不了 provider 依元数据裁决 | ADR-0002 Accepted:`InterceptMeta` = 声明 d(k) ⊕ 上下文 ι(k) 的 monoid 合并,provider 为 ℳₖ→𝒱ₖ 函数;`Intercept` 保留为附加读值变换能力 | ✅ 已对齐 |
| §3.1.3 + L-Iter/L-Divert | 旧实现 Apply 一次到位,per-iteration divert 粒度缺失 | ADR-0003 Accepted:+ `IterComponent` 逐 yield 激活 + 迭代边界 divert(inertial landing);见 `runtime/iterator_test.go` | ✅ 已对齐 |
| §4.2.2 末段 | ownership 树强制 child cleanup 严格先于 parent,比论文更强 | 保留为 **operational strengthening**(不违背论文;论文要求 guard 沿 coeffect 排序,本仓库额外保证子先父后) | ✅ 已接受的差异 |

### 1.3 扩展层分类(非内核语义,不得成为第二生命周期权威)

R4 评审(2026-09-09)按"是否服务论文实现"将扩展定类:

| 类 | 判据 | 成员 |
|---|---|---|
| **A — 论文 §5.2 实现章** | 直接实现 declarative loader / config reconciliation / HMR(含 §5.2.1 disable/re-enable:ComponentConfig.Enabled 开关,R5 落地) | loader、config、configwatch、watch、hmr、console 前端模块调和(2026-11-20:ui-client + webui manifest + web/console module manager——§5.2.1 禁用即 dispose、重新启用重执行 register、`?v=` 内容修订=迷你 HMR 热重导入;失败重试仅发生在调和触发,镜像 config Controller 的 applied-diff 重试,非自发循环);**UI-03 契约加固(2026-11-20)**:webui AccessPolicy 传输层授权接缝(应用拥有策略、默认全允许=开发姿态不变;§6.3 内核 capability 控制仍归 P3/ADR-0002,未提前)、config replace 孤儿化修复(C21:no-partial-state-lie)、`plugin.exited` 异常退出观察(降级不阻断+恢复走声明开关=铁律 5)、FACADE_VERSION 门面契约版本+shims 守卫、跨模块覆盖告警(注册隔离可见化);**联合审查整改(2026-11-20)**:`follows` 插件业务单元(后端 Provide `console:plugin-alive/<id>`,UI 行 Require,启停经依赖门控同进退=§5.2.1+铁律 7,降级不级联)、坏 manifest 产 `plugin-error` 行(Apply raise→Failed 可见,修复声明即恢复;坏配置≠卸载=铁律 6,恢复 DiscoverPlugins 注释宣称的"must surface, not vanish") |
| **B — 论文 §6 讨论项落地** | P7 逐项实现 | broker(§6.2)、loader/wasm(§6.3 沙箱) |
| **C — 平台能力(非论文语义,有消费方与测试约束)** | runtime 的通用补充 | event(派发模式之家,P6 起)、registry(稳定成员,broker 底座)、scheduler(周期任务)、UI-03 事件流(extensions/observe,2026-09-09:Observer 环形保留 + 序号锚点续传订阅;内核只留单调序号计数器 + WithEventSink 汇点钩子,paper-neutral 观测面进一步收缩);**G-2 关闭 (2026-09-09)**:runtime/rehome.go — Fiber.Rehome(RehomeWithFreshIsolation) = 论文 §5.2.1 realm 迁移短路由(provider 不下线,序号取号+投递原子化保证 sink 按 Sequence 序);**语义修复 (2026-11-20)**:阶段二迁移 provision 必须用阶段 1 捕获的 `oldRealms` 快照——改 `f.keyRealms` 后再 `fiberNamespaces(f)` 会丢掉 per-key isolation realm(ρ 在插入时固定,论文 Def 24/25);Isolate(keys) 的 scopeKeys 必须传到 cmdSpawnChild,否则 per-key isolation 退化为 WithScope;**联审整改 (2026-11-21)**:①Rehome 路由表改**合并语义**——只重指本 fiber 的 **Provide** 键到 fresh 命名空间,inject-only 键保持解析原处(短路由承诺"同 activation 保持 Active"才成立),继承的非本 fiber 声明键保留给后代(ρ 插入时固定,Def 24/25);②阶段 1 的 rehome 标记与 retiring sweep 放入同一 routeMu 临界区,等待期注册的 provision 原子地带 retiring(registerOwnRetiring),新依赖无法绑定被撤离命名空间——consumer-first 覆盖**整个** rehome 窗口而非冻结的消费者快照;③Child 的 per-key 表改在 cmdSpawnChild.apply(orchestrator 决策点)从父 fiber **当前**表构建,与创建原子化(插入时快照,非调用方快照);④Revise:revising CAS 防重 + Gone 拒复活 + 拒绝 finalizing parent + 替代 fiber 在插入时读旧 fiber **最终**表(退役窗口内完成的 Rehome 被继承);⑤**自依赖(inject∩provide 同 key)在装载边界拒绝**(ErrSelfDependency)——门控无法被自身激活前的 provision 满足,永久 Pending 或 Apply 期撞 ErrDuplicateProvider,该声明不可解;⑥ProviderPublished 事件语义 = "已可绑定":self-undo 的 Provide 与 mid-rehome retiring 注册不再发布 Published |
| **D — 实例/演示(转移)** | 某绑定模式的可运行样例,非语义、非通用能力 | ~~extensions/http~~ → **cmd/httpd**(2026-09-09:外部资源绑定模式参考——Active ⇔ serving、撤销先于终态、同地址重绑;零生产消费方,聚焦测试随迁) |

- `RuntimeDeterministic` 调度插桩:定理验证基础设施(已内部化,R3),保留。
- `cmd/` 演示程序(backfill/collector/host/example/wasmhmr/httpd):论文 §5.3 case study 的角色。
- `extensions/config` 的 `Schema`/`SchemaFactory`(JSON Schema 子集,仅 Reconcile 期对声明了 schema 的工厂校验)与 `extensions/console/insight`(hub 只读查询:声明图取自 Config Controller,运行图只取自 Runtime.Snapshot)属 A 类辅助:前者服务 §5.2 声明校验,后者不解析依赖、不改 Fiber(铁律 7 单一生命周期权威)。
- `extensions/market`(Phase 3 backend,2026-11-21):本地目录 catalog + 仅经 Config Controller 声明的装卸(不改 Fiber);trust 只是描述性元数据、无安全保证,`Source.Checksum` 仅校验工件完整性。
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

- [x] 决策:以**逐 key realm 表(插入时固定)**为原语;运行时 isolate(k, r) 落为 revision
      (卸下→重插,§4.4 Configuration 复合)。实施:`providers.go:lookupOwn` 严格逐 realm,
      `fiber.keyRealms` 为 ρ,Rehome 只迁移本 fiber 的 Provide 键。
- [x] 现有层级 scope 的处置:WithScope/Isolate 降级为逐 key 表的派生语法糖;祖先回退/shadow
      **已移除**,不冒称论文语义。证据:`def24_isolation_test.go`、`gap01_retiring_shadow_test.go`。
- [x] 论文 §4.4 K×R 配对论证:多 realm 并存与 Theorem 64/68/70/73/80 兼容,
      定理生成器扩展到 realm 维度(`c2_thm64_every_step_test.go`、`c3_thm70_ordering_test.go`、
      `phase4_random_test.go`)。
- 验收:Def 24/25 逐条 conformance(get/set/isolate 运输预条件、重指派语义);
  Theorem 80 合流生成器含 realm 交错。

### P3 — ADR-II:Interception 回归论文语义(Def 26/27)✅ 2026-09-08 完成(ADR-0002 Accepted)

- [x] `MetaKey[T,M]` 携带 monoid(zero ⊕ combine);provider 通过 `ProvideMeta` 声明为解释元数据的函数,
      `RequireMeta` 在读取时以 d(k) ⊕ ι(k) 解释。证据:`meta_intercept_test.go`。
- [x] `InterceptMeta` 实现论文元数据合并(right-biased、context 优先);旧 `Intercept` 保留为
      Go 附加的读值变换能力,**不冒充论文语义**(见 §4 语义复审 / ADR-0002)。
- [x] §6.3 capability 式访问控制:provider 可按合并元数据裁决,拦截/installment 可逆且走 Effect,
      不触发 reload。证据:`meta_intercept_test.go`。
- 验收:Def 26/27 conformance;访问控制示例进入 case study;fuzz 覆盖合并序。

### P4 — 效果迭代器(§3.1.3 / L-Iter / L-Divert)✅ 2026-09-08 完成(ADR-0003 Accepted)

- [x] 组件激活可选表达为效果迭代器:`IterComponent.ApplyIter(ctx, yield)` — 每 yield 一步 install,
      逆操作搬入 LIFO 栈;Apply-once 形态保留为单迭代特例。采用回调式而非 `iter.Seq` range-over-func
      (Go 自身的惯用取舍,语义等价:逐 yield 激活)。证据:`runtime/component.go`、`iterator_test.go`。
- [x] orchestrator 支持 **L-Divert 落在迭代边界**(inertial host:只取 landing 替代——论文 §4.4
      明确该替代下元定理仍成立)。证据:`iterator_test.go`(中途 Dispose、依赖消失、step raise 的 divert 路径)。
- [ ] **延后(非语义缺陷)**:deterministic driver 尚未把每次 yield 作为独立可调度步骤(当前仅停靠
      ApplyDone/UnwindDone)。迭代交错由 `iterator_test.go` 的 channel 屏障验证,非穷尽。见
      FINAL-ACCEPTANCE.md 延后事项 G-05;不为它重构 Kernel。
- 验收:Theorem 68/70 迭代粒度 conformance 由 `iterator_test.go` 覆盖;deterministic driver 逐 yield
      调度为测试工具增强,列为延后。

### P5 — 声明式 revision 统一(§4.4 Configuration + §5.2)✅ 2026-09-08 内核原语完成

- [x] 内核 revision 原语:`Fiber.Revise(ctx, component)` — 论文复合的严格实现
      (retire → deactivate(relied 守卫排序)→ O-Remove(子先父后)→ 同位重插:
      同 parent / 同 namespace / 同逐 key 表 + 新定义);依赖者经 target-view 比较
      自发重激活;Failed fiber 的 Revise = 有据重试(重插无 outcome)。
- [x] Theorem 80 终点性质:`TestThm80RevisionEndpointEquivalence` — revision 终点的
      无身份 canonical observable(逐名状态/绑定/提供集)与从头加载修订配置逐行相等。
- [x] (R1 评审补齐,2026-09-08)`WithFreshIsolation()` — revision 重指派 realm 对
      (论文 §4.4:"the new realm pairs");`TestReviseReassignsIsolation` 固化。
- [x] loader/HMR 短路径(候选先行、载荷不变不 reload)对齐同一终点 — `extensions/hmr`、`extensions/loader`
      实现并配 H1–H20 测试;Revision 目标选择按捕获的 ProviderIdentity 跟随,不做全局扫描。
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
- [ ] **延后(产品验证优先)**:WASM 沙箱一致性(§6.3):guest 能力面 = 声明集的形式化。
      WASM Backend 生命周期已冻结(见 FINAL-ACCEPTANCE.md),沙箱能力面形式化属于后续产品安全验收。
- [ ] **延后(产品验证优先)**:依赖类型/版本(§6.6):Key 元数据演进的自然延伸。

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

**编号澄清**：论文无 Theorem 66——第 66 条是 Lemma 66(Pairwise independence,
p.44)；T66/T73 是沿袭 stc-go 的旧编号，映射正确但勿与论文编号混用。

## §4 语义复审记录（2026-11-20，论文 × JS Cordis）

- **P0 已修（语义分两段，当前实现）**：
  - `Fiber.Ready` 在 RUnlock 后读 `f.intent`（Load/Dispose 在锁内写）→ 锁内
    快照，并发测试 `-race` 绿；
  - **`Context.Effect` 屏障（Effect lifecycle barrier）**：`installing` 计数
    覆盖安装 + 安装后补偿逆 + Effect 终态事件发布；`beginUnwind()` 只做非阻塞
    状态翻转（Active→Unwinding + cancel），**绝不在 Orchestrator 决策线程
    等待用户代码**；`collectInverses()` 在 unwind goroutine 等 `installing==0`
    后才快照 Committed slots，LIFO 撤销由 `runInverses()` 执行。Gone 保证
    所有逆及 EffectCommitted/EffectUndoing/EffectUndone 事件已发布（联合审查
    P1）。late-install 自撤销统一走 `guardedInverse`，panic 不逃逸。
  - conformance：`TestInstallingInverseCompletesBeforeGone`、
    `TestPhaseOrchestratorStaysLiveDuringSlowInstall`（临时恢复旧阻塞实现会
    10s 超时）、`TestPhaseEffectEventsOrderedBeforeGone` 与
    `TestPhaseMultipleInstallingAllDoneBeforeLIFO`（严格 LIFO）。
- **Isolation**：`runtime/providers.go` 注释曾写 "lookup walks own → parent"，
  与 `lookupOwn` 的实际逐 realm 无祖先回退语义相悖，已修正；**不得**为方便
  重新引入祖先回退（论文 Def 24/25、ADR-0001）。
- **Interception 分层**：`InterceptMeta` 按元数据 monoid 合并由 provider 解释，
  是论文 Def 26/27（§6.3 capability 访问控制）的正式实现；`Intercept` 是
  读值变换链，属 Go Runtime 附加能力，不应与论文机制混为一谈。
- **Interception 作用域 (2026-11-21,审计 P-CO-01)**：`InterceptMeta` 初版把 ι 写入共享
  `rootRealm`,违反 Def 27 的 Derived realization —— 独立根 Fiber 互相读到对方元数据。
  现 ι 存于 `Context.metaView`(派生视图),`Child()` 派生时快照继承,`RequireMeta` 从当前
  Context 读取;独立根无继承。ADR-0002 已记录该修订,防泄漏测试 + 变异验证双绿。
- **事件边界**：Kernel 只保留事件注册（可逆 effect）与 EventBindings 读模型；
  emit/serial/parallel/bail/waterfall 派发在 extensions/event；observe 层
  负责 Runtime 观察事件的订阅/重放。Kernel 不演化为大型事件总线。
- **定位**：论文定义核心语义；JS Cordis 是实现参考与兼容参照；Go 用自身的
  类型系统（typed Key[T]、Require）与并发模型（orchestrator + goroutine），
  **复刻语义而非语法**。本轮不新增功能，只证明既有语义在并发/动态替换下
  仍成立。
