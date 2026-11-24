# gocordis → C#/.NET 迁移文档集（历史归档）

> **本目录是历史迁移记录，不是当前状态。** 当前分支、HEAD、产品入口、验证计数与已知边界只看
> [`../current-status.md`](../current-status.md)；强制工程规范看根目录 [`AGENTS.md`](../../AGENTS.md)。
> 本目录中的分支名、SHA、轮次计数（396/396、443/443 等）都是当时的历史记录，**不得当作当前 HEAD 的事实**。
> 仍然有效的是语义裁决顺序（论文 → JS Cordis → Go）、条目对照表与裁决理由，下面保留作为规范参考。

本目录曾是 gocordis 从 Go 迁移到 C#/.NET 的执行权威；迁移结束后转为归档。
任何语义争议仍以论文为最终裁决，参照本目录记录的权威顺序。

> 目标背后的目标：把已经验证过的语义**固化成可独立验收的规范**，使七个阶段可以逐段冻结，
> 最终只维护 C# 版本，而不是做一次 Go 代码的逐行翻译。

## 文档索引

| 文档 | 对应阶段 | 回答的问题 | 冻结条件 |
| --- | --- | --- | --- |
| [`migration-plan.md`](migration-plan.md) | 全周期 | 技术方向、七阶段、验收标准、范围边界、分支策略 | 本文件批准即冻结；阶段验收后不得回头重审 |
| [`migration-baseline.md`](migration-baseline.md) | 阶段 0 | Go 基线到底是什么；哪些事实不可变 | 记录 commit + 工作区叠加层 + 模块/测试清单后冻结 |
| [`semantic-mapping.md`](semantic-mapping.md) | 阶段 0 / 2 | 论文、JS Cordis、Go 三层语义如何对应；冲突如何裁决 | 语义条目编号后冻结；C# 实现逐条对齐 |
| [`paper-coverage-matrix.md`](paper-coverage-matrix.md) | 批次 C | 论文 C01–C40 逐项：锚点、C# 测试 file:line、覆盖状态（含待补/接受差异） | `PaperItemMatrixTests` 逐行校验，状态与测试名单边改动即红 |
| [`theorem-matrix.md`](theorem-matrix.md) | 批次 C | Go 定理测试 → C# 矩阵用例映射，标注移植/接受差异 | 每定理 ≥3 场景；Thm70 已归位 |
| [`js-cordis-csharp-differences.md`](js-cordis-csharp-differences.md) | 批次 C | JS 对齐表与接受差异账本每一项 → C# 决定 + 证据 | 不得空缺；`待补` 不冒充 `接受差异` |
| [`koishi-case-study.md`](koishi-case-study.md) | 批次 C | 论文 §5.3 双应用组合在 C# 的复现与断言 | 与 `KoishiCaseTests` 对齐 |
| [`contract-closure.md`](contract-closure.md) | 最终冻结 | Manifest 字段级契约、三后端统一路径、四账本口径与显式边界 | 字段级验收，不把能力清单当实现证据 |
| [`ui-contribution-lifecycle.md`](ui-contribution-lifecycle.md) | UI 批次 A | Page + Client Module 生命周期闭环、四参照对照与未迁移边界 | 与 `UiCompositionTests` / `WebUiCompositionTests` 对齐 |
| [`feature-matrix.md`](feature-matrix.md) | 完整迁移 | Go 52 个功能域逐项源码证据、C# 状态、Gap/Target/Acceptance | 批次 1 账本；实施与验收唯一入口 |
| [`plugin-package-lifecycle.md`](plugin-package-lifecycle.md) | 批次 2 | 插件包/安装/声明/启停/升级/卸载闭环与 CLI | 与 `PluginPackageLifecycleTests` / `CliAndSourceTests` 对齐 |
| [`extension-parity.md`](extension-parity.md) | 批次 3 | Go 24 扩展缺口关闭、仍保留差异与新增验收测试 | 按 `feature-matrix.md` 的 Gap/Target 逐项关闭 |
| [`extension-integration.md`](extension-integration.md) | 扩展使用面 | 哪些扩展进入产品组合、哪些仅测试及理由 | 诊断 / Host ACL / 出向 HTTP 已真实接线 |
| [`runtime-integrity.md`](runtime-integrity.md) | 批次 4 | Managed/Process/WASM/Go Native 边界、错误策略、64 MiB 上限 | 与 BackendLifecycleTests / WasmRuntimeTests 对齐 |
| [`multi-host-ui.md`](multi-host-ui.md) | 批次 5 | Web/WinForms/Avalonia 同一插件 UI 契约与降级边界 | 与 `MultiHostUiContractTests` 对齐 |
| [`review-fix-closure.md`](review-fix-closure.md) | 审查修复 | Adapted/Divergent 收敛、可关闭项与平台边界 | 与 `CliProductTests` / `ProductGateTests` / `Gocordis.Product.Tests` 对齐 |
| [`package-security.md`](package-security.md) | 安全收敛 | Plugin ID、ZIP 资源上限、checksum 绑定、Store 文件系统事务 | 与 `PackageSecurityTests` 对齐 |
| [`final-calibration.md`](final-calibration.md) | 最终校准 | 唯一可信的 52 功能域状态、冲突修正与剩余能力清单 | 取代所有旧基线和状态计数 |
| [`ui-control-plane.md`](ui-control-plane.md) | UI 控制面 | 通过 UI 安装/配置/启停/升级/卸载插件的完整闭环 | 与 `PluginControlPlaneTests` + React Console 对齐 |
| [`control-plane-pluginization.md`](control-plane-pluginization.md) | 一切皆插件 | 控制面作为 Core Fiber 提供 typed capability，宿主只解析能力 | 与 `ControlPlaneIsAPluginContribution` 对齐 |
| [`review-round-2.md`](review-round-2.md) | 审查修复 | Round 2 P0/P1 关闭记录与下一轮候选 | 红测 + 本地验证 |
| [`review-round-3.md`](review-round-3.md) | 审查修复 | Round 3 剩余 P1/P2 关闭记录 | 红测 + 本地验证 |
| [`review-round-4.md`](review-round-4.md) | 审查修复 | Round 4 插件 CLI 工具链关闭记录 | 红测 + 本地验证 |
| [`review-round-5.md`](review-round-5.md) | 审查修复 | Round 5 远程只读插件源关闭记录 | 红测 + 本地验证 |
| [`review-round-6.md`](review-round-6.md) | 审查修复 | Round 6 包签名与验签关闭记录 | 红测 + 本地验证 |
| [`review-round-7.md`](review-round-7.md) | 审查修复 | Round 7 跨层事务、启动清理与原生 Host 边界 | 红测 + 本地验证 |
| [`review-round-8.md`](review-round-8.md) | 冻结前修正 | Round 8 Upgrade 依赖判断、清理重试、账本 SHA 与基线漂移 | 红测 + 本地验证 |
| [`product-execution-chain.md`](product-execution-chain.md) | 产品主链路 | 包 → Managed 后端 → 真实 Component → Core Fiber 的执行闭环 | 与 `ManagedPluginProductE2ETests` 对齐 |
| [`review-round-9.md`](review-round-9.md) | 产品闭环 | Round 9 真实插件执行、CLI 脚手架、读取安全与共享依赖计划 | 红测 + 本地验证 |
| [`pc-gate-disposition.md`](pc-gate-disposition.md) | Go 收敛 | PC01–PC30 逐项去向，消除“24 项未移植”无状态描述 | 与 C# 场景测试对齐 |
| [`review-round-10.md`](review-round-10.md) | 四维冻结 | 论文 C34/迭代器/失败语义、JS 增量、Koishi E2E、Go 基线裁决 | 红测 + 本地验证 |
| [`review-round-11.md`](review-round-11.md) | 实跑验收 | 真实启动应用、安装 Managed 插件、浏览器检查控制台 | 运行实例 + 红测 |
| [`review-round-12.md`](review-round-12.md) | 事务与安全 | Registry 多字段组合/回滚、HMR 真实路径、Process 最小环境、观察游标与 Include 环检测 | 红测 + 本地验证 |
| [`review-round-13.md`](review-round-13.md) | 论文语义 | 观察等价语义身份、Divert 逆失败、Context 声明权威、HMR 版本原子性 | 红测 + 本地验证 |
| [`../guide/csharp-developer-manual.md`](../guide/csharp-developer-manual.md) | 批次 C | C# 开发者手册：十二铁律 → C# API 与反模式 | 与 `AGENTS.md` 十二铁律逐条锚定 |
| [`migration-coverage.md`](migration-coverage.md) | 全周期 | 每个 Go 模块迁移到哪个 C# 项目；用什么机制替代；什么不迁移 | 清单逐项推进；状态只能由验收证据推进 |
| [`execution-log.md`](execution-log.md) | 全周期 | 每一步做了什么、为什么、证据是什么 | 持续追加，不回改历史条目 |
| [`review-closure-2026-11-22.md`](review-closure-2026-11-22.md) | 终审 | 外部审查问题逐条闭合证据与仍存限制 | 一次性记录 |
| [`final-acceptance.md`](final-acceptance.md) | 收尾 | 七阶段结果、最终验证、未达成缺口 | 一次性总结，缺口逐项登记 |
| [`baseline.sha256`](baseline.sha256) / [`verify-baseline.sh`](verify-baseline.sh) | 阶段 0 | 如何独立验证基线未被偷偷改动 | 任一校验失败即视为基线漂移 |

## 权威顺序（冲突时从上到下裁决）

1. **Dynamic Composable Runtime 论文**（`docs/论文.pdf`）—— 决定框架语义。
2. **JavaScript Cordis**（基线 `5e2e6c17…`，cordis 4.0.0-rc.13）—— 成熟实现与能力覆盖参照。
3. **当前 gocordis / Go** —— 已有代码、测试与实现经验的迁移参照。

不得为了照搬 Go 代码而保留与论文冲突的语义。

## 当前进度

- 阶段 0：**已验证**（基线冻结、语义对照、迁移清单；见 [`execution-log.md`](execution-log.md)）。
- 阶段 1：**已验证**（.NET 10 Solution 骨架、依赖边界架构测试、`dotnet build`/`dotnet test` 全绿）。
- 阶段 2：**已验证**（Core 语义内核完整可运行，含 Realm/MetaKey 与确定性重放）。
- 阶段 3–6：**已执行**（Manifest/Registry、Managed ALC、WASM/Process、UI/Console/API 均有可运行实现）。
- **原缺口已逐项关闭**：WASM 能力桥/HMR、Process 边界行为、官方扩展套件、内嵌前端、
  定理与平台收敛测试；Windows runner 实跑由 GitHub Actions `windows-dotnet` workflow 覆盖；CI 状态必须按 SHA 核对，
  不得用历史 SHA 的成功结果代表当前提交。历史留痕：`2ccdaad` Windows 曾失败，
  `62d7de15` 修复后三组 CI 全绿。当前 CI 已移除全部 Go 步骤，仅保留 `ci`
  与 `windows-dotnet`；当前 SHA 以 GitHub Actions 现场结果为准。
  详见 [`final-acceptance.md`](final-acceptance.md) §4。
- **批次 A（Channel 驱动生命周期决策域）已完成**：冻结基线 119 项测试
  （Core 22 / Conformance 28 / Extensions 19 / Runtime 50）；新增回归用例后为 **123**，
  连续 10 次全绿，见 [`adr-channel-orchestrator.md`](adr-channel-orchestrator.md)。
- **批次 B（PluginRegistry 事务、Discovery/Config、三后端统一协议）已完成**：声明批次具备
  Prepare/Commit/Rollback；坏清单隔离、删除文件调和、坏版本不推进；Managed/WASM/Process
  共用统一后端协议与世代栅栏。
- **批次 C（论文矩阵 / 定理矩阵 / JS 差异统一 / 最终验收）已完成**：C01–C40 矩阵
  38 已覆盖 / 0 待补 / 2 接受差异；Thm64/68/70/73/80 每定理 3 场景；
  Koishi 真实消息链、provider 启停与 HTTP E2E 已覆盖。历史基线 **396/396**，连续 39 次全绿；
  控制面插件化、R7/R9 产品主链路、R10 四维修正与 R11 实跑修正后当前 **443/443**，见
  [`product-execution-chain.md`](product-execution-chain.md) 与
  [`review-round-10.md`](review-round-10.md)。
  Go 基线当前存在漂移，尚未通过验证，见 `review-round-8.md`。
- 后续阶段状态以 [`migration-coverage.md`](migration-coverage.md) 的逐项状态为准，不在本文件重复维护。

Existence / authority audit: [`existence-audit.md`](existence-audit.md) (current per-capability classification and single-authority map; maintained alongside `docs/current-status.md`, not a historical round).
Migration completeness ledger: [`completeness-ledger.md`](completeness-ledger.md) (Go/JS/C# capability matrix with source anchors, status, test evidence and acceptance conditions; maintained current).
