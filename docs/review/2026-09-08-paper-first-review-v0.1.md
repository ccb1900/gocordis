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

## 8. 第四轮 (R4, 2026-09-09):http 扩展转移 + 扩展层定类

用户质疑 http 扩展"只是实例",裁决转移。全仓消费方核查:
- **http:零包外消费**(仅自身测试引用),doc 自称示例,矩阵标 "P2 示例能力"
  → **转移 `extensions/http` → `cmd/httpd`**(自包含 demo:外部资源绑定模式参考,
  只依赖 runtime;保留最高价值行为测试 Active⇔serving、撤销先于终态、同地址重绑;
  1400 行针对扩展自身 API 的套件随扩展消亡)。
- 同类扫描定类(ROADMAP §1.3):A=论文 §5.2(loader/config/configwatch/watch/hmr)、
  B=§6 落地(broker/wasm)、C=平台能力(event/registry/scheduler,有消费方、有边界
  审计、非论文语义但属 runtime 补充)、D=实例(http,已转移)。
- C 类的保留依据:event 是 P6 后派发模式之家且被 p2.1/门禁套件依赖;registry 是
  broker(§6.2)底座;scheduler 有 3 个集成消费点。三者均通过"仅靠测试续命"审计
  (有集成消费方),但均明确标注**非论文语义**。

**R4 判定:扩展层边界自此与"服务论文实现"的目标对齐;无其余 D 类候选。**

观察名单(非阻断):`TestWHMR16CloseDuringReplacement` 在一次全仓并行执行中失败
一次("hmr closed"),独立复跑 3 次、集成+runtime 高负载复跑 3 次、基线 6 次均未
复现;与 http 转移无依赖关系。列为时序敏感观察项,后续复现时再立项。

## 9. 第五轮 (R5, 2026-09-09):插件开关落地 + 评审方法论修正

### 9.1 为什么此前没有发现(诚实复盘)

用户提出"上层应用做插件开关管理,状态存哪"后,本轮才意识到这是缺口。复盘结论,
三个原因,按责任排序:

1. **评审循环缺了"应用场景"维度(结构性原因)。** R1–R4 的评审轴是
   代码↔论文语义、冗余度、边界纪律——全部是 bottom-up(代码到规范)。
   "应用要做什么"(top-down:用户任务 ↔ 能力映射)从来不是检查项。
   论文是范式论文,不枚举应用功能;只锚定论文的评审**结构性看不到**产品级缺口。
2. **这其实也是一次论文完备性漏审(更不该)。** 论文 §5.2.1 明文写着
   declarative layer 可 "disabling and later re-enabling" a component——
   这属于 §5.2 的实现章承诺。P5 审计了 revision/HMR 的替换路径,却没有
   逐条核对 §5.2.1 的能力清单,"禁用/重启用"从指缝里漏掉了。归档旧规格时
   也没有先从中提炼一份应用能力清单再归档。
3. **收敛判定混淆了两个维度。** R2 说"论文实现完成"——对语义面成立;
   但被同时暗示的"平台完备"从未被独立审计。

修正:本评审文档自 R5 起增加 **§10 应用场景对照表** 维护义务;
后续每个评审轮次必须同时跑 bottom-up(语义)与 top-down(场景)两轴。

### 9.2 落地:声明层插件开关(论文 §5.2.1 disable/re-enable)

- `ComponentConfig.Enabled *bool`(nil = enabled):开关是**声明的一部分**,
  持久化在声明存储(TOML 支持 `enabled` 键);runtime 内部状态依旧零持久化。
- 语义 = 论文 entry/τ 模型:disabled 条目**仍然被应用**(factory 校验其定义),
  但 Runtime 中不存在对应 fiber("entry 是存活身份,fiber 是单次 enablement
  的身份");开关翻动 = Load/Dispose;翻动走 replace 复合并重新校验定义。
- `Owned()` 对 disabled 条目返回 `Fiber == nil`(文档已注明)。
- 一致性测试(`switch_test.go` 六项):boot 即关(零 fiber)、关→开(enablement)、
  开→关(撤回且 entry 存活)、重启等价(全新 Controller + 同声明复现同状态)、
  幂等无搅动、disabled 期间改定义(不创建 fiber,启用时装载新定义)。

### 9.3 持久化立场(本轮讨论的结论,记入档案)

持久化三分为:**声明(开关/配置)→ 持久化于声明存储;业务成果 → 组件层
journal(backfill 模式);runtime 派生态 → 永不持久化**(registry 是从声明
调和而来的派生态,持久化它会制造第二真相源)。内核为持久化提供的唯一承诺是
**顺序**(Theorem 70:依赖在 consumer 的 Cleanup 期间仍可解析——"趁依赖还在
时落盘"),不承诺持久化写入的可逆性(论文 §4.4 信任边界)。

## 10. 平台完备性评审 (R6, 2026-09-09) — top-down 应用场景轴

方法:从"上层应用要在插件平台上做的事"出发映射能力(R5 §9.1 补上的轴),
清单来源 = 论文 §5/§6 + 归档完成规格三能力域 + JS Cordis 功能面 + 通用插件平台
工程惯例(VSCode/Koishi/OSGi 模式)。每行给状态与处置。

### 10.1 场景矩阵

| # | 应用场景 | 状态 | 证据/缺口 |
|---|---|---|---|
| 1 | 插件声明式装载/卸载 | ✅ | config.Controller 调和 + runtime 调和(C1–C12) |
| 2 | 插件开关(持久化) | ✅ R5 | ComponentConfig.Enabled + TOML;switch_test 六项;解析测试 T-enabled |
| 3 | 插件替换/升级 | ✅/⚠️ | 内核 Revise 终点等价已测;**loader/HMR 短路径终点对齐未测**(P5 遗留) |
| 4 | 依赖声明与反应式门控 | ✅ | 内核依赖图;Thm70 证据 |
| 5 | 失败隔离(单插件失败不扩散) | ✅ | §4.4 Failure 语义 + TestApplyFailureEndsFailed 等 |
| 6 | 级联清理(ownership) | ✅ | disposeChildren;C-04 |
| 7 | 服务发布/注入 | ✅ | Provide/Require + 声明强制 |
| 8 | 多实现共存/多路复用 | ✅ 最小 | extensions/broker(P7;RollingUpdate 测试);加权策略未做 |
| 9 | 插件间事件 | ✅ | extensions/event 五模式(87 项) |
| 10 | 跨进程调用 | ⭕ 显式出界 | §6.2 讨论项,P7 记录 |
| 11 | 配置热更新(存储→调和) | ✅ | configwatch 全链路集成 |
| 12 | 运行态观测 | ✅/⚠️ | UI-02 Snapshot/RuntimeEvent(编排器线性化);**无流式订阅**(UI-03):控制台只能轮询 |
| 13 | 代码供给:内置 / WASM 热载 | ✅ | loader builtin + wasm 后端(V 系列 + wasm_hmr_e2e) |
| 14 | 插件沙箱 | ✅/⚠️ | wasm 能力面=声明集;**能力面形式化未做**(§6.3,P7 记录) |
| 15 | 业务状态持久化 | ⭕ 模式即位 | backfill journal 模式;按 R5 立场不做扩展 |
| 16 | runtime 状态持久化 | ⛔ 有意拒绝 | R5 §9.3:registry 是派生态,持久化=第二真相源 |
| 17 | 全平台优雅关停 | ✅ | E2E30/PC 关停序 |
| 18 | 并发安全 | ✅ | 全量 -race 套件 |
| 19 | 插件作者 API(DX) | ✅ 最小 | Component(+IterComponent)接口即插件模型;糖层为可选 DX 项 |
| 20 | 平台文档 | ⚠️ | 插件开发指南 v0.1 已随旧文档归档;**paper-first 后无统一的"如何写插件"入口文档**(扩展 README 分散) |
| 21 | CI/平台矩阵 | ❌ | runner 未执行、Windows 未验证(既有记录) |
| 22 | 性能基线 | ❌ | benchmark 延后(既有记录) |

### 10.2 判定与处置

- **功能性完备**:#1–9、11、13、17、18 全部落地且被测试约束;#15/16 的持久化
  立场为有意设计(见 §9.3);#10/14/21/22 为已记录的显式出界。
- **两处真实欠账(记录,不阻断)**:
  1. **#3 短路径终点对齐**:HMR 候选先行路径应回答与 Revise 相同的终点——
     内核侧已测,扩展侧待补(P5 遗留,建议下一优先)。
  2. **#12 流式观测**:控制台集成今天只能轮询 Snapshot;UI-03 Subscribe
     (事件流 + EventSequence 续传锚点)是观测完备性的下一块。
- **一处文档欠账**:**#20 平台入口文档**——paper-first 转向后没有统一的
  插件开发指南(旧指南已归档)。建议以 ROADMAP §1 对照表 + cmd/ demos 为骨架
  重写一份简明 guide(纯文档任务)。
- **DX 判定**:Component 接口即插件模型,最小但完备;不引入糖层,除非出现
  真实作者反馈。

**R6 判定:平台功能性完备(按本矩阵口径);欠账集中在流式观测、短路径终点
对齐、入口文档与工程环境项,均已记录归属。**

## 11. R7 (2026-09-09):进程外插件后端落地(§6.2)

R6 判定的第 10 行(跨进程调用,显式出界)按用户裁决升级落地:
`extensions/loader/proc` — loader 第四后端("proc")。

- **形态**:插件 = 独立 OS 进程;stdio 行分隔 JSON-RPC 2.0;`proc.Serve` 让 Go
  插件 3 行成型;宿主侧 `NewBackend[T](key, bind)` 把 RPC Caller 绑定为契约
  接口(契约在应用 contracts 包,依赖倒置不变)。
- **生命周期同 WASM 后端**:每激活一进程;Apply = spawn + 握手 + effect
  (逆 = shutdown → 有界等待 → kill → wait)+ Provide 契约能力;Active ⇔
  握手完成;插件崩溃 → 调用路径 `ErrPluginUnavailable`(错误链经 markDead
  保持 `errors.Is` 可链),fiber 不自动重启(§4.4 无自动重试)。
- **边界声明**:进程边界非沙箱(§6.3);v1 顺序调用、不支持插件→宿主方法。
- 一致性(P-01..P-05,helper-process 模式,测试二进制自重为插件):回环+卸载、
  崩溃错误链、坏制品 Load 拒绝、替换复合(每激活新进程)、config 开关集成;
  `-count=3` + `-race` 绿。
- 教训入档:本轮修的三个 bug 全在**自己写的测试**里(-test.v 污染 stdout 协议
  通道、测试通道容量 1 死锁、断言错把 echo("explode") 当 explode 方法)——
  实现本体一次通过握手/调用/卸载语义。

## 12. R8 (2026-09-09):论文全面复审 + JS Cordis 逐特性对照

方法:论文 §2–§6 逐章核对当前实现;Cordis(cordiverse/cordis)按公开特性面
逐项对照。先声明:**Cordis 只是行为参照,不是规范**——分歧不自动是缺陷,
只有"论文承诺了而我们没做"或"机制形似而语义走样"才算。

### 12.1 论文逐章状态(经本轮重核)

| 章节 | 状态 | 备注 |
|---|---|---|
| §3.1 可逆 effect(functions + **iterators**) | ✅ | 槽位形态 + IterComponent;**默认形态反转**(论文默认迭代器,我们默认 Apply-once)——inertial 宿主许可,ADR-0003;driver per-step 停靠未做(已记录) |
| §3.2 coeffects(spec/notification) | ✅ | 注入驱动激活;target view 调和 |
| §3.2.3 isolation(Def 24/25) | ✅ | 逐 key ρ(R1 补全)+ WithScope/Isolate 糖;插入时固定;重指派=revision |
| §3.2.3 interception(Def 26/27) | ✅ | MetaKey monoid;右偏 context 优先,与论文方向一致 |
| §3.3 context paradigm / ≃_K | ✅(证据为近似) | 合流用 canonical observable(名字+观察序)近似 ≃_K——≈-不变性未逐条编码,记录为生成器近似 |
| §4 演算六规则 + confinement | ✅ | API 结构性执行 confinement(所有写经 context);proof invariants P1–P5 |
| Thm 64/68/70/73/80 | ✅ | 定理编号套件 + 迭代/revision/isolation 维度扩展;**合流前置"total on provision"(Def 76)在生成器中隐式满足(提供者恒提供),未显式断言**——小改进项 |
| §4.4 四扩展 | ✅ | Asynchrony(inertial)/Failure/Isolation(K×R)/Configuration(revise+Enabled 开关) |
| §5.1 core library | ✅ | kernel 面 |
| §5.2 loader/config/HMR | ⚠️ 两处不完备 | 见 12.3 G-1/G-2 |
| §6 讨论项 | 见 12.3 | broker/跨进程/沙箱已落地;§6.6 未做 |

### 12.2 Cordis ↔ gocordis 逐特性对照

| Cordis 特性 | gocordis 对应 | 判定 |
|---|---|---|
| Context Proxy 魔法(`ctx.foo` 取服务) | `Require(ctx, Key[T])` 显式取用 | ✅ 语言差异等价(更强:声明权威) |
| 服务引用反应式更新(替换不 reload 消费者) | 提供者替换 → 消费者按 target-view **reload**(与论文一致);**broker 吸收扰动** = 论文 §6.2 同款解法 | ✅ 语义分歧有据(论文优先),UX 经 broker 补齐 |
| `ctx.isolate(key, value)` | `Isolate(keys...)` 逐 key ρ(Def 24/25) | ✅ 同源(论文) |
| 事件 emit/parallel/serial/bail/waterfall | extensions/event 五模式 | ✅;bail 为 fail-fast 语义(独立定义,Cordis 值短路不适用——见 R3) |
| 服务生命周期 `start()/stop()` 钩子 | Apply/Cleanup + Active 门 | ✅ |
| `ctx.scope` / `scope.dispose()` | `WithScope`/`Isolate` + ownership 撤销 | ✅ |
| loader + 声明配置 + schemastery 校验 | loader/config/Enabled;**无 schema 校验助手**(map[string]any + 工厂自校验) | ⚠️ G-4 |
| HMR(文件变更 → 模块替换) | extensions/hmr | ✅(短路径终点对齐待测,见 G-1) |
| fiber 树/注册表内省 | UI-02 Snapshot(+RuntimeEvent 序号) | ✅;流式订阅 UI-03 未做(G-3) |
| `ctx.mixin` | 根命名空间 Provide | ✅ 等价 |
| 插件进程外运行 | —(Cordis 无此) | ➕ gocordis 多出 proc 后端(§6.2) |

### 12.3 未实现 / 不完备清单(R8 定稿)

| # | 级别 | 项 | 论文/来源锚点 | 处置 |
|---|---|---|---|---|
| G-1 | **不完备(论文 §5.2.1)** | HMR/loader **短路径终点对齐**:候选先行替换须回答与从头装载相同的 quiescent 终点——内核 Revise 已测,扩展 warm 路径未测 | §5.2.1 + Thm 80 | 已记录(P5 遗留),**建议下一优先** |
| G-2 | **不完备(论文 §5.2.1)** | **realm 迁移短路径**("a realm moved without reloading its provider"):现仅实现严格复合(WithFreshIsolation 必然重载提供者);论文许可的"迁移不重载"优化未做 | §4.4 Configuration/§5.2.1 | 记录;需先有公开命名空间句柄,与短路径语义一起做 |
| G-3 | ~~平台缺失(观测)~~ **已闭环 (R9, 2026-09-09;PAPER-NEUTRAL 平台层,非论文语义)** | `Runtime.Subscribe(ctx, from)` 落地:Snapshot(EventSequence S)→Subscribe(S) 无缝续传;慢消费者溢出→订阅关闭(emit 永不阻塞);Runtime.Close 以 ErrRuntimeClosed 终结全部订阅;锚点早于环形缓冲报 ErrSequenceTooOld。一致性 S-01..S-07(`ui03_subscribe_test.go`),`-count=3`+`-race` 绿 | UI-03 阶梯 | **CLOSED** |
| G-4 | DX 缺口 | 插件配置 **schema 校验助手**(Cordis 生态有 schemastery;本仓库 map[string]any 裸配 + 工厂自校验) | §5.2.1 邻接 | 记录为 DX 项,等真实作者反馈 |
| G-5 | 论文 §6.6 未实现 | 依赖**类型/版本**维度(Key 无版本;Module 有 Version 但不进能力解析) | §6.6 讨论 | 记录;§6.6 本为讨论章 |
| G-6 | 测试缺口(小) | **HMR × proc 后端**替换未测(proc 经 hmr.Controller.Replace 的组合路径) | §5.2.2 | 小;建议随 G-1 一并补 |
| G-7 | 证据近似 | 合流前置 Def 76"total on provision"在生成器中隐式而非显式断言 | Def 76 | 小;随下次生成器扩展补 |
| G-8 | 形态反转(有据) | 激活默认形态 = Apply-once(论文默认迭代器);driver per-step 停靠未做 | §3.1.3/§4.4 | 维持:inertial 宿主许可 + Go 惯用;文档已声明 |

**明确不做(非缺口)**:Bail 值短路语义(Cordis 特有,见 R3);Cordis Proxy 魔法;
runtime 状态持久化(§9.3 立场);跨语言宏/装饰器。

### 12.4 R8 判定

论文 **§2–§5 规范性内容:实现完成且持续被测试约束**——本轮未发现新的语义级
缺口;G-1/G-2 是 §5.2.1 的**优化路径**未落地(严格复合已实现并有终点等价证据,
故不构成语义不完备)。§6 讨论项:broker/proc/沙箱已落地,§6.6 与流式观测按
平台路线记录。对照 Cordis:除 bail 语义(有意分歧)与 DX 校验助手(G-4)外,
特性面覆盖或超越;**gocordis 独有**:进程外插件、逐 key 隔离的 ρ 表、
Enabled 声明开关、确定性定理驱动。

**下一步优先级建议:G-1(+G-6 顺带)> G-3 > G-4 > G-2/G-5/G-7。**

## 13. R9 补充 (2026-09-09):流程修正 — 内核触碰类工作的分级授权

R9 落地 UI-03 后用户质问"为什么突然改内核、遵循论文了吗"。复盘结论:

1. **S-01..S-07 为本仓库自编套件编号**(Subscription),不锚定论文;
2. **UI-03 属 PAPER-NEUTRAL 平台层**:事件流/订阅不在论文语义面(§2–§5 无此
   概念),不触碰演算、无第二生命周期权威(纯只读观察)、定理面零变化;
   其设计依据是仓库既有的 UI-01/UI-02 观测模型(EventSequence 续传锚点为
   订阅预留),而非论文条文;
3. **授权边界失误**:"连续实现"授权覆盖 P1–P7 路线图;该弧线完成后,内核触碰
   类新工作(如 G-3)应当先提案、由用户点单。R9 未等点单,判定越权,认。

修正:自本节起,所有 kernel 触碰类工作在 ROADMAP/评审中 MUST 标注三级之一——
**paper-anchored**(锚定论文条目)/ **paper-neutral**(平台层,论文不涉)/
**paper-divergent**(有意分歧,须 ADR)。paper-neutral 与 paper-divergent 的
内核变更实施前 MUST 先提案。
