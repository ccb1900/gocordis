# ADR: Channel 驱动的生命周期决策域（批次 A 设计记录）

Status: **设计已验证方向、实现未达收敛，未合入**
Date: 2026-11-22
Branch: `migration/dotnet`,基线 `b3371fe5`

## 背景与目标

用户指出：C# Runtime 目前用 `AsyncLocal<GateLease>` 做全局重入锁，Apply 阻塞决策、
后台任务继承租约会绕过串行化；Go 版用 Orchestrator + 独立 goroutine + ActivationID
完成同样的论文语义。C# 的 `System.Threading.Channels` 是 Go channel 的 .NET 对应物，
应当用它替代门控锁，降低迁移心智负担。

目标：论文语义为内核、.NET 原生能力为底座、能像 Koishi 一样组合真实应用。

## 设计（已落地并编译通过，见下方“为何未合入”的命令）

- `Channel<RuntimeCommand>` 单一决策队列；所有生命周期状态只由决策循环写。
- 内部命令：`LoadCommand` / `DisposeCommand` / `CloseCommand` / `MutateCommand` /
  `StepRequestCommand` / `StepInstalledCommand` / `ApplyCompletedCommand` /
  `UnwindCompletedCommand`。
- 独立 `Activation` 对象：`Id` / `Context` / `CancellationTokenSource` /
  `Phase(Installing→Committed→Unwinding→Unwound)` / `Effects` / `Dependencies`。
- `ProviderBinding(FiberId, ActivationId, ...)`：旧激活完成无法污染新激活。
- Effect 迭代器：每个 step 前由 `StepRequestCommand` 检查目标，论文 L-Divert 语义。
- Child 默认继承父 Realm + per-key 路由表 + 创建时 Metadata 快照。
- Relied guard 升级为按 Activation 的等待计数：Consumer Cleanup 期间 Provider 保持
  Active/Committed；子 Fiber 同属等待集合。
- 公开 API 语义保持：`LoadAsync` 在本次加载引发的激活完成后返回；`LoadChildAsync`
  不等待子激活（避免 Apply 内死锁）。

## 未合入原因（实测，非推测）

1. 单元测试逐个通过；全量套件在 Core/Conformance/Runtime 项目并行执行时出现
   PC19、SingleStepDivert、Thm68、Koishi disable 等场景的不稳定失败或
   testhost 在测试完成后挂起。
2. 排查中确认并修复过多个真实缺陷：provider waiter 未释放导致关停死锁、
   single-step Apply 在决策前执行导致自 Isolate 被误判 divert、in-flight Apply
   的 cleanup 在 unwind 后丢失、Committed 绑定身份比较、terminal fiber 的
   Dispose/Close 未转 Gone、Snapshot 绑定格式破坏观察等价。
3. 最后一类问题（testhost 挂起与并行偶发）与 .NET 11 SDK 运行 net10 目标、
   测试宿主并行资源竞争交织，未能在本次会话内达到“连续三次全绿”的门禁。
   按工程纪律：**不提交未收敛的运行时内核**。

## 合入前置条件（下次批次 A 的验收清单）

- [ ] 全量 `dotnet test Gocordis.sln` 连续三次零失败、零挂起（Linux + Windows）。
- [ ] Koishi disable/re-enable、PC19 陈旧完成、Thm68 恢复等价在并行压力下稳定。
- [ ] `LoadAsync` / `LoadChildAsync` 的返回语义与 Go 基线逐项对照测试。
- [ ] `Mutate` 短锁与决策循环的锁序有形式化说明，禁止任何用户回调持锁。
- [ ] 测试结束后不残留决策循环 / 插件子进程。

## 产物

本次尝试的代码全部回退；基线 `b3371fe5` 已重新验证 118 项测试全绿。本 ADR 保留
设计与失败证据，供下一次批次 A 直接复用，不重复从零设计。
