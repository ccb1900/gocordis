# Paper-First Review v0.1 — P1–P7 落地后的完备性/差异/冗余评审

Date: 2026-09-08
Authority: [`docs/论文.pdf`](../论文.pdf)(唯一规范来源);参照系:JS Cordis(仅行为参照,**非规范**)、本仓库实现。
Scope: P1–P7 七个提交(`b849141`..`ed9cd27`)后的全量评审。本文档为**活跃**评审,不受归档影响。

## 0. 结论

**核心判定:论文 §2–§5 的语义面(可逆 effect、反应式 coeffect、context paradigm、
演算六规则、五定理的性质面、四个扩展)在本仓库已实现并有可执行证据;
实现完成度对该范围成立。** §6 讨论项按扩展层策略部分落地、部分显式出界。
本轮评审发现 **1 个一致性缺陷(已修复)**、**2 个完备性缺口(已补齐)**、
**3 个文档性澄清(已落)**;无过度设计需要裁撤。

## 1. 评审发现与处置

| # | 级别 | 发现 | 处置 |
|---|---|---|---|
| F-1 | **缺陷(潜伏)** | P2 的逐 key 表没有公开入口:`Isolate` 粒度的 ρ 从未被填充(所有 key 默认指向 scope realm),且 `provideCap`/retirement 标记写 `c.realm` 而解析读 `effectiveRealm`——一旦表有覆写即写读不一致 | **已修复**:`runtime.Isolate(keys...)`(Def 24/25 的逐 key 原语,子树继承表);provide/withdraw/retire 全部走 effectiveRealm(reconcile 按 scope realm ∪ keyRealms 扫描);`TestDef24PerKeyIsolationWithSharing` 固化 |
| F-2 | **完备性缺口** | 论文 §4.4 Configuration:revision 可"reassign its realms"——`Revise` 原先只能继承旧表 | **已补齐**:`Revise(..., WithFreshIsolation())`;依赖者跨命名空间不跟随(自身 ρ 未变),是论文忠实语义;`TestReviseReassignsIsolation` 固化 |
| F-3 | **完备性缺口** | ROADMAP P2 曾声称"Thm80 合流生成器含 realm 交错"为验收项,实际未落地(claim 超前于证据) | **已补齐 + 诚实化**:`TestThm80ScopedNamespaceConfluence`(双子系统 scope 维度合流);ROADMAP 措辞修正 |
| F-4 | 语义澄清 | `Bail` 最初注释自称 Cordis 语义,但 JS Cordis 的 bail 是**值短路**(首个非 undefined 返回值),与本仓库的 fail-fast serial 不同 | **已澄清**:dispatch.go divergence note + ADR-0004——论文无论派发模式,kernel 注册面(error 返回)不支持值短路,故独立定义,不冒称 Cordis 对齐 |
| F-5 | 评审决定 | 旧 `Intercept[T]` 函数链去留(P3 留待 P6 复审) | **决定:保留**——"值变换"用例的最短表达,scoped_realm 套件持续约束其语义;ADR-0002 记录边界:改值用 Intercept,策略/访问控制用 InterceptMeta |
| F-6 | 契约澄清 | `Revise` 若在被修订 fiber 自己的激活内调用会自锁(等待自身激活结束) | **已澄清**:方法契约注明——revision 是 loader/orchestrator 级复合操作 |

## 2. 论文 ↔ 仓库:完备性矩阵(经本轮复核)

| 论文条目 | 状态 | 证据 |
|---|---|---|
| §2–3.1 可逆 effect(effect function/iterator、witness=LIFO 逆) | ✅ | ctx.Effect 槽位 + `IterComponent`(ADR-0003);iterator_test |
| §3.2 反应式 coeffect(get/set、specification 驱动激活/去激活) | ✅ | Require/Inject + reconcile/thm73_thm80 |
| §3.2.3 isolation(Def 24/25,**逐 key** ρ、插入时固定、重指派=revision) | ✅(本轮补全) | Isolate + keyRealms;def24_isolation_test;revise_isolation_test |
| §3.2.3 interception(Def 26/27 元数据 monoid,右偏 context 优先) | ✅ | MetaKey/RequireMeta/InterceptMeta;meta_intercept_test |
| §3.3 context paradigm / observational equivalence | ✅ | 统一 Context + Thm80 canonical observable 证据 |
| §4.1–4.2 演算(O-Insert/Remove、L-Begin/Iter/Finish/Divert/Leave/Unload、confinement) | ✅ | orchestrator/ownership/dependency_graph;claim 映射见 ROADMAP §1.1 |
| Thm 64 / 68 / 70 / 73 / 80 | ✅ | thm64/thm68/thm70/thm73_thm80 + proof_invariants + iterator/revision 扩展证据 |
| §4.4 Asynchrony(inertial host,L-Divert landing 替代) | ✅(含迭代粒度) | divert 探针;iterator_test |
| §4.4 Failure(raise→Unwind→Failed,outcome 阻止重入,不向父传播) | ✅ | finalizeActivation;iterator raise 测试 |
| §4.4 Isolation(多 realm 下五定理原样成立,K×R 编码) | ✅(本轮补合流维度) | scoped_confluence_test |
| §4.4 Configuration(revision 复合,含 realm 重指派) | ✅(本轮补重指派) | Revise + WithFreshIsolation;revision_thm80_test |
| §5 实现:core library / declarative loader / HMR | ✅(extension 形态) | extensions/loader、hmr、config(+watch);loader 短路径对齐 Thm80 终点仍为后续项(ROADMAP P5) |
| §6.2 service multiplexing(broker/滚动升级) | ✅(本轮前落地) | extensions/broker |
| §6.2 跨进程调用 / §6.3 沙箱形式化 / §6.6 依赖类型版本 | ⭕ 显式出界 | ROADMAP P7(非核心演算语义;记录为扩展层后续项) |

**已知且有据的宿主限制(非缺陷)**:激活不可中途 abort(论文 §4.4 明确 inertial 宿主
只需 landing 替代,五定理不受影响);deterministic driver 仍为 ApplyDone 粒度停靠
(per-step 停靠为 driver 细化项);CI runner/Windows/benchmark(环境项)。

## 3. 多余 / 过度设计检查

| 项 | 判定 |
|---|---|
| `RuntimeDeterministic` 调度插桩 | **保留**——Theorem 73/80 驱动器依赖;测试专用面(`det*` API)集中在 internal/测试 |
| 旧 `Intercept[T]` 函数链 | **保留**(F-5 决定) |
| `EventBinding.Order` 只读字段 | **保留**——注册序是撤销语义与确定性的一部分,读模型完整性所需 |
| `Context.Cancel` | **保留**——协作式自取消的公开面(emit 中途退出依赖它);非生命周期决策 |
| extensions/event 旧 `Bus`(Publish/Subscribe) | **R3 改判:移除**(2026-09-09)——核实无任何生产消费方(仅 4 处测试引用),属"仅靠测试续命的生产 API 面";R1 的"正交关注点"判断偏宽,用户裁决采纳彻底方案 (b)。同批将仅由内部测试消费的确定性驱动面内部化 |
| `cmd/` 演示程序 | **保留**——case study 角色(§5.3 对应物) |

未发现需要裁撤的冗余:kernel 公开面经 P6 瘦身后逐项可锚定论文条目或 ADR;
扩展层无第二生命周期权威(P1-08 边界审计 + p2.1 套件持续约束)。

## 4. 与 JS Cordis 的差异(参照,非规范)

| Cordis | 本仓库 | 判定 |
|---|---|---|
| Proxy 魔法声明合并 | 显式 Inject/Provide 声明 + 强制(Phase 2) | 语言差异的等价物,且更强(声明权威) |
| ctx.isolate(key, value) | `Isolate(keys...)` 逐 key ρ(Def 24/25) | 同源(paper),机制按论文 |
| 事件五模式 | extensions/event 五模式;bail 语义独立定义 | F-4 澄清;名称同、语义异已声明 |
| Service/inject | Require/Provide + broker(§6.2) | ✅ |
| loader/HMR | extensions/loader/hmr | ✅(短路径终点对齐为后续) |
| Koishi 插件生命周期 | Component Apply/Cleanup(= 演算 episode) | ✅ |

## 5. 循环判定

目标定义:论文实现完成 = §2–§5 语义面完备 + 五定理证据 + §4.4 四扩展 + 显式声明的
宿主限制与出界项。**本轮复核后判定达成**;剩余项(P5 loader 短路径、P4 per-step
停靠、P7 其余 §6 项、环境门禁)均为已记录的扩展层/工程项,不动摇核心语义完备性。

## 6. 第二轮复核 (R2, 2026-09-09,dbf2697 之后)

对 R1 修复的对抗性核查与收敛判定:

- **nil 安全**:R1 的 `effectiveRealm(c.fiber, ...)` 依赖 `ctx.fiber` 已设——核实
  `newContext` 仅由 orchestrator `startActivation` 调用并随即设 fiber,无绕行路径。
- **覆盖面迁移完整性**:extensions/event 87 项 PASS(含迁移的 P1.1–P1.4 套件与
  gap 套件 + 新 Bail 三项),runtime 161 项 PASS——迁移零丢失,新增证据全数落地。
- **门禁**:全量 `go test -count=1 ./...`、`go test -race -count=1 ./...`、vet、
  gofmt 全绿(R1 修复后重跑)。
- **代码卫生**:新增内核面无 TODO/FIXME;无未消费的公开 API 需处置。

**R2 判定:循环收敛。** 评审发现的缺陷/缺口已全部修复或补齐并有固化测试;
三方对比(论文/JS Cordis/本仓库)无新的语义分歧;剩余项均为已记录的
扩展层/工程项(ROADMAP P5 短路径、P4 per-step 停靠、P7 其余 §6 项、环境门禁),
不构成"论文实现未完成"的判定依据。目标达成。

## 7. 第三轮 (R3, 2026-09-09):Bus 移除 + 驱动面内部化

用户复核 R1 §3 时质疑 event 双机制并存,裁决采纳彻底方案 (b)。本轮:

1. **Bus 整体移除**(event.go + 三个测试文件,约 800 行):生产消费方为零;
   E12–E14 的"稳定性能力"意图由 registry/scheduler 套件承载。
2. **集成消费点迁移**:E2E15 改经 event.Emit(断言不变);E2E24/E2E30 摘除
   Bus 噪音(其余压力源保留)。
3. **同类问题全扫**:确定性驱动面(Step/RuntimeMode 等 8 个导出标识符)消费方
   全部为内部测试 → 内部化,公开面收缩,零功能变化。
4. **其余候选复核**:Intercept[T](ADR-0002 保留决定维持)、Context.Cancel
   (派发取消语义的机制依赖,非 test-alive)、EventBinding.Order(读模型完整性)
   —— 均有据保留。
5. C3 随机调度的单次失败经 3 次独立复跑 + 基线 stash 对比确认为既有偶发
   (convergence-matrix P0-15 记录),与本轮改动无关。

**R3 判定:全仓再无"仅靠测试续命的生产公开面";收敛维持。**
