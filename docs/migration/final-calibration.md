# Migration Ledger Final Calibration

Calibration date: 2026-11-22
Go source baseline: `ed2ef724a24331ecc3fa21974539315fd90212bf`
C# calibrated target: current `migration/dotnet-complete` HEAD (product host added after calibration)

本文件是唯一可信的最终账本校准。它取代旧文档中的基线、状态计数和“不迁移”列表。
其他迁移文档中的状态冲突，以本文件为准。

## 1. 状态定义（只允许四类）

| 状态 | 含义 |
| --- | --- |
| Equivalent | 可观察行为、生命周期与错误语义与 Go 参照等价；实现可不同。 |
| Adapted | Go 能力已用 C#/.NET 原生方式实现，但 API 形状、平台边界或子命令范围有明确适配。 |
| Divergent | 有意不照搬，论文/平台理由明确；不是等价，也不是遗漏。 |
| Missing | 无实现。行级 Missing 必须为 0，否则不能冻结。 |

`Partial` 已从状态集合中移除。原先的 Partial 已重新归为 `Adapted` 或 `Equivalent`。

## 2. 校准后的行级统计

```text
Equivalent 44 / Adapted 3 / Divergent 5 / Missing 0   (total 52)
```

### 3 个 Adapted 行

| Feature | 适配内容 |
| --- | --- |
| plugin CLI (create/doctor/package/sandbox) | 全部实现：`plugin create/doctor/pack|package/sandbox`；sandbox 是 declaration-only 加载检查，不是安全沙箱。 |
| WinForms UI host | Host Profile + RenderPlan + web-module 降级已实现；原生窗口/控件 Renderer 未实现。 |
| Avalonia UI host | 同 WinForms；仅契约层适配。 |

### 5 个 Divergent 行

| Feature | 语义差异理由 |
| --- | --- |
| Provider realm/isolation | C34：无 Go `Rehome` API，采用逐 Key Realm 路由。 |
| Observation/equivalence | C# 使用线性化 Snapshot + HTTP 观察，不暴露 Go EventSink 对象。 |
| HMR | 通用 target/binding/usage 已迁移；WASM adapter 仍是 warm-validate + 旧 Fiber Gone 后提交。 |
| Managed runtime | ALC 不是安全沙箱；Go 无等价物。 |
| Go Native runtime | .NET 不加载 Go native plugin，Process backend 作为隔离替代。 |

## 3. 剩余子能力缺口（不是行级 Missing，但必须诚实列出）

| 缺口 | 当前决定 | 影响 |
| --- | --- | --- |
| 远程 package registry | 已实现 `HttpPluginPackageSource` + 本地只读 `PluginSourceServer` + `registry serve` / `plugin fetch` | 可自托管只读源；不是 Koishi 市场服务 |
| 包签名与来源真实性 | RSA-2048 可选签名/验签已实现（keygen/sign/doctor --public-key） | 无签名包仍可读；签名 opt-in |
| WinForms/Avalonia 原生控件 Renderer | 明确延期，不为此造第二套 Plugin Registry | 两种桌面 Host 目前只消费 RenderPlan |
| Koishi 完整市场/Console/聊天插件生态 | 有意不迁移 | KoishiCase 只验证组合语义 |
| JS 公开 API 选项/异常/关闭时序严格等价 | 接受差异 | 机制等价，API 形状不同 |
| WASM 零停机原子 HMR | 接受差异 | 当前为 warm-validate + 旧 Fiber Gone 后提交 |
| UI 控制面鉴权 | 仅 loopback 安全；remote 需外部鉴权或宿主 AccessPolicy | `--allow-remote` 无 policy 时写端点被拒绝（403） |
| 平台收敛 gate 覆盖 | 代表性 6/30，不宣称全覆盖 | 其余 24 项未移植 |

因此：**行级 Missing = 0，但“Go 100% 逐能力等价”仍未成立**。等价的是主体框架与迁移范围；上述子能力属于明确边界或延期项。

## 4. 冲突修正清单

| 原冲突 | 修正后 |
| --- | --- |
| feature-matrix 仍以 `488e959d` 为 C# 基线 | 改为历史基线 + 当前校准目标 `60b13311` |
| feature-matrix 曾把 plugin CLI 标 Missing/Adapted | 改为 Equivalent，指向 PluginToolingTests |
| 状态集合含 Partial | 改为 Equivalent / Adapted / Divergent / Missing |
| extension-parity 说 create-cordis/client SDK Missing | 改为已完成，指向 CLI create 与 Gocordis.Client |
| plugin-package-lifecycle 曾列 doctor/sandbox 未完成 | 改为全部实现并指向 PluginToolingTests |
| migration-coverage §9 说 Application Package 不设计 | 改为已实现（Gocordis.Packaging + CLI） |
| migration-coverage §9 说 Go 前端资产不搬运 | 改为 web/console dist 由 C# host 交付，TS 为产品前端资产 |
| migration-plan 说不设计 Application Package | 标注为历史计划，已被后续实现 superseded |
| js-cordis-csharp-differences 说不新增 socks 后端 | 改为 SOCKS5 CONNECT 已实现，socks4/4a 与本地 DNS 语义不移植 |
| final-acceptance 仍写旧状态计数/基线 | 与本文对齐 |

## 5. 验证与结论

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 397/397（控制面插件化后）
```

结论：

- 主体架构冻结：不再重审 Core、PluginRegistry、三后端、UI Profile。
- 行级 Missing 为 0，可信的剩余清单就是第 3 节。
- 只有当新增需求明确要求原生桌面 Host 或 Koishi 产品生态时，才重新打开对应范围；远程只读源与包签名已实现。
