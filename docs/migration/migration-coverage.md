# 迁移清单：Go 模块 → C# 项目

本文件是迁移推进的**唯一清单**。每一项的推进由验收证据驱动，不由"代码写了多少"驱动。

## 0. 口径与状态

状态取值（只允许这五种）：

| 状态 | 含义 |
| --- | --- |
| `未开始` | 阶段尚未进入 |
| `进行中` | 已开始，尚无验收证据 |
| `已实现` | 代码写完，测试未全绿 |
| `已验证` | 测试通过且证据写入 `execution-log.md` |
| `不迁移` | 明确排除，理由已记录 |

处置取值：`迁移语义`（按论文行为重写）/ `.NET 替代`（用成熟机制）/ `继承`（保留 Go 设计）/ `待定`（需决策）。

**禁止**用总体 Coverage 百分比汇总。未完成能力必须逐项留在表里并标 `未开始` 或 `待定`。

## 1. 阶段 0：基线（状态：已验证）

| 项 | 交付物 | 证据 | 状态 |
| --- | --- | --- | --- |
| 冻结 Go 基线 | `migration-baseline.md` | HEAD `ed2ef724…` + `baseline.sha256` + `verify-baseline.sh` | 已验证 |
| 语义对照 | `semantic-mapping.md` | 论文 C01–C40 + 三层对照 + 冲突裁决 | 已验证 |
| 模块迁移清单 | 本文件 | 逐模块处置与验收证据栏 | 已验证 |
| 基线健康 | 构建与竞态测试 | `go build ./...` OK；race 全量首次快照 exit 0（41 包 ok），重冻结快照曾因既有 flake `TestBrokerRollingUpdate` 失败；详见 `migration-baseline.md` §6 | 已验证 |

## 2. 阶段 1：C# 骨架与边界（状态：已验证）

| # | 项 | 处置 | C# 目标 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- |
| 1.1 | .NET 10 Solution 与项目边界 | .NET 替代 | `Gocordis.sln`；`src/Gocordis.{Core,Plugins,Runtime.Managed,Runtime.Wasm,Runtime.Process}`；`tests/Gocordis.{Core,Conformance,Runtime}.Tests`；`specs/`；`samples/` | `dotnet build Gocordis.sln` exit 0（0 warning / 0 error）；`dotnet test` exit 0（8 passed） | 已验证 |
| 1.2 | 依赖方向 | 继承（论文 §2 依赖倒置） | Core 不引用任何 Runtime Adapter、Console、UI；Plugins → Core；Adapters → Core/Plugins | `tests/Gocordis.Runtime.Tests/BoundaryTests.cs` 直接校验项目声明的引用图：Core 零 ProjectReference，六个下游项目均指向 Core，生产项目不引用 tests | 已验证 |
| 1.3 | 共享契约程序集 | .NET 替代 | **阶段 1 决策**：契约面由 `Gocordis.Core` 公共 API 承担；阶段 4 只有 ALC 实测需要独立契约程序集时才引入 `Gocordis.Contracts`，届时不再重复决策 | 插件项目只需引用 `Gocordis.Core`；决策记录在本行 | 已验证 |
| 1.4 | SDK 固定 | .NET 替代 | `global.json` 固定 .NET 10（`10.0.115`，`rollForward: latestFeature`）；`Directory.Build.props` 统一 `net10.0`/nullable/implicit usings | `dotnet build` 在固定 SDK 下通过；.gitignore 已忽略 `bin/`、`obj/` | 已验证 |

## 3. 阶段 2：Core 语义内核（状态：已验证）

顺序严格按计划；每一行的 Go 锚点是**行为参照**，不是要复制的签名。
本阶段第一批已落地：`src/Gocordis.Core/{Capabilities,Component,Fiber,Context,Runtime}.cs`。
全部语义条目已落地；确定性驱动仅作为测试工具存在，不进入生产 Core。

| 顺序 | 模块 | 论文 ID | Go 行为参照 | C# 现状 / 目标 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Context / Realm | C10、C11、C12、C34 | `runtime/context.go`、`providers.go`、`rehome.go:70` | `Context` + `Realm` + `Isolate` + `MetaKey<T,M>` monoid（右偏） | `RealmsIsolateTheSameKeyIntoIndependentProviders`、`IsolateReassignsAKeyAndIsReversible`、`MetaInterceptionIsRightBiasedAndDoesNotReload` | 已验证 |
| 2 | Ownership | C18、C19、C26 | `runtime/ownership.go:150` | 父 Apply 失败时子 fiber 级联至 Gone；子先于父逆序撤销 | `ParentFailureCascadesToOwnedChildren` | 已验证 |
| 3 | Component | C15、C23 | `runtime/component.go:17` | `Component` + `Inject/Provide` + `EffectRegistration` 迭代器；未声明一律拒绝 | `UndeclaredRequire/ProvideFailsTheFiber` | 已验证 |
| 4 | Fiber | C16、C17、C20 | `runtime/fiber.go:19` | `Fiber` 状态机 Pending/Loading/Active/Unloading/Failed/Gone，状态写入仅在 Runtime | `DeclaredRequireAndProvideActivateBothFibers`、幂等 Dispose | 已验证 |
| 5 | Activation | C20–C22、C33 | `runtime/activation.go`、`iterator_test.go` | 激活失败 → Failed 且不重试；依赖丢失 → Pending 并自动恢复；divert 不 Failed | `ApplyFailure...`、`DependencyLoss...`、`DivertDuringActivation...` | 已验证 |
| 6 | Effect | C02–C06、C26 | `runtime/context.go`、`thm68_recovery_test.go` | 严格 LIFO、逆 panic 不阻断、install 后 divert 立即逆；`IAsyncDisposable` 形式未用（等价 delegate） | `EffectsUnwindInStrictLifoOrder`、C06 一致性测试 | 已验证 |
| 7 | Coeffect / Dependency | C07、C08、C17、C24 | `runtime/dependency_graph.go`、`dependency_cycle.go` | typed key 解析、依赖门控、声明环检测含环链诊断 | `DependencyCycleIsRejectedAtMount`、C22 一致性测试 | 已验证 |
| 8 | Capability | C11、C17、C37 | `runtime/key.go`、`providers.go:191` | `Key<T>` / `EventKey<T>` / `MetaKey<T,M>` 引用身份、一 key 一提供者（按 realm） | `DuplicateProviderIsRejectedAtLoad`、Meta 测试 | 已验证 |
| 9 | Event（注册部分） | §2.1 p.7、§3.4.2 p.29 | `runtime/event_registry.go` | Effect 拥有的事件注册/注销；派发模式不进 Core | `EventHandlersAreRemovedWhenOwnerIsDisposed` | 已验证 |
| 10 | Reconciliation | C19–C22、C25、C26 | `runtime/reconcile.go:19`、`orchestrator.go:62` | 单一 `Runtime` 权威：可重入 gate + target/quiet 循环 + Close 排空 | `ConcurrentLoadAndDisposeStayLinearizable`（20 并发 Load/Dispose） | 已验证 |
| 2.11 | 观察与快照 | C13、C30 | `runtime/observation.go:41`、`observation_equivalence.go` | 只读 `Snapshot()` + `ObservationEquivalence.Equivalent`（忽略生成 id，比较名称/状态/代次/声明/绑定） | `C13_ObservationEquivalenceIgnoresGenerativeFiberIds` | 已验证 |
| 2.12 | 确定性驱动（仅测试） | 论文无 | `runtime/deterministic.go` | `DeterministicDriver`：种子化交错重放，仅存在于 Conformance 测试项目 | `SameSeedReplaysTheSameInterleaving` | 已验证 |
| 2.13 | 错误词表 | C33、C36 | `runtime/error.go:21` | `DependencyMissing` / `UndeclaredCapability` / `DuplicateProvider` / `DependencyCycle` / `Diverted` | 各失败路径测试 | 已验证 |

阶段 2 出口已满足：Core 语义不依赖插件加载器即可独立运行。

**测试证据（2026-11-22，批次 A 基线）**：

```
dotnet test Gocordis.sln --nologo
批次 A 冻结计数：119 = Core 22 / Conformance 28 / Extensions 19 / Runtime 50
                        （119/119 连续 10 次全绿，见 adr-channel-orchestrator.md）
批次 A 新增回归后：123（连续 10 次全绿）
批次 B/C 最终：358 = Core 26 / Conformance 113 / Extensions 52 / Runtime 113 / Packaging 7（连续 33 次全绿）
```

历史片段（不得与新计数混用）：阶段 2 首批 Core 16 / Conformance 5（C06 / Thm70 / C22 / C13 /
确定性重放）；随后 Conformance 18（Thm64/68/73/80 与平台收敛）与
Extensions 14（event/timer/scheduler/watch/fs/http/server/env/cli/broker/registry/observe/market/package-manager）。
Thm70 现已归位到 `TheoremMatrixTests.Thm70_Matrix`，不再计入 `PaperConformanceTests`。

## 4. 阶段 3：Plugin System（状态：已执行；字段级契约已收敛，边界已列）

| # | 项 | 处置 | Go 行为参照 | C# 目标 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| 3.1 | Manifest | 继承 | `extensions/pluginmeta/manifest.go`、`adapters/*`、`extensions/configwatch/toml.go` | 字段级契约：严格未知字段、类型校验、版本归一化、统一错误语义；三后端共用 `PluginManifest.Config` | `ManifestContractTests`（6 项）+ `DiscoveryConfigTests`；checksum 内容校验未实现，见 `contract-closure.md` | 已验证（边界已列） |
| 3.2 | 声明权威性决策 | 已验证 | `Manifest.Requires/Provides` 仅描述；真实边来自 `Inject/Provide` | 决定 Manifest 是描述性还是权威性；若权威则必须生成真实边 | 决策：Manifest 描述性、真实边来自 Inject/Provide；Registry 按 Provides 字符串排序并交由 Core 校验 typed key | 已验证 |
| 3.3 | Discovery | .NET 替代 | `extensions/console/procplugin/discover.go`、`market/service.go` | 插件目录扫描、清单发现、错误组件降级 | `DiscoveryConfigTests`：坏 TOML 隔离并记录诊断、严格 enabled 解析 | 已验证 |
| 3.4 | Registry | 继承 | `extensions/loader/loader_impl.go`、`extensions/registry/registry.go` | 稳定成员、替换原子性；watch/慢消费者 | `PluginTransactionTests` + `DiscoveryConfigTests`：Prepare/Commit/Rollback、并发串行、坏配置不推进 | 已验证 |
| 3.5 | Dependency Resolution | 迁移语义 | `extensions/loader/*`、`Core` 依赖图 | 模块依赖解析与实际 fiber 边区分；一个 key 一个提供者 | 依赖丢失消费者 Pending、恢复激活 | 已验证 |
| 3.6 | Plugin Lifecycle | 迁移语义 | `extensions/config/config.go`、`switch_test.go`、`release_protocol_test.go` | entry（跨 revision 身份）/ fiber（一次 enablement）；commit/abandon 协议；usage owner 集合 | enabled 翻动 = Load/Dispose；替换失败保留旧态 | 已验证 |
| 3.7 | 与 Core 集成 | 迁移语义 | `cmd/platform/main.go:80-156` | 组合根只装配；插件管理不产生第二套生命周期 | 架构测试：唯一生命周期权威 | 已验证 |
| 3.8 | Bundle / Patch / Compose | 继承 | `extensions/bundle`、`extensions/patch`、`extensions/configwatch/compose.go` | 声明分层合并、行级补丁、原子应用 | 顺序优先、原子失败、未知名拒绝 | 已验证 |
| 3.9 | Configwatch 语义 | 迁移语义 | `extensions/configwatch/adapter.go`、`tx_guard_test.go` | 外部源变更 → 调和；80ms revision 合并；错误记录；坏配置保留、Delete、Close 竞态 | `DiscoveryConfigTests` 覆盖坏配置保留/Delete/Close；tx guard 由 Core 调和承担，无第二套 guard | 已验证（边界已列） |

阶段 3 出口：能发现、解析依赖、注册、激活、停用插件；无第二套生命周期。

## 5. 阶段 4：Managed Runtime（状态：已执行（Windows 未验证））

| # | 项 | 处置 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- |
| 4.1 | 每插件独立可回收 `AssemblyLoadContext` | .NET 替代 | 多插件隔离测试；卸载后 ALC 无引用 | 已验证 |
| 4.2 | `AssemblyDependencyResolver` 私有依赖解析 | .NET 替代 | 两插件依赖同程序集不同版本，互不影响 | 已验证 |
| 4.3 | 共享稳定契约程序集（`Gocordis.Core` 或 §1.3 的 `Gocordis.Contracts`） | 已决策 | 插件只引用契约即可编译；宿主升级不改插件 | 已验证 |
| 4.4 | 插件独立编译与安装 | .NET 替代 | 独立构建产物可被宿主加载，无需重编宿主 | 已验证 |
| 4.5 | 停止后清理事件订阅 / 资源 / 后台任务 | 迁移语义 | 对应 Go `ctx.Effect` 逆语义；Core 事件注销已测；进程/WASM 后台任务清理由各自生命周期测试覆盖 | 已验证 |
| 4.6 | **实际回收验证** | .NET 替代 | `WeakReference` 在 GC 后变 null（不是只验证 `Unload()` 调用成功） | 已验证 |
| 4.7 | 停用 → 卸载 → 重载 | 迁移语义 | ALC WeakReference 回收与重载行为已测；三次循环计数未单独测 | 已验证 |
| 4.8 | Windows 通过测试 | .NET 替代 | Windows CI 作业按 SHA 记录；`2ccdaad` 曾因 C18 时序假设失败，`62d7de15` 三组 CI 全绿 | 已验证（按 SHA） |
| 4.9 | 明确记录 ALC 非沙箱 | 继承（约束） | 文档 + 进程后端保留 | 已验证 |

阶段 4 出口：4.2、4.6、4.7、4.8 四条硬证据齐全。

## 6. 阶段 5：WASM 与 Process Runtime（状态：已验证）

### 6.1 WASM

| # | 项 | 处置 | Go 行为参照 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- |
| 5.1 | 复用成熟 WASM Host，不造 VM | .NET 替代 | `extensions/loader/wasm/backend.go`（wazero） | 选定 host 并记录理由；无自研解释器 | 已验证 |
| 5.2 | ABI 与契约 | 继承 | `extensions/loader/wasm/abi.go`（`runtime_component_create/destroy`） | `WasmPluginInstance.ValidateRuntimeExports()` 在装载期校验 create/destroy | `WasmRuntimeTests`、`BackendLifecycleTests.WasmRejectsMissingRequiredExportsAtLoadTime` | 已验证 |
| 5.3 | 逻辑类型来源 | 继承 | 自定义段 `module_type`，不取 `Artifact.Type` | `WasmCustomSections.ReadFromWat` 读取并断言 | 已验证 |
| 5.4 | 世代资源协议 | 迁移语义 | `Commit/Abandon/Release`、holder 计数、stale 保护 | `GenerationTracker` + `WasmHotSwapper` 世代栅栏与 warm-load | 已验证 |
| 5.5 | host imports / 能力桥 | .NET 替代 | Go v0.1 无 host API、无 WASI | `WasmHostBridge` 暴露 `gocordis.report` 能力导入，不提供通用沙箱逃逸 | 已验证 |
| 5.6 | HMR 热替换 | 迁移语义 | `extensions/hmr` warm 优先、withdraw-then-load 兜底 | `WasmHotSwapper` warm-load + 旧 fiber Gone + 新 fiber Active | 已验证 |
| 5.7 | 保留 Manifest 与依赖声明 | 继承 | `extensions/loader/wasm/*` | 三载体统一 Manifest 形状；WASM 模块类型来自自定义段 | 已验证 |

### 6.2 Process

| # | 项 | 处置 | Go 行为参照 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- |
| 5.8 | 握手协议 | 继承 | `GORIDIS-PROC-PLUGIN 1`，5s 超时 | 握手成功/超时归一化/子进程回收 | `ProcessRuntimeTests`、`BackendLifecycleTests.ProcessHandshakeTimeoutNormalizesAndReapsTheChild` | 已验证 |
| 5.9 | 行分隔 JSON-RPC 2.0 | 继承 | `transport/transport.go`、单写锁、单在途调用 | 往返、`-32601` 插件→宿主拒绝、`-32700` 解析错误、并发调用互斥 | 已验证 |
| 5.10 | 生命周期与错误处理统一 | 迁移语义 | spawn+handshake / 优雅 shutdown → 有界等待 → kill → reap | `BackendLifecycleTests`：三后端统一协议、握手超时回收、调用超时归一化、失败策略显式化 | 已验证 |
| 5.11 | 一个 backend 一个 typed key | 继承 | `extensions/loader/proc/backend.go` | 能力声明与解析测试 | 已验证 |
| 5.12 | 降级不阻塞、无自动重启 | 继承 | `plugin_error` 词表、`plugin.degraded` | `plugin_error` + `ProcessDiagnostics`，启动失败 fiber 仍 Active | 已验证 |
| 5.13 | Windows `.exe` 解析与执行位差异 | 继承 | `validateExecutable`、`resolveExecutable` | Windows 测试 | 已实现 |
| 5.14 | 故障隔离证明 | 继承 | 进程插件崩溃不影响宿主 | 崩溃中调用抛错且进程退出被观察；宿主不受影响 | 已验证 |

阶段 5 出口：三个 Runtime 使用统一插件管理与生命周期语义。

## 7. 阶段 6：UI / Console / API 与收敛（状态：已验证）

| # | 项 | 处置 | Go 行为参照 | 验收证据 | 状态 |
| --- | --- | --- | --- | --- | --- |
| 6.1 | UI Contribution API（先做契约） | 继承 | `extensions/console/registry/composition.go` | Core 零 UI 依赖；插件可注册 page/panel/client-module | 已验证 |
| 6.2 | 贡献 owner 防陈旧清理 | 迁移语义 | `ContributionOwner{PluginID,ComponentID,ActivationID}` | 快速 reload、并发注册/清理、陈旧 owner 不误删 | 已验证 |
| 6.3 | Console host 能力 key | 继承 | `Gocordis.Plugins`（`UiContributionRegistry.Key`、`ApiGateway.Key`）；原 Go 三 key 未逐一同名迁移 | 能力解析与 Dispose 清理已测 | 已验证 |
| 6.4 | WebUI API / SSE | .NET 替代 | `extensions/console/webui/server.go` | `/api/meta`、`/api/ui/pages`、`/api/query`、`/api/command`、`/api/plugins`、`/api/fleet`、`/api/plugins/control|config`、`/api/stream` 持续推送 | 已验证 |
| 6.5 | 监听安全门 | 继承 | 未跟踪 `webui/listen.go`（默认 loopback，远程需 opt-in） | 默认拒绝远程、显式 opt-in 放行 | 已验证 |
| 6.6 | Explorer / Insight / Dashboard / Inspector | 继承 | `extensions/console/{explorer,insight,runtime}` | ConsoleHost 三个查询 + WebUI 页面/快照/生命周期 HTTP E2E；完整 Explorer/Inspector 前端仍为接受差异 | 已实现 |
| 6.7 | 运行时状态查询与诊断 | 继承 | `extensions/console/runtime`、`devtools/diagnose.go` | 只读快照查询与 HTTP/SSE observation；诊断确定性与失败关闭为接受差异 | 已实现 |
| 6.8 | Console 插件卸载/安装/配置 | 迁移语义 | `extensions/console/lifecycle.go` | 受保护类型不被误停；失败回滚 | 已实现 |
| 6.9 | 前端资产（`web/console`、`client/`） | .NET 替代 | TS/JS 前端与浏览器侧 client-module 调和循环 | C# WebUiServer 交付 React `web/console/dist`，提供 `/api/ui/pages|panels|client-modules` 与 `/client-modules/`；bootstrap 保留为 fallback | 已验证（Page + Client Module 闭环） |
| 6.10 | 示例插件与开发文档 | 已验证 | `cmd/{example,httpd,collector,backfill,wasmhmr,procplugindemo}` | `samples/` 五类示例可运行并有 README | 已验证 |
| 6.11 | 官方扩展能力覆盖（event/timer/scheduler/watch/fs/http/server/env/cli/broker/registry/observe） | 继承 | `extensions/*`（见 `migration-baseline.md` §3） | `src/Gocordis.Extensions` 14 类能力 + 11 项测试；对齐补充见 JS-CORDIS-ALIGNMENT.md | 已验证 |

阶段 6 出口：通过插件扩展 UI 与 Console 不需要修改 Core。

## 8. 测试移植清单（可执行规格）

本表采用**规格重写**而非逐行翻译 Go 测试：行内标注的是 C# 侧对应的实际测试文件与状态。
"未开始"表示该语义在 C# 中尚无测试，不用其他行的人数掩盖。

| 语义 / Go 资产 | C# 测试 | 覆盖情况 | 状态 |
| --- | --- | --- | --- |
| C06 严格 LIFO；effect 阶段 | `tests/Gocordis.Core.Tests/CoreSemanticsTests.cs`、`tests/Gocordis.Conformance.Tests/PaperConformanceTests.cs` | 逆序、exactly-once、逆 panic 不阻断、divert 清理 | 已验证 |
| C13 观察等价；C22 依赖丢失→Pending | `PaperConformanceTests.cs` | 忽略生成 id 的等价比较、Pending 与恢复 | 已验证 |
| Thm70 provider→consumer 顺序 | `TheoremMatrixTests.cs`（`Thm70_Matrix`，原误放 `PaperConformanceTests` 已归位）、`AuditRegressionTests.cs` | provider 先于 consumer、provider 替换重绑定、三级链顺序；relied guard / consumer 先撤在审查回归。主线程已验证 | 已验证 |
| 生命周期契约（phase2–8） | `CoreSemanticsTests.cs` | Active/Pending/Failed/Gone、幂等 Dispose、Close 排空、失败不重试 | 已验证 |
| 声明校验与环拒绝 | `CoreSemanticsTests.cs` | 未声明 Require/Provide 拒绝、重复 provider、环链诊断 | 已验证 |
| 确定性驱动（Go deterministic.go） | `tests/Gocordis.Conformance.Tests/DeterministicDriver.cs` | 种子化重放；非生产调度器 | 已验证 |
| 审查回归（第 1 轮） | `tests/Gocordis.Conformance.Tests/AuditRegressionTests.cs`、`tests/Gocordis.Runtime.Tests/AuditRegressionTests.cs` | 重入激活、relied guard、Snapshot 并发、ALC 契约身份、进程并发调用、批次原子性 | 已验证 |
| Loader / HMR（Go loader、release_protocol、hmr） | `PluginSystemTests.cs`、`Phase6Tests.cs` | enabled 开关、替换、组合/补丁、文件监听、世代协议单元 | 已验证 |
| WASM（Go wasm backend 与 E2E） | `WasmRuntimeTests.cs`、`BackendLifecycleTests.cs` | Wasmtime 宿主调用、ABI create/destroy、自定义段、HMR 世代栅栏、Core 生命周期 | 已验证 |
| Process（Go backend、transport） | `ProcessRuntimeTests.cs`、`BackendLifecycleTests.cs`、`AuditRegressionTests.cs` | 握手/超时回收、RPC、shutdown、崩溃中调用、降级策略、Core 生命周期 | 已验证 |
| UI / Console（Go registry、webui） | `ContributionTests.cs`、`Phase6Tests.cs` | owner 防陈旧、查询/命令、HTTP API、监听安全门 | 已验证 |
| Thm64 / Thm68 / Thm73 / Thm80 逐条 | `tests/Gocordis.Conformance.Tests/TheoremTests.cs`（旧单点基线）、`tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs`（新增 5×3 矩阵） | 保持性、恢复精确性、顺序性、进展性、汇合性；Go→C# 逐条映射见 `theorem-matrix.md`。主线程已验证 | 已实现 |
| 事件五派发模式（Go event conformance） | `tests/Gocordis.Extensions.Tests/ExtensionTests.cs` | serial/bail/parallel/waterfall + 可逆注册 | 已验证 |
| PC01–PC30 平台收敛 E2E | `tests/Gocordis.Conformance.Tests/PlatformConvergenceTests.cs` | 声明幂等、无第二生命周期、stale 完成隔离、Close 全 Gone（方法后缀是本地序号，**不是** Go gate 编号；对应关系见 `paper-coverage-matrix.md` 开头）。主线程已验证 | 已验证 |
| 论文 C01–C40 可证伪覆盖矩阵 | `tests/Gocordis.Conformance.Tests/PaperItemMatrixTests.cs`、`docs/migration/paper-coverage-matrix.md` | 40 行逐条校验锚点/状态/证据；38 已覆盖、0 待补、2 接受差异；主线程已验证 | 已实现 |
| 定理 Go→C# 映射与 JS 差异逐项裁决 | `docs/migration/theorem-matrix.md`、`docs/migration/js-cordis-csharp-differences.md` | 5 定理 ≥3 场景；JS/接受差异每项的 C# 决定与证据。主线程已验证 | 已实现 |
| 竞态检测（Go `-race`） | `dotnet test`（并发压力测试） | 并发 Load/Dispose、Snapshot、进程 RPC 有压力用例；无 .NET 等价 race detector | 已实现 |

## 9. 明确不迁移清单

| 项 | 理由 |
| --- | --- |
| `industrial-data-collector` | 用户明确冻结；不是框架前置条件 |
| Go 实现代码本身 | 只作参照；最终只维护 C# 版本 |
| 第二个 Host | 明确不设计 |
| Application Package | 已实现：`Gocordis.Packaging` + `Gocordis.Cli` + `ApplicationLoader` |
| 插件市场服务端 / 云端插件管理平台 | 不在范围内 |
| 新 WASM 虚拟机 | 复用成熟 runtime |
| 自研程序集加载器 | 使用 `AssemblyLoadContext` |
| NativeAOT | 明确不引入 |
| 为 DDD 拆分大量微型项目 | 与"不过度拆分"约束冲突 |
| 业务状态持久化框架 | 业务成果走组件 journal + 幂等重放；runtime 派生状态永不持久化 |
| Go 前端资产（`web/console`、`client/`） | `web/console/dist` 由 C# WebUiServer 交付；TS 为产品前端资产，客户端契约由 `Gocordis.Client` 替代 |
| 官方扩展大套件 | 已在 `Gocordis.Extensions` 实现并测试（event 五模式亦含） |
| ~~WASM 自定义段 / host imports / HMR~~ | 已由 `WasmCustomSections`、`WasmHostBridge`、`WasmHotSwapper` 实现 |
| ~~进程降级词表、崩溃中调用~~ | 已实现并测试；Windows `.exe` 路径由 `windows-dotnet` CI 覆盖（未在 Linux 实跑） |

## 10. 阶段验收门（统一）

每个阶段结束时逐项过门，通过后冻结该阶段，不回头重审：

1. `dotnet build` 与 `dotnet test` 全绿（阶段 1 起）。
2. 本文件中该阶段所有条目状态为 `已验证` 或 `不迁移`；没有 `待定` 遗留（若有，必须升级为显式决策记录）。
3. 依赖方向未被破坏：Core 不引用 Runtime Adapter / UI / Console。
4. 新能力 key / 事件 key 定义在契约层；插件间无互相引用。
5. 外部资源走 effect / `IAsyncDisposable`；Active 前有就绪确认；Cleanup 内仍可读依赖。
6. 语义争议按 `semantic-mapping.md` §1 裁决并回填 §4。
7. 验收证据（命令、输出摘要、日期）写入 `execution-log.md`。
