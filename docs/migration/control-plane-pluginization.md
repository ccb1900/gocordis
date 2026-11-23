# Control Plane Pluginization

Branch: `migration/dotnet-complete`

用户指出：控制面若由宿主组合根直接构造，就不是“一切皆插件”。源码审计确认
旧实现确实如此：`PlatformApplication` 构造函数里 `new PluginControlService`，
`WebUiServer` 也直接依赖具体服务类型。控制面没有自己的 Fiber，也不受
Core Runtime 调和，这与论文“单一生命周期权威”和“一切皆插件”的哲学冲突。

本轮按“红测 → 最小实现 → 全量验证”关闭该缺口。

## 1. 新的领域边界

```text
PlatformApplication (组合根)
  └─ Runtime.LoadAsync(PluginControlPlaneComponent)
       └─ Fiber: plugin-control-plane (Active)
            ├─ Provide: ControlPlaneKeys.Service : Key<IPluginControlPlane>
            ├─ Apply: new PluginControlService(...)
            └─ UI Page: plugin-control-plane (可逆 effect)
  └─ Runtime.RequireCapability(ControlPlaneKeys.Service)  // 只读，无绑定
  └─ WebUiServer(control: IPluginControlPlane?)
```

源码证据：

- `src/Gocordis.Packaging/PluginControlService.cs`
  - `IPluginControlPlane`：WebUi 使用的全部控制面方法
  - `ControlPlaneKeys.Service`：唯一 typed capability key
  - `PluginControlService : IPluginControlPlane`：既有实现不变
- `src/Gocordis.Packaging/PluginControlPlaneComponent.cs`
  - `Component.Name = "plugin-control-plane"`
  - `Provide() => [ControlPlaneKeys.Service]`
  - `ApplyAsync` 通过 `context.Provide` 提供能力，通过
    `context.RegisterUiPageAsync` 贡献可逆 UI 页
- `src/Gocordis.Core/Runtime.cs`
  - `RequireCapability<T>(Key<T>)`：默认 realm 只读解析，要求 Active +
    Committed + 已有 provided value；不绑定依赖、不安装 effect、不改状态
- `src/Gocordis.Platform/PlatformApplication.cs`
  - 不再直接构造控制服务；`Control` 是解析得到的 `IPluginControlPlane?`
- `src/Gocordis.Console.WebUi/WebUiServer.cs`
  - 只依赖 `IPluginControlPlane?`，HTTP 路径和 DTO 完全不变

## 2. 为什么这不是第二套插件系统

- 控制面是 Core Fiber，启停和撤销由同一个 Runtime 决策域负责。
- 控制服务本身仍不改 Fiber 状态：它只写声明并触发
  `PluginRegistry.ApplyAsync`，维护单一生命周期权威。
- `Runtime.RequireCapability` 是只读读取器，不引入依赖解析副作用；组件之间
  仍只能通过 `Require`/`Provide` 和事件协作。
- UI 页走 `Context.RegisterUiPageAsync`，Fiber 撤销时由 LIFO effect 撤回。
- 没有新增 PluginRegistry、没有自定义生命周期表、没有持久化 runtime 状态。

## 3. 红测与行为断言

`tests/Gocordis.Product.Tests/PlatformApplicationTests.cs`
新增 `ControlPlaneIsAPluginContribution`，断言：

1. Runtime snapshot 中存在唯一 Active `plugin-control-plane` Fiber；
2. Fiber 声明提供 `plugin-control-plane` typed capability；
3. `app.Control` 与 `Runtime.RequireCapability(ControlPlaneKeys.Service)` 同一实例；
4. UI 贡献包含 `plugin-control-plane` 页面；
5. `/api/plugins`、`/api/plugins/store`、`/api/plugins/control` 原 HTTP 合同仍工作；
6. `StopAsync` 后 Fiber、UI 页和已提供能力全部消失。

`NativeProfileStartsBackendWithoutWebServerAndRendersByProfile` 同步更新：
原生 profile 现在应同时渲染产品测试页与控制面页，且仍不启动 WebUi Fiber。

## 4. 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 397/397（本批完成时）；R7 后 404/404；R8 后 412/412；R9 后 418/418；R10 后 442/442，连续 3 次全绿
console npm test         → 88 passed
npm run build            → success
```

不检查 CI；不引入 `sleep` 或时序等待。失败过的断言已如实修正后重跑。

## 5. 明确边界

- 控制面是平台内置插件，仍由组合根显式 `LoadAsync`；它不伪装成从 manifest
  安装的可拆卸业务插件。
- WebUi 仍是宿主规定的传输适配器，不是控制面权威；它只消费解析后的能力。
- 既有包操作、声明事务、AccessPolicy 与 SSE 协议不变。
