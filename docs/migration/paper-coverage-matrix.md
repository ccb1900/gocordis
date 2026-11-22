# 论文条目覆盖矩阵（C01–C40）

本文件是批次 C 的**可证伪覆盖账本**。论文条目 `C01`–`C40` 的稳定编号与论文锚点来自
[`semantic-mapping.md`](semantic-mapping.md) §2；语义裁决以 `docs/论文.pdf` 为准。
`tests/Gocordis.Conformance.Tests/PaperItemMatrixTests.cs` 会读取本文件并逐行校验：
ID、论文锚点、状态、测试名或文档理由必须与该测试内的 `MemberData` 完全一致；
标为 `已覆盖` 的测试名还必须在本测试程序集中通过反射真实存在。
删除/改名测试、虚报覆盖、删行或改状态都会让该测试变红。

状态计数：已覆盖 38 / 待补 0 / 接受差异 2（共 40 项）。

**编号口径必须区分**：下表的 `Cxx` 是论文语义条目编号（`semantic-mapping.md` §2），
不是平台收敛编号。平台收敛编号 `PC-01`–`PC-30` 来自
`docs/review/GOCORDIS - Platform Convergence E2E Gate Report v0.1.md`：
`PC-03` 依赖激活 ≠ `C03` effect 上下文，`PC-19` reconciliation idempotence ≠ `C19` O-Insert/O-Retire。
C# `PlatformConvergenceTests.cs` 中的方法后缀是**本地用例序号**，不是 Go gate 编号；
对应关系为：`PC01_DeclarationReconcileIsIdempotent` → Go `PC-19`；
`PC12_PluginRegistryNeverCreatesASecondLifecycle` → Go `PC-27`；
`PC19_StaleActivationCannotAffectANewGeneration` → Go `PC-22`；
`PC30_CloseLeavesEveryFiberGone` → Go `PC-21`/`PC-30`。
`PC01`–`PC-30` 全集的状态另见 [`migration-coverage.md`](migration-coverage.md) §8，
本矩阵不把平台编号冒充论文覆盖。

`file:line` 为 2026-11-22 批次 C 补口时的快照；测试通过反射保证方法仍存在，
行号若因后续编辑漂移需同步更新本表。

| ID | 论文锚点 | C# 测试 file:line | 状态 | 证据 / 未覆盖理由 |
| --- | --- | --- | --- | --- |
| C01 | §1.1 p.4 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:17 (`C01_TemporalRecoveryAndSpatialResolutionAreOneContract`) | 已覆盖 | C01_TemporalRecoveryAndSpatialResolutionAreOneContract：同一组件先声明后提供（空间维门控+响应式激活），effect 安装/逆撤销（时间维恢复）；逆序断言 install/undo 日志。 |
| C02 | §3.1 p.9 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:71 (`C02_EffectInverseIsLeftInverseNotRightInverse`) | 已覆盖 | C02_EffectInverseIsLeftInverseNotRightInverse：同一上下文栈上断言 g∘f=id（撤销后回到 []）与 f∘g≠id（恢复态再前向产生不同状态）。 |
| C03 | Def 2 p.10；Thm 7 p.11 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:137 (`Thm68_Matrix`) | 已覆盖 | Thm68_Matrix |
| C04 | Def 1/3/6 pp.9–11；Thm 5 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:107 (`C04_EffectCompositionHasIdentityAndReverseOrderInverse`) | 已覆盖 | C04_EffectCompositionHasIdentityAndReverseOrderInverse：effect iterator 的空序列=单位元，左/右分组安装序列一致（结合律），逆按 g3∘g2∘g1 执行；不伪装公开 API 中存在 trackΓ 对象。 |
| C05 | Def 8/9 pp.12–13；Thm 10–11 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:186 (`C05_WitnessEffectMovesDeltaToGammaAndBack`) | 已覆盖 | C05_WitnessEffectMovesDeltaToGammaAndBack：断言 delta→gamma 见证在 Active 成立、逆把 gamma→delta，状态序列精确为 gamma,delta。 |
| C06 | Thm 16 p.15；Def 17/18 pp.15–16 | tests/Gocordis.Conformance.Tests/PaperConformanceTests.cs:13 (`C06_StrictLifoRecoveryLeavesNoResidue`) | 已覆盖 | C06_StrictLifoRecoveryLeavesNoResidue |
| C07 | Def 19/20 p.17 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:223 (`C07_CoeffectGetSetPreconditionsAndReversibleProvision`) | 已覆盖 | C07_CoeffectGetSetPreconditionsAndReversibleProvision：无 provider 的声明保持 Pending 且 Apply 零次（get 前置条件），未声明 get/set 均抛 UndeclaredCapabilityException，同键二次 Provide 抛 DuplicateProviderException，撤销后同键可由新 provider 占用。 |
| C08 | Def 21/22 p.18 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:300 (`C08_DeclarationGuardHasThreeObservableNotificationPhases`) | 已覆盖 | C08_DeclarationGuardHasThreeObservableNotificationPhases：不宣称读取 notify_d 本体；用公开可观测的 Pending/Loading→Active/Unloading 三阶段与 Context 读门控断言 activating/neutral/deactivating 的行为差异，并断言未绑定键从不读出。 |
| C09 | Def 23 p.19 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:363 (`C09_InSituProvisionAndDerivedIsolationBothRestoreOnUnload`) | 已覆盖 | C09_InSituProvisionAndDerivedIsolationBothRestoreOnUnload：就地式 Provide 的逆撤销 realm provider；派生式 Isolate 新视图在卸载后被丢弃（后续 consumer 看到原路由），两种方式均验证恢复。 |
| C10 | Def 24/25 pp.19–20；§4.4 p.56 | tests/Gocordis.Conformance.Tests/AuditRegressionTests.cs:119 (`IsolateCannotHijackAKeyOwnedByAnotherRealm`) | 已覆盖 | IsolateCannotHijackAKeyOwnedByAnotherRealm |
| C11 | Def 26/27 pp.20–21；§6.3 p.72 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:439 (`C11_MetaInterceptionIsRightBiasedAndDoesNotReload`) | 已覆盖 | C11_MetaInterceptionIsRightBiasedAndDoesNotReload：用字符串 monoid 断言 combine(declared,override)="DO"≠"OD"(右偏)，新组件读到 identity 证明派生视图不跨 root 泄漏，Generation 不变。 |
| C12 | Def 28–30 pp.21–22 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:476 (`C12_OneContextMediatesCapabilitiesEffectsMetaDataEventsAndChildren`) | 已覆盖 | C12_OneContextMediatesCapabilitiesEffectsMetaDataEventsAndChildren：单个 Context 内依次做 Require/Provide/EffectAsync/DeclareMeta+OverrideMeta+Meta/OnAsync+EmitAsync/LoadChildAsync，逐项断言外部可观测结果。 |
| C13 | Def 31–37 pp.23–25 | tests/Gocordis.Conformance.Tests/PaperConformanceTests.cs:93 (`C13_ObservationEquivalenceIgnoresGenerativeFiberIds`) | 已覆盖 | C13_ObservationEquivalenceIgnoresGenerativeFiberIds |
| C14 | Def 40/42 pp.26–27；Thm 43/45/47 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:574 (`C14_IndependentInversesCommuteAcrossPermittedOrders`) | 已覆盖 | C14_IndependentInversesCommuteAcrossPermittedOrders：两个独立 effect 分属不同 fiber，按两种排列撤销，断言逆集合相同且最终状态一致；不宣称单个 fiber 内可乱序（C# 仍严格 LIFO）。 |
| C15 | Def 48 p.31 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:631 (`C15_DeclaredComponentSurfaceGatesAndMatchesTheRuntimeView`) | 已覆盖 | C15_DeclaredComponentSurfaceGatesAndMatchesTheRuntimeView：Snapshot 的 Requires/Provides 与声明逐项相等，缺依赖时 Apply 零次，满足后恰好一次且绑定可见。 |
| C16 | Def 49 p.32 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:676 (`C16_FiberStatesAndCommittedProviderViewAreObservable`) | 已覆盖 | C16_FiberStatesAndCommittedProviderViewAreObservable：实测 Loading→Active→Unloading→Gone，Loading 期 ω 为空、Active 后才出现绑定；另断言 Pending 与 Failed 终态。 |
| C17 | Def 50/51 p.32–33；Eq.46 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:742 (`C17_UniqueProviderAndBrokerBasedMultiImplementation`) | 已覆盖 | C17_UniqueProviderAndBrokerBasedMultiImplementation：同一 realm 同键第二个 provider 被拒（DuplicateProviderException），撤销后可重插；多实现通过 Broker 共存且路由随 fiber 卸载自动撤回。 |
| C18 | Def 52 p.35 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:821 (`C18_InstantiationRetiresChildrenBeforeTheirOwner`) | 已覆盖 | C18_InstantiationRetiresChildrenBeforeTheirOwner：父子指针与 ParentId 可观测；单独 O-Retire 子 fiber 不影响父；卸载父时逆序日志为 child-cleanup→parent-cleanup。 |
| C19 | §4.2.1 p.34 | tests/Gocordis.Conformance.Tests/PlatformConvergenceTests.cs:11 (`PC01_DeclarationReconcileIsIdempotent`) | 已覆盖 | PC01_DeclarationReconcileIsIdempotent |
| C20 | Def 53 p.35；Eq.48/49 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:889 (`C20_TargetBindingAndQuiescenceAreObservable`) | 已覆盖 | C20_TargetBindingAndQuiescenceAreObservable：缺依赖时 target 为 ⊥（Pending）且签名稳定；provider 提交后绑定到具体 fiber id；撤回后再次稳定于 Pending，全程无 Loading/Unloading 残留。 |
| C21 | §4.2.2 pp.36–37；Table 1 p.40 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:947 (`C21_LifecycleRulesAreObservableForIterativeActivation`) | 已覆盖 | C21_LifecycleRulesAreObservableForIterativeActivation：三场景 [Theory] 覆盖 L-Begin 门控、L-Iter 多步、L-Finish 全提交、L-Divert（在途步逆只覆盖已落步）、L-Leave（依赖消失回 Pending）、L-Unload（整栈逆序）。 |
| C22 | Def 54 p.36；§4.4 Config p.57 | tests/Gocordis.Conformance.Tests/PaperConformanceTests.cs:51 (`C22_DependencyLossIsWithdrawalNotFailure`) | 已覆盖 | C22_DependencyLossIsWithdrawalNotFailure |
| C23 | Def 55/56 pp.38–39；Lemma 57 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1093 (`C23_EffectsMayOnlyWriteOwnStateAndDeclaredProviderSlots`) | 已覆盖 | C23_EffectsMayOnlyWriteOwnStateAndDeclaredProviderSlots：越界 Provide 被 UndeclaredCapabilityException 拒绝，外部键的 provider 值不变，Snapshot 中 owner 的 Provides/Requires/绑定只含声明键。 |
| C24 | Def 63 p.43 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:25 (`Thm64_Matrix`) | 已覆盖 | Thm64_Matrix |
| C25 | Thm 64 p.43 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:25 (`Thm64_Matrix`) | 已覆盖 | Thm64_Matrix |
| C26 | Thm 68 p.46；Cor 69 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:137 (`Thm68_Matrix`) | 已覆盖 | Thm68_Matrix |
| C27 | Thm 70 pp.47–48 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:265 (`Thm70_Matrix`) | 已覆盖 | Thm70_Matrix |
| C28 | Thm 71 p.48 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1140 (`C28_IterativeStepsShareOneResolutionAndRecoverOnDivert`) | 已覆盖 | C28_IterativeStepsShareOneResolutionAndRecoverOnDivert：多步迭代每步 Require 得到同一值与同一绑定字符串；在途 Divert 时只逆已提交步，状态 Gone 且无 Failure。 |
| C29 | Def 72 p.49；Thm 73 pp.49–50 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:413 (`Thm73_Matrix`) | 已覆盖 | Thm73_Matrix |
| C30 | Def 74–76 pp.51–52；Thm 80 pp.54–55 | tests/Gocordis.Conformance.Tests/TheoremMatrixTests.cs:521 (`Thm80_Matrix`) | 已覆盖 | Thm80_Matrix |
| C31 | Lemmas 59–62 pp.40–42 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1218 (`C31_WritePartitionIsInvariantUnderRenamingAndVestigialRecords`) | 已覆盖 | C31_WritePartitionIsInvariantUnderRenamingAndVestigialRecords：名字双射后规范化结构（状态/声明边/绑定）相同；retire→重插后旧 fiber id 与旧绑定在 Snapshot 中不可见。 |
| C32 | §4.4 Asynchrony pp.55–56 | tests/Gocordis.Conformance.Tests/PlatformConvergenceTests.cs:42 (`PC19_StaleActivationCannotAffectANewGeneration`) | 已覆盖 | PC19_StaleActivationCannotAffectANewGeneration |
| C33 | §4.4 Failure p.56 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1321 (`C33_FailureIsIsolatedOnTheFiberAndBlocksReentry`) | 已覆盖 | C33_FailureIsIsolatedOnTheFiberAndBlocksReentry：子 fiber Apply raise → Failed，失败迭代内的逆恰好一次，父保持 Active（不向父传播），后续操作不触发重入。 |
| C34 | §4.4 Isolation/Configuration pp.56–57 | —（无 C# 测试） | 接受差异 | C# 无 Rehome API；realm 重赋值按 retire→新 fiber 实现，未逐条对齐 retire→deactivate→O-Remove→同名重插（semantic-mapping.md:C34）。 |
| C35 | §6.1 pp.70–71 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1367 (`C35_ReversibleBoundaryCoversOwnedEffectsButNotExternalEmission`) | 已覆盖 | C35_ReversibleBoundaryCoversOwnedEffectsButNotExternalEmission：Context 拥有的 effect 修改被逆恢复，组件主动调用外部 emission（Context 之外的委托）在卸载后仍然存在；据此断言"按位置判定"的可逆边界。 |
| C36 | §6.5 p.74 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1404 (`C36_DependencyCyclesAreRejectedAtMountWithAnExplicitChain`) | 已覆盖 | C36_DependencyCyclesAreRejectedAtMountWithAnExplicitChain：二元环在装载边界抛 DependencyCycleException 且消息含 A->B 链，被拒组件不产生 fiber；自依赖同样被拒。 |
| C37 | §6.3 p.72 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1451 (`C37_DeclaredCapabilitiesAreArbitratedAndMetaCanRefinePolicy`) | 已覆盖 | C37_DeclaredCapabilitiesAreArbitratedAndMetaCanRefinePolicy：未声明的 get/set 被拒、已声明的 get 解析成功；Meta 声明 10 与覆盖 99 改变子 fiber 读到的策略值而父 Generation 不变。 |
| C38 | §6.6 p.75 | —（无 C# 测试） | 接受差异 | C# 用 VersionedKey<T> + NuGet 版本范围，不实现 K×P 名义命名空间/peer 结构兼容（semantic-mapping.md:C38）。 |
| C39 | §6.4/§6.7 pp.73–77 | tests/Gocordis.Conformance.Tests/PaperGapClosureTests.cs:1508 (`C39_BackendNeutralFactoriesComposeThroughTheCoreContract`) | 已覆盖 | C39_BackendNeutralFactoriesComposeThroughTheCoreContract：三个不同工厂（managed/wasm/proc 标签）仅经 typed key + PluginRegistry 声明引入/撤回，观测结构相同、每份引入的逆各一次；不声称真实 IPC/WASM 后端执行（那属 Runtime.Tests）。 |
| C40 | §5.2.1 pp.65–69 | tests/Gocordis.Conformance.Tests/PlatformConvergenceTests.cs:28 (`PC12_PluginRegistryNeverCreatesASecondLifecycle`) | 已覆盖 | PC12_PluginRegistryNeverCreatesASecondLifecycle |

## 未覆盖项的处理原则

`待补` 不等于"论文语义已实现"：它表示该项没有可直接执行的单点断言。
批次 C 补口后 `待补 = 0`；仅 C34/C38 为 `接受差异`，理由逐条写在表里，
它们不是"待补"的复制粘贴：C34 缺少 Rehome API 导致无法逐条对齐 revision 规则序列，
C38 是 .NET 依赖管理机制对 K×P 名义命名空间的显式替代。

新补的 24 项断言中，C08/C14/C39 是**行为等价映射**而非字面复制论文内部对象：
C08 用公开 FiberState/Context 读门控映射 notify_d 三态，不宣称能读到 notify_d 本身；
C14 用两个独立 fiber 的两种撤销排列验证逆可交换，不宣称单 fiber 内可乱序；
C39 用三个可替换工厂验证 Core 契约的后端无关性，不宣称覆盖进程/WASM 执行细节。
其余各项均直接断言公开 API 的可观察行为，没有反射检查内部状态，也没有把
其它条目的测试数字当作本条覆盖。后续若补上测试或改变状态，需同时改
`PaperItemMatrixTests.PaperItems()` 与本表的状态、证据与计数，测试会拒绝单边修改。
