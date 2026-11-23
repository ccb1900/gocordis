# 示例插件与程序集

本目录承载迁移验证用的可运行示例：

| 目录 | 用途 |
| --- | --- |
| `SamplePlugin/` | 可被 `AssemblyLoadContext` 独立加载的 C# 插件 |
| `DepV1/`、`DepV2/` | 同名不同版本的私有依赖 `SharedDep`，验证依赖隔离 |
| `PluginA/`、`PluginB/` | 分别引用 DepV1/DepV2，验证两个插件共存 |
| `EchoProcPlugin/` | 进程插件：握手 + 行分隔 JSON-RPC |
| `UiPlugin/` | 通过 UI/API 贡献接口注册页面与查询，不改 Core |
| `SpatiotemporalShowcase/` | 严肃的可插拔/时空组合示例：四个真实 managed 包共享契约程序集，演示跨包依赖门控、可逆 effect、事件与声明式 UI（见其 README） |

WASM 示例以 WAT 形式内嵌在 `tests/Gocordis.Runtime.Tests/WasmRuntimeTests.cs`，
使用 Wasmtime 执行 `runtime_component_create/destroy` ABI。

这些示例证明：插件只依赖契约、三种后端共用 Core 生命周期、UI 通过贡献扩展。
