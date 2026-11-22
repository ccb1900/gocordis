# Review Round 7 — Product Control Consistency

Branch: `migration/dotnet-complete`

本轮来自对最新提交的产品级审查。审查结论逐条用源码核实，不把历史文档数字或
提交说明当证据。Go 基线 `ed2ef724` 与 `migration/dotnet` 未改动。

## 1. 跨层事务：审查成立，但需精确表述

用户指出 Install/Upgrade/Uninstall 把 Store、Application Manifest、Runtime
当独立操作。源码核实结果：

- `PluginControlService.cs` 已有 `PersistAndApplyAsync` / `TryRestoreAsync`
  补偿链；`PluginStore` 也有 staging/backup/rollback。因此“完全没有事务”不成立。
- 真实缺陷是：前进写入与回滚共用固定 `<manifest>.tmp`；补偿步骤串行且异常被吞；
  store 逆操作重复正向依赖检查；失败后没有三方一致性验证。

修复：

- `PluginStore.UninstallExactAsync`：仅补偿使用，精确版本且跳过正向依赖检查。
- `PluginControlService.MutationBaseline`：变更前捕获 manifest 原始字节、store
  版本集合、runtime 声明指纹。
- `CompensateAsync`：manifest / store inverses / runtime 各步独立执行，LIFO，
  错误累计；最后比对三方。补偿不完整写入错误响应，不再吞掉。
- `RestoreManifestAsync`：回滚使用唯一临时文件，不复用可能已被注入故障的 `.tmp`。
- revision 只在整次 mutation 成功后推进。

红测（`tests/Gocordis.Conformance.Tests/PluginControlPlaneTests.cs`）：

- `ManifestSaveFailureDuringInstallRollsBackStoreAndRuntime`
- `ManifestSaveFailureDuringUpgradeLeavesOnlyOriginalPackage`
- `StoreInverseFailureIsReportedAndRecoverableAfterFaultClears`

三条先失败，修复后转绿；Conformance 全套 141/141。

## 2. 启动失败资源泄漏：审查成立

`PlatformApplication.StartAsync` 原先把 `_started = true` 放在最后，而
`StopAsync` 以 `!_started` 直接返回。manifest 加载失败、版本不匹配、
control ApplyAll 失败、WebUi 绑定失败都会让 Runtime / Registry / RouteGuard
无法释放。

修复：

- `StartAsync` 用 try/catch 包裹启动阶段；失败时清理后重新抛原始异常。
- `StopAsync` 移除 `!_started` 守卫，先置 `_stopped`，按 WebUi → Registry →
  Runtime → RouteGuard 顺序用 try/finally 清理；部分启动状态也能收敛。

红测（`tests/Gocordis.Product.Tests/PlatformApplicationTests.cs`）：

- `FailedWebHostStartReleasesRuntimeAndRegistry`
- `FailedApplyAllReleasesControlPlaneFiber`
- `FailedManifestLoadReleasesRuntime`

三条先失败，修复后转绿。

## 3. WinForms / Avalonia 边界：文档已承认，CLI 会误导

源码证据：

- `UiHostProfiles` 是无状态 renderer，不加载 fiber，也不引用原生 UI 包。
- `PlatformApplication` 只有 `UiHostKind.Web` 启动 WebUi。
- 多份文档已明确“不是原生控件宿主包”。

真实缺口只在 CLI help 和启动提示：`--host winforms|avalonia` 看起来像可运行
原生宿主。已更新 `PlatformRunner.UsageText`、未知 host 错误提示和非 Web 启动输出，
明确写为 `render-plan profiles only: no native UI window and no web console`。
新增 `PlatformCliUsageStatesNativeHostBoundary` 断言。

## 4. 不再扩大范围

本轮不新增原生 UI 系统、不重写 Core / Registry / Runtime 决策域、不引入 CI 检查、
不使用 sleep。剩余历史边界仍按 `final-calibration.md` / `final-acceptance.md`
登记为 Adapted / Divergent，不伪装成 Equivalent。

## 5. 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 404/404，连续 3 次全绿
npm test                 → 85 passed
npm run build            → success（仅既有 chunk-size 提示）
git diff --check         → clean
```

CI 按用户要求不在本轮核验；只报告本地真实执行结果。
