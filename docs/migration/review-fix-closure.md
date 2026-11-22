# Review-Fix Loop Closure

Branch: `migration/dotnet-complete`

本轮不是新一轮全局审查，只处理上一轮账本中仍可落地的 Adapted/Divergent。
目标：把“有具体验收缺口”的行关闭，把“平台/语言边界”的行明确重新分类为 Divergent，不伪造完成。

## 关闭的项

| 项 | 处理 | 证据 |
| --- | --- | --- |
| platform host | CLI `app run` 使用真实 ApplicationLoader + WebUiServer，validate-only 可测 | `CliProductTests.AppRunValidateOnlyResolvesInstalledPluginAndHonorsDisable` |
| cli-cordis | CLI `app enable/disable/run/validate` 闭环 | `CliProductTests.AppEnableAndDisableToggleManifestFlagsWithoutChangingOtherFields` |
| httpd sample | 新增专用 `samples/HttpdHost`，Active⇔ready、可逆 Stop | `HttpdProbeTests.HttpdProbeRespondsAndStops` |
| backfill sample | 新增 `samples/BackfillApp`，append-only journal + 幂等重放 + 取消安全 | `BackfillJournalTests.BackfillJournalReplaysOnlyMissingDatesAndIsIdempotent` |
| sample parity | 显式列出每个 backend/composition 的 sample | `SampleParityTests.SampleParityListsExpectedBackendsAndComposition` |
| console Explorer/Dashboard | 真实 HTTP 验证 plugins/dashboard/insight-graph，并验证 dist 资源可服务 | `ProductGateTests.ExplorerDashboardConformance_*` |
| integration gates | PC-01/19/21/27/30 平台收敛语义 | `ProductGateTests.PCGateParity_*` |
| deterministic theorem | Thm73 installing-effect quiescence 用公开可观察量验证 | `ProductGateTests.Thm73InstallingQuiescence_*` |
| web/console SPA | 由 C# host 提供 dist，TS 作为产品前端资产，不再视为缺口 | `ProductGateTests.ExplorerDashboardConformance_BuiltExplorerAndDashboardAssetsAreServed` |

## 重新分类为 Divergent（明确设计/平台边界）

- Provider realm/isolation：C34 接受差异，C# 无 Rehome API。
- Observation/equivalence：C# 用线性化 Snapshot + HTTP 轮询，不暴露 JS EventSink 对象。
- HMR：通用 target/binding/usage 已迁移；WASM adapter 仍是 warm-validate + 旧 Fiber Gone 后提交，不是零停机原子替换。
- Managed runtime：ALC 不是安全沙箱，Go 无等价物。
- Go Native runtime：.NET 不加载 Go native plugin，Process backend 作为隔离替代。

## 仍保留 Adapted（2 项）

- WinForms UI host：契约与 render-plan 已实现，但不含原生控件宿主包。
- Avalonia UI host：同上。

这两项不阻塞统一插件模型：同一插件在三个 profile 中保持同一 Fiber/Activation，
Web-only client module 在原生 Host 中明确降级，不出现第二套生命周期。

## 完成度

```text
Equivalent 44 / Adapted 3 / Divergent 5 / Missing 0   (total 52; calibrated in final-calibration.md)
Tests: 352/352, 0 warnings, 3 consecutive full-suite runs
```

## 修复日志（ub-mode）

1. 先列账本剩余项，只选有明确验收缺口的 9 行。
2. 三个 agent 并行写红测和实现，主线程统一构建，避免 obj/bin 竞争。
3. 修真实集成错误：HttpdHost 缺 AspNetCore.Hosting using；CLI Console 命名空间冲突；
   ProductGate orphan capability 未声明；CLI 全局 Console 捕获竞争；iterative component 缺 ApplyAsync override。
4. 全量 352/352 绿后更新账本状态，并把不可跨平台落地的项重新分类为 Divergent，
   不用模糊状态伪装成未完成，也不把边界项伪称 Equivalent。
