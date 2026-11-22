# ADR: Channel 驱动的生命周期决策域（批次 A 实施记录）

Status: **已合入并冻结（批次 B/C 均已完成）**
Date: 2026-11-22
Branch: `migration/dotnet`,基线 `b3371fe5`

## 背景与目标

用户指出：C# Runtime 旧模型用 `AsyncLocal<GateLease>` 做全局重入锁，Apply 阻塞决策、
后台任务继承租约会绕过串行化；Go 版用 Orchestrator + 独立 goroutine + ActivationID
完成同样的论文语义。C# 的 `System.Threading.Channels` 是 Go channel 的 .NET 对应物，
应当用它替代门控锁，降低迁移心智负担。

目标：以论文语义为内核、.NET 原生能力为运行基础、能像 Koishi 一样组合真实应用。

## 设计

- `Channel<RuntimeCommand>` 单一决策队列独占生命周期决策（Load/Dispose/Close/
  Reconcile/Unwind）。`Context` 侧的能力绑定、元数据、effect 登记通过短临界区
  `Mutate` 写入共享表；这是 Channel 决策 + 短锁保护的混合模型，不是"所有状态
  只由决策循环写"。公开回复统一在快照发布后完成。`GetFiber` 等公开读也走同一短锁。
- 内部命令：`LoadCommand` / `DisposeCommand` / `CloseCommand` / `MutateCommand` /
  `StepRequestCommand` / `StepInstalledCommand` / `ApplyCompletedCommand` /
  `UnwindCompletedCommand`。
- 独立 `Activation` 对象：`Id` / `Context` / `CancellationTokenSource` /
  `Phase(Installing→Committed→Unwinding→Unwound)` / `Effects` / `Dependencies`。
- `ProviderBinding(FiberId, ActivationId, ...)`：旧激活完成无法污染新激活。
- Effect 迭代器：每个 step 前由 `StepRequestCommand` 检查目标，论文 L-Divert 语义。
- Child 默认继承父 Realm + per-key 路由表 + 创建时 Metadata 快照。
- Relied guard 按 Activation 的等待计数：Consumer Cleanup 期间 Provider 保持
  Active/Committed；子 Fiber 同属等待集合。
- 公开 API：`LoadAsync` 在本次加载引发的激活完成后返回；`LoadChildAsync`
  不等待子激活（避免 Apply 内死锁）。

## 收敛过程中修复的真实缺陷

1. **Terminal fiber 的 Dispose 回复丢失**：`HandleDispose` 对 Pending/Unwound/Gone
   fiber 直接置 Gone，但只调用 `CompleteDispose(fiber)`；该 fiber 的回复从未登记到
   `_disposeWaiters`，导致 `Koishi_DisablePluginRevertsEffectsWithoutTouchingProviders`
   与 `PluginRegistry.DisposeAsync` 永久挂起。改为在terminal分支直接完成当前命令的回复。
2. **等待方看到过期快照**：回复 TCS 使用 `RunContinuationsAsynchronously`，若在命令处理
   中途 `TrySetResult`，调用方可能在决策循环执行 `Reconcile()`/`PublishSnapshot()` 之前
   恢复，观察到仍为 `Unloading` 或尚未移除的 fiber。现在所有公开回复统一登记到
   `_pendingReplies`，在快照发布后统一完成（`ProcessRuntimeTests.CoreLifecycleOwnsTheProcess`
   与 `Koishi_WebConsoleIsAnIndependentSecondApplication` 的偶发失败根因）。
3. **退役 Activation 钉住插件 ALC**：`_activations` 与 `Activation.Dependencies` 长期持有
   Fiber/Component 强引用；依赖消失后 Pending consumer 仍可能通过旧 ProviderBinding
   钉住已卸载插件。新增 `ForgetActivation`：撤销完成后从 `_activations` 移除、清空
   Dependencies/Effects，并在 fiber 终止时清空 `CurrentActivation`。
   `CheckCloseComplete` 同时清理 provider/handler/versioned 表。
4. **GC 敏感测试**：ALC 回收断言在 xUnit 并行压力下依赖偶然的 gen2 回收，偶发假阴性。
   新增 `GcAssert.Reclaimed`，使用阻塞压缩式强制回收；不改变“ALC 必须可回收”的断言。
5. **决策循环生命周期**：`Runtime.DisposeAsync` 在 `CloseAsync` 后 await `_loop`，
   避免决策循环任务在处理完最后一个命令后仍短暂钉住插件对象。
6. **并发 Dispose/Close 与子 fiber 所有权**（review 收敛）：`_disposeWaiters` 与
   `_closeWaiters` 从单回复改为等待者集合，避免第二个调用覆盖第一个回复；所有
   fiber 进入 Gone 的路径统一 `Parent.RemoveChild(child)`，父 fiber 不再长期持有
   已终止子组件的 Component/ALC；`GetFiber` 加 `_stateLock`，公开读与决策写入线性化。

## 回归测试

- `LifecycleBoundaryTests.DisposingAnAlreadyWoundDownFiberCompletesAndPublishesRemoval`
  覆盖 terminal Pending fiber 的 Dispose 完成 + 快照新鲜度；带 5s 超时，回归时失败而非挂起。
- 既有 `Koishi_DisablePluginRevertsEffectsWithoutTouchingProviders`、
  `ProcessRuntimeTests.CoreLifecycleOwnsTheProcess`、
  `Koishi_WebConsoleIsAnIndependentSecondApplication` 成为快照顺序回归。
- 既有 `ManagedRuntimeTests` / `AuditRegressionTests` ALC 用例覆盖 Activation 清理语义。

## 验证证据

- `dotnet build Gocordis.sln`：0 error。
- `dotnet test Gocordis.sln`（`DOTNET_ROLL_FORWARD=LatestMajor`，本机仅安装 .NET 11 SDK
  运行 net10 目标）：批次 A 连续 10 次全绿，119 项测试
  （Core 22 / Conformance 28 / Extensions 19 / Runtime 50）。
- 批次 A/B/C 最终全量：**311/311**，连续 24 次零失败、零挂起
  （Core 26 / Conformance 113 / Extensions 52 / Runtime 113 / Packaging 7）；build 0 warning / 0 error。

## 合入前置条件（批次 A 完成）

- [x] 全量 `dotnet test Gocordis.sln` 连续三次零失败、零挂起（本机 10 次）。
- [x] Koishi disable/re-enable、PC19 陈旧完成、Thm68 恢复等价在并行压力下稳定。
- [x] `LoadAsync` / `LoadChildAsync` 的返回语义与 Go 基线逐项对照测试。
- [x] Channel 独占生命周期决策；`Context` 侧写入走短锁 `Mutate`，公开读走 `_stateLock`；
      公开回复在快照发布后完成。
- [x] 测试结束后不残留决策循环 / 插件子进程。

批次 B（PluginRegistry 事务、Discovery/Config、三后端统一协议）与批次 C
（Extensions/Console/四方矩阵/最终验收）均已完成并推送。
