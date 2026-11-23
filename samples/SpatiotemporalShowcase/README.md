# 时空组合 Showcase（Serious Plugin Examples）

一个**可观测的、全部以真实 managed `.gocordis-plugin` 包交付**的组合示例，演示框架的核心能力，而不是玩具式的单 Provide：

| 能力 | 论文条目 | 本套件中的体现 |
| --- | --- | --- |
| 可插拔（打包即插件） | C40 | 三个独立包经 CLI `pack/install`、声明驱动装载；禁用/启用/升级/卸载都走控制面 |
| 空间维 / 反应式 coeffect | C07/C17/C22 | `Worker`、`Dashboard` 通过**共享契约 Key** 声明跨插件依赖；provider 消失 → Pending，恢复 → 自动 Active |
| 时间维 / 可逆 effect | C02–C06 | 后台 writer、事件订阅、UI/API 贡献都由 `EffectAsync` 安装并逆序撤销（LIFO） |
| 事件（可逆注册） | §3.4.2 | `StoreChanged` 事件：provider 发、worker 收；订阅是 effect-owned 注册，撤销即消失 |
| 单一生命周期权威 | C19/C21 | 没有手写顺序、没有启动编排；一切由声明 + 调和驱动 |
| 多依赖门控 | C22 | `Dashboard` 同时依赖 store + metrics，两者都在才 Active |
| UI 贡献（可逆） | §4.4 Config | 页面 / 面板 / 客户端模块 / API 查询 / API 命令全部经 `EffectAsync` 注册，撤销即消失 |

## UI 能力覆盖矩阵（全部以"数据声明 + 可逆 effect"实现）

| UI 能力 | 提供位置 | 验证端点 |
| --- | --- | --- |
| 页面 · 声明式 `views` | `Dashboard` 的 overview / blocks 页 | `GET /api/ui/pages` |
| 页面 · 内置渲染器别名（insight-graph / runtime-inspector / framework-dashboard） | `Dashboard.RegisterRendererPages` | 同上，`renderer` 字段 |
| 页面 · 客户端模块渲染器（showcase-custom-page） | `UiClientModule/frontend/src/ui.tsx` → `ui.js` + Dashboard 声明 | 同上 |
| 视图块全谱：kv / table / list / stats / trend / query-table / form / json / widget / master-detail / calendar / 自定义块 | `Dashboard` blocks 页 `Views` | 页面 `views` 数组 |
| 页面 actions（含命令按钮） | `UiPageAction("刷新报告", "showcase/report")` | 页面 `actions` |
| 面板 · right / bottom（含 `pages` 过滤、`views`） | `Dashboard.RegisterPanels` | `GET /api/ui/panels` |
| 客户端模块（6 扩展点：widget / block / page / panel / action / settings） | `UiClientModule/frontend/src/ui.tsx` → `ui.js` | `GET /api/ui/client-modules` + `GET /client-modules/showcase-client-module/ui.js` |
| API 查询（views 的数据源） | `showcase/store` `showcase/events` `showcase/trend` `showcase/dashboard` `showcase/config` `showcase/keys` `showcase/worker` | `GET /api/query/<name>` |
| API 命令（form/json/action 的目标） | `showcase/config.set` `showcase/report` | `POST /api/command/<name>` |
| 配置面板类型化表单（表单/JSON、保存生效） | `StoreProvider/config/gocordis-config.schema.json` 随包声明 + manifest `Config` 值 | `GET /api/query/config/schema` + `POST /api/plugins/showcase-store/config` |

### UI 菜单分级

页面定义可声明 `group`（`/` 分隔的多级路径），控制台据此把菜单渲染成 antd 子菜单；
没有 `group` 的页面仍是顶级项。`order` 决定同级排序（小的在前，组排在同级页之前）。
本套件把 `showcase-graph` / `showcase-inspector` / `showcase-framework` 归到
`时空组合 Showcase/框架能力`，把 TSX 自定义页归到 `时空组合 Showcase/前端扩展`，
直接演示两级菜单。分组随 `gocordis-ui.json` 与插件一起安装/卸载。

### 配置面板示例

`showcase-store` 的包里带了 `config/gocordis-config.schema.json`（通过 `plugin pack --config-schema` 纳入声明并进 checksum），
`gocordis.app.json` 给它配置了 `intervalMs / label / verbose / mode` 的真实值。宿主在装配阶段把包声明的 schema 注册到
registry，`GET /api/query/config/schema` 返回 `components:[{type,schema}]`，控制台 ConfigEditor 据此渲染「表单」页的
integer / string / boolean / enum 字段；「JSON」页可直接编辑同一份配置。点「保存配置」经控制面写入声明存储并重新调和，
managed 插件的 ALC 以「版本+配置指纹」为 key 重新装载——所以配置变更不会撞 "already loaded"，fiber 保持 Active。

### 真正的自定义前端（TSX）

`UiClientModule/frontend/src/ui.tsx` 是**完整的 React/TSX 组件**（hooks + antd `Card`/`Descriptions`/`Statistic`/`Tag`/`Alert`/`Button`），不是声明式内置块的组合。它经 `@gocordis/plugin-kit` 用 esbuild 构建成单文件 ESM：`react`/`antd` 的导入在构建期重定向到控制台门面 shim，运行时全页面只有一个 React 实例。该模块在 `register(api)` 里注册页面/面板/块/widget/action/settings 六个扩展点，自定义页面/面板的声明由 kit 写进 `gocordis-ui.json`，随插件包一起安装/卸载。自定义页面通过 `ctx.hubQuery` 读插件自己的 `showcase/store`、`showcase/worker` 查询——前端不碰 Runtime 内部。

### 配套修复的前端查询契约

验证页面时发现三个既有的前后端契约缺陷（API 有数据但页面空白）：`framework/dashboard` 扁平 `counts`、`insight/graph` 扁平 `nodes`、`runtime/events` 未注册（404）。已按 Go 基线 DTO 修正 C# `ConsoleHost` 并为 Core 增加有界事件环。回归测试：`ProductGateTests`（断言前端消费的嵌套形状 + 事件查询非 404）、`web/console/src/views/framework-dashboard.test.ts`（把**真实 C# payload** 喂给前端解析器，断言解析出非零计数）。

关键点：**UI 完全属于插件包，不侵入控制面或框架**。页面/面板/客户端模块都写在各插件自己的 `gocordis-ui.json`（包根）+ `ui/*.json` 定义里；控制面用 `UiPackageBinder` 把该声明作为**插件 fiber 的 effect** 注册，所以安装插件即安装 UI，禁用/卸载插件即逆撤销 UI——不存在"框架里写死某插件的页面"。C# 组件只贡献运行时数据能力（API 查询/命令），那些不是 UI。只有真正需要自己的组件时才打包一个 JS `client-module`（也由同一机制随包装载）。

包级别还用 `--provides` / `--dependency` 声明了真实的能力图（`worker→store`、`dashboard→store+worker`、`client-module→dashboard`），所以控制台"依赖关系"面板不再空白，能看到包依赖、被依赖、能力依赖与提供能力。

## 为什么这很重要：打包形态下的契约共享

`Key<T>` 是**引用标识**（`src/Gocordis.Core/Capabilities.cs`）。过去 `PluginLoadContext` 只共享 `Gocordis.Core` / `Gocordis.Plugins` 两个程序集，于是两个独立插件包各带一份 `Contracts.dll` 时，它们的 Key 是不同对象，跨插件依赖在打包形态下**静默失效**——只有把组件塞进同一进程才能演示依赖。

本套件配套的框架补齐（commit 于本分支）修复了这一点：

- `PluginPackageManifest.SharedAssemblies` + CLI `--shared-assembly <name>` 声明共享契约程序集；
- `SharedAssemblyRegistry` 把该 DLL 提升进 default ALC 一次，并校验各插件版本一致；
- `PluginLoadContext` 对共享名返回 `null`，回落到 default 的**唯一**实例。

结果：`StoreProvider.Provide(K.Store)` 与 `Worker.Inject(K.Store)` 即使来自不同包，也解析到同一 Key —— 空间维组合在真实部署形态下成立。

## 项目结构

```
Contracts/        共享契约：IStore / IMetrics / StoreChanged / EventHistory / ShowcaseConfig / K 静态 Key / EffectLog
StoreProvider/    Provide(K.Store, K.StoreChanged)；后台 writer + 事件源 + store/events/trend 查询
Worker/           Inject(K.Store, K.StoreChanged)；事件订阅 + Provide(K.Metrics) + 查询
Dashboard/        Inject(K.Store, K.Metrics, API)；仅查询/命令；UI 在 ui/gocordis-ui.json（7 资源）
UiClientModule/
  frontend/src/ui.tsx   真正的 TSX 前端：React hooks + antd 组件，注册六扩展点
  frontend/ui.config.json  页面/面板声明（插件包的 gocordis-ui.json 由此生成）
  ui.js                 plugin-kit 把 TSX 打成自包含 ESM（react/antd 走控制台门面）
  gocordis-ui.json      构建产物：client-module + 自定义页面/面板（随包安装/卸载）
build-and-run.sh  一键 build(含 TSX) → pack(带 provides/dependency) → install → 写 manifest → 可选启动
```

## 运行

```bash
# 构建 + 打包 + 安装 + 写声明（到 /tmp/gocordis-showcase）
./samples/SpatiotemporalShowcase/build-and-run.sh

# 启动产品宿主（web 控制台）
dotnet run --project src/Gocordis.Platform/Gocordis.Platform.csproj -- \
  --manifest /tmp/gocordis-showcase/gocordis.app.json \
  --store /tmp/gocordis-showcase/plugins \
  --address http://127.0.0.1:8170
```

浏览器打开 `http://127.0.0.1:8170/`（硬刷新），左侧出现 **时空组合 Showcase** 页面，两个 KV 块分别展示 store 与 worker 的实时状态。

## 可观察的验证步骤

```bash
BASE=http://127.0.0.1:8170

# 1) 四个插件全部 Active（依赖已满足）
curl -s $BASE/api/plugins | python3 -m json.tool
curl -s $BASE/api/query/framework/dashboard

# 2) 查询由插件贡献、经共享契约读取的状态
curl -s $BASE/api/query/showcase/store
curl -s $BASE/api/query/showcase/events        # table / list / calendar 数据源
curl -s $BASE/api/query/showcase/trend         # trend 数据源
curl -s $BASE/api/query/showcase/worker
curl -s $BASE/api/query/showcase/dashboard

# 3) UI 能力：页面 / 面板 / 客户端模块 / 真正由 package 提供的 JS
curl -s $BASE/api/ui/pages | python3 -m json.tool
curl -s $BASE/api/ui/panels | python3 -m json.tool
curl -s $BASE/api/ui/client-modules | python3 -m json.tool
curl -s -o /dev/null -w "%{http_code}\n" $BASE/client-modules/showcase-client-module/ui.js

# 4) API 命令（form/json/action 的目标）：HTTP 202 且状态变化
curl -s -o /dev/null -w "%{http_code}\n" -X POST $BASE/api/command/showcase/report \
  -H 'Content-Type: application/json' -d '{}'
curl -s -X POST $BASE/api/command/showcase/config.set \
  -H 'Content-Type: application/json' -d '{"label":"ui-ok","threshold":"7"}'
curl -s $BASE/api/query/showcase/config        # {"doc":{"label":"ui-ok","threshold":7}}

# 5) 时间维证据：effect/undo 顺序（设置 GOCORDIS_E2E_LOG 后）
cat /tmp/showcase.log

# 6) 可逆 effect：按依赖逆序停用（控制面不允许留下悬空声明，见下方说明）
for id in showcase-client-module showcase-dashboard showcase-worker showcase-store; do
  curl -s -X POST $BASE/api/plugins/control -H 'Content-Type: application/json' \
    -d "{\"pluginId\":\"$id\",\"enable\":false}"
done
cat /tmp/showcase-effect.log    # client-module:undo / dashboard:undo / worker:undo / writer:undo / store:undo（严格逆序）

# 7) 按依赖顺序重新启用 → 自动重激活，writer 与客户端模块重新安装
for id in showcase-store showcase-worker showcase-dashboard showcase-client-module; do
  curl -s -X POST $BASE/api/plugins/control -H 'Content-Type: application/json' \
    -d "{\"pluginId\":\"$id\",\"enable\":true}"
done
curl -s $BASE/api/query/showcase/dashboard
```

预期：停用后每个插件的 effect 按 LIFO 逆序撤销（日志可证），页面/面板/模块/查询随 fiber 一起消失；
重新启用后四者回到 `Active`，`observed`/`writes` 继续增长，客户端模块 JS 重新可被加载。

### 为什么不是"直接禁用 provider 看消费者变 Pending"

产品控制面在装载边界做**声明一致性校验**（`PluginRegistry.SortByTypedDependencies`）：
若 `showcase-worker` 仍声明依赖 `showcase.store`，却把它唯一的 provider 禁用，控制面会返回
`conflict: plugin 'showcase-worker' requires capability 'showcase.store' with no provider` 并**拒绝**这次变更。
这是铁律 1（声明权威）在产品层的落地——不允许产生悬空声明。

运行时的"依赖消失 → Pending"（论文 C22 / 铁律 6）由 Core 语义保证，本套件的
`tests/Gocordis.Product.Tests/SharedContractPackagingTests.cs` 用**两个真实打包插件**证明：
provider fiber 被撤回后，consumer 回到 `Pending`（非 `Failed`），新 provider 携同一共享 Key 到达后自动重激活。
控制台里要看这个行为，先按逆序停用消费者再停 provider，或运行该测试。
