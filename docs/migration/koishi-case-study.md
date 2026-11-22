# 论文 §5.3 Koishi 案例的 C# 复现

目的：用一个真实应用形态证明"宿主只提供领域词汇，功能经声明/能力/事件组合而成"，
并把它逐项锚定到论文 §5.3、AGENTS.md 十二铁律与可执行测试。
案例代码在 `samples/KoishiCase/`，断言在 `tests/Gocordis.Conformance.Tests/KoishiCaseTests.cs`
（该文件由另一 agent 负责，本文件不修改它）。

## 1. 应用拓扑

| 角色 | C# 落点 | 论文 / 铁律锚点 |
| --- | --- | --- |
| 服务端宿主 | `KoishiHost`：一个 `Runtime` + 一个 `PluginRegistry`；`HostVocabulary` 只 `Provide(K.Commands, K.Message)` | §5.3 宿主提供领域词汇；铁律 1/7/9 |
| DB driver | `DatabasePlugin`：`Provide(K.Database)`，`EffectAsync` 登记连接逆 | 铁律 3/4；C02–C06 |
| IM adapter | `AdapterPlugin`：`Require(K.Database)` → `Provide(K.Adapter)`；配置 `tag` 来自 manifest | 铁律 1/2；C07/C08/C20 |
| 功能插件 | `FeaturePlugin`：`Require(K.Adapter)` + `Require(K.Commands)` + `OnAsync(K.Message, ...)`，effect 逆撤销命令注册与事件处理 | 铁律 3/6/11；C13/C22 |
| 浏览器控制台 | `BrowserConsoleApp`：**另一个独立 `Runtime`**，`UiHost` 提供 `UiContributionRegistry.Key`，`PagePlugin` 注册页面并在逆中注销 | §5.3 双应用组合；铁律 7/11；C40 |

词汇定义在 `samples/KoishiCase/Vocabulary.cs`：`K.Database`、`K.Adapter`、`K.Commands`、
`EventKey<string> K.Message`。插件之间只通过这些 key 协作，没有插件→插件的 import。

## 2. 从声明到调和（不写启动顺序）

`KoishiHost.StartAsync()` 先装载 `HostVocabulary`，再 `PluginRegistry.ApplyAsync` 提交三个
manifest（db / adapter / feature）。启用开关与 adapter 配置改的是**声明**：

| 用户动作 | C# 实现 | 观察到的论文语义 |
| --- | --- | --- |
| `SetEnabledAsync("feature", false)` | `PluginRegistry.ApplyAsync` 把 enabled 置 false → Core Dispose 该 fiber | entry 保留、fiber Gone；只有该插件的 effect 被逆（铁律 8；C40） |
| `ReplaceAdapterAsync("v2")` | 同名 entry 的配置变更 → 旧 fiber 退役、新 fiber 装载 | provider episode 更新，依赖者重激活；DB 连接不被误关（C27；铁律 5/8） |
| DB provider 消失 | 无 `Provide(K.Database)` 的 active fiber → adapter/feature 依次回 Pending | 依赖消失是撤回而非 Failed；恢复后自动重激活（C22；铁律 6） |
| 消息事件 | `EmitAsync(K.Message, ...)` 经事件 key 路由到已订阅的 feature | 跨组件通知经事件，不经对象引用（铁律 11） |
| Web Console | 独立 `Runtime` + 贡献注册；页面 owner 用 `ContributionOwner.For("browser", Name)` | 第二个 Cordis 应用；卸载只撤自己的 contribution（§5.3；C40） |

## 3. 与十二铁律的逐条对应

| 铁律 | Koishi 案例中的体现 |
| --- | --- |
| 1 声明权威 | 三个插件的 `Inject()/Provide()` 与 `Require/Provide` 调用逐 key 一致；manifest 的 `Requires/Provides` 只是描述性元数据。 |
| 2 不做启动顺序编排 | feature 先声明也只会 Pending；DB→adapter→feature 的激活顺序由 Runtime 调和决定。 |
| 3 后果经 Effect/Provide | 数据库连接、adapter 注册、命令注册、消息 handler 全部在 `EffectAsync` 逆中撤销。 |
| 4 Active ⇔ 就绪 | `LoadAsync` 返回时 provider 已提交；测试断言 Active 后能力可读。 |
| 5 失败 = raise | 插件失败只影响自身 fiber；不向宿主传播、不自动重试；重新启用是一次新装载。 |
| 6 依赖消失 → Pending | `Koishi_DisablingProviderRetractsDependentWithoutFailure` 验证依赖者不 Failed。 |
| 7 单一生命周期权威 | 宿主与控制台各有一个 `Runtime`；`PluginRegistry` 不写 fiber 状态。 |
| 8 entry 存活 / fiber 单次启停 | disabled 插件仍在 manifest 中；重新 enabled 得到新 fiber。 |
| 9 持久化三分 | `manifest.toml` 存声明；业务数据（连接/命令）不写盘；`Snapshot()` 只读。 |
| 10 趁依赖还在时落盘 | feature 的 effect 逆在 adapter 仍可 `Require` 时执行；清理顺序由 Core 保证。 |
| 11 一个 key 一个提供者 | `K.Database`/`K.Adapter` 各只有一个 provider；多实现应走 `Broker<T>`，案例未自建注册表。 |
| 12 合作式假设（工程约束） | Apply 有限步；effect 逆可在取消时完成；声明环由 Core 在装载边界拒绝。注意论文 Thm73 本身**不含**合作式前提（见 `semantic-mapping.md` §4.1）。 |

## 4. 可执行证据与计数口径

`KoishiCaseTests.cs` 当前用例（由另一 agent 维护，名称以其文件为准）：

- `Koishi_DisablePluginRevertsEffectsWithoutTouchingProviders`
- `Koishi_MissingProviderStaysPendingWithoutFailure`
- `Koishi_ProviderSwitchReactivatesOnlyItsDependents`
- `Koishi_WebConsoleIsAnIndependentSecondApplication`
- `Koishi_DisablingProviderRetractsDependentWithoutFailure`
- `Koishi_MessageEventDrivesFeatureEchoAndIsRevokedOnDisable`
- `Koishi_WebUiServesSecondApplicationPagesAndSnapshot`

最终通过数由主线程统一执行 `dotnet build Gocordis.sln --nologo -m:1` 与
`dotnet test Gocordis.sln --nologo --no-build -m:1` 确定：378/378，连续 35 次全绿；
结果回填 `execution-log.md`「批次 B/C 最终验证」。

## 5. 明确不宣称的内容

- 案例不实现插件市场、远程分发或浏览器侧真正的 TS/JS 调和循环；Web Console 用内嵌静态资产 +
  贡献 API 表达"第二个应用"（见 `migration-coverage.md` §6.9）。
- 案例不证明 Thm73 的完整形式化前提（见 `paper-coverage-matrix.md` C29 的证据边界）。
- `KoishiCaseTests` 已覆盖消息事件链、provider 启停/恢复、adapter 替换与独立 Web Console，
  但案例不实现 Koishi 完整插件生态、市场或远程分发；这是产品化边界，不是迁移阻塞项。
