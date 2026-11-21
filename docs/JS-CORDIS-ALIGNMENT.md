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
| `timer` | `extensions/timer`：五个 `*Owned` API（创建即绑定调用方 Fiber，Gone 后无回调）；`Stop()` 可在自身回调安全调用，等待由 Effect 内部完成 | 部分等价：无 JS 的 Promise/AsyncIterable 形式与参数转发 |
| `fs` / `fs-node` / `fs-memory` | `extensions/fs`：挂载路由、local/memory Provider、递归 Watch（fsnotify + doublestar 过滤，Change/Ready/Error/Dispose）、`os.Root` 防逃逸与显式 Close、Provider conformance | 部分等价：Stream/Range/Lstat/Copy/Access/Symlink 元数据尚未覆盖 |
| `env` | `extensions/env`：compose-go dotenv 解析（`$VAR`/`${VAR}`/`${VAR:-default}`/多行）、`NODE_ENV` 自动 mode、4 文件优先级、process 优先、多实例 Populate 栈式所有权 | 完整等价（进程级注入的所有权已按注册栈管理） |
| `package-manager` | `extensions/package-manager`：x/mod/modfile 解析/编辑、原子 temp+rename、批量去重、Report/Apply 同锁、before/after diff | 部分等价：Go 原生定位，Agent=go toolchain；不做 npm/pnpm/yarn lockfile 安装 |
| `http` | `extensions/http`：拦截器链、`RegisterProxy` 身份注册、h12.io/socks 真实拨号、in-flight 请求等待 + 关闭超时、类型化 timeout/status 错误 | 部分等价：Stream Response / Decoder / Fetch 配置继承未覆盖 |
| `http-socks` | `extensions/http-socks` + h12.io/socks：socks4/4a/5/5h（含 `socks` 别名）真实拨号，逆只撤销自身注册 | 部分等价：Go 的 socks5/5h 均为远端 DNS，与 JS 的 local-DNS socks5 语义不同 |
| `server` | `extensions/server`：stdlib `ServeMux` 路由（`{id}`/`{path...}` catch-all）、`Handle`/`All`/`Use`/`Serve`/`ServeRange`、404/405/OPTIONS 204 | 部分等价：WebSocket 需额外依赖，本批未实现 |
| `server-acl`/`echo`/`proxy`/`static` | `extensions/server/*`；static 支持嵌套路径、编码路径与穿越拒绝 | 语义等价 / API 不同 |
| `cli` / `cli-cordis` | `extensions/cli`（上下文敏感 tokenizer：Windows 路径保留反斜杠、引号内转义、组合短旗标）+ `cmd/cli-cordis` | 部分等价：输入为命令文本而非完整 shell 解析 |
| `create-cordis` | `cmd/create-cordis`：空目录前置校验、文件+目录回滚、生成项目通过 `go mod tidy`/`go build` 测试 | 部分等价：本地静态模板，不下载远程模板/交互提示 |

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
WebSocket、HTTP Stream/Decoder、FS Stream/Range/Lstat/Copy/Access、远程脚手架模板
属于已标注的**部分等价**，不再用笼统"已实现"掩盖；是否补齐以 `docs/r1.md` 的
清单为准，不在本记录里自动扩展。
