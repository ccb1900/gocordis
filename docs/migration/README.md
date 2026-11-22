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
  定理与平台收敛测试；Windows runner 实跑由 CI 配置覆盖（本机 Linux 无法执行）。
  详见 [`final-acceptance.md`](final-acceptance.md) §4。
- 后续阶段状态以 [`migration-coverage.md`](migration-coverage.md) 的逐项状态为准，不在本文件重复维护。
