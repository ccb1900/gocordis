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
3. **Windows runner 实跑**：CI 配置已就绪，本机 Linux 未执行。
4. **论文 §5.3 Koishi 端到端案例**（G-B1）与 **§6.6 版本化依赖路由**（G-B2）仍未复现，属于早前已记录的独立缺口。

## 验证

```
bash docs/migration/verify-baseline.sh   → BASELINE VERIFIED
dotnet build Gocordis.sln -m:1           → 0 Warning / 0 Error
dotnet test Gocordis.sln -m:1            → 19 + 18 + 48 + 14 = 99 passed
```
