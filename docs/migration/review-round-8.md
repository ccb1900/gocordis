# Review Round 8 — Migration Freeze Prerequisites

审查基准：`aa8e609`。R8 代码提交：`8c4e7eb3`。分支：`migration/dotnet-complete`。

本轮按用户要求只处理真实缺口，不重审 Core / Runtime，不实现原生桌面宿主，不查 CI。

## 1. Upgrade 反向依赖判断（已关闭）

源码确认 `UpgradeAsync` 把新版本传给 `FindInstalledDependentsAsync`，与被检查函数的
语义相反：该函数检查“将被移除的版本是否有依赖者，以及剩余版本是否仍满足”。

修复位置：`src/Gocordis.Packaging/PluginControlService.cs`

- `removedVersions`：当前声明解析到的旧版本，且仅在旧版本 != 新版本时存在；
- `survivingVersions`：升级后仍存在的同 id 版本 + 新版本；
- `FindInstalledDependentsAsync(id, removedVersions, ct, survivingVersions)`；
- 同版本升级没有 removed version，不再误报。

四类红测（`tests/Gocordis.Conformance.Tests/PluginControlPlaneTests.cs`）：

1. `UpgradeAllowsWhenNewVersionStillSatisfiesDependentRange` — 新版本仍在依赖范围内，允许；
2. `UpgradeRejectsWhenDependentRangeOnlyMatchesRemovedVersion` — 只有旧版本满足，拒绝；
3. `UpgradeAllowsSameVersionWithInstalledDependent` — 同版本升级无移除，允许；
4. `UpgradeAllowsWhenAnotherInstalledVersionSatisfiesDependent` — 多版本共存，允许。

原有 `UpgradeRejectsWhenInstalledDependentRangeWouldBeUnsatisfied` 继续通过。
Conformance 全套 145/145。

一个测试事实：NuGet 裸版本 `"1.0.0"` 表示 `>=1.0.0`，不是精确版本；多版本用例
据此断言旧解析版本被移除、其余版本保留。

## 2. PlatformApplication 清理（已关闭）

源码确认 Round 7 的 try/finally 仍有两个缺陷：Dispose 抛错会阻断后续资源；
`_stopped` 在清理前置位导致失败后无法重试。

修复位置：`src/Gocordis.Platform/PlatformApplication.cs`

- 五个独立步骤：WebUi Stop、WebUi Dispose、Registry、Runtime、RouteGuard；
- 每步异常被汇总，后续步骤照常执行；
- `_stopped` 只在全部成功后置位；失败抛 `AggregateException`，再次 `StopAsync`
  或 `DisposeAsync` 可重试；
- 增加 protected virtual 清理钩子用于故障注入，不改变生产释放路径。

红测 `CleanupFailureIsIsolatedAndRetried`（4 个 inline case）：
WebUi / Registry / Runtime / RouteGuard 各自注入一次 Dispose 故障，断言其他资源
仍被释放、异常可观察、重试成功、成功后重复 Stop/Dispose 不再重复清理。

## 3. 账本与范围裁决

- 最终测试计数：**412/412**（Core 27 / Conformance 145 / Runtime 119 /
  Extensions 59 / Packaging 40 / UI 3 / Client 2 / Product 17），连续 3 次本地全绿。
- WinForms/Avalonia 仍是 Host Profile + RenderPlan，不是原生 UI Host。CLI 已明确标注，
  最终验收继续保留为延期项，不因测试通过而改写成已交付。
- `final-calibration.md` / `final-acceptance.md` 的校准目标指向 R8 代码提交
  `8c4e7eb3`；本账本提交仅同步文档，最终 HEAD 以 `git rev-parse HEAD` 为准。

## 4. 未关闭的外部证据项

- **Go 基线验证失败**：`bash docs/migration/verify-baseline.sh` 报告
  `main` 从冻结的 `ed2ef724a24331ecc3fa21974539315fd90212bf`
  漂移到 `44c1f27158d4b765cf16663695de1d587026a3b9`。
  本轮没有修改 Go 代码，也没有切换/重置 `main`。该漂移必须由仓库维护方裁决：
  要么确认新基线并更新冻结脚本，要么把 `main` 恢复到原基线。
  在此之前，不声称 Go 基线验证通过。
- **CI 未核验**：按用户此前明确约束“不看 CI 结果”，未查询任何 workflow run。
  本报告只记录本地真实执行结果。

## 5. 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 412/412，连续 3 次全绿
npm test                 → 85 passed
npm run build            → success（仅既有 chunk-size 提示）
git diff --check         → clean
verify-baseline.sh       → FAIL（main 漂移，见上）
```
