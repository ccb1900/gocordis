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
dotnet test Gocordis.sln --nologo -m:1   → 17 + 10 + 38 = 65 passed
```

测试构成：

- `Gocordis.Core.Tests`（17）：声明一致性、严格 LIFO、失败不重试、依赖丢失→Pending、divert、
  Cleanup 内 Require、所有权级联、事件注销、环拒绝、Realm 隔离、MetaKey 右偏、并发调和。
- `Gocordis.Conformance.Tests`（10）：C06、C13、C22、Thm70、确定性重放 + 第 1 轮审查回归 5 项 + 第 4 轮 EventKey 解析。
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

## 4. 未达成 / 明确缺口（不得用总数掩盖）

1. **Windows 专项验证**：ALC 回收与 `.exe` 解析在本机 Linux 通过；Windows CI 未执行（§5 4.8/5.13）。
2. **WASM 能力缺口**：自定义段 `module_type`、host imports / WASI 能力桥、WASM HMR 未实现（`migration-coverage.md` §6 的 5.3–5.6）。
3. **进程边界行为**：降级词表、调用中崩溃测试、插件→宿主方法错误码未实现/未验证（`migration-coverage.md` §6 的 5.9/5.12/5.14）。
4. **官方扩展套件**：event 派发五模式、timer、scheduler、watch、fs、http、server、env、cli、broker、
   registry、observe、market、package-manager 未迁移到 C#。它们是平台能力，不是框架语义。
5. **前端资产**：`web/console`、`client/` 未迁移；UI 通过贡献契约扩展。
6. **JS Cordis 全能力对齐**：`JS-CORDIS-ALIGNMENT.md` 中标注为"部分等价"的项未逐条收敛。
7. **Go 基线 flake**：冻结快照中的 `broker_test.go:321` 存在 race 假失败；冻结后工作区被外部写入者修复（`recvOut` + 5s 超时），该变化未纳入基线叠加层，见 `migration-baseline.md` §2。C# 测试已避免该写法。

## 5. 收敛结论

- 七个阶段均已执行并留下可运行产物；框架语义（论文优先）在 C# Core 中有可执行测试。
- 三种 Runtime 共用 Core `Component`/`Key<T>` 契约与生命周期；插件管理没有第二套生命周期。统一 Manifest 仍是缺口（见 `migration-coverage.md` §6 的 5.7）。
- Core 不引用任何 Runtime Adapter、Console 或 UI（由 `BoundaryTests` 固化）。
- 未完成项已逐项登记在 `migration-coverage.md` §9 与本文件 §4，**未用总体覆盖率掩盖**。
- Go 版本仍由 `main` 分支 + `baseline.sha256` 冻结；后续只维护 `migration/dotnet`，
  验收转正时通过 tag/分支保留 Go 历史。
