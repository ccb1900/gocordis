# PC01–PC30 Disposition

本文件冻结 Go `integration/platform_convergence_*` 30 个平台收敛 gate 的去向。
状态口径：

- **已移植**：C# 有同名或明确映射的红测。
- **等价覆盖**：C# 有场景化测试覆盖同一语义，但不逐行翻译 Go gate。
- **接受差异**：语义由论文裁决，C# 刻意不照搬 Go/JS 行为。
- **延期**：明确未交付，不能用测试数量掩盖。

## 1. 组合与声明（PC01–PC09）

| Gate | Go 语义 | C# 去向 | 证据 |
| --- | --- | --- | --- |
| PC01 | Artifact → Fiber | 等价覆盖 | `ManagedPluginProductE2ETests`、`ApplicationLoaderDrivesEnableDisableConfigAndReenable` |
| PC02 | Config desired → reconcile | 等价覆盖 | `PluginControlPlaneTests` 配置/启停持久化、`ConfigWatcherTxGuardCoversReconcile...` |
| PC03 | Dependency activation | 等价覆盖 | typed capability 依赖排序与 `DependenciesReflectTypedBindings` |
| PC04 | Dependency withdrawal → Pending → recovery | 等价覆盖 | Core 依赖消失恢复测试、`C07_CoeffectGetSetPreconditionsAndReversibleProvision` |
| PC05 | Effect ownership | 等价覆盖 | `C02/C04/C05` LIFO 与 owned effect 测试 |
| PC06 | Event ownership | 等价覆盖 | `EventModesCoverSerialBailParallelAndWaterfall`、`EventDispatchGapTests` |
| PC07 | Event dependency composition | 等价覆盖 | KoishiCase provider/consumer 事件组合测试 |
| PC08 | Realm scope composition | 等价覆盖（C34 revision 语义） | `IsolateCannotHijackAKeyOwnedByAnotherRealm`、C34 revision 测试 |
| PC09 | Child ownership | 等价覆盖 | `C18_InstantiationRetiresChildrenBeforeTheirOwner`、child dispose 测试 |

## 2. 扩展与 HMR（PC10–PC17）

| Gate | Go 语义 | C# 去向 | 证据 |
| --- | --- | --- | --- |
| PC10 | ConfigWatch reconcile | 等价覆盖 | `ConfigWatcherCoalescesFileChangesIntoOneRevision`、`ConfigWatcher_DeletedFileReconciles...` |
| PC11 | HMR warm replacement | 等价覆盖 | `GenericHmrBindRetiresPreviousValue`、`WarmHotSwapReplacesTheModule...` |
| PC12 | HMR failure preservation | 等价覆盖 | `GenericHmrReplaceValidatesFirstAndKeepsOldTargetOnFailure` |
| PC13 | HMR withdraw then load fallback | 等价覆盖 | `GenericHmrReplaceDefersRetireWhileUsageHeld`、PluginRegistry rollback |
| PC14 | WASM composition | 等价覆盖 | `WasmComponentFollowsCoreActivationAndDisposal` |
| PC15 | WASM HMR | 等价覆盖 | `WarmHotSwapReplacesTheModuleAndKeepsTheOldFiberGone`、`ConcurrentWasmReplacementsAreSerialized` |
| PC16 | Registry capability | 等价覆盖 | `BrokerAndRegistryManageMembers`、registry 架构测试 |
| PC17 | Scheduler/Watch boundary | 等价覆盖 | `SchedulerAdvancesJobsDeterministically`、`WatchRecursiveFilterReadyErrorAndRevoke` |

## 3. 完整应用与 gate（PC18–PC30）

| Gate | Go 语义 | C# 去向 | 证据 |
| --- | --- | --- | --- |
| PC18 | Full application composition | 等价覆盖 | `ManagedPluginProductE2ETests`、`PlatformApplicationTests` |
| PC19 | Reconciliation idempotence | 已移植 | `PC01_DeclarationReconcileIsIdempotent` / `PCGateParity_PC01...` |
| PC20 | Repeated convergence | 等价覆盖 | `Thm73_ProgressReachesQuiescenceFromReverseOrder`、批次 reconcile 测试 |
| PC21 | Runtime close | 已移植 | `PCGateParity_PC21_CloseSendsEveryFiberToGone` |
| PC22 | Stale async completion | 已移植 | `PC19_StaleActivationCannotAffectANewGeneration` / `PCGateParity_PC19...` |
| PC23 | Panic boundary | 等价覆盖 | Core Apply/Cleanup panic 测试；失败不向父传播 |
| PC24 | Cancellation boundary | 等价覆盖 | C32 惯性异步/可取消操作测试、Backend cancellation 测试 |
| PC25 | Reentrancy / nested composition | 等价覆盖 | `Runtime` 命令串行化与 nested Load/Dispose 测试 |
| PC26 | Public API only | 等价覆盖 | 架构测试禁止反射/直接 fiber 状态写入 |
| PC27 | No second lifecycle | 已移植 | `PC12_PluginRegistryNeverCreatesASecondLifecycle` / `PCGateParity_PC27...` |
| PC28 | Identity separation | 等价覆盖 | `PCGateParity_PC30_FinalQuiescenceKeepsStableFiberIdentity`、artifact/realm revision 测试 |
| PC29 | Ownership graph | 等价覆盖 | `C20_TargetBindingAndQuiescenceAreObservable`、owns/depends 图测试 |
| PC30 | Final quiescence | 已移植 | `PC30_CloseLeavesEveryFiberGone` / `PCGateParity_PC30...` |

## 4. 裁决

- 30/30 均有明确去向，不再使用“其余 24 项未移植”这种无状态描述。
- 6 个为 **已移植**，其余为 **等价覆盖**；没有 Unknown。
- PC08 的 C34 revision 行为、PC24 的惯性异步行为由语义工作包补齐，
  不接受“API 不同所以接受差异”作为未实现理由。
- WinForms/Avalonia 原生 Host 与 Koishi 业务生态不属于 PC gate，继续按
  `final-calibration.md` 的 Adapted / Divergent 边界处理。
