# GoCordis ↔ JS Cordis 有限能力对照（2026-11-21）

Baseline: GoCordis `5522e1f`; JS Cordis `5e2e6c172335d207d11289959322edaaba4447f0`
(`cordis` 4.0.0-rc.13). 本文件是**一次性、有限**的能力矩阵，不是逐轮审查报告。
证据取自该 JS 提交的一手源码（本地只读检出核对），不依赖目录名或旧版功能清单。

判定：**实现** = 本批次落代码；**接受差异** = API/策略不同但运行时能力不缺失；
**不适用** = JS 的生态/产品能力不属于本框架目标；**延期** = 有真实能力缺口但需独立批次。
论文冲突时以 `docs/论文.pdf` 为准；JS 只是工程参考（`docs/README.md:15`）。

---

## 1. Core

| 能力 | JS 5e2e6c1 行为（证据） | Go 落点 | 判定 / 影响 / 决定 |
|---|---|---|---|
| Component / Fiber | 定义与实例分离；`ctx.use()` 每次新建 Fiber（`packages/core/src/registry.ts:239-254`） | `runtime/component.go`、`runtime/fiber.go`；`Load`/`Child` | 接受差异。能力等价，状态名 Gone↔Disposed；Go 额外有声明校验。 |
| 原位替换 | `registry.replace()` 保留 Fiber uid、config、外部引用，只换 runtime 指针（`registry.ts:187-209`、`fiber.ts:422-437`；`replace.spec.ts:7-27`） | `Fiber.Revise` 退役→Gone→重插，返回新 Fiber（`runtime/fiber.go:401-492`） | **接受差异，不实现**。原位替换与论文 Revision Composite 冲突；稳定句柄若将来需要，另立提案，不改 Fiber 身份模型。 |
| 依赖声明 / 查找 | 动态字符串 + Proxy `ctx.foo`；`inject/provide/get/has`（`reflect.ts:8-14,105-170`） | `Component.Inject/Provide` + `NewKey[T]` + `Require/Provide`（`runtime/key.go:33`、`context.go:548-725`） | 接受差异。Go 类型化 Key 在编译期发现错误；无 Proxy 复刻计划。 |
| 隔离 | `isolate(name,label)` 按服务名隔离（`context.ts:63-67`） | 逐 key realm ρ：`Fiber.keyRealms`、`Isolate`/`WithScope`（`context.go:379-395`、`providers.go:lookupOwn`） | Go 与论文 Def 24/25 对齐且更细粒度；保留。 |
| 拦截 / 元数据 | `intercept()` + `Service.resolveIntercept` 链（`context.ts:69-75`、`service.ts:53-70`） | `MetaKey`、`ProvideMeta/RequireMeta/InterceptMeta`，派生 `metaView`（`context.go:557-689`） | 接受差异。Go 有 monoid + 可逆拦截；JS 无对应 monoid。 |
| Effect 表达形式 | `ctx.effect` 接受 disposer / `Iterable` / `Promise` / `AsyncIterable`，单 effect 内多逆 LIFO，跨 effect 并发 `Promise.all`（`fiber.ts:60-76,261-332,502-523`；`dispose.spec.ts:37,103`） | 一个 `install func()(inverse,error)` + 槽位状态机 + `IterComponent` 分步（`context.go:263-340,824-855`；`component.go:74-102`） | **接受差异，不改 API**。JS 的多种形式是同一「被追踪的可逆后果」的语法糖：Go 已覆盖就绪等待、install/unwind 竞态自撤销、严格 LIFO、逆错误隔离（`errors.Join` 后继续）。跨 effect 并发清理与论文 LIFO 冲突，不照搬。 |
| 异步安装取消 | epoch 失效中止 async generator 并调 `iter.return()`（`fiber.ts:288-306`） | install 屏障 + `ctx.Done()` 合作式等待（`context.go:783-819`） | 接受差异。Go 用协作式取消，不引入 generator close 协议。 |
| 生命周期 / 失败 | PENDING/LOADING/ACTIVE/FAILED/DISPOSED/UNLOADING；Failed 仅 `update()` 恢复（`fiber.ts:90-97,386-406,456-457`） | Pending/Loading/Active/Failed/Unloading/Gone；修正声明后重新启用（`runtime/state.go`、`finalizeActivation`） | 接受差异。失败不向父传播一致。 |
| Event 注册 | `on/once` 返回可调用 disposer；`once` 调用前先移除；派发中已移除监听器被跳过、新增监听器下一轮生效（`events.ts:185-206,110-112,121-163`；`events.spec.ts:134,149,163,177`） | `On/OnWaterfall` 仅返回 error，移除只在逆；快照在捕获时固定（`runtime/event_registry.go:172-260,148-167`） | **实现**：`OnWithHandle` + `OnOnce` + binding `Active` 检查（见 §3）。 |
| Event 派发模式 | `emit/bail/serial` 同步、`parallel` 并发 `allSettled` + `AggregateError`、`waterfall` 值穿透 + 一次性 `next()`（`events.ts:108-161`） | `Emit/Serial/Parallel/Bail/Waterfall`（`extensions/event/dispatch.go:55-266`）；值通道由共享 payload 承担，`Bail` 用错误停机（ADR-0004） | 接受差异。Go 的 handler 同步返回即代表完成，不需要 Promise 等待；value-bail 可由 payload.Result + 哨兵错误表达。`next()` 二次调用检测比 JS 更严格。 |
| Logger / 配置校验 | Core Logger + Standard Schema | Console/logstore + 类型化 Config（`extensions/config`） | 接受差异。无 API 形状对齐目标。 |
| 运行时观察 | Fiber/Registry/Events 手工查询 | `Runtime.Snapshot` 线性化全量 + Observe（`runtime/observation.go`、`extensions/observe`） | Go 更强，保留。 |

## 2. 官方扩展（23 个 workspace 包，2 个为 Core/测试助手）

| JS 包 | 能力 | Go 对应 | 判定 / 决定 |
|---|---|---|---|
| `cordis` | Kernel | `runtime` | 已覆盖（上表）。 |
| `loader`、`include`、`hmr`、`group` | 配置树加载 / 文件同步 / 原位 HMR / 分组 | `extensions/loader`、`config`+`configwatch`、`hmr`、`bundle` | 部分等价；Loader/WASM 资源协议已冻结，不重开。HMR 的 retire→reinsert 与论文一致，不改。 |
| `timer`、`logger-console` | 生命周期计时 / 日志导出 | `extensions/scheduler`、`console` | 等价能力，接受 API 差异。 |
| `fs`、`fs-node`、`fs-memory`、`fs-conformance` | URL 挂载式文件系统服务 + 本地/内存 Provider + 一致性测试 | `extensions/watch`（文件变更监控）、无统一 FS 服务 | **延期**。`fs` 是独立服务生态（被 loader/hmr/env/include 依赖），非 Kernel；Go 现有 watch 已满足采集场景的文件监听。触发条件：出现需要多 Provider 挂载/内存 FS 的真实应用。fs-conformance 为私有测试包，不适用。 |
| `package-manager`、`env` | 运行时包依赖安装 / `.env` 注入 | 无直接对应 | **不适用**（应用/部署层关注点，非论文运行时）。Go 依赖由 go.mod 与声明存储管理。 |
| `http`、`http-socks`、`server`、`server-acl`、`server-echo`、`server-proxy`、`server-static` | HTTP 客户端/服务端与路由 | 应用侧 `cmd/httpd` 等 | **不适用**（业务能力，不进框架；Console 已有受控 HTTP 面）。 |
| `cli`、`cli-cordis`、`create-cordis` | CLI 服务 / 启动命令 / 脚手架 | `cmd/*`、`scripts` | **不适用**（开发体验，非运行时语义）。 |

## 3. 本批次实现范围

只在 Event 注册生命周期补齐 JS Core 的真实运行时能力，不动 Effect、不动 Loader/WASM、
不扩张定理账本、不引入第二生命周期权威：

1. `runtime.OnWithHandle` / `OnWaterfallWithHandle` / `OnOnceWithHandle`：注册返回幂等手动
   disposer；逆仍由 Effect 管理，手动提前移除与逆撤销互不重复。
2. `runtime.OnOnce`：一次性注册，调用 handler **前**移除自身（防递归重入，且 bindings 立即可见），
   并以原子领取保证并发派发下恰好触发一次。
3. `EventBinding.Active`：注册移除后快照中的绑定失效；五种派发模式在调用前跳过失效绑定
   （对齐 JS「派发中移除即跳过、派发中新增下一轮生效」）。`Parallel` 以 goroutine 并发启动，
   "被同批另一监听器移除" 在 Go 中受调度影响（JS 的同步 map 启动顺序更强）；该边界已由
   测试固定，不通过伪造顺序掩盖。

## 4. 非目标

不复制 Proxy 动态属性访问；不做 Fiber 身份保留的原位替换；不移植 HTTP/SOCKS/Package Manager/
脚手架；不把跨 Effect 并发撤销引入内核；不在本批次实现完整 FS 服务。
延期项只在真实应用需求出现时另立批次，避免无限对照。
