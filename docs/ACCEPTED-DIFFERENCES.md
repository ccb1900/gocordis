# 接受差异冻结账本（r1 终版）

基线：`docs/r1.md`、JS Cordis `5e2e6c1`、GoCordis r1 整改后。
本文件逐项冻结"不照搬 JS / 不做进一步实现"的决定；每项都有理由、影响与验证边界。
凡下表未列出的差异，均已实现或属于 API 形状差异，不再是待办。

| 项 | JS 行为 | Go 决定 | 理由 / 影响 |
|---|---|---|---|
| Fiber 身份保留的原位替换 | `registry.replace()` 保留 uid/config/外部引用 | `Fiber.Revise` 退役→Gone→重插新 Fiber | 论文 Revision Composite 要求新 Fiber，身份保留违背论文；Go 是论文忠实实现，不改 |
| Proxy 动态属性访问 | `ctx.foo` / Reflect / accessor | 类型化 `NewKey[T]` + `Require/Provide` | Go 编译期发现错误；不复制 JS Proxy，能力不缺失 |
| 跨 Effect 并发撤销 | 顶层 effect 以 `Promise.all` 并发清理 | 严格顺序 LIFO（effect 槽位状态机） | 论文时序语义要求 LIFO，并发撤销与之冲突 |
| Effect 语法形式 | disposer/Iterable/Promise/AsyncIterable | install/inverse + `IterComponent` + ctx-bound 助手 | Go 用闭包/channel/iter 表达等价能力；不引入 JS 语法糖 |
| 异步安装取消协议 | epoch + async generator `return()` | install 屏障 + `ctx.Done()` | 合作式取消是论文的宿主假设，不引入 generator close 协议 |
| Event `Parallel` 同批移除 | 同步 map 启动保证移除即跳过 | goroutine 启动时检查 `Active`；同批并发移除受调度影响 | Go 并发模型差异，已写入正式契约并由测试固定，不伪造顺序 |
| Event 旧 `Bail` | 值短路 | 保留 fail-fast-on-error；新增 `OnValue`/`BailValue` 承担 JS 值短路 | 兼容既有调用方，同时能力已覆盖 |
| `http-socks` DNS 语义 | socks5 本地 DNS、socks5h 远端 DNS | Go socks5/5h 均为远端 DNS；socks4a 远端 DNS 已正确映射 | 由成熟库 h12.io/socks 的协议实现决定，无法在 Go 语义内区分 5/5h |
| `package-manager` Agent | npm/pnpm/yarn 检测、lockfile、install/restore | Go toolchain Agent + `modfile` 原子编辑；报告+变更由 Go 工具链执行 | Go 模块生态与 npm 不同；不再是缺失，只是原生定位 |
| `create-cordis` 远程模板 | 下载 registry tarball、交互提示、git init | 本地静态模板 + 空目录校验 + 回滚 + 生成项目 build/run 测试 | 远程模板属于产品分发/网络能力，不是运行时语义 |
| CLI 输入形态 | shell 风格完整 token/参数 schema | 命令文本 tokenizer（Windows 路径保留反斜杠）+ 组合旗标/引号 | Go CLI 输入来自应用参数而非 shell；实测 `google/shlex`、`mattn/go-shellwords` 会吞掉 Windows 反斜杠，`pflag` 不解析命令文本且不支持 `--no-X`，均不采用；由测试固定现有行为 |
| HTTP 有界关闭 | JS 单线程取消后不存在活动回调 | `Close` 取消 + 有界等待；超时返回 `ErrCloseTimeout`，`State()` 暴露 `Closed/Active/OpenStreams/CloseTimedOut` | 严格区分 Request Completed / Stream Closed / Service Closed；不把有界等待宣称成完整 Join。后续幂等 `Close()` 返回 nil 以保证 Runtime 卸载不被超时阻断 |
| 普通拦截器适配流式响应 | JS fetch 拦截可保持流 | 普通 `Interceptor` 可能缓冲响应；`StreamInterceptor` 保持 live body | 兼容既有 `Interceptor` 签名；缓冲行为在 API 注释与测试中固定，不冒充流式 |
| HTTP `file:` / `data:` URL | 内置 fetch 拦截器支持本地/内联资源 | 不实现；HTTP Service只处理 http/https 与代理 | Go 的文件访问由 FS Service 提供，职责边界清晰；不构成运行时能力缺失 |
| loader / hmr 模块模型 | ES module 图 + 原位 Fiber 替换 | Go 静态链接 + `Revise` 退役重插 | 语言运行时差异；能力已由 loader/HMR/Revision 覆盖，模块身份不作逐条对齐 |
| WS 回调自关闭 | JS 单线程内回调可安全 dispose 自身 | 公开 `Close`/disposer 会 join 回调 goroutine；回调内不得调用自身所在注册的关闭 | Go goroutine 模型的自等待边界；由 API 注释固定，正常逆撤销仍保证 Gone 后无 socket |
| `fs-conformance` 包 | JS 私有测试 workspace | Go 以同仓 conformance 测试文件覆盖 Local/Memory Provider | 测试包形态差异，不影响能力 |
| 采用 afero 作为统一 FS 后端 | JS fs-node/fs-memory 共享抽象后端 | 不采用 afero；保留 `extensions/fs/contract` + Local/Memory 两个 provider + 同仓 conformance | afero 的 `MemMapFs` 没有 ctx、watch（Change/Ready/Error/Dispose）、symlink/Readlink 与 os.Root 防逃逸语义，需重包装反而更重；标准库 `os.Root` 与 mutex map 已足够，且生命周期由 Effect 统一管辖 |
| JS `copy` 的 `filter` / `dereference` 与 `readFile` 的 `encoding`、`open` 的 flags/mode | 谓词过滤、解引用符号链接、编码字符串、文件打开标志 | 不实现；`copy` 仅支持 `{Recursive,Overwrite}`，读写为 `[]byte`，符号链接按链接/目标规则固定处理 | JS 专有选项；Go 用字节切片与调用方解码表达，能力不缺失但不是逐选项等价，故不计入"语义等价" |
| Loader/WASM 资源协议 | — | 保持冻结，仅因可复现正确性缺陷重开 | `FINAL-ACCEPTANCE.md` 已冻结；r1 未发现需要重开的缺陷 |
| 定理证据体系 | — | 不扩张 | 论文语义基线已冻结，新增能力都在 Extension 层 |

## 已消除的差异（不再冻结）

- Timer 调用方生命周期：`*Owned` API + `TimeoutWait`/`Ticks`/`TimeoutContext`/`*Args` 已实现。
- FS 操作面：Range/Stream/Lstat/Rename/Access/Symlink 元数据 + 递归 Watch 已实现；本轮补齐
  `Remove{R Recursive,Force}`、`Mkdir{Recursive}`、`Copy{Recursive,Overwrite}`、
  `Symlink`/`Readlink`、`ResolveWithBase` 与 `io/fs.FS` 适配器（`NewFS`）。
  Local 的 `Rename` 用 build tag 门控：Go 1.25+ 走 `os.Root.Rename`，Go 1.24 走等价的
  受限 copy/remove 回退，`GOTOOLCHAIN=go1.24.0 go build ./...` 已验证。
- HTTP：StreamResponse/Decoder/BaseURL/Params/Extend + 身份代理 + 活动请求关闭 +
  方法族（PUT/PATCH/DELETE/HEAD）+ WS 客户端已实现。
- Server：catch-all 路由 + WebSocket（gorilla/websocket）+ RouteCheck 已实现。
- Server ACL：loopback/点前缀/空列表/路由覆写/404 拒绝已按 JS 5e2e6c1 对齐。
- Server Proxy：路径与查询透传/状态透传/头过滤/双向 WS 代理已按 JS 5e2e6c1 对齐。
- Event 值短路：`OnValue`/`BailValue` 已实现。

本账本为终版：仅 loader/hmr 模块模型保留部分等价（语言运行时差异），其余不再以
"延期"名义保留待办。
