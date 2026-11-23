# 定理移植矩阵：Go 测试 → C# 用例（批次 C）

论文定理编号与条目锚点以 [`semantic-mapping.md`](semantic-mapping.md) §2 为准
（C24–C30 对应 Thm64/68/70/73/80）。本表逐条说明 Go 的可执行证据在 C# 侧的落点；
`移植` 表示用现有 Core 公共 API 重写了可观察行为断言，
`接受差异` 表示有意不照搬（论文前提不同、宿主能力不同或属于测试工具），
`待补` 表示尚无 C# 单点断言。绝不把"有同名文件"当作覆盖。

批次 C 之前 Thm70 的唯一 Conformance 用例误放在 `PaperConformanceTests`；现已移入
`TheoremMatrixTests.Thm70_Matrix` 的第一个场景。Thm64/68/70/73/80 各自独立成
`[Theory]+MemberData` 矩阵，每定理 3 个场景，共 15 个用例；旧的单点 `TheoremTests`
保留作为历史基线（不计入矩阵覆盖数）。

## Thm64 保持性（C25）

| Go 测试 | C# 用例 | 状态 | 说明 |
| --- | --- | --- | --- |
| `runtime/proof_invariants_test.go:223` `TestProofPreservationDeterministicScenario` | `TheoremMatrixTests.cs:25` `Thm64_Matrix["independent-providers"]` | 移植 | 5 个独立 provider 装载后 `AssertWellFormed`：parent 存在、provider 名唯一、Active fiber 的 Requires 与绑定数一致、绑定目标 Active。 |
| `runtime/thm64_preservation_test.go:225` `TestThm64PreservationRandomized` | `TheoremMatrixTests.cs:25` `Thm64_Matrix["dispose-reload-provider"]` | 移植（场景化） | 随机操作序列改为确定性的 dispose→同名重插，断言旧 fiber Gone、新 fiber Active、registry 仍良构；随机调度未逐条复现。 |
| `runtime/c2_thm64_every_step_test.go:423` `TestC2Thm64EveryStepSingleFiber` | `TheoremMatrixTests.cs:25` `Thm64_Matrix["dependency-withdrawal-recovery"]` | 移植 | 依赖消失→Pending→恢复，逐步检查良构性；对应"每步保持"的可观察子集。 |
| `runtime/proof_invariants_test.go:318` `TestProofPreservationRandomSchedules` | —（无 C# 用例） | 接受差异 | C# 只用固定 3 场景矩阵；随机调度器属于 Go 测试基础设施，不进入 C# Conformance。 |

## Thm68 恢复精确性（C26）

| Go 测试 | C# 用例 | 状态 | 说明 |
| --- | --- | --- | --- |
| `runtime/thm68_recovery_test.go:85` `TestThm68LIFOExactlyOnceRecovery` | `TheoremMatrixTests.cs:137` `Thm68_Matrix["consumer-effect"]` | 移植 | 逆恰好一次 + 观察等价回到初始快照。 |
| `runtime/thm68_recovery_test.go:125` `TestThm68BaselineEquivalence` | `TheoremMatrixTests.cs:137` `Thm68_Matrix["single-provider"]` | 移植 | 单 provider 装载后撤销，空快照与初始快照 `ObservationEquivalent`。 |
| `runtime/iterator_test.go:127` `TestThm68IteratorLIFOExactlyOnceOnDivert` | `TheoremMatrixTests.cs:137` `Thm68_Matrix["three-stage-chain"]` | 移植（顺序链） | 三级链逆序装载/逆序撤销，两个 effect 的逆各一次；per-yield divert 由 `AuditRegressionTests.SingleStepDivertStillRunsTheApplyCleanup` 覆盖。 |
| `runtime/iterator_test.go:273` `TestThm68IteratorRaiseMidActivation` | `Core.Tests/CoreSemanticsTests.cs:437` `FailedEffectInstallationCompensatesPartialSideEffects` | 移植（跨项目） | 失败迭代的补偿在 Core 测试中；Conformance 矩阵不重复。 |
| `runtime/phase5_generators_test.go:71` `TestPRecRecoveryExactnessRandomStacks` | —（无 C# 用例） | 接受差异 | 随机 effect 栈生成器未移植；C# 仅固定 3 场景。 |

## Thm70 顺序性（C27）

| Go 测试 | C# 用例 | 状态 | 说明 |
| --- | --- | --- | --- |
| `runtime/c3_thm70_ordering_test.go:698` `TestC3Thm70ActivationOrderingSingleProviderConsumer` | `TheoremMatrixTests.cs:265` `Thm70_Matrix["consumer-declared-first"]`（原 `PaperConformanceTests.Thm70_ProviderEpisodeContainsConsumerEpisode`，已归位） | 移植 | consumer 先声明只能 Pending，provider 提交后才激活并绑定。 |
| `runtime/c3_thm70_ordering_test.go:740` `TestC3Thm70WithdrawalOrderingSingleProviderConsumer`；`runtime/thm73_thm80_test.go:543` `TestThm70WithdrawalOrdering` | `TheoremMatrixTests.cs:265` `Thm70_Matrix["provider-replacement"]` | 移植 | provider 撤回→consumer Pending→新 provider 提交→consumer 重激活且绑定到新 id；新 episode 的代次严格在后。 |
| `runtime/c3_thm70_ordering_test.go:870` `TestC3Thm70MultiConsumerOrdering` | `TheoremMatrixTests.cs:265` `Thm70_Matrix["three-node-chain"]` | 移植（单链） | root→middle→consumer 三重顺序与 root 替换后的重激活；多 consumer 并发扇出未逐条移植。 |
| `runtime/c3_thm70_ordering_test.go:797` `TestC3Thm70ReloadGeneration` | `TheoremMatrixTests.cs:265` `Thm70_Matrix["provider-replacement"]` | 移植 | 通过 Generation 与绑定 id 断言 episode 不串代。 |
| `runtime/thm70_ordering_test.go:87` `TestThm70RandomizedOrdering`；`runtime/c3_thm70_ordering_test.go:929` `TestC3Thm70RandomizedSchedule` | —（无 C# 用例） | 接受差异 | 随机 ordering oracle 未移植；C# 平台收敛用 `PlatformConvergenceTests` 的固定用例。 |
| `runtime/c3_thm70_ordering_test.go:1031/1155/1270` stale apply/unwind injection 与 oracle sanity | `PlatformConvergenceTests.cs:42` `PC19_StaleActivationCannotAffectANewGeneration` | 移植（故障注入改为阻塞式） | 用阻塞 Apply + dispose + release 复现陈旧完成，不向运行时注入内部事件。 |

## Thm73 进展性（C29）

| Go 测试 | C# 用例 | 状态 | 说明 |
| --- | --- | --- | --- |
| `runtime/thm73_thm80_test.go:190` `TestThm73BoundedProgress` | `TheoremMatrixTests.cs:413` `Thm73_Matrix["reverse-order-chain"]` | 移植 | consumer/middle/root 逆序装载后达到全部 Active 的定态。 |
| `runtime/thm73_thm80_test.go:508` `TestThm73QuiescenceAllowsActive` | `TheoremMatrixTests.cs:413` `Thm73_Matrix["independent-providers"]` | 移植 | 4 个无依赖 provider 全部 Active，无 Pending。 |
| `runtime/obs_test.go:361` `TestThm73QuiescenceSeesInstallingEffect` | `TheoremMatrixTests.cs:413` `Thm73_Matrix["missing-dependency-quiesces"]` | 移植（门控侧） | 缺依赖时稳定在 Pending 且无 Failure；provider 到达后自动激活。"installing effect 可见"由 Core 测试覆盖。 |
| `runtime/thm73_thm80_test.go:695` `TestThm73AcyclicPrecedenceInvariant` | `Core.Tests/CoreSemanticsTests.cs:317` `DependencyCycleIsRejectedAtMount` | 移植（跨项目） | 环在装载边界拒绝并给环链诊断；Conformance 不重复断言。 |
| `runtime/theorem_evidence_test.go:125` `TestTheorem73QuiescenceWithInstallingEffect` | —（无 C# 用例） | 待补 | C# 没有"installing 中仍算 quiescent"的等价观察断言；列为未覆盖，不借 Thm73 矩阵冒充。 |

## Thm80 汇合（C30）

| Go 测试 | C# 用例 | 状态 | 说明 |
| --- | --- | --- | --- |
| `runtime/thm73_thm80_test.go:474` `TestThm80ConfluenceRandomizedSchedules` | `TheoremMatrixTests.cs:521` `Thm80_Matrix["provider-consumer-load-order"]` | 移植（两序对照） | consumer-first / provider-first 两种装载序达观察等价终态。 |
| `runtime/thm73_thm80_test.go:914` `TestThm80DAGTopologicalSchedules` | `TheoremMatrixTests.cs:521` `Thm80_Matrix["chain-load-order"]` | 移植（链序） | 三级链两种拓扑序；任意 DAG 调度未逐条移植。 |
| `runtime/thm80_det_interleaving_test.go:137/163/283` apply-done / withdrawal / revise 交错 | `TheoremMatrixTests.cs:521` `Thm80_Matrix["teardown-order"]` | 移植（拆卸序） | provider/consumer 两种拆卸序终态一致且全 Gone；revise 交错未移植。 |
| `runtime/theorem_evidence_test.go:139` `TestTheorem80RenamingEquivalence` | `PaperConformanceTests.cs:93` `C13_ObservationEquivalenceIgnoresGenerativeFiberIds` | 移植 | 观察等价忽略生成 fiber id；证明改名下的汇合可比。 |
| `runtime/thm73_thm80_test.go:1038` `TestThm80TwoIndependentSubsystems` | `PlatformConvergenceTests.cs:28` `PC12_PluginRegistryNeverCreatesASecondLifecycle` | 部分移植 | 第二个应用/第二个生命周期 Authority 由 Koishi case 覆盖（`KoishiCaseTests`），本用例仅覆盖 entry 与 fiber 不重复。 |
| `runtime/revision_thm80_test.go:201` `TestThm80RevisionEndpointEquivalence`；`runtime/scoped_confluence_test.go:43` `TestThm80ScopedNamespaceConfluence` | `PaperGapClosureTests.cs` `C34_RealmReassignmentIsARetireReinsertComposite`；`Core.Tests/RevisionSemanticsTests.cs` `RehomeRetiresOldFiberAndReinsertsWithNewIdentityAndRealm` | 移植（跨项目，revision 复合） | C# 拒绝 fiber 保身份替换：realm 重赋值走 `Runtime.RehomeAsync` 的 retire→deactivate→O-Remove→同名重插，新 fiber 的 Id/Generation/Revision 递增；端点性质通过"旧 realm 绑定移除 + 新 realm 可解析"对照，不通过保身份 revise 对照（见 `semantic-mapping.md` §4 裁决 5）。 |
| `runtime/phase5_generators_test.go:270` `TestPConfConfluenceGeneratedSchedules` | `DeterministicDriver.cs:52` `SameSeedReplaysTheSameInterleaving` | 平台能力替代 | 固定种子重放代替调度生成器；不等价于全调度空间证明。 |

## 边界与前提

- 论文 Thm73 **没有"合作式"前提**（`semantic-mapping.md` §4.1）；C# 的有限步/可取消是无环工程约束，
  不写入定理前提。Go 的 `TestTheoremPreconditionsGate` 对应 C# `DeterministicDriver` 的固定种子重放，
  状态为平台能力替代，不冒充定理证明。
- Thm43（独立逆任意排列）：C# 单 fiber 内保持严格 LIFO（`C06_StrictLifoRecoveryLeavesNoResidue`），
  跨独立 fiber 的可交换性已有可执行断言：`TheoremMatrixTests.cs` 的
  `Thm43_IndependentInversesCommuteAcrossPermittedOrders` 与
  `PaperGapClosureTests.cs` 的 `C14_IndependentInversesCommuteAcrossPermittedOrders`；
  C14 在 `paper-coverage-matrix.md` 中状态为"已覆盖"，不再标为待补。
- Thm68/Thm73 之外新增诚实性边界：逆失败仍走完整 LIFO 栈，但失败槽位转 `EffectState.Failed`
  并由 `Fiber.CleanupFailures` / `RuntimeSnapshot.CleanupFailures` 暴露"清理不完整"
  （`Core.Tests/InverseFailureTests.cs`），不把失败逆伪装成 Undone 成功。
- `TheoremTests.cs` 的 4 个单点用例保留为历史基线；新增矩阵是独立用例集，两者不互相计数。
