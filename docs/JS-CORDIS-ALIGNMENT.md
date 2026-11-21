# GoCordis ↔ JS Cordis 能力对照（r1 整改后分级记录）

Baseline: JS Cordis `5e2e6c172335d207d11289959322edaaba4447f0` (`cordis` 4.0.0-rc.13)。
审查基线见 `docs/r1.md`；本文件不再是笼统的"已实现"清单，而按下列级别分级：

- **完整等价**：行为契约和资源生命周期均可对照，测试覆盖。
- **语义等价 / API 不同**：解决的问题和运行时行为一致，Go 使用原生类型化 API。
- **部分等价**：核心问题已解决，但有明确未覆盖的公开能力。
- **接受差异**：论文/语言冲突或刻意取舍，不视作缺失。
- **缺失**：确认无对应物。

论文冲突以 `docs/论文.pdf` 为准。

## 1. Core

| 能力 | Go 落点 | 判定 |
|---|---|---|
| Component / Fiber / 生命周期调和 | `runtime/component.go`、`fiber.go`、`orchestrator.go`、`reconcile.go` | 语义等价 / API 不同 |
| 依赖注入 / 隔离 / 拦截 | `NewKey[T]`、`Require/Provide`、逐 key realm、`MetaKey`（`key.go:41`、`context.go:379-689`） | 语义等价 / API 不同；不复制 JS Proxy |
| Effect / 异步安装取消 | 槽位状态机 + install 屏障 + `ctx.Done()`（`context.go:263-340,783-855`）、`IterComponent`（`component.go:81-99`） | 语义等价 / API 不同：Go 严格顺序 LIFO，JS 跨 effect 并发撤销；后者与论文 LIFO 冲突，接受差异 |
| Revision 原位替换 | `Fiber.Revise` 退役→Gone→重插（`fiber.go:401-492`） | 接受差异：论文要求新 Fiber，JS 保留身份是其特有 API |
| Event 注册 | `OnWithHandle`/`OnOnce`/`OnOnceWithHandle`/`OnWaterfallWithHandle` + `EventBinding.Active` | 完整等价（Parallel 同批移除受 goroutine 调度影响，已写入契约） |
| Event 派发 | `Emit/Serial/Parallel/Bail` + `BailValue`（`runtime.OnValue`，第一个 `present=true` 短路） | 完整等价；旧 `Bail` 仍为 fail-fast-on-error，保留兼容 |
| 运行时观察 | `Runtime.Snapshot` + EventSink + Observe | Go 更强 |

## 2. 官方扩展

| JS 包 / 能力 | Go 实现 | 判定 |
|---|---|---|
| `loader`、`include`、`hmr`、`group` | `extensions/loader`、`config`+`configwatch`、`hmr`、`bundle` | 部分等价（模块模型不同；Loader/WASM 协议冻结） |
| `logger-console` | `extensions/console/logstore` | 语义等价 / API 不同 |
| `timer` | `extensions/timer`：五个 `*Owned` API + `TimeoutWait`/`Ticks`(iter.Seq)/`TimeoutContext`/`*Args`；Gone 后无回调 | 语义等价 / API 不同：Go 用 channel/iter 对应 JS Promise/AsyncIterable |
| `fs` / `fs-node` / `fs-memory` | `extensions/fs`：挂载路由、递归 Watch、完整操作面（Range/Stream/Lstat/Mkdir/Rename/Copy/Access/Symlink 元数据）、`os.Root` 防逃逸、Provider conformance | 语义等价 / API 不同；JS 私有 glob filter 由 doublestar 对应 |
| `env` | `extensions/env`：compose-go dotenv 解析、NODE_ENV 自动 mode、4 文件优先级、process 优先、栈式 Populate 所有权 | 完整等价 |
| `package-manager` | `extensions/package-manager`：x/mod/modfile、原子 temp+rename、Report/Apply 同锁、before/after diff | 接受差异（Go 原生定位，见账本） |
| `http` | `extensions/http`：拦截器链、身份代理、真实 SOCKS、活动请求关闭、StreamResponse/Decoder/BaseURL/Params/Extend | 语义等价 / API 不同；file:/data: URL 为接受差异（见账本） |
| `http-socks` | `extensions/http-socks` + h12.io/socks：socks4/4a/5/5h（含 `socks` 别名）真实拨号 | 接受差异（DNS 语义，见账本） |
| `server` | `extensions/server`：stdlib `ServeMux` catch-all、Handle/All/Use/Serve/ServeRange、404/405/OPTIONS 204、WS(gorilla/websocket) | 语义等价 / API 不同 |
| `server-echo`/`static` | `extensions/server/*`；static 支持嵌套路径、编码路径与穿越拒绝 | 语义等价 / API 不同 |
| `server-acl`/`proxy` | `extensions/server/{acl,proxy}`：本地行为测试已覆盖 | 部分等价：JS 授权/转发语义未逐条对照 |
| `cli` / `cli-cordis` | `extensions/cli` + `cmd/cli-cordis`：Windows 路径、引号、组合旗标、别名/help | 接受差异（命令文本输入，见账本） |
| `create-cordis` | `cmd/create-cordis`：空目录校验、回滚、生成项目 tidy/build/run 真实验收 | 接受差异（本地静态模板，见账本） |

## 3. r1 整改记录

- P1-01 static catch-all：改用 stdlib `net/http.ServeMux`（Go 1.22 模式），按 id 键维护路由表并在注册变化时重建 mux；`Param` 用 `PathValue`。
- P1-02 timer 生命周期：新增五个 `*Owned` API，创建即安装 Effect 逆；公开的 `Stop()` 不等待且可在自身回调调用，等待通过内部 `stopAndWait` 由 Effect 逆/Close 完成（不暴露给调用方）。
- P1-03 SOCKS 代理：HTTP Service 改为带 ID 的代理注册，逆只撤销自身；SOCKS4/4a/5 通过 `h12.io/socks` 真实拨号。
- P1-04/05 FS：递归 Watch + Ready/Error/Dispose + doublestar 过滤；Provider 嵌入 `io.Closer`，仅 Service 拥有的 Provider 在 Close 时释放。
- P1-06 HTTP：活动请求计数 + 服务级取消上下文 + 有界优雅等待，Close 后拒绝新请求。
- P1-07 package-manager：`modfile` + 原子替换 + Report/Apply 同锁 + 批次去重 + 恢复错误传播。
- CLI：tokenizer 区分 Windows 路径与引号内转义；create-cordis 校验空目录并清理目录。
- Event：`OnValue` + `BailValue` 补齐 JS 结果短路。

## 4. 明确保留的边界

不复制 Proxy 动态属性访问、JS 特有的 Fiber 身份保留替换、跨 Effect 并发撤销。
逐项冻结的接受差异见 `docs/ACCEPTED-DIFFERENCES.md`（终版账本）。
表中"部分等价"仅剩 loader/hmr 模块模型差异与 server-acl/proxy 的 JS 语义未逐条对照；
其余差异均已作为接受差异冻结（HTTP file/data URL、SOCKS DNS、package-manager Agent、
CLI 输入、远程脚手架模板），不再自动扩展。
