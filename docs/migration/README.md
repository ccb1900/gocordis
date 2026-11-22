# gocordis → C#/.NET 迁移文档集

本目录是 gocordis 从 Go 迁移到 C#/.NET 的**唯一执行权威**。迁移期间不再另开设计文档；
任何语义争议回到这里记录的三级权威顺序，并以论文为最终裁决。

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
  定理与平台收敛测试；Windows runner 实跑由 GitHub Actions `windows-dotnet` job 覆盖；CI 状态必须按 SHA 核对，
  不得用历史 SHA 的成功结果代表当前提交。历史留痕：`2ccdaad` Windows 曾失败，
  `62d7de15` 修复后三组 CI 全绿；当前 SHA 以 GitHub Actions 现场结果为准。
  详见 [`final-acceptance.md`](final-acceptance.md) §4。
- **批次 A（Channel 驱动生命周期决策域）已完成**：冻结基线 119 项测试
  （Core 22 / Conformance 28 / Extensions 19 / Runtime 50）；新增回归用例后为 **123**，
  连续 10 次全绿，见 [`adr-channel-orchestrator.md`](adr-channel-orchestrator.md)。
- **批次 B（PluginRegistry 事务、Discovery/Config、三后端统一协议）已完成**：声明批次具备
  Prepare/Commit/Rollback；坏清单隔离、删除文件调和、坏版本不推进；Managed/WASM/Process
  共用统一后端协议与世代栅栏。
- **批次 C（论文矩阵 / 定理矩阵 / JS 差异统一 / 最终验收）已完成**：C01–C40 矩阵
  38 已覆盖 / 0 待补 / 2 接受差异；Thm64/68/70/73/80 每定理 3 场景；
  Koishi 真实消息链、provider 启停与 HTTP E2E 已覆盖。最终 **265/265**，连续 16 次全绿。
- 后续阶段状态以 [`migration-coverage.md`](migration-coverage.md) 的逐项状态为准，不在本文件重复维护。
