# 验收基线（有限校准版）

本文件是 Task 01 的**有限校准**结果，不重复 C01–C40 的逐行账本；逐行状态与测试名仍以
[`paper-coverage-matrix.md`](paper-coverage-matrix.md) 为准，该文件由 `PaperItemMatrixTests`
机器校验，不允许随意改状态。

- 用户指定基线：`dc2ad429b16570c7bfcc5d854ad048847d323283`。
- 本次实施起点：`5795678`（在指定基线之上已落地统一跨后端能力契约）。
- 校准原则：**测试映射 ≠ 语义完整实现**；模拟/标签测试 ≠ 真实后端执行。

## 三个独立验收域（不得混算）

| 域 | 内容 | 主要证据 | 不能替代什么 |
| --- | --- | --- | --- |
| 论文语义 | C01–C40 | `PaperConformanceTests`、`PaperGapClosureTests`、`TheoremMatrixTests` | 不能代替平台能力或真实后端 |
| 平台迁移 | Go → C# 的 Registry/Store/CLI/UI/Event/Config 等 | 产品测试与 packaging 测试 | 论文条目不覆盖这些工程能力 |
| 产品闭环 | 包 → 安装 → 加载 → 组合 → 运行 → 撤回 → 恢复 → 卸载 | Showcase 脚本 + 产品 E2E | 单一单元测试不构成闭环 |

## 证据等级校准

- C02/C03/C04/C05：原账本给 `UNVERIFIED/PARTIAL`，但 `PaperGapClosureTests` 已有定向行为断言
  （左逆、状态/逆不变量、组合结合律、Delta↔Gamma 见证）。校准为 **PARTIAL→E2**；仍不是定理级证明。
- C29/C30：`Thm73_Matrix` / `Thm80_Matrix` 各三场景之外，`TheoremPremiseTests` 已补上前提级断言
  （有限名字、无环优先、有限可取消迭代、installing 中 quiescent）和结论级有界穷举
  （24 种装载序唯一正规形）。校准为 **E3（前提与结论均可执行覆盖，仍是有限状态空间）**。
  为此修正 `ObservationEquivalence`：Generation 是重试点数，不属于 Def 33 的可观察键级状态。
- C39：旧证据 `C39_BackendNeutralFactoriesComposeThroughTheCoreContract` 只验证工厂标签，
  **E1/E2，不构成真实 IPC/WASM 互操作**。在 `5795678` 之后，
  `CrossBackendCapabilityTests` 提供真实 Wasmtime 执行与真实子进程 JSON-RPC 的 E2/E3 证据，
  校准为 **PARTIAL→E2/E3**；仍需产品级 E4。
- C38：`ACCEPTED`，`VersionedKey<T>` + NuGet 范围，边界不变。
- 其余 C 项沿用现有矩阵的 E2 映射；本文件不把 E2 冒充 E3/E4。

## P0 缺口状态（以 `5795678` 为界）

| ID | 状态 | 证据 / 剩余工作 |
| --- | --- | --- |
| P0-01 跨后端 Capability Contract | 已关闭 | `CapabilityContract.cs`；`CapabilityDeclarationTests`；`CrossBackendCapabilityTests` |
| P0-02 Backend Require/Provide | 已关闭 | `CrossBackendCapabilityTests`：Managed↔WASM、Managed↔Process、Process↔WASM 双向真实调用（Wasmtime 执行 + 子进程 JSON-RPC） |
| P0-03 Backend Withdrawal | 已关闭 | 三后端组合撤回 → Pending；`WasmTrapFailsTheProviderWhileWithdrawalOnlyPendsTheConsumer` 区分 Trap(Failed) 与撤回(Pending) |
| P0-04 Package Backend Closure | 已关闭 | `PluginPackageWriter` 接受 wasm/process 入口校验；`BackendPackageValidationTests` + `BackendDispatchProductTests.Declared*CapabilityPackage*` 覆盖 pack→install→load→invoke→stop |

## Task 04 / Task 05 收敛结果

- **事件事实来源**：`Gocordis.Extensions.EventDispatcher`（测试续命的第二套注册字典）已删除；Core 的 `Context.OnAsync/EmitAsync` 是唯一注册与派发权威；Core 新增注册顺序与首个 handler 失败的行为测试。
- **Registry 指纹**：`Capabilities` 与 `SharedAssemblies` 纳入 `PluginRegistry.Fingerprint`，声明变化会替换 fiber 而不是复用旧 ALC。
- **动态契约授权**：运行时安装声明未封印的共享程序集被明确拒绝为 `host_restart_required`，不再在 loader 里变成泛化冲突。
- **版本展示**：`InstalledVersion` 改为按应用声明范围解析到的最高版本，与实际装载一致。
- **Config Schema**：同类型不同文档冲突即拒绝；动态安装也走 `RegisterSchema`。
- **DTO 分离**：`declaredCapabilityRequires/Provides` 与 `packageDependencies` 分开，包依赖 ID 不再冒充能力契约。
- **Task 05**：新增 `MixedBackendProductE2ETests`，一个 manifest 中 Managed → Process → WASM 真实调用、按依赖逆序停用/恢复、子进程 PID 与 fiber 回收均有断言。

## 收敛批次（Endpoint 所有权 / 版本 / 契约语义 / 产品 E2E）

- **Endpoint 所有权**：Process/WASM Bridge 释放时只清本地 handle，不再销毁 `Context.Require` 得到的 provider Endpoint；Bridge 重新 Attach 时重置 handle 表和 disposed 标志。回归：`ConsumerReleaseDoesNotDisposeTheSharedProviderEndpoint`（两消费者共享一 provider，先退出的不影响后者；provider 撤回→Pending，恢复→Active）。
- **版本解析**：`CapabilityCatalog.ValidateCompatibility()` 是唯一的版本门禁——provider 版本不满足 consumer range 或同一 Id 多版本则在装配期拒绝；缺 provider 仍是 Pending。Core 仍保持单 provider/key，不引入第二套选择逻辑。回归：`CapabilityDeclarationTests` 三条版本用例。
- **统一契约语义**：新增 `CapabilityInvocationException` 的稳定错误码；Process/WASM provider 响应用声明的 `MediaType`；Bridge 使用激活的 `CancellationToken`；WASM 超长 payload 报 `invalid-payload` 而不是裸返回值。回归：`ProviderMediaTypeAndOversizedPayloadHaveOneContractSemantic`。
- **产品 E2E**：`MixedBackendProductE2ETests` 现覆盖安装→加载→真实跨后端调用→逆序停用→恢复→逆序卸载→停止，并断言子进程 PID 回收与 fiber 清理。
- **已知测试边界**：Process 互操作 fixture 是 POSIX 脚本，Windows 上直接跳过；这是未关闭的覆盖缺口，不宣称 Windows Process 后端已验收。

## P0 收敛批次（第二轮）

- **P0-01 所有权补齐**：`DelegatingCapabilityEndpoint` 增加在途调用租约——Dispose 只执行一次，且等所有在途调用结束后才释放底层资源；新增跨后端双消费者测试（Process+WASM 共享一个 Managed provider，先退出的不影响共享 Endpoint，provider 撤回→Pending，恢复→Active）。
- **P0-02 声明事务统一**：`ApplicationLoader.DeclarationPlan/Rollback` 是启动与动态安装的唯一声明入口；`CapabilityCatalog.Replace` 原子替换声明集合（Key 实例保持稳定）；组件工厂不再改 Catalog，WASM 自定义段声明用 `EnsureConsistent` 做结构等价校验。动态安装/卸载 E2E：`DynamicInstallPlansDeclarationsAndUninstallRollsThemBack`。
- **P0-03 Process 失败语义**：capability-only Process 启动失败改为 raise（fiber Failed），不再发布不可调用 Endpoint；`BackendDegradationPolicy` 按 mode 返回。回归：`FailedProcessStartDoesNotPublishACapabilityEndpoint`。
- **P0-04 契约收缩**：打包与 WASM 自定义段只接受 `endpoint` kind；冲突声明在装载时拒绝。回归：`NonEndpointValueKindIsRejectedInPackages`。
- **P0-05 可执行位**：`PackageFile.Executable` 进入包格式与校验和（有值才版本化），安装器恢复受控 execute 位；测试去掉手动 chmod，改为作者标记。回归：三后端包 E2E 仍绿。
- **P0-06 WASM 中断/限额**：Engine 开启 epoch interruption，每次 guest 导出调用设置 epoch deadline（默认 10s），Store 设内存/表上限；跑飞 guest 被中断并映射为稳定错误码。回归：`RunawayGuestIsInterruptedByTheExecutionBudget`。
- **附带修复（Core 语义缺陷）**：`PublishSnapshot`/`HandleUnwindCompleted`/withdrawal/cycle 检查曾重复调用 `component.Inject()/Provide()`；声明性组件（从 Catalog 派生声明）在计划替换后会在快照/撤销路径抛异常。现在声明与 Key 在装载时由决策域捕获一次，生命周期与快照不再回询组件。这是动态声明事务能够成立的前提。

## P1 收敛批次（语义与协议边界）

- **P1-01 Effect 补偿可观察**：install 失败后的补偿逆，成功→`Undone`，失败→`Failed` 槽位（后续 unwind 不再重跑）+ `CleanupFailureRecord`；fiber 表面仍保留原始安装异常，不掩盖。回归：`FailedCompensationInverseIsObservableAndNotReRun`、`FailedCompensationInverseThatSucceedsIsMarkedUndone`。
- **P1-02 Override 严格逆**：`OverrideMeta` 的逆恢复**前一个 override**（LIFO 语义），不再整块置空。回归：`OverrideMetaUnwindsInStrictLifoOrderToTheDeclaredView`。
- **P1-03 Versioned 环检测 Realm 一致**：`FindCycle` 的 versioned 候选按 consumer 的 EffectiveRealm 过滤，与 `TargetVersioned` 同一规则；正反测试：同 realm 环仍拒绝、跨 realm 不产生幻影环（consumer 无祖先回退，保持 Pending）。
- **P1-04 Event 边界**：`EmitAsync` 补齐 registration-realm scoping、owner 撤退实时检查（Loading 视为在途有效）、emitter token 协作检查；ADR-0004 增加 2026-11-23 C# 裁决（Core 保留最小串行 emit，多模式不复活，删除第二注册权威）。
- **P1-05 Process RPC 防护**：所有插件→主机请求必有 Result/Error（未处理异常→`-32603`，消息截断）；帧大小上限（1 MiB char cap），超限立刻杀死子进程而不是留下阻塞写者；`HasExited` 读取硬化。回归：`OversizedPluginFrameIsRejectedWithoutHangingTheHost`、`UnhandledHostHandlerFailureAnswersMinus32603`。
- **P1-06 平台证据诚实化**：新增 `PosixOnlyFact`/`WindowsOnlyFact`，被排除平台的测试显式 Skipped（可在 CI 计数中区分），不再用裸 `return` 冒充通过。Windows 真实 Process fixture 仍未实现——这是登记在案的覆盖缺口。

## P2 收敛批次（产品闭环与边界）

- **P2-01 动态升级**：`DynamicUpgradeKeepsCompatibleCapabilityLiveWithNewGeneration`——运行中上传 v1→安装→调用→上传 v2→`UpgradeAsync`→fiber 新代际、`InstalledVersion` 显示 2.0.0、调用仍成功；声明计划随升级重放。
- **P2-02 事务一致性**：`FailedActivationRollsBackStoreManifestRegistryAndCatalogTogether`——capability-only 进程启动失败导致 apply 失败后，manifest 字节、store 版本、registry 条目、runtime fiber 与 capability catalog 全部回到基线（真实 catalog 通过 Plan/Rollback 接线）。
- **P2-03 多宿主边界**：`current-status` 明确 WinForms/Avalonia 原生宿主为**显式延期**能力（P2-03），`UiHostKind` 数量不计为已完成宿主。
- **P2-04 并发与可观察性**：`Runtime.PendingCommands` 命令队列深度观察（入队/执行配对计数，仅观察不是背压闸门）+ 消化测试；`StableRegistry` 订阅契约落码——通知跳过已 Dispose 的 watcher，Dispose 不再合成"最后一次回调"，文档写明快照/Dispose 竞态的边界；`ManagedPluginLoader` 的 Load/Unload 内部串行化（产品路径外直接调用也安全）；`ObserveStream` 明确标注为单消费者工作队列，与 fan-out 的 `ObservationLog` 区分。
- **P0 遗留关闭（跨平台执行证据）**：Process 后端支持启动参数（`ProcessPluginComponent`/`ProcessBackend` 可携带 arguments）；同一 `dotnet <dll>` 启动路径的 fixture 在 Linux 上**真实执行**（`DotnetHostedProcessFixtureRuns`），并在 Windows 上以 `[WindowsOnlyFact]` 登记（`WindowsDotnetProcessFixtureRuns`）——CI 将分别报告 Passed/Skipped。

## 自审批次（2026-11-23，HEAD 5eb2855 之后）

对 P0–P2 三批改动做了对照式自审（逐条核对本仓测试存在性与真实执行），发现并处置：

1. **修：Endpoint 进入租约的 check-then-increment 竞态**——原实现先查 disposed 再计数，
   与 Dispose 的 drain 窗口存在缝隙；改为 enter-then-check（先计数后校验），并发撤销
   下不再可能"漏计后释放"。
2. **修：Emit 吞掉 handler 自身的取消**——原先所有 `OperationCanceledException` 一律
   不计失败；现在仅当 emitter 自己在取消时才视为协作取消，否则计为首失败。
3. **补：capability consumer 的 divert 语义证据**——新增
   `DivertedWasmConsumerUnwindsInsteadOfFailing`：在 guest 激活途中 retire，
   断言 Gone 且无 Failure（Pending/撤销路径，而不是 C33 的 Failed）。
4. **补：阻塞边界的书面化**——WASM import 与 Process read-loop 里的同步端点调用
   是按协议必然的阻塞点：自环不可能（provide+require 同键在 mount 被拒），
   注释写明边界与上界来源。
5. **测试构造错误自纠**：divert 测试初版给纯消费者误传了 provide 声明（撞上
   provider 的重复声明），修正为 require 声明——它本身也是"声明权威"语义的
   一次意外验证。
6. **账本真实性核验**：逐条 grep 确认验收基线引用的每个测试名都真实存在并可执行
   （含同步 `void` 测试）；`chmod` 绕过确认已移除；六方向互操作测试矩阵齐全。

自审后全量：**507 passed + 1 显式 Skipped（Windows 用例在 POSIX 上登记跳过）/ 0 failed**，
build 0 warning / 0 error。

## 终验收敛批次（针对 5eb2855 审查报告六项）

1. **Endpoint 线性化**：`DelegatingCapabilityEndpoint` 的所有 Dispose 共享同一个完成 Task（晚到调用也等待底层资源真正释放/失败）；进入租约采用 enter-then-check；压力测试 `ConcurrentInvokeAndDisposeNeverUseAReleasedEndpoint`（多轮 × 32 Invoke × 4 Dispose）断言释放后零调用进入。
2. **WASM 全模块预算**：**每个** Store（含无 Bridge 的普通模块）都创建 EpochTicker；`AttachCancellation` 把 Activation token 记录为调用边界标志，中断后的 Instance 标记不可复用。**边界说明**：Wasmtime 不允许跨线程修改 Store/Engine，因此取消不会从外部线程强行打断正在执行的 guest；运行中的无限循环由**调用前武装的 epoch 预算**兜底（10s），取消在下一个 guest 调用边界生效，且在途激活按 divert 语义撤销（fiber Gone 无 Failure）。测试：普通模块跑飞 → `execution-budget-exceeded`；激活期取消 → fiber Gone 无 Failure。
3. **Catalog 原子事务**：`Replace` 在锁外构建完整候选状态并校验（含同版本多 Provider 拒绝），一次锁内交换；`ValidateCompatibility` 改为纯候选校验；失败零污染（`ReplaceRejectsTwoProviders...` 断言旧声明与 Key 身份不变）。
4. **WASM 声明双向严格一致**：Manifest 与 Custom Section 按完整结构（Id/方向/版本/范围/媒体类型/Schema）双向相等校验，冲突拒绝；`capability_invoke` 导出在激活前验证，缺失即 Failed（`CapabilityProviderWithoutCapabilityInvokeExportCannotActivate`、`ConflictingWasmDeclarationIsRejectedAtLoadTime`）。
5. **真实 Consumer 版本链路**：新增 `VersionedConsumer` fixture，`RealConsumerStaysCompatibleAcrossProviderUpgradesAndRejectsIncompatible` 覆盖 v1→v2 兼容升级保持 Active、v2→v3 越界升级被拒且维持 v2 与 Active。
6. **平台与事务基线**：剩余裸 `return` 全部改为 `[PosixOnlyFact]`；控制面 `MutationBaseline` 纳入宿主提供的只读声明指纹（Platform 提供，Packaging 不碰 Catalog），补偿后比较——`TransactionBaselineDetectsAnUnrestoredDeclarationFingerprint` 验证检测能力。声明计划改为自包含事务对象（`IDeclarationTransaction`），去掉 PlatformApplication 共享快照字段。

## 冻结规则

1. 本基线只记录**已核实**的证据等级，不把 E0/E1 升级为 E2。
2. 后续任务不允许因为非阻断性发现重新开启已冻结项；新证据必须写入新文件，不回改本文件。
3. Task 02 的进入条件已满足（P0-01 落地），立即推进，不再为表格计数拖延。
