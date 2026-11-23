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
| 统一插件 manifest | `extensions/pluginmeta` + `adapters`：描述性 `Manifest`（语义化版本、约束、校验、JSON/TOML 严格解析），投影 native/WASM/proc 元数据 | 接受差异：`Requires`/`Provides` 只描述意图，不建运行时依赖边、不接管生命周期（见账本） |
| `logger-console` | `extensions/console/logstore` | 语义等价 / API 不同 |
| `timer` | `extensions/timer`：五个 `*Owned` API + `TimeoutWait`/`Ticks`(iter.Seq)/`TimeoutContext`/`*Args`；Gone 后无回调 | 语义等价 / API 不同：Go 用 channel/iter 对应 JS Promise/AsyncIterable |
| `fs` / `fs-node` / `fs-memory` | `extensions/fs`：`contract`/`local`/`memory` 分包；挂载路由 + 递归 Watch（Change/Ready/Error/Dispose、doublestar filter）；`Remove{Recursive,Force}`、`Mkdir{Recursive}`、`Copy{Recursive,Overwrite}`、`Symlink`/`Readlink`、`ResolveWithBase`、Range/Stream/Lstat/Rename/Access；`os.Root` 防逃逸；双 provider 同一 conformance；`NewFS` → `io/fs.FS`（支持 http.FileServerFS） | 核心操作面语义等价 / API 不同；**JS 专有选项 `copy.filter`/`dereference`、`readFile.encoding`、`open` flags/mode 为接受差异**（见账本），不宣称逐选项等价 |
| `env` | `extensions/env`：compose-go dotenv 解析、NODE_ENV 自动 mode、4 文件优先级、process 优先、栈式 Populate 所有权 | 完整等价 |
| `package-manager` | `extensions/package-manager`：x/mod/modfile、原子 temp+rename、Report/Apply 同锁、before/after diff | 接受差异（Go 原生定位，见账本） |
| `http` | `extensions/http`：拦截器链、身份代理、真实 SOCKS、请求侧 `BodyReader` 流式上传、StreamResponse/Decoder/BaseURL/Params/Extend、方法族、WS 客户端；`State()` 区分 Request/Stream/Service 关闭状态；`RoundTrip` 为 Proxy 提供有边界的传输 | 语义等价 / API 不同；file:/data: URL、有界关闭、普通拦截器缓冲为接受差异（见账本） |
| `http-socks` | `extensions/http-socks` + h12.io/socks：socks4/4a/5/5h（含 `socks` 别名）真实拨号 | 接受差异（DNS 语义，见账本） |
| `server` | `extensions/server`：stdlib `ServeMux` catch-all、Handle/All/Use/Serve/ServeRange、404/405/OPTIONS 204、WS(gorilla/websocket) | 语义等价 / API 不同 |
| `server-echo`/`static` | `extensions/server/*`；static 基于 `http.FileServerFS` + `io/fs.FS` 适配器，支持嵌套路径、编码路径、穿越拒绝与 Range(206) | 语义等价 / API 不同（标准库承担传输） |
| `server-acl`/`proxy` | `extensions/server/{acl,proxy}`：ACL 的 loopback/点前缀/路由覆写/404 拒绝；Proxy 基于 `httputil.ReverseProxy` + 服务 `RoundTrip`，保留路径透传/状态透传/头过滤/双向 WS 代理 | 语义等价 / API 不同（r1 "需要验证" 已逐条核实并对齐） |
| `cli` / `cli-cordis` | `extensions/cli` + `cmd/cli-cordis`：Windows 路径、引号、组合旗标、别名/help | 接受差异（命令文本输入，见账本） |
| `create-cordis` | `cmd/create-cordis`：空目录校验、回滚、生成项目 tidy/build/run 真实验收 | 接受差异（本地静态模板，见账本） |

## 3. r1 整改记录

- P1-01 static catch-all：改用 stdlib `net/http.ServeMux`（Go 1.22 模式），按 id 键维护路由表并在注册变化时重建 mux；`Param` 用 `PathValue`。
- P1-02 timer 生命周期：新增五个 `*Owned` API，创建即安装 Effect 逆；公开的 `Stop()` 不等待且可在自身回调调用，等待通过内部 `stopAndWait` 由 Effect 逆/Close 完成（不暴露给调用方）。
- P1-03 SOCKS 代理：HTTP Service 改为带 ID 的代理注册，逆只撤销自身；SOCKS4/4a/5 通过 `h12.io/socks` 真实拨号。
- P1-04/05 FS：递归 Watch + Ready/Error/Dispose + doublestar 过滤；Provider 嵌入 `io.Closer`，仅 Service 拥有的 Provider 在 Close 时释放。
- FS Core 收敛：先写失败 conformance（Local/Memory 同一套断言）再实现 `Remove/Mkdir/Copy`
  选项、Symlink/Readlink、URL base 解析；共享类型下沉 `extensions/fs/contract`，
  `extensions/fs` 以别名保持外部 API 不变。Local `Rename` 用 build tag 门控
  （Go 1.25+ `os.Root.Rename`，Go 1.24 受限回退），两工具链均 build/test 通过。
  新增 `NewFS(svc, ctx) io/fs.FS` 适配器（ValidPath/拒 scheme/Seek/ReadDir，
  可用于 `http.FileServerFS`）。未采用 afero，理由见账本。
- P1-06 HTTP：活动请求计数 + 服务级取消上下文 + 有界优雅等待，Close 后拒绝新请求。
  后续收敛：`Request.BodyReader` 流式上传；流式 Body 的生命周期延伸到 `Body.Close()`；
  `State()` 区分 Request Completed / Stream Closed / Service Closed；超时返回 `ErrCloseTimeout`；
  `RoundTrip` 复用同一所有权/拦截器/代理路由，`server-proxy` 改用 `httputil.ReverseProxy`；
  `static` 改用 `http.FileServerFS`（Range 206，不再全文件读入）。
- P1-07 package-manager：`modfile` + 原子替换 + Report/Apply 同锁 + 批次去重 + 恢复错误传播。
- CLI：tokenizer 区分 Windows 路径与引号内转义；create-cordis 校验空目录并清理目录。
- Event：`OnValue` + `BailValue` 补齐 JS 结果短路。

## 4. 明确保留的边界

不复制 Proxy 动态属性访问、JS 特有的 Fiber 身份保留替换、跨 Effect 并发撤销。
逐项冻结的接受差异见 `docs/ACCEPTED-DIFFERENCES.md`（终版账本）。
表中"部分等价"仅剩 loader/include/hmr/group 的模块模型差异（语言运行时差异）；
server-acl/proxy 的 "需要验证" 已逐条核实并对齐。其余差异均已作为接受差异冻结
（HTTP file/data URL、SOCKS DNS、package-manager Agent、CLI 输入、远程脚手架模板），
不再自动扩展。FS 的 JS 专有选项（copy filter/dereference、readFile encoding、
open flags/mode）同样冻结为接受差异，不计入等价范围。

## C# 迁移对齐补充（2026-11-22）

`migration/dotnet` 分支的 C# 实现按本文件的"部分等价"项逐条收敛：

| 原参照项 | C# 实现 | 对照结论 |
| --- | --- | --- |
| loader / include / hmr / group（模块模型差异） | `Gocordis.Plugins.PluginRegistry`（Tomlyn Manifest、组合/补丁、ConfigWatcher）、`Gocordis.Runtime.Managed`（ALC + 私有依赖）、`Gocordis.Runtime.Wasm.WasmHotSwapper` | 能力等价；模块身份按论文 Revision（退役→重插），不追求 JS 原位替换 |
| Event `emit/serial/parallel/bail/waterfall` | `Gocordis.Extensions.EventDispatcher` | 五模式齐备；注册/注销可逆；parallel 传播 handler 异常 |
| timer / scheduler / watch | `TimerService`、`Scheduler`、`WatchService` | JS 风格 Timeout/Interval/Debounce/Throttle + 确定性 pump + revision 文件监听 |
| fs / http / server | `FileSystemService`（挂载路由与防逃逸）、`HttpService`（拦截器）、`ServerHost`（ASP.NET Core） | 用 .NET 成熟库实现，能力覆盖不再标"缺失" |
| env / cli / broker / registry / observe | `EnvService`、`CliDispatcher`、`Broker<T>`、`StableRegistry<T>`、`ObserveStream<T>` | 对应 JS 官方扩展能力 |
| market / package-manager | `MarketService`（校验和/版本门）、`PackageManager`（报告/应用/回滚） | 仅本地声明层；插件市场服务端与云端平台仍按范围排除 |
| WASM 能力桥 | `WasmHostBridge`（`gocordis.report` 导入）、`WasmCustomSections`（`module_type`） | 继承 Go ABI 并补上 Go v0.1 缺失的宿主桥 |

**等价边界**：上表证明的是“能力等价”，不是 JS 公开 API 的逐项行为等价。模块身份采用论文
Revision（退役→重插）而非 JS 原位替换；公开选项、异常传播、资源关闭、边界条件尚未逐项与 JS
对齐验证，逐项状态见 [`migration/js-cordis-csharp-differences.md`](migration/js-cordis-csharp-differences.md)
的“公开行为等价边界”。验收时不得用“已有对应类”替代行为证据。

**刻意不接受差异**（论文优先，非能力缺失）：`ctx.foo` Proxy 动态属性、跨 effect 并发撤销、
`registry.replace()` 保身份替换。详见 `docs/ACCEPTED-DIFFERENCES.md` 与
`docs/migration/semantic-mapping.md` §4。

## 增量基线更新（2026-11-23）

发布版基线仍为 `5e2e6c172335d207d11289959322edaaba4447f0`（4.0.0-rc.13）。
当前上游开发基线更新为 `f4c6b3ae00d6649b0b13cdf108aeb28b66403d87`
（相对 rc.13 的提交：`feat(fs): add atomic and exclusive writes and copies`）。
上游发布版 rc.13 还包含 `registry.replace()`、`hmr.reconcile()`、generator epoch、
loader scope、FS mount/watch 与 package-manager 等变化；论文冲突项仍按上表冻结。

增量对照结论：

- fs：`WriteOptions.exclusive/atomic` 与 `CopyOptions.exclusive/atomic` 已在 C#
  `FileSystemService` 以原生 `FileStream`/同目录暂存 + `File.Move` 实现，
  红测 `FsAtomicAndExclusiveWriteAndCopyOptions` 覆盖覆盖写、独占失败、
  原子失败清理。**该增量能力已闭合**。
- 其余 rc.13 变化（registry.replace 保身份、HMR 分单元 reconcile、
  generator epoch、loader scope）仍由 C# 的论文 Revision / 批次事务 /
  IAsyncEnumerable / manifest-aware factory 以语义等价或接受差异处理，
  逐项边界继续见 `docs/migration/js-cordis-csharp-differences.md`。

因此，“最新 JS 等价”现在指的是：rc.13 + 当前 main 增量 SHA `f4c6b3a` 的显式对照，
而不是笼统宣称与上游所有公开 API 逐项一致。
