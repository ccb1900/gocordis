# UI Contribution Lifecycle（批次 A）语义对照

Date: 2026-11-22
Branch: `migration/dotnet`

本文件只记录**批次 A：Page + Client Module 生命周期闭环**的语义对照与边界。
不宣称 Explorer / Dashboard / Inspector / 插件市场已迁移。

## 1. 四个参照系

| 参照系 | 本批继承的语义 | 明确不照搬 |
| --- | --- | --- |
| 论文 | UI 注册是组件 Activation 的可逆 Effect；依赖变化驱动激活/撤销；Owner 绑定 Fiber + Activation | 任何具体 UI 技术 |
| JS Cordis | Component / Context / Effect 的注册-撤销机制；一个插件贡献前后端能力 | JS 动态对象与 API 形状 |
| Koishi | 一个插件同时贡献后端 Service 与前端页面；Console 服务插件生态 | 前后端拆成两个插件身份 |
| Go Gocordis | PageDefinition / PanelDefinition / ClientModule、Composition Registry、`/api/ui/*`、模块 revision URL、路径边界 | Go 接口与实现细节 |

## 2. 唯一生命周期权威

```text
Runtime (唯一 Fiber 生命周期权威)
   └── PluginRegistry (唯一插件声明/启停入口)
          └── Component Activation
                 ├── 后端能力（Key / ApiGateway）
                 └── UI 贡献（UiContributionRegistry）
                        ├── Page
                        ├── Panel（契约已实现，本批不做产品化渲染）
                        └── ClientModule
```

- `UiContributionRegistry` 是 Composition Registry：只存贡献数据。
- `WebUiServer` 是传输宿主：HTTP / SSE / 静态资源。
- `ClientModuleManager`（`web/console/src/lib/client-modules.ts`）只调和浏览器模块，不决定插件启停。
- 不存在 `UiPluginRegistry`，不存在第二套 Effect 栈。

## 3. Owner 与 Effect

`ContributionOwner` 现在包含：

```text
PluginId / ComponentId / FiberId / ActivationId
```

`Context` 只新增只读：

```text
FiberId / ActivationId / ComponentName
```

注册 API：

```csharp
context.RegisterUiPageAsync(registry, owner, page);
context.RegisterUiPanelAsync(registry, owner, panel);
context.RegisterUiClientModuleAsync(registry, owner, module);
context.RegisterApiQueryAsync(api, owner, name, handler);
```

每个注册都通过 `Context.EffectAsync(install, inverse)`：

- install：在当前 Activation 内注册贡献
- inverse：按 Owner 精确撤销，旧 Activation 不能误删新 Activation

## 4. HTTP 契约

| 端点 | 契约 |
| --- | --- |
| `GET /api/ui/pages` | `{ data: { pages: UIPage[] } }` |
| `GET /api/ui/panels` | `{ data: { panels: UIPanel[] } }` |
| `GET /api/ui/client-modules` | `{ data: { modules: [{name,url}], revision } }` |
| `GET /client-modules/{name}/{**path}` | 同源插件前端资源，安全边界校验 |
| `GET /api/stream` | observation + `event: ui-manifest` revision 通知 |

模块 URL 使用 8 位 revision hash，保证内容/注册变化时 ESM 重新导入。

## 5. 测试证据

- `UiCompositionTests`（7）：生命周期、重复启停、重复 ID、Owner 隔离、并发、Runtime 关闭撤销
- `WebUiCompositionTests`（8）：typed pages/panels/modules、静态资源安全、revision URL、SSE、禁用后撤销
- `web/console/src/lib/client-modules.test.ts`：模块激活失败回滚、manifest 拉取失败保留、revision 重载（既有前端测试，CI `npm test` 执行）
- `KoishiCaseTests`：插件组合与 UI/E2E 回归

## 6. 尚未迁移 / 明确边界

以下属于 Go Console 的后续批次或产品化范围，本批不宣称完成：

- Explorer / Inspector 的完整前端产品化
- Dashboard 全部视图与诊断面板
- Widget / Settings / Action 的完整前端渲染（强类型契约已具备，产品化未闭合）
- 插件市场服务端、远程安装与分发
- 桌面宿主适配
- JS API 名称/选项/异常的逐项等价

本批完成标准仅为：**Page + Client Module 从插件激活、浏览器加载，到插件停用清理，形成完整闭环。**
