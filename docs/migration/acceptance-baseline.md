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

## 冻结规则

1. 本基线只记录**已核实**的证据等级，不把 E0/E1 升级为 E2。
2. 后续任务不允许因为非阻断性发现重新开启已冻结项；新证据必须写入新文件，不回改本文件。
3. Task 02 的进入条件已满足（P0-01 落地），立即推进，不再为表格计数拖延。
