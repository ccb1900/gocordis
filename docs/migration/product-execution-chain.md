# Product Plugin Execution Chain

代码提交：`61f15ae1`（`migration/dotnet-complete`）。

本文件记录产品级主链路从“占位适配器”到“真实插件执行”的闭合。
用户审查基准 `aa8e609` 时，默认产品宿主只创建 `ProductPluginComponent`，
`plugin create` 生成的真实 `Component` 从未被加载。本批不改 Core 决策域，
只在包层和组合根之间补上执行适配。

## 1. 调用链

```text
plugin create (真实 Component 源码)
  → dotnet build
  → plugin pack --backend managed --entry-assembly ... --entry-type ...
  → PluginStore install
  → ApplicationManifest declaration
  → ApplicationLoader (透传 Backend/EntryAssembly/EntryType/Source)
  → PluginRegistry.RegisterComponentFactory
  → PlatformApplication.CreateComponent
  → ManagedPluginHost.CreateComponent("id@version", ...)
  → ManagedBackend / Core.Context
  → Fiber Active + 真实 Effect / Capability / UI / API
```

源码证据：

- `src/Gocordis.Packaging/PluginPackage.cs`
  - `PluginPackageManifest` 末尾追加 `Backend`、`EntryAssembly`、`EntryType`
  - managed 包必须提供入口且 payload 中存在入口 dll
  - checksum 纳入新字段；旧包空字段保持旧字节序列
- `src/Gocordis.Plugins/PluginManifest.cs` / `ApplicationLoader.cs`
  - 声明层携带相同三字段，并把包 `InstallPath` 作为 `Source` 透传
- `src/Gocordis.Plugins/PluginRegistry.cs`
  - 新增 `RegisterComponentFactory(PluginManifest)`；旧 `RegisterFactory` 为兼容适配
  - 未变声明复用原组件，避免重复加载 ALC
  - Prepare 失败/未激活 staged 组件释放
- `src/Gocordis.Runtime.Managed/ManagedBackend.cs`
  - `ManagedPluginComponent` 实现 staged 释放，`Name` 反映真实组件名
- `src/Gocordis.Platform/PlatformApplication.cs`
  - 持有 `ManagedPluginHost`，按 `Backend` 分发
  - 先加载提供 `UiContributionRegistry` / `ApiGateway` 的 framework fiber
  - 禁用声明返回零能力组件，不打开 ALC
  - loader key 使用 `id@version`，避免升级时裸 id 重复加载
- `src/Gocordis.Platform/ConsoleCapabilitiesComponent.cs`
  - UI/API 能力与插件一样是 Core fiber 贡献，不是宿主旁路

后端为空或 `builtin` 的包仍走 `ProductPluginComponent`，保持向后兼容。

## 2. 生命周期语义

- 插件自己的 `ApplyAsync` 通过 `ManagedBackend` 在真实 Core `Context` 上执行。
- `Inject` / `Provide` 由 `ManagedPluginComponent` 转发，Core 仍强制声明权威。
- Effect 逆、UI 页、API query 与 Fiber 同生共死；禁用/卸载由 Core effect 撤销。
- 升级通过 `id@version` 加载新 ALC，generation fence 防止旧逆卸掉新程序集。
- 没有第二套 Registry，没有第二套生命周期状态，没有持久化 runtime snapshot。

## 3. E2E 验收

`tests/Gocordis.Product.Tests/ManagedPluginProductE2ETests.cs`：

1. 写真实 C# Component 源码，`dotnet build` 产出 dll + deps.json
2. 打包为 managed 包，安装到真实 store
3. `PlatformApplication.StartAsync` 后断言：
   - Fiber Active 且名称来自真实组件
   - 插件自定义 Key 可解析
   - Effect 副作用可见
   - UI 页/API query 存在且 owner 为插件 Fiber
4. Disable：Fiber 撤销、能力消失、UI/API 消失、ALC 可回收
5. Enable：重新激活，新 generation，Effect 重装
6. Upgrade：v1 fiber 退出、v2 Active、旧 ALC 可回收
7. Uninstall：无 fiber / UI / capability / store 目录残留

测试使用 `WaitForExitAsync` + 取消令牌，不使用 `sleep`。

## 4. 同批关闭的其他产品缺口

- `PluginStore.UpgradeAsync` 现在与 UI 控制面共享 `PluginDependencyPlanner`，
  CLI/Store 升级不再绕过反向依赖检查。
- `gocordis create` 生成薄壳 `PlatformRunner` 应用；csproj 使用 PackageReference，
  CLI 创建时向输出目录写入本地 NuGet feed。测试证明复制到另一目录后仍可 `dotnet build`。
- WebUi 远程读取面新增 `Read` / `Observe` scope；SSE 在写 header 前鉴权；
  插件配置按字段名脱敏；store DTO 不再下发完整安装路径。
- MarketCatalog 使用 `id + version` 组合 rowKey。

## 5. R10 后续关闭与剩余边界

已由 `review-round-10.md` 关闭：

- CLI `app run` 统一委托 `PlatformRunner`，不再自建第二套宿主。
- Process / WASM 后端接入 `PlatformApplication.CreateComponent` 统一分发。
- 插件包 UI 资源从安装目录自动注册，禁用/升级/卸载随 Fiber effect 撤销。
- Go 基线裁决为冻结 `ed2ef724...`，后续 main 提交记为上游演进 NOTE。

仍未关闭的产品边界：

- 按指定版本卸载时保留仍满足声明的其他版本、Dependencies 查询按版本解析等
  多版本语义尚未统一。
- 签名仍是 opt-in provenance 检查，不是安装信任策略。
- WinForms / Avalonia 仍是 RenderPlan profile，不是原生 UI Host。
- CI 按用户约束未核验；不等同于 CI 绿灯。
