# Manifest 契约与四账本收敛（最终冻结记录）

Date: 2026-11-22
Branch: `migration/dotnet`

本文件把“字段存在”与“契约已实现”区分开，避免账本用功能清单冒充字段级验收。
论文语义裁决仍以 `docs/论文.pdf` 为准；Go/JS 对照只是参照，不是规范。

## 1. 统一 Manifest 契约

C# 侧只有一种声明形状：`Gocordis.Plugins.PluginManifest`。Managed、WASM、Process
三种执行后端都通过 `PluginRegistry` 的工厂接收 `Manifest.Config`，没有第二套 manifest
解析器，也没有第二套生命周期权威。

| 字段 | 解析 | 校验 | 运行时用途 | 状态 |
| --- | --- | --- | --- | --- |
| `id` | 必需字符串 | 缺失 → `ManifestParseException` | entry 身份 | 已实现 |
| `type` | 可选字符串，默认 `id` | 非字符串 → `ManifestParseException` | 选择工厂 | 已实现 |
| `enabled` | 可选 bool，默认 `false` | 非 bool → `ManifestParseException` | Load / Dispose 开关 | 已实现 |
| `config` | 可选 table | 非 table → `ManifestParseException` | 传给后端工厂 | 已实现 |
| `requires` | 可选 string 或 string 数组 | 非法类型/非字符串元素 → `ManifestParseException` | 描述性字段；真实依赖边来自 `Inject()` | 已实现（描述性） |
| `provides` | 同 `requires` | 同 `requires` | 描述性字段；排序辅助；真实性由 `Provide()` + Core 裁决 | 已实现（描述性） |
| `version` | 可选字符串，默认 `1.0.0` | `NuGetVersion.TryParse`；非法 → `ManifestParseException` | 归一化语义版本（`1.2` → `1.2.0`） | 已实现 |
| `source` | 可选字符串 | 非字符串 → `ManifestParseException` | 来源描述 + fingerprint | 已实现 |
| `checksum` | 可选字符串 | 非字符串 → `ManifestParseException` | 参与 fingerprint，触发替换 | 已实现（字段/指纹） |
| 未知字段 | — | 拒绝并报告 id 与字段名 | 防止配置漂移被静默吞掉 | 已实现 |

**明确边界**：`checksum` 目前只做字段解析和 fingerprint 输入，**没有实现模块内容哈希校验**。
因此任何“checksum 校验已验证”的表述都是过度声称；正确表述是“checksum 字段与替换语义已实现，
内容校验未实现（显式边界）”。

**错误语义统一**：所有声明级错误在 Component 构造前以 `ManifestParseException` 抛出；
批次内的重复 provider、未知 type、依赖环在 `PluginRegistry.ApplyAsync` 的 Prepare 阶段抛出；
Apply 失败走 Rollback，不会登记 Failed fiber 为活插件。

证据：`ManifestContractTests`、`DiscoveryConfigTests`、`PluginTransactionTests`。

## 2. 四账本清理口径

| 账本 | 当前口径 | 边界 |
| --- | --- | --- |
| 论文 `paper-coverage-matrix.md` | C01–C40：38 已覆盖 / 0 待补 / 2 接受差异；Thm64/68/70/73/80 各 3 场景 | 可执行覆盖矩阵不是数学证明；C34/C38 保留原因与边界 |
| JS Cordis `js-cordis-csharp-differences.md` | 能力等价已逐项给出 C# 决定与证据 | 公开选项、异常传播、资源关闭、边界条件未逐项证明等价；产品化阶段继续 |
| Koishi `koishi-case-study.md` | 核心组合行为已复现：typed key 依赖、provider 撤回/恢复、adapter 替换、独立 Runtime | 不代表 Koishi 完整插件生态、市场或控制台产品 |
| Go 迁移 `migration-coverage.md` / `final-acceptance.md` | 按最新代码与测试状态逐项回填；WASM 自定义段/HMR、Process 崩溃/降级、ConfigWatcher 竞态均已验证 | tx guard、checksum 内容校验、Koishi 生态为显式边界 |

## 3. 明确不做

- 不重写 Core，不替换 Channel 决策模型。
- 不重新设计三种 Runtime，不新增第二套 Manifest 或 Host。
- 不为了 JS 名称一致牺牲论文语义。
- 不开发 Koishi 插件市场服务端。

本文件是最终冻结记录；后续新增能力必须更新对应账本并重新跑完整验收。
