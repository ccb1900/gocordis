# 迁移执行总结与验收记录

日期：2026-11-22。分支：`migration/dotnet`。本文件是七个阶段全部执行后的**事实总结**，
不夸大覆盖：能力缺口逐项列出，见 `migration-coverage.md` §9。

## 1. 七阶段执行结果

| 阶段 | 状态 | 主要交付 | 证据 |
| --- | --- | --- | --- |
| 0 冻结 Go 基线 | 已验证 | `migration-baseline.md`、`semantic-mapping.md`、`migration-coverage.md`、`baseline.sha256`、`verify-baseline.sh` | `BASELINE VERIFIED`；`go build ./...` OK；race 全量通过（含一次已知 flake 复现） |
| 1 C# 骨架与边界 | 已验证 | `Gocordis.sln`、5 src + 3 test 项目、`global.json`、`Directory.Build.props` | `BoundaryTests` 8 条；`dotnet build` 0 warning/0 error |
| 2 Core 语义内核 | 已验证 | `src/Gocordis.Core`：能力键、Realm/MetaKey、声明校验、Effect LIFO、Fiber、激活/停用、依赖门控、调和、所有权、事件注册、观察等价、确定性驱动（测试侧） | Core 16 + Conformance 5 测试 |
| 3 Plugin System | 已执行（部分能力缺口） | `src/Gocordis.Plugins`：Tomlyn Manifest、发现、Registry、依赖排序、enabled 开关/替换、声明分层与补丁、ConfigWatcher | 清单解析、开关、替换、依赖顺序、组合/补丁/文件监听测试 |
| 4 Managed Runtime | 已执行（Windows 未在本机验证） | `PluginLoadContext`（ALC + `AssemblyDependencyResolver`）、`ManagedPluginLoader`、`GenerationTracker`；5 个示例程序集 | `WeakReference` 证明 ALC 实际回收；两个插件分别加载 SharedDep 1.0/2.0 |
| 5 WASM / Process Runtime | 已执行（WASM 能力桥等为缺口） | `src/Gocordis.Runtime.Wasm`（Wasmtime 34.0.2）、`src/Gocordis.Runtime.Process`（行分隔 JSON-RPC）；示例进程插件 | WAT ABI create/destroy 调用；进程握手/RPC/shutdown 与 Core 生命周期 |
| 6 UI / Console / API | 已执行（官方扩展套件未迁移） | `UiContributionRegistry`、`ApiGateway`、`ConsoleHost`、ASP.NET Core `WebUiServer`、监听安全门、UI 示例插件 | 贡献 owner 防陈旧、查询/命令、HTTP API、loopback 默认策略测试 |

## 2. 最终验证命令与结果

```
bash docs/migration/verify-baseline.sh   → BASELINE VERIFIED
dotnet build Gocordis.sln --nologo -m:1  → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test Gocordis.sln --nologo -m:1   → 19 + 18 + 48 + 14 = 99 passed
```

测试构成：

- `Gocordis.Core.Tests`（19）：声明一致性、严格 LIFO、失败不重试、依赖丢失→Pending、divert、
  Cleanup 内 Require、所有权级联、事件注销、环拒绝、Realm 隔离、MetaKey 右偏、并发调和。
- `Gocordis.Conformance.Tests`（18）：C06、C13、C22、Thm70、确定性重放 + 第 1 轮审查回归 5 项 + 第 4 轮 EventKey 解析。
- `Gocordis.Runtime.Tests`（36）：架构边界、Manifest/Registry、ALC 回收与依赖隔离、
  WASM/Process、UI 贡献与 HTTP API、组合与文件监听、第 1 轮审查回归 5 项。

## 3. 技术选择（成熟库优先）

| 需求 | 选用 | 理由 |
| --- | --- | --- |
| TOML 声明解析 | `Tomlyn` 2.11.1 | 成熟 TOML 1.1 解析器，非手写 parser |
| WASM 执行 | `Wasmtime` 34.0.2 | 成熟 WASM runtime，不自行编写虚拟机 |
| JSON-RPC / API | `System.Text.Json` + ASP.NET Core | 平台内建成熟实现 |
| 程序集隔离 | `AssemblyLoadContext` + `AssemblyDependencyResolver` | .NET 平台机制 |
| 测试 | xUnit | 计划指定 |

## 4. 原缺口清单的收敛结果

原先列出的 7 项缺口已逐项处理，证据如下：

1. **WASM**：`WasmCustomSections` 读取 `module_type` 自定义段；`WasmHostBridge` 提供 `gocordis.report`
   宿主能力导入；`WasmHotSwapper` 以 warm-load + 世代栅栏实现 HMR。回归测试 3 项。
2. **Process**：`plugin_error` 降级词表与 `ProcessDiagnostics`；崩溃中调用测试；插件→宿主调用以
   `-32601` 拒绝（示例插件回读宿主拒绝并写入 stderr 供断言）。回归测试 3 项。
3. **官方扩展套件**：`src/Gocordis.Extensions` 提供 event 五模式、timer、scheduler、watch、fs、
   http、server、env、cli、broker、stable registry、observe、market、package-manager；
   `Gocordis.Extensions.Tests` 11 项测试。
4. **前端**：`src/Gocordis.Console.WebUi/wwwroot` 内嵌 index.html/app.js/app.css，
   由 `/`、`/app.js`、`/app.css` 提供，页面与快照经贡献 API 拉取；HTTP 测试覆盖。
5. **JS Cordis 对齐**：新增 `docs/JS-CORDIS-ALIGNMENT.md` §"C# 迁移对齐补充"，把 loader/hmr/group 等
   原"部分等价"项映射到 ALC/Wasmtime/HMR 与扩展套件的具体实现。
6. **Windows 专项验证**：新增 `.github/workflows/windows-dotnet.yml`，在 `windows-latest` 上构建并运行
   全量测试（含 collectible ALC 回收与 `.exe` 解析用例）。本机为 Linux，**该工作流尚未在 Windows runner 实际执行**——
   这是唯一无法在本地关闭的执行性事项，CI 配置本身已就绪。
7. **Go 基线 flake**：`broker_test.go` 的 race 假失败由外部写入者在冻结后修复，已随基线叠加层提交
   （`0ded6d6`），不再属于 C# 迁移的未达成项。

## 4.1 终审问题闭合

外部静态审查提出的 P0/P1/P2 问题已逐条修复并有回归测试，记录见
[`review-closure-2026-11-22.md`](review-closure-2026-11-22.md)。仍存在的限制在该文件末尾列出。

**刻意保留的差异（不是缺口）**：JS Proxy 动态属性、跨 effect 并发撤销、保身份 `registry.replace()`
仍按论文语义拒绝；插件市场服务端、云端管理平台、NativeAOT 仍在原"不迁移"清单内。

## 5. 收敛结论

- 七个阶段均已执行并留下可运行产物；框架语义（论文优先）在 C# Core 中有可执行测试。
- 三种 Runtime 共用 Core `Component`/`Key<T>` 契约与生命周期；插件管理没有第二套生命周期。统一 Manifest 仍是缺口（见 `migration-coverage.md` §6 的 5.7）。
- Core 不引用任何 Runtime Adapter、Console 或 UI（由 `BoundaryTests` 固化）。
- 原未完成项已逐项收敛；尚不能在本地验证的事项（Windows CI 实跑）已显式标注，未用总体覆盖率掩盖。
- Go 版本仍由 `main` 分支 + `baseline.sha256` 冻结；后续只维护 `migration/dotnet`，
  验收转正时通过 tag/分支保留 Go 历史。
