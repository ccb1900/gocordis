# gocordis 开发者手册

> **读者**:阅读/修改 gocordis 框架本身的工程师,以及要给框架写扩展
> (新 loader 后端、新平台能力)的贡献者。
> **与用户手册的分工**:用户手册讲"怎么用";本手册讲"怎么造、为什么这样造、
> 哪些不变式不能破"。语义争议以论文(`docs/论文.pdf`)为最终裁决。
> **原则**:本文所有路径与签名均以**当前代码**为第一性参照;改代码必须同步改
> 对应章节。

---

## 目录

1. [架构总览](#1)
2. [内核代码地图](#2)
3. [核心机制](#3)
   - 3.1 [单一决策域与命令循环](#31)
   - 3.2 [Fiber/Activation 与状态机](#32)
   - 3.3 [调和与撤回级联(relied 守卫)](#33)
   - 3.4 [能力解析:逐 key 命名空间](#34)
   - 3.5 [Effect 系统](#35)
   - 3.6 [效果迭代器与 divert](#36)
   - 3.7 [环检测与命名空间相等](#37)
   - 3.8 [观测面:序号计数器 + 事件汇点](#38)
   - 3.9 [Revision 与 Rehome](#39)
4. [论文锚定与定理证据](#4)
5. [扩展系统](#5)
   - 5.1 [扩展边界规则](#51)
   - 5.2 [Loader 后端开发](#52)
   - 5.3 [扩展示例:proc 后端要点](#53)
6. [测试基础设施](#6)
7. [变更流程与三级标注](#7)
8. [质量门禁](#8)
9. [已知边界与开放项](#9)

---

## <a id="1"></a>1. 架构总览

```
┌────────────────────────────────────────────────────────┐
│ client/  应用前端(消费者;经 HTTP/SSE 读观测面)        │
├────────────────────────────────────────────────────────┤
│ extensions/  平台能力层(每一项都是普通扩展,无特权)     │
│   console/   控制台平台(host/hub/registry/webui/explorer)│
│   observe/   事件流观察器(环形保留 + 订阅)              │
│   config/ configwatch/ loader(+wasm +proc)/ hmr/        │
│   event/ registry/ scheduler/ watch/ broker/ patch/ bundle│
├────────────────────────────────────────────────────────┤
│ runtime/  内核(论文 §2–§5):组件模型、串行决策域、      │
│           可逆 effect、反应式 coeffect、调和、事件词汇    │
└────────────────────────────────────────────────────────┘
```

两条铁律贯穿所有层:

1. **单一生命周期权威**:只有 `runtime` 内核决定 fiber 的生死;其他一切
   (extensions/console/应用)只经公开 API 表达意图。
2. **依赖方向单向**:`extensions/*` 与 `console/*` 只依赖 `runtime` 公开 API
   与彼此的公开能力;`runtime` 不 import 任何上层。

---

## <a id="2"></a>2. 内核代码地图

| 文件 | 职责 | 论文锚点 |
|---|---|---|
| `runtime.go` | Runtime 入口;选项;组合根 API(Load/Close/Snapshot/Subscribe) | §5.1 core library |
| `orchestrator.go` | **单一决策域**:命令循环;Apply/Unwind goroutine 管理;状态迁移 | §4.2.1 orchestration |
| `command.go` | 命令族(cmdLoadIntent/cmdDispose/cmdSpawnChild/cmdApplyDone/cmdUnwindDone/cmdReviseInsert/…) | — |
| `fiber.go` | Fiber 公开面(Load/Dispose/Ready/Gone/WaitInactive/Revise/Rehome);FiberID/ActivationID | Def 49 |
| `component.go` | Component/IterComponent 接口;Cleanup 类型;声明缓存 | Def 48 |
| `activation.go` | Activation(一次启停)与激活代际 | Def 58 episode |
| `state.go` | FiberState/Intent 常量与状态表 | §4.2.2 生命周期 |
| `context.go` | 激活 Context:Require/Provide/Effect/Child/On;声明缓存;divert 标记 | §3.3 unified context |
| `providers.go` | 命名空间(realm)+ 提供者记录;逐 key 隔离表;hub? 否——纯解析 | §3.2.3 / Def 24 |
| `dependency.go` `dependency_graph.go` | 依赖解析(resolveDependency)、图边(identity→consumers)、waitGates(撤回门)、级联通知 | Def 54 relied |
| `dependency_cycle.go` | 装载边界环检测(命名空间相等判定) | ≺ acyclic 假设 |
| `ownership.go` | ctx.Child 装配;子 fiber 级联处置;disposeChildren 门 | Def 52 |
| `reconcile.go` | 调和:意图对比 → beginWithdrawal(撤回级联)→ Pending/Loading | Def 53 target view |
| `events.go` | 事件词汇 + 单调序号计数器 + EventSink 汇点钩子 | 观测面(paper-neutral) |
| `eventkey.go` `event_registry.go` | 插件事件 key;处理器注册(可逆 effect)与可见性 | §5.1 effect tracking 实例 |
| `observation.go` | Snapshot 投影(orchestrator 线性化;四遍构建) | UI-02 |
| `rehome.go` | Rehome:realm 迁移短路由 | §4.4/§5.2.1 |
| `deterministic.go` | 确定性模式:完成停靠(park)+ 驱动 API(**内部**;定理基础设施) | Thm 73/80 驱动器 |
| `error.go` | sentinel 错误族 | — |
| `id.go` `key.go` `eventkey.go` | 类型化身份(Key/MetaKey/EventKey) | Def 29/26 |

---

## <a id="3"></a>3. 核心机制

### <a id="31"></a>3.1 单一决策域与命令循环

`orchestrator.run` 是唯一的 goroutine:所有生命周期决策都是命令
(`command` 接口)经 `submit`/`admitCommand` 进入队列,串行执行。含义:

- **线性化点** = 命令执行;外部读(Snapshot/fiber 状态)是已发布事实;
- Apply/Cleanup 的**用户代码永远不在决策域内跑**(`go runApply`),完成经
  `cmdApplyDone` 回到决策域;
- 确定性模式下,完成(`StepApplyDone`/`StepUnwindDone`)**停靠**在
  `deterministicState.pending`,由驱动器显式 `detExecute` 放行(定理基础设施;
  注意 det API 已内部化,外部不可达)。

**不变式**:决策域内不得阻塞等用户代码;用户代码不得直接改 fiber 状态。

### <a id="32"></a>3.2 Fiber/Activation 与状态机

- Fiber = 逻辑实例(跨启停存活);Activation = 一次启停(id 单调递增);
- 状态:Pending / Loading / Active / Unloading / Failed / Gone(+intent:
  Mounted/Unmounted);
- **陈旧完成隔离**:cmdApplyDone/cmdUnwindDone 按 ActivationID 校验,旧激活的
  完成绝不污染新激活;
- Failed:outcome 记录在 fiber 上,阻止 L-Begin 重入;恢复 = Dispose+Load
  或 Revise。

### <a id="33"></a>3.3 调和与撤回级联(relied 守卫)

`reconcile(f)`:对比 intent/依赖满足性 → Pending/Loading。撤回路径
(`beginWithdrawal`,reconcile.go):

```
1. 标记提供记录 retiring(跨 scope + keyRealms 全部命名空间扫描)
2. 经依赖图收集直接消费者
3. addWaitGate(f, c):f 的收尾门控在消费者上
4. reconcile(c):请求消费者撤回(递归 consumer-first)
5. disposeChildren(f)
6. maybeStartUnload(f):门全清后才进入 Unloading(应用 LIFO 逆)
```

对应论文 **Theorem 70(2)**(依赖者的 episode 被提供者的 episode 严格包夹)
与 **Def 54 relied 守卫**。改这段代码必须重跑:
`TestThm70*`、`beginWithdrawal` 相关级联测试、`TestRehome*`。

### <a id="34"></a>3.4 能力解析:逐 key 命名空间

- `Fiber.keyRealms map[CapabilityKey]*realm`:论文 Def 24 的 ρ 表,**插入时
  固定**(Load/Child/Revise 时构建);
- `effectiveRealm(f, key)`:keyRealms 命中→该命名空间;否则 f.realm;
- 解析/提供/撤回标记/快照全部经 `effectiveRealm` 单命名空间——**没有祖先
  回退**(论文 §4.4 Isolation);
- `WithScope()` = 全新命名空间糖;`Isolate(keys...)` = 部分 key 迁入新命名
  空间(其余共享父);
- 环检测边条件 = `effectiveRealm(x,key) == effectiveRealm(y,key)`(命名空间
  相等,不做路径可达)。

**不变式**:provision 记录只能写入 `effectiveRealm`;retire 扫描必须覆盖
`{f.realm} ∪ keyRealms 值`(见 reconcile.go,曾因漏扫描出过写读不一致)。

### <a id="35"></a>3.5 Effect 系统

`ctx.effect(kind, key, install)`:install 运行成功 → slot 进入 committed
(逆暂存);激活撤销 → `beginUnwind` 收集 slots → `runInverses` LIFO 执行
→ 每个逆对应一个 undone 事件。panic 在 install/逆两个边界都受控
(`ErrEffectInstallPanic`/逆转错误)。`Provide` 就是"注册提供者记录"的
预置 effect(`EffectKindProvider`)。

### <a id="36"></a>3.6 效果迭代器与 divert

`IterComponent.ApplyIter(ctx, yield)`:每 `yield(step)` = 论文一次 L-Iter;
step 的 Cleanup 提交为该步的逆。yield 内部先发 `cmdDivertProbe`(决策在
orchestrator:mounted intent / 无 unloadRequested / 未 closing / 依赖快照
有效),divert 则返回 `ErrDiverted` → `cmdApplyDone.diverted` → 走
`unwindAfterApply`(**不是失败**)。已知限制:确定性驱动仍按 ApplyDone 粒度
停靠。

### <a id="37"></a>3.7 环检测与命名空间相等

`findDeclaredCycle`:边 x→y 当且仅当 x 声明需要某 key 且
`effectiveRealm(x,key) == effectiveRealm(y,key)`(y 声明提供)。装载边界
(Load/Child/ReviseInsert)调用;命中即 `ErrDependencyCycle` 带环链诊断。

### <a id="38"></a>3.8 观测面:序号计数器 + 事件汇点

- `events.go` 只有三样:事件词汇(RuntimeEvent/EventType)、单调序号计数器
  (取号+汇点投递**原子**,保证 sink 按 Sequence 序收到)、`EventSink` 接口
  + `WithEventSink` 选项;
- **内核不保留环形缓冲**——保留/重放/订阅在 `extensions/observe`(Observer);
  Resume 协议:Snapshot(EventSequence S) → Observer.Subscribe(S);
- 订阅溢出协议:慢消费者缓冲溢出 → 该订阅关闭(`ErrSubscriptionOverflow`),
  发射器永不阻塞;重接连环:重 Snapshot → 重 Subscribe。

### <a id="39"></a>3.9 Revision 与 Rehome

- `Fiber.Revise(ctx, comp, opts...)`:论文 §4.4 revision 严格复合(retire →
  relied 守卫撤回 → 子先父后移除 → 同位重插);`WithFreshIsolation()` 变体
  迁移 realm 对;终点性质已测(= 从头装载的稳态);
- `Fiber.Rehome(ctx, RehomeWithFreshIsolation())`:**短路由**——provider 不
  下线。两阶段:
  1. `cmdRehomeBegin`:跨命名空间标记 retiring → 经依赖图通知旧消费者撤回 →
     `addWaitGate` 门控在消费者上;
  2. 最后一个消费者 detach 后(经 `notifyGateWaiters` 续延)
     `executeRehomeStep2`:keyRealms 全表重写到新命名空间 + provision 记录
     原子搬移 + sweepWaiting。
- `provideCap` 的逆是**命名空间搜索式移除**
  (`removeProvisionAround`,providers.go):迁移后 unwind 仍能找到记录,
  不泄漏。改 provide/逆/rehome 任一处,必须同时核对这两处一致性。

---

## <a id="4"></a>4. 论文锚定与定理证据

论文编号 ↔ 测试(文档:`docs/theorem-verification.md`;评审:`docs/review/`):

| 论文 | 测试 | 机制 |
|---|---|---|
| Thm 64 Preservation | `proof_invariants_test.go`(on-orchestrator 不变量) | 良构性逐步保持 |
| Thm 68 Recovery | `thm68_recovery_test.go`、`phase5_generators_test.go` | LIFO/恰好一次/基线等价 |
| Thm 70 Ordering | `thm70_ordering_test.go`、撤回顺序 | relied 守卫/门控 |
| Thm 73 Progress | `thm73_thm80_test.go`(确定性步进驱动,无 sleep) | 有界步数收敛 |
| Thm 80 Confluence | `thm73_thm80_test.go`、`scoped_confluence_test.go`、`revision_thm80_test.go` | 合流 canonical observable |

假设(必须随测试声明):组件/效果有限;Apply/Cleanup 合作式;依赖图无环;
仅托管 effect 可逆。R 系列评审记录(`docs/review/2026-09-08-…`)保存了全部
判定与教训,新贡献者应通读。

---

## <a id="5"></a>5. 扩展系统

### <a id="51"></a>5.1 扩展边界规则

一个包可以成为 `extensions/X` 当且仅当:

1. 只依赖 `runtime` 公开 API(可依赖其他 extensions 的公开能力);
2. **不是第二生命周期权威**:不直接改 fiber 状态、不自带 spawn/kill fiber 的
   私有机制(进程/模块的生死是"资源",不是"生命周期";两者都经 ctx.Effect);
3. 语义分类已标注(§7 三级):paper-anchored / paper-neutral / paper-divergent。

### <a id="52"></a>5.2 Loader 后端开发

新代码供给形态 = 新 `loader.Backend`(参考:`extensions/loader/wasm`、
`extensions/loader/proc`):

```go
// 契约:Artifact → Module(仅此而已)
func (b *Backend) Load(ctx context.Context, a loader.Artifact) (loader.Module, error)
```

- **Load 只做验证与描述**:校验 Source(存在/可执行/可编译),返回
  `loader.Module{ID, Type(逻辑类型), Version, Factory}`;
- **Factory.Create 只造 Component**:不 Load fiber、不碰 Provider/Config;
- 实例资源(进程/WASM 实例)在 **Component.Apply 内物化**,以 ctx.Effect
  注册其销毁逆——"每激活一个新实例,撤销即回收";
- Backend.Close 只拒绝新 Load,绝不杀活实例(实例属激活);
- 事件:建议实现 Observer 风格的诊断观察(参考 wasm.WithObserver),只观测、
  不影响决策。

### <a id="53"></a>5.3 扩展示例:proc 后端要点

`extensions/loader/proc`:插件 = 独立 OS 进程,stdio 行分隔 JSON-RPC 2.0。

- 每激活一个新进程:Apply = spawn + 握手 + effect(逆 = shutdown→有界等待→
  kill→wait),随后 Provide 契约能力——Active ⇔ 握手完成;
- 契约绑定:`proc.NewBackend[T](key, bind(Caller) T)`,接口定义在应用 contracts;
- 崩溃 = 调用路径 `ErrPluginUnavailable`(错误链保持 errors.Is);**不自动
  重启**(§4.4);
- 进程边界不是沙箱(§6.3;不受信代码用 wasm);
- 插件侧一行接入:`proc.Serve(map[string]proc.Handler{...})`。

---

## <a id="6"></a>6. 测试基础设施

- **门禁**:`go build ./... && go vet ./... && gofmt -l .` +
  `go test -race -count=1 ./...`(Makefile: `make verify`);
- **确定性种子**:所有随机测试固定 seed;失败必须可复现;
- **定理套件**:Thm64(`proof_invariants_test.go`)、Thm68/70
  (`thm68_recovery_test.go`、`thm70_ordering_test.go`、`c3_thm70_*`)、
  Thm73/80(`thm73_thm80_test.go`,确定性步进驱动 `det*`,**内部 API,外部
  不可达**)、`scoped_confluence_test.go`、`revision_thm80_test.go`、
  `iterator_test.go`;
- **helper 模式**:进程外插件测试用"测试二进制自重"(`os.Executable` +
  env 门控 + `-test.run=…`),见 `proc_replace_test.go`;
- **已知偶发**:`TestC3Thm70RandomizedSchedule`(观察名单;单跑复现即立项)。

新增机制的测试四件套:一致性(conformance)+ 终点等价(若为替换/迁移类)
+ 失败路径(raise/divert/溢出)+ `-race` × `-count≥2`。

---

## <a id="7"></a>7. 变更流程与三级标注

触碰 `runtime/`(内核)的变更 MUST 在 ROADMAP/评审中标注:

| 标注 | 含义 | 授权 |
|---|---|---|
| **paper-anchored** | 锚定论文具体条目(附 Definition/Theorem/Section 编号) | 路线图内可直接实施 |
| **paper-neutral** | 平台层,论文不涉(观测、驱动、DX) | **MUST 先提案** |
| **paper-divergent** | 有意分歧(如 Bail 语义) | MUST ADR + 用户裁决 |

历史依据:评审报告 §9(R9)——"内核触碰类工作的分级授权";§13/§14/§15 为
历次评审的裁决档案。**先提案后动手**适用于后两类。

---

## <a id="8"></a>8. 质量门禁

```bash
make verify   # gofmt + vet + test + race
```

- 全量 `go test -race -count=1 ./...` 必须绿;
- 偶发失败:单跑 ×3 + 基线 stash 对比;确认既有则在评审报告观察名单挂号,
  **不得**以"偶发"为由跳过分析;
- 新公开 API 必须有测试与文档(用户手册或开发者手册对应章节);
- 提交信息格式:`type(scope): 摘要 — 正文(动机/语义/门禁)`。

---

## <a id="9"></a>9. 已知边界与开放项

- HMR 暖路径的**候选先行**是论文许可的短路由,其终点等价已在内核 Revise 与
  HMR capability-provider 路径分别测试(G-1);
- `det*` 确定性驱动为内部 API(外部不可达);per-step 停靠未做;
- 控制台无鉴权(用户裁决延期;需要时在 webui 加中间件缝);
- G-5 依赖类型/版本维度(§6.6 讨论章)未实现,记录中;
- WASM 能力面形式化(§6.3)与跨节点服务发现未做(扩展层按需)。
