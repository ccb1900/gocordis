# GoCordis ↔ JS Cordis 有限能力对照与落地记录（2026-11-21）

Baseline: GoCordis `141be38`（本批实现前）；JS Cordis `5e2e6c172335d207d11289959322edaaba4447f0`
(`cordis` 4.0.0-rc.13)。证据取自该 JS 提交的一手源码，不依赖目录名或旧版清单。
判定：**实现** = 已落代码并有测试；**接受差异** = API/策略不同但运行时能力不缺失，
或与论文语义冲突不能照搬。论文冲突时以 `docs/论文.pdf` 为准。

## 1. Core

| 能力 | JS 5e2e6c1 行为 | Go 落点 | 判定 |
|---|---|---|---|
| Component / Fiber | 定义与实例分离；`ctx.use()` 每次新建 Fiber（`core/src/registry.ts:239-254`） | `runtime/component.go`、`runtime/fiber.go`；`Load`/`Child` | 等价；状态名 Gone↔Disposed，Go 额外有声明校验。 |
| 原位替换 | `registry.replace()` 保留 uid/config/外部引用（`registry.ts:187-209`） | `Fiber.Revise` 退役→Gone→重插（`runtime/fiber.go:401-492`） | 接受差异：保留 Fiber 身份是 JS 特有 API；论文要求 Revision 退役旧 Fiber 并插入新 Fiber，Go 是论文忠实实现，不改身份模型。 |
| 依赖注入 | 动态字符串 + Proxy `ctx.foo`（`reflect.ts:8-14,105-170`） | 类型化 `NewKey[T]` + `Require/Provide`（`runtime/key.go:41`、`context.go:548-725`） | 接受差异：Go 编译期发现错误，不复制 Proxy。 |
| 隔离 / 拦截 | `isolate`/`intercept`（`context.ts:63-75`） | 逐 key realm ρ + `MetaKey` monoid + 派生 `metaView`（`context.go:379-689`） | Go 与论文 Def 24–27 对齐且更细粒度。 |
| Effect 表达形式 | disposer/Iterable/Promise/AsyncIterable（`fiber.ts:60-76,261-332`） | 单 install/inverse 槽位状态机（`context.go:263-340,824-855`）+ `IterComponent` 分步（`runtime/component.go:81-99`） | 接受差异：JS 多形式是同一「可逆后果」的语法糖；跨 effect 并发撤销与论文 LIFO 冲突。 |
| 异步安装取消 | epoch 中止 async generator（`fiber.ts:288-306`） | install 屏障 + `ctx.Done()`（`context.go:783-819`） | 接受差异：Go 用协作式取消，不引入 generator close 协议。 |
| 生命周期 / 失败 | 六状态；Failed 仅 `update()` 恢复（`fiber.ts:90-97,456-457`） | Pending…Gone；修正声明后重启用 | 等价能力，策略差异。 |
| Event 注册与派发 | `on/once` 返回 Disposable；移除即跳过、新增下一轮生效（`events.ts:108-206`） | `OnWithHandle`/`OnOnce`/`OnOnceWithHandle`/`OnWaterfallWithHandle` + `EventBinding.Active`，五模式实时检查（`runtime/event_registry.go`、`extensions/event/dispatch.go`） | **已实现**；`Parallel` 并发启动导致同批移除受调度影响，边界已测。 |

## 2. 官方扩展逐项落地

| JS 包 | 能力 | Go 实现 | 判定 |
|---|---|---|---|
| `loader`、`include`、`hmr`、`group` | 配置树 / 文件同步 / 热替换 / 分组 | `extensions/loader`、`config`+`configwatch`、`hmr`、`bundle` | 已覆盖；Loader/WASM 资源协议保持冻结。 |
| `timer` | timeout/interval/throttle/debounce | `extensions/timer`（`ServiceKey`；`Owned`/`DebounceHandle`/`ThrottleHandle`/`ThrottleNoTrailing`） | **已实现**：所有权跟随消费者 Fiber，Gone 后无回调泄漏。 |
| `logger-console` | 日志导出 | `extensions/console/logstore` | 等价能力。 |
| `fs`、`fs-node`、`fs-memory` | URL 挂载文件系统 + 本地/内存 Provider + Watch | `extensions/fs`（`ServiceKey`、`Mount`、`NewLocalProvider`、`NewMemoryProvider`、最长前缀路由、watch 重路由、`os.Root` 防逃逸） | **已实现**；`fs-conformance` 为 JS 私有测试包，不适用。 |
| `env` | `.env` 分层注入 | `extensions/env`（`ServiceKey`，`.env`/`.local`/`mode` 优先级、`${VAR}` 展开、process 优先、Populate 可逆） | **已实现**。 |
| `package-manager` | 依赖报告/变更 | `extensions/package-manager`（`ServiceKey`、`Agent`/`Report`/`Apply`，原子编辑 go.mod + before/after diff） | **已实现**；安装由 Go 工具链完成，不做 npm 式 lockfile 管理。 |
| `http` | HTTP 客户端服务 | `extensions/http`（`ServiceKey`、拦截器链、超时/状态类型化错误、代理、连接池回收） | **已实现**。 |
| `http-socks` | SOCKS 代理 | `extensions/http-socks`（`ProxyURL` + Component 经 `SetProxy`） | **部分实现**：socks5/socks5h 可用；socks4/socks4a 仅生成 URL，Go 标准库无 SOCKS4 拨号器，需 x/net/proxy 才能真正工作。 |
| `server` | HTTP 服务端/路由/中间件 | `extensions/server`（`ServiceKey`、`Handle`/`All`/`Use`/`Serve`/`ServeRange`、`{id}` 参数、404/405/OPTIONS 204） | **已实现**；WebSocket 需新增依赖，未纳入本批。 |
| `server-acl` / `echo` / `proxy` / `static` | 访问控制 / 回显 / 反向代理 / 静态资源 | `extensions/server/{acl,echo,proxy,static}` | **已实现**。 |
| `cli` | 命令服务 | `extensions/cli`（`ServiceKey`、引号感知解析、组合短旗标、别名/子命令/help） | **已实现**。 |
| `cli-cordis` | 启动命令 | `cmd/cli-cordis`（`run`/`version`，Effect 拥有注册，真实 Runtime 生命周期） | **已实现**。 |
| `create-cordis` | 项目脚手架 | `cmd/create-cordis`（`Generate` 生成 go.mod/manifest/main/组件，失败回滚，生成代码语法测试） | **已实现**（本地静态模板；不下载远程模板）。 |

## 3. 实现约束与证据

- 每个新服务都以 `runtime.NewKey[T]` 暴露能力，Provider 的 `Inject/Provide` 与
  `Require/Provide` 一致；注册、挂载、路由、定时器、监听、文件编辑全部是 Effect 或
  显式可逆 API，Fiber Gone 后无残留。
- 用户回调/拦截器/处理器从不在持锁状态下运行；并发相关测试均 `go test -race`。
- 独立审查发现并已修复：计时器注册越过消费者生命周期、FS 符号链接 TOCTOU、
  env Populate 部分写入、HTTP 退役连接池泄漏、CLI 引号参数、脚手架部分写入、
  go.mod 回滚文件模式等；详见各包测试。
- 未复制 Proxy 动态属性访问、JS 特有的 Fiber 身份保留替换、跨 Effect 并发撤销；
  这些不是能力缺失，而是论文/语言冲突的明确取舍。
- 已知边界：`extensions/timer` 的 handle `stop` 不可由该 entry 自己的回调调用
  （会自等待）；`extensions/http-socks` 的 socks4/a 只形成 URL，真正拨号需额外依赖。
