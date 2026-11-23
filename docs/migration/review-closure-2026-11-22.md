# 迁移质量终审问题闭合记录（2026-11-22）

来源：对 `migration/dotnet@ce44f8e` 的独立静态审查。本文件逐条记录**问题 → 修复 → 证据**；
不用"已写文档"代替实现，也不把测试覆盖不到的行为标成完成。

## P0

| 审查发现 | 修复 | 证据 |
| --- | --- | --- |
| Managed 插件卸载未绑定 Fiber 生命周期，`ManagedPluginLoader.Unload` 可在 Component 仍被 Runtime 引用时调用 | 新增 `ManagedPluginHost`：唯一所有者，固定顺序 `Dispose Fiber → await Gone → 清引用 → ALC.Unload → GC` | `AuditRegressionTests.ManagedUnloadIsBoundedToFiberGone`：完整走 `PluginRegistry → Fiber → Gone → ALC.Unload → WeakReference 回收` |
| Effect 安装失败时部分副作用无补偿；Context 缺少统一激活代次校验；效果状态机缺失 | `Context.EffectAsync` 先持有 inverse，install 失败即 best-effort 补偿；`EnsureGeneration()` 覆盖 Require/Provide/Isolate/Meta/On/Emit/LoadChild；新增 `EffectState.Installing/Committed/Undoing/Undone` 并在 unwind 中迁移状态 | `FailedEffectInstallationCompensatesPartialSideEffects`、`StaleActivationContextCannotMutateTheFiber`；全部既有 LIFO/divert 测试 |

## P1

| 审查发现 | 修复 | 证据 |
| --- | --- | --- |
| Manifest 的字符串依赖与 Core 的 typed key 依赖是两套图 | `PluginRegistry` 排序改为从 `Component.Inject()/Provide()` 的 typed key 建图；`manifest.Requires/Provides` 仅作描述；未解析 key 必须显式登记为 `ExternalCapabilities`；环检测基于 typed key | `DependencyOrderComesFromTypedKeysNotManifestStrings`、`UnresolvedTypedCapabilityIsRejectedEvenWhenManifestClaimsItExists`、`UnknownFactoryAndTypedDependencyCyclesAreRejected` |
| `WasmHostBridge` 存在但 `WasmComponent` 未使用；HMR 只是停旧启新且未串行化 | `WasmComponent` 接受 bridge 并传入 `FromWat`；`WasmHotSwapper` 用 `SemaphoreSlim` 串行化，候选实例保持到旧 fiber Gone 后才释放 | `WasmComponentUsesTheSuppliedHostBridge`、`ConcurrentWasmReplacementsAreSerialized` |
| `ProcessKeys.ErrorVocabulary` 只声明未提供 | 健康路径提供 `"ok"`，降级路径提供 `"plugin_error"`，消费者可 Require | `FailedBackendDegradesWithoutFailingTheFiber` 新增跨组件 Require 断言 |
| 扩展套件并发/资源/路径安全 | `FileSystemService` 用 `Path.GetRelativePath` 做目录边界校验并检查符号链接目标；`TimerService` 移除已完成定时器并同步 Throttle；`Scheduler` 用 `_running` 防止同一 Job 重入；`Broker<T>`/`StableRegistry<T>` 加锁；`ConfigWatcher` 用信号量串行 reconcile；`ObserveStream<T>` 暴露序号 API 以显式检测缺口 | `SchedulerDoesNotRunTheSameDueJobTwice`、`BrokerAndRegistryAreSafeUnderConcurrentMutation`、`ObserveStreamExposesSequenceGaps`、路径逃逸断言 |

## P2

| 审查发现 | 修复 | 证据 |
| --- | --- | --- |
| Console 缺少 `/api/plugins`、`/api/fleet` | `WebUiServer` 接受可选 `PluginRegistry`，补两个端点 | `WebUiServesQueriesOverHttp` 扩展断言 |

## 仍然存在的限制（不冒充完成）

1. **WASM HMR** 是"串行化 warm 验证 + 旧 fiber Gone 后提交"，不是零停机原子提交；文档已下调表述。
2. **Effect 补偿**只在调用方把 inverse 交给 `ctx.EffectAsync` 时成立；绕过 Context 的外部副作用不在可逆边界内（论文 §6.1 `idΓ`）。
3. **Windows runner 实跑**：GitHub Actions `windows-dotnet` job 按 SHA 独立核对；`2ccdaad` 曾失败，`62d7de15` 已成功，不复用历史结果。
4. ~~论文 §5.3 Koishi 案例（G-B1）与 §6.6 版本化依赖路由（G-B2）~~ 已在同一次连续执行中关闭，见下节。

## 闭环审查第二轮（同一执行内）

无处 agent 可用，改由实现者按审查清单做独立自审并修复：

| 发现 | 修复 | 证据 |
| --- | --- | --- |
| `Broker<T>.RoundRobin` 对负数 ticket 抛 `ArgumentOutOfRangeException` | 使用模归一化索引 | `BrokerAndRegistryManageMembers` 增加 `RoundRobin(-1)` |
| `Scheduler.Jobs` 未加锁读取字典 | 加 `lock (_jobs)` 快照 | 全量测试 |
| `FileSystemService` watcher 列表与 Dispose 竞态 | `lock (_watchersSync)` 保护添加/移除/释放 | 全量测试 |
| `ConfigWatcher` 释放时队列回调可能释放已 dispose 的 gate | `Release` 捕获 `ObjectDisposedException` | 全量测试 |
| `PluginRegistry` typed 图中版本化自依赖未判环 | `best.Id == id` 抛 `DependencyCycleException` | 闭环审查修复 |

## G-B1 / G-B2 关闭

- **G-B1**：`samples/KoishiCase` 复现论文 §5.3：服务端宿主只提供聊天领域词汇；IM adapter/DB driver 提供
  coeffect；功能插件消费；控制台禁用插件时效果就地撤销；provider 重配置只让依赖者重激活且保留数据库连接；
  Web Console 是第二个独立 Runtime。`KoishiCaseTests` 4 项覆盖这些主张。
- **G-B2**：`VersionedKey<T>` + `NuGet.Versioning` 实现 peer range、最高兼容版本选择、多版本共存、
  interface drift 与 key collision 的显式拒绝；`PluginRegistry` 的 typed 依赖图与环检测纳入版本维度。
  `VersioningTests` 5 项覆盖。

## 验证

```
bash docs/migration/verify-baseline.sh   → BASELINE VERIFIED
dotnet build Gocordis.sln -m:1           → 0 Warning / 0 Error
dotnet test Gocordis.sln -m:1            → 21 + 28 + 19 + 50 = 118 passed
```

## 最终质量修复批次（同日收敛）

按审查给出的唯一最终批次执行，不再扩范围：

| 优先 | 项 | 落地 |
|---|---|---|
| P0-1 | Context 失效边界 | Context 绑定创建时激活令牌；写操作统一校验 Generation / LifecycleIntent / Cancellation；读操作保留 Cleanup 期 Require 的论文语义；回归：撤销中的旧 Context 不得注册能力/事件/子 Fiber |
| P0-2 | 重入锁继承 | `AsyncLocal<bool>` → 可撤销 GateLease；后台任务继承 ExecutionContext 后拿到的是已撤销租约，不再绕过串行化；回归：后台任务继承后 Load/Dispose |
| P0-3 | Committed 引用清理 | `UnwindAsync` 在所有逆执行后清空 Committed，Pending Consumer 不再持有 Provider Fiber/ALC；回归：Managed Provider + 常驻 Consumer，卸载后 ALC 可回收 |
| P1-1 | 版本化重绑定 | `TargetVersioned` 成为解析与 Satisfied 的唯一目标绑定来源：更高兼容版本出现时旧 Consumer 撤销重激活；注册/移除版本化能力触发重新调和；回归测试 |
| P1-2 | 版本化声明校验 | Registry 对版本化依赖采用与普通 Key 一致的 ExternalCapabilities 规则：无 Provider 且未声明 external → 拒绝；声明 external → 合法但保持 Pending；自环仍报环 |
| P1-3 | Scheduler / WatchService | Scheduler 所有 Job 状态变更统一加锁、取消的运行中任务不复活、Dispose 等待在飞任务并拒绝新任务；WatchService 的 watcher/revision 共享一把锁且 Dispose 幂等；回归 3 项 |
| P1-4 | 文件系统链接边界 | Mount 前缀按路径边界匹配（Windows 大小写不敏感）；Resolve 对每一段已存在的路径检查链接目标，阻止中间目录 symlink 逃逸（含新建文件场景）；回归 2 项 |
| P2 | 文档一致性 | 测试数量按批次回填；Windows CI 改为“按 SHA 记录，不复用历史结果” |

状态：以上全部带针对性回归测试并通过；Go 基线保持通过。**迁移分支冻结**。

补充：在 macOS 上执行 Go 基线时发现 `extensions/fs/local` 的 watch 路径把
`/var` 与 `/private/var` 两种拼写混用，导致 watch 目录被误判越界、事件无法
匹配。已在 `insideRoot` 对两侧统一做“最长存在前缀物理化”、事件路径回映
为调用方拼写；Go 全量 53 包测试通过。此修复属于“Go 基线验证仍然通过”
的完成条件，不涉及 .NET 迁移语义。
