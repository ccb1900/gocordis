# JS-CORDIS / 接受差异逐项 → C# 决定与证据（批次 C）

范围：把 [`JS-CORDIS-ALIGNMENT.md`](../JS-CORDIS-ALIGNMENT.md) §1–§4（含 C# 迁移对齐补充表）
与 [`ACCEPTED-DIFFERENCES.md`](../ACCEPTED-DIFFERENCES.md) 主表及"已消除的差异"中的**每一项**
逐条落到 C# 决定与证据。两类编号互不相干：`JS` = JS 能力对照项，`AD` = 冻结接受差异项，
`Cxx` = 论文语义条目（见 `paper-coverage-matrix.md`）。
证据列给出可执行测试名或文档锚点；无 C# 实现时明确写 `不适用（原因）`，不允许留空，
也不把"文档写了"当成测试覆盖。

## 公开行为等价边界（最终冻结）

能力类已迁移不等于公开行为等价。以下维度按当前证据标记为“待严格验证”，不计入等价断言：

| 维度 | 当前证据 | 边界 |
| --- | --- | --- |
| loader / include / hmr / group 模块模型 | `PluginRegistry`、`ManagedPluginLoader`、`WasmHotSwapper` 的行为测试 | C# 用论文 Revision 语义（退役→新 fiber），不追求 JS 模块原位身份 |
| 公开选项 / 配置形状 | `ManifestContractTests` 固定 TOML 字段契约 | JS 公开 options 名称与默认值未逐项镜像，按论文与 .NET 习惯收敛 |
| 异常传播 | `ManifestParseException` / `DuplicateProviderException` / `ProtectedPluginException` 有回归测试 | JS 异常类型、消息、传播层级未逐项对照 |
| 资源关闭与取消 | Core effect 撤销、Observation 订阅终止、进程 reap 有测试 | JS `dispose()` 的时序与重入语义未逐项验证 |
| fs / http / server 边界条件 | 路径防逃逸、HTTP 拦截器、WebUI 路由有测试 | JS 流式 body、SOCKS/DNS、CLI 组合旗标仍为接受差异 |
| timer / scheduler / watch 选项 | C# 类型与测试齐备 | JS 全部公开选项默认值未逐项对照 |
| market / package-manager | 本地声明层报告/应用/回滚 | 远端市场与云端平台按范围排除 |

以上边界是显式未完成项，不是“缺失能力”列表；产品化阶段按公开 API 逐项收敛。

## A. JS-CORDIS-ALIGNMENT.md 逐项

### A1. §1 Core

| 项 | JS / Go 参照 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| Component / Fiber / 生命周期调和 | `runtime/component.go`、`fiber.go`、`orchestrator.go`、`reconcile.go` | 语义等价、API 不同：强类型 `Component` + `Fiber` + 单一 Channel 决策域；不复制 JS 动态对象。 | `tests/Gocordis.Core.Tests/CoreSemanticsTests.cs` `DeclaredRequireAndProvideActivateBothFibers`；`tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs` `Thm64_Matrix`。 |
| 依赖注入 / 隔离 / 拦截 | `NewKey[T]`、`Require/Provide`、逐 key realm、`MetaKey` | 强类型 key + per-realm provider 表 + 派生式 MetaKey；不做 JS Proxy。 | `CoreSemanticsTests.RealmsIsolateTheSameKeyIntoIndependentProviders`、`MetaInterceptionIsRightBiasedAndDoesNotReload`；`AuditRegressionTests.IsolateCannotHijackAKeyOwnedByAnotherRealm`。 |
| Effect / 异步安装取消 | 槽位状态机 + install 屏障 + `ctx.Done()`；`IterComponent` | 严格顺序 LIFO；`EffectAsync` 与 `IIterativeComponent`；`CancellationToken` 即论文 divert 门控。 | `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`；`CoreSemanticsTests.DivertDuringActivationUnwindsWithoutFailure`；`TheoremMatrixTests.Thm68_Matrix`。 |
| Revision 原位替换 | `Fiber.Revise` 退役→Gone→重插（论文要求新 Fiber） | 不保身份：退役旧 fiber、装载新 fiber；无 `registry.replace()` 等价 API。 | `PluginSystemTests.ConfigChangeReplacesTheFiber`；`theorem-matrix.md` Thm80「revision endpoint equivalence → 接受差异」。 |
| Event 注册 | `OnWithHandle`/`OnOnce*`/`OnWaterfallWithHandle` + `EventBinding.Active` | Effect 拥有的注册，owner 撤销即注销；五模式派发在扩展层。 | `CoreSemanticsTests.EventHandlersAreRemovedWhenOwnerIsDisposed`；`ExtensionTests.EventModesCoverSerialBailParallelAndWaterfall`。 |
| Event 派发 | `Emit/Serial/Parallel/Bail` + `BailValue`（`OnValue` 短路） | 五模式齐备；`BailValue` 以第一个 present 结果短路；旧 `Bail` 保留 error fail-fast。 | `ExtensionTests.EventModesCoverSerialBailParallelAndWaterfall`；`EventDispatchGapTests.BailValueStopsAtFirstPresentResult`、`ParallelAggregatesErrorsInRegistrationOrder`。 |
| 运行时观察 | `Runtime.Snapshot` + EventSink + Observe | 只读 `Snapshot()` + `ObservationEquivalence`，忽略生成 id；永不持久化。 | `PaperConformanceTests.C13_ObservationEquivalenceIgnoresGenerativeFiberIds`；`AuditRegressionTests.SnapshotIsSafeUnderConcurrentLoadDispose`；`ExtensionTests.ObserveStreamDeliversSequencedItems`。 |

### A2. §2 官方扩展

| 项 | JS / Go 参照 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| `loader`、`include`、`hmr`、`group` | `extensions/loader`、`config`+`configwatch`、`hmr`、`bundle` | 能力等价、模块身份按论文 Revision（退役→重插）；组合/补丁由 `PluginRegistry` + `Tomlyn` manifest 实现。 | `PluginSystemTests.TomlManifestInheritsTheGoDeclarationShape`；`Phase6Tests.CompositionAndPatchesKeepDeclarationsDeterministic`、`ConfigWatcherCoalescesFileChangesIntoOneRevision`；`WasmRuntimeTests.WarmHotSwapReplacesTheModuleAndKeepsTheOldFiberGone`。 |
| 统一插件 manifest | `extensions/pluginmeta` + adapters | `PluginManifest` 仅描述性；`Requires/Provides` 不建运行时边，真实 typed key 边来自 `Inject/Provide`。 | `PluginSystemTests.UnresolvedTypedCapabilityIsRejectedEvenWhenManifestClaimsItExists`、`DependencyOrderComesFromTypedKeysNotManifestStrings`；`docs/ACCEPTED-DIFFERENCES.md` 末行。 |
| `logger-console` | `extensions/console/logstore` | 语义等价、API 不同：由 ConsoleHost + 观察流承担日志/快照输出，不移植 JS 日志 API 形状。 | `Phase6Tests.ConsoleExposesFrameworkQueries`、`WebUiStreamPushesObservationsUntilDisconnect`。 |
| `timer` | 五个 `*Owned` API + `TimeoutWait`/`Ticks`/`TimeoutContext` | JS 风格 Timeout/Interval/Debounce/Throttle + 确定性 pump；取消后不复活、Dispose 有界等待。 | `ExtensionTests.TimerSemanticsIncludeDebounceAndThrottle`；`ConcurrencyTests.CancelledRunningIntervalJobDoesNotResurrect`、`DisposeWaitsForRunningJobAndRejectsNewWork`。 |
| `fs` / `fs-node` / `fs-memory` | `extensions/fs/contract`、`local`、`memory` | 挂载路由 + 防逃逸；JS 专有选项 `copy.filter`/`dereference`、`readFile.encoding`、`open` flags/mode 明确不实现。 | `ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`；`FileSystemBoundaryTests.MountPrefixRequiresPathBoundary`、`IntermediateDirectorySymlinkCannotEscapeMount`；AD-19。 |
| `env` | compose-go dotenv 解析、4 文件优先级、栈式 Populate | 等价实现：按优先级解析并可通过 Effect 逆回滚。 | `ExtensionTests.EnvParsePrecedenceAndRollback`。 |
| `package-manager` | `x/mod/modfile`、原子 temp+rename、Report/Apply 同锁 | 接受差异的 .NET 定位：本地声明层报告/应用/回滚，不实现 npm Agent。 | `ExtensionTests.MarketAndPackageManagerVerifyAndRollBack`；AD-09。 |
| `http` | 拦截器链、身份代理、SOCKS、流式上传、`State()`、`RoundTrip` | 用 `HttpClient` 风格拦截器与 ASP.NET Core 传输；file:/data: URL 与有界关闭差异冻结。 | `ExtensionTests.HttpInterceptorsAndServerHostWorkTogether`；AD-12、AD-14。 |
| `http-socks` | socks4/4a/5/5h 真实拨号 | SOCKS5 CONNECT 已实现；socks4/4a 与 socks5 本地 DNS 语义不移植。 | `InfrastructureGapTests.SocksProxyRegistrationAndUndo`；`src/Gocordis.Extensions/HttpSocks.cs`。 |
| `server` | stdlib `ServeMux` catch-all、Handle/All/Use/Serve/ServeRange | ASP.NET Core `WebUiServer` 承担 catch-all 与状态/头语义。 | `Phase6Tests.WebUiServesQueriesOverHttp`、`WebUiUsesDataAndErrorEnvelopesWithBodyLimit`。 |
| `server-echo`/`static` | `http.FileServerFS` + `io/fs.FS` 适配器 | `web/console/dist` 由 C# WebUiServer 交付；TS 作为产品前端资产。 | `ProductGateTests.ExplorerDashboardConformance_*`；`migration-coverage.md` §7 6.9。 |
| `server-acl`/`proxy` | ACL loopback/点前缀/路由覆写；ReverseProxy WS 双向代理 | 监听安全门默认拒绝远程 + opt-in；代理路由由 ASP.NET Core 管线实现。 | `Phase6Tests.ListenGuardRejectsRemoteBindingByDefault`；`migration-coverage.md` §7 6.4–6.5。 |
| `cli` / `cli-cordis` | tokenizer（Windows 路径、引号、组合旗标、别名/help） | 命令文本 tokenizer；不引入 shell 参数 schema。 | `ExtensionTests.CliTokenizerAndAliasesWork`；AD-11。 |
| `create-cordis` | 空目录校验、回滚、生成项目 tidy/build/run | 本地静态模板 + 回滚；远程 registry tarball 不在范围。 | `migration-coverage.md` §9「官方扩展大套件」；AD-10。 |

### A3. §3 r1 整改记录

| 项 | JS / Go 整改 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| P1-01 static catch-all | Go 1.22 `ServeMux` + `PathValue` | 由 ASP.NET Core 路由承担，端点用 `{**name}` catch-all。 | `Phase6Tests.WebUiServesQueriesOverHttp`。 |
| P1-02 timer 生命周期 | 五个 `*Owned` API + 内部 `stopAndWait` | 取消后不复活；Dispose 等待在飞任务并拒绝新工作。 | `ConcurrencyTests.CancelledRunningIntervalJobDoesNotResurrect`、`DisposeWaitsForRunningJobAndRejectsNewWork`。 |
| P1-03 SOCKS 代理 | 带 ID 的 Service 注册，逆只撤销自身 | 不新增 C# SOCKS 后端；继承 AD-08 的 DNS 边界。 | AD-08；`docs/JS-CORDIS-ALIGNMENT.md` C# 迁移对齐补充「market / package-manager」以外行。 |
| P1-04/05 FS | 递归 Watch + Ready/Error/Dispose + doublestar 过滤；Provider 所有权 | `WatchService` 有界 revision + Dispose 幂等；挂载 Provider 生命周期由 Core Effect 拥有。 | `ExtensionTests.WatchServiceIncrementsRevisions`；`ConcurrencyTests.WatchServiceRevisionReadsAreSerializedWithWatchChanges`。 |
| FS Core 收敛 | 先写失败 conformance 再实现选项；`io/fs.FS` 适配器 | C# 不采用 afero 等价抽象；以挂载路由 + 路径边界测试固定能力。 | `FileSystemBoundaryTests.MountPrefixRequiresPathBoundary`、`IntermediateDirectorySymlinkCannotEscapeMount`；AD-18。 |
| P1-06 HTTP | 活动请求计数 + 有界优雅等待 + `RoundTrip` 统一所有权 | 用平台 HttpClient/ASP.NET Core 生命周期；有界关闭差异冻结。 | `ExtensionTests.HttpInterceptorsAndServerHostWorkTogether`；AD-12。 |
| P1-07 package-manager | `modfile` + 原子替换 + Report/Apply 同锁 | 同语义的本地报告/应用/回滚，不引入 npm Agent。 | `ExtensionTests.MarketAndPackageManagerVerifyAndRollBack`。 |
| CLI | tokenizer 区分 Windows 路径与引号内转义 | 沿用命令文本 tokenizer，测试固定现有行为。 | `ExtensionTests.CliTokenizerAndAliasesWork`。 |
| Event | `OnValue` + `BailValue` 补齐 JS 结果短路 | 五模式 + `BailValue`；parallel 聚合错误按注册序。 | `EventDispatchGapTests.BailValueStopsAtFirstPresentResult`、`ParallelAggregatesErrorsInRegistrationOrder`。 |

### A4. §4 明确保留的边界

| 项 | JS / Go 边界 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| 不复制 Proxy 动态属性访问 | `ctx.foo` / Reflect | 强类型 key，编译期发现错误。 | `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`；AD-02。 |
| 不复制 JS 特有的 Fiber 身份保留替换 | `registry.replace()` | 论文 Revision：退役→重插。 | `PluginSystemTests.ConfigChangeReplacesTheFiber`；AD-01。 |
| 不复制跨 Effect 并发撤销 | `Promise.all` 清理 | 严格顺序 LIFO。 | `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`；AD-03。 |
| loader/include/hmr/group 模块模型差异 | ES module 图 + 原位替换 | ALC/Wasmtime + rebuild/retire；能力覆盖、模块身份不对齐。 | `ManagedRuntimeTests.LoadsComponentFromAnIndependentAssembly`、`UnloadReclaimsTheCollectibleAssemblyContext`；AD-15。 |
| server-acl/proxy "需要验证" 已核实 | Go r1 逐条对齐 | C# 用监听安全门与 ASP.NET Core 管线覆盖同能力。 | `Phase6Tests.ListenGuardRejectsRemoteBindingByDefault`。 |
| FS JS 专有选项冻结 | copy.filter/dereference、encoding、open flags/mode | 不实现，字节切片由调用方解码。 | AD-19；`ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`。 |

### A5. §"C# 迁移对齐补充"表

| 原参照项 | C# 实现 | 决定 | C# 证据 |
| --- | --- | --- | --- |
| loader / include / hmr / group（模块模型差异） | `PluginRegistry` + `Gocordis.Runtime.Managed` + `WasmHotSwapper` | 能力等价；模块身份按论文 Revision。 | `PluginSystemTests.TomlManifestInheritsTheGoDeclarationShape`；`WasmRuntimeTests.WarmHotSwapReplacesTheModuleAndKeepsTheOldFiberGone`。 |
| Event emit/serial/parallel/bail/waterfall | `Gocordis.Core.Context.OnAsync/EmitAsync` | 只保留论文可锚定的可逆注册与串行 emit；五模式扩展派发无产品消费者，第二套注册权威已删除（ADR-0004）。 | `CoreSemanticsTests.CoreEmitDispatchesInRegistrationOrderAndThrowsTheFirstHandlerFailure`。 |
| timer / scheduler / watch | `TimerService`、`Scheduler`、`WatchService` | JS 风格 API + 确定性 pump + revision 文件监听。 | `ExtensionTests.TimerSemanticsIncludeDebounceAndThrottle`、`SchedulerAdvancesJobsDeterministically`、`WatchServiceIncrementsRevisions`。 |
| fs / http / server | `FileSystemService`、`HttpService`、`WebUiServer` | .NET 成熟库实现，能力覆盖不标缺失。 | `ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`、`HttpInterceptorsAndServerHostWorkTogether`；`Phase6Tests.WebUiServesQueriesOverHttp`。 |
| env / cli / broker / registry / observe | `EnvService`、`CliDispatcher`、`Broker<T>`、`StableRegistry<T>`、`ObserveStream<T>` | 对应 JS 官方扩展能力。 | `ExtensionTests.EnvParsePrecedenceAndRollback`、`CliTokenizerAndAliasesWork`、`BrokerAndRegistryManageMembers`、`ObserveStreamDeliversSequencedItems`。 |
| market / package-manager | `MarketService`、`PackageManager` | 仅本地声明层；市场服务端/云端平台按范围排除。 | `ExtensionTests.MarketAndPackageManagerVerifyAndRollBack`；`migration-coverage.md` §9。 |
| WASM 能力桥 | `WasmHostBridge`、`WasmCustomSections` | 继承 Go ABI 并补宿主桥（Go v0.1 缺失项）。 | `WasmRuntimeTests.HostCapabilityBridgeReceivesGuestCalls`、`ModuleTypeCustomSectionIsReadFromTheBinaryModule`。 |

## B. ACCEPTED-DIFFERENCES.md 主表逐项（AD-01–AD-22）

| 项 | JS 行为 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| AD-01 Fiber 身份保留的原位替换 | `registry.replace()` 保留 uid/config/外部引用 | 不移植；C# 以 retire→重插建新 fiber，论文优先。 | `PluginSystemTests.ConfigChangeReplacesTheFiber`；`theorem-matrix.md` Thm80 revision 行。 |
| AD-02 Proxy 动态属性访问 | `ctx.foo` / Reflect / accessor | 不移植；`Key<T>`/`EventKey<T>`/`MetaKey<,>` 强类型。 | `CoreSemanticsTests.UndeclaredRequireFailsTheFiber`。 |
| AD-03 跨 Effect 并发撤销 | 顶层 effect `Promise.all` 并发清理 | 不移植；严格顺序 LIFO。 | `PaperConformanceTests.C06_StrictLifoRecoveryLeavesNoResidue`；`TheoremMatrixTests.Thm68_Matrix`。 |
| AD-04 Effect 语法形式 | disposer/Iterable/Promise/AsyncIterable | `EffectAsync` + `IIterativeComponent` + `Cleanup` 委托；不引入 JS 语法糖。 | `CoreSemanticsTests.EffectsUnwindInStrictLifoOrder`；`TheoremMatrixTests.Thm68_Matrix`。 |
| AD-05 异步安装取消协议 | epoch + async generator `return()` | `CancellationToken`/`IsDiverted` 合作式取消，无 generator close 协议。 | `CoreSemanticsTests.DivertDuringActivationUnwindsWithoutFailure`；`AuditRegressionTests.SingleStepDivertStillRunsTheApplyCleanup`。 |
| AD-06 Event `Parallel` 同批移除 | 同步 map 启动保证移除即跳过 | goroutine 启动检查 Active 的等价语义；同批并发移除受调度影响，由测试固定。 | `EventDispatchGapTests.DispatchSkipsHandlerDisposedInsideSnapshot`、`ParallelAggregatesErrorsInRegistrationOrder`。 |
| AD-07 Event 旧 `Bail` | 值短路 | 保留 fail-fast-on-error；`BailValue` 承担值短路。 | `EventDispatchGapTests.BailValueStopsAtFirstPresentResult`、`BailValueContinuesWhenResultIsAbsent`。 |
| AD-08 `http-socks` DNS 语义 | socks5 本地 DNS、socks5h 远端 DNS | SOCKS5 CONNECT 已实现；socks4/4a 与本地 DNS 选项为接受差异。 | `InfrastructureGapTests.SocksProxyRegistrationAndUndo`；`src/Gocordis.Extensions/HttpSocks.cs`。 |
| AD-09 `package-manager` Agent | lockfile / install / restore | 本地声明层报告/应用/回滚，不解析 npm lockfile。 | `ExtensionTests.MarketAndPackageManagerVerifyAndRollBack`。 |
| AD-10 `create-cordis` 远程模板 | 下载 tarball、交互提示、git init | 不移植；本地静态模板 + 回滚。 | `migration-coverage.md` §9；`docs/migration/README.md`。 |
| AD-11 CLI 输入形态 | shell 风格完整 token/参数 schema | 命令文本 tokenizer（Windows 路径保留反斜杠）+ 组合旗标/引号。 | `ExtensionTests.CliTokenizerAndAliasesWork`。 |
| AD-12 HTTP 有界关闭 | 单线程取消后无活动回调 | 有界等待 + 拒绝新请求；不冒充完整 Join；幂等 Close 返回 nil。 | `ExtensionTests.HttpInterceptorsAndServerHostWorkTogether`；`PlatformConvergenceTests.PC30_CloseLeavesEveryFiberGone`（Core 层）。 |
| AD-13 普通拦截器适配流式响应 | fetch 拦截保持流 | 普通 Interceptor 可能缓冲；流式响应由扩展层专门处理。 | `ExtensionTests.HttpInterceptorsAndServerHostWorkTogether`。 |
| AD-14 HTTP `file:` / `data:` URL | 本地/内联资源拦截器 | 不实现；文件访问归 FS Service。 | `ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`。 |
| AD-15 loader / hmr 模块模型 | ES module 图 + 原位 Fiber 替换 | 静态程序集 + ALC/WASM retire→重插；模块身份不对齐。 | `ManagedRuntimeTests.LoadsComponentFromAnIndependentAssembly`；`WasmRuntimeTests.WarmHotSwapReplacesTheModuleAndKeepsTheOldFiberGone`。 |
| AD-16 WS 回调自关闭 | 回调内可安全 dispose 自身 | 不在回调内关闭自身注册；正常逆撤销保证 Gone 后无连接。 | `docs/ACCEPTED-DIFFERENCES.md` AD-16；`Phase6Tests.WebUiStreamPushesObservationsUntilDisconnect`。 |
| AD-17 `fs-conformance` 包 | JS 私有测试 workspace | 不建同名包；以 C# conformance/边界测试固定行为。 | `ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`、`FileSystemBoundaryTests.MountPrefixRequiresPathBoundary`。 |
| AD-18 采用 afero 作为统一 FS 后端 | 共享抽象后端 | 不采用；.NET 侧以 `FileSystemService` + Core Effect 所有权表达。 | `FileSystemBoundaryTests.MountPrefixRequiresPathBoundary`、`IntermediateDirectorySymlinkCannotEscapeMount`。 |
| AD-19 JS copy `filter`/`dereference`、readFile `encoding`、open flags/mode | 谓词过滤/解引用/编码字符串/打开标志 | 不实现；读写为字节切片，调用方自行解码。 | `ExtensionTests.FileSystemMountRoutesAndRejectsEscapes`；本文件 A2 `fs` 行。 |
| AD-20 Loader/WASM 资源协议 | — | 冻结；仅因可复现正确性缺陷重开。 | `WasmRuntimeTests.ModuleTypeCustomSectionIsReadFromTheBinaryModule`、`HostCapabilityBridgeReceivesGuestCalls`。 |
| AD-21 定理证据体系 | — | 不扩张；新增能力都在 Extension 层。 | `TheoremMatrixTests`（5×3 矩阵）；`paper-coverage-matrix.md`（40 项状态）。 |
| AD-22 统一插件 manifest | JS manifest 可承载装配语义 | `PluginManifest` 描述性；真实 typed 边来自 `Inject/Provide`，不接管生命周期。 | `PluginSystemTests.UnresolvedTypedCapabilityIsRejectedEvenWhenManifestClaimsItExists`、`EnabledSwitchLoadsAndDisposesThroughCoreOnly`。 |

## C. ACCEPTED-DIFFERENCES.md "已消除的差异"逐项

| 项 | Go 已消除内容 | C# 决定 | C# 证据 |
| --- | --- | --- | --- |
| Timer 调用方生命周期 | `*Owned` API + `TimeoutWait`/`Ticks`/`TimeoutContext` | 等价取消语义：注册即 Effect，Gone 后无回调。 | `ConcurrencyTests.CancelledRunningIntervalJobDoesNotResurrect`。 |
| FS 操作面 | Range/Stream/Lstat/Rename/Access/Symlink + 递归 Watch | 挂载路由 + 链接边界 + Watch revision；按 .NET 能力实现。 | `FileSystemBoundaryTests.IntermediateDirectorySymlinkCannotEscapeMount`；`ExtensionTests.WatchServiceIncrementsRevisions`。 |
| HTTP | StreamResponse/Decoder/BaseURL/Params/Extend + WS 客户端 | 由 `HttpService`/ASP.NET Core 管线承担；流式边界由 AD-13 固定。 | `ExtensionTests.HttpInterceptorsAndServerHostWorkTogether`。 |
| Server | catch-all 路由 + WebSocket + RouteCheck | ASP.NET Core catch-all + SSE/WS 端点。 | `Phase6Tests.WebUiServesQueriesOverHttp`、`WebUiStreamPushesObservationsUntilDisconnect`。 |
| Server ACL | loopback/点前缀/空列表/路由覆写/404 拒绝 | 监听安全门默认 loopback，远程需 opt-in。 | `Phase6Tests.ListenGuardRejectsRemoteBindingByDefault`。 |
| Server Proxy | 路径/查询/状态透传、头过滤、双向 WS 代理 | ASP.NET Core 管线 + Core 生命周期；不逐头逐字节对齐 JS。 | `Phase6Tests.WebUiUsesDataAndErrorEnvelopesWithBodyLimit`；AD-22。 |
| Event 值短路 | `OnValue`/`BailValue` | `BailValue` 以第一个 present 结果短路。 | `EventDispatchGapTests.BailValueStopsAtFirstPresentResult`、`BailValueSkipsHandlerDisposedInsideSnapshot`。 |

## 决策原则

`接受差异` 不是"未实现"的同义词：它表示 C# 有意不照搬 JS/Go 的某形状，且论文或 .NET 平台理由已记录。
`待补`（如 Thm43 任意逆序、C20 target/quiet 形式定义）必须保持未覆盖状态，见
`paper-coverage-matrix.md`；不得在本文件中被改写成"已对齐"。
