# Extension Parity（批次 3）

Branch: `migration/dotnet-complete`

本批按 `feature-matrix.md` 的 Gap/Target/Acceptance 关闭 Go 24 扩展的公开面与生命周期差异。
不重写 Runtime，不新增外部包，所有新副作用都通过 Core Effect 或可逆句柄撤销。

## 已关闭的缺口

| Go 包 | C# 新增/扩展 | 验收测试 |
| --- | --- | --- |
| bundle | `BundleLibrary`：文件级 bundle 加载、有序展开、未知名报错 | `BundleFileExpandsAndRejectsUnknown` |
| cli | `CliDispatcher.RegisterTyped` / `CliCommandSchema` / help | `CliHelpAndSchema` |
| config | `ConfigSchema` 同步校验，`RegisterValidatedFactory` 在 Prepare 阶段拒绝 | `ConfigSchemaRejectsInvalidDeclarationWithNoFiberChange` |
| configwatch | `TxGuard` 覆盖 parse+reconcile，失败不推进 Revision | `ConfigWatcherTxGuardCoversReconcileAndDoesNotAdvanceRevisionOnFailure` |
| devtools | `DiagnosticsService.Diagnose` 纯函数、确定性诊断 | `DiagnoseReports*` |
| env | 4 文件优先级 + mode + 栈式可逆 ApplyLayer | `EnvFourFilePriorityAndRevert`、`EnvApplyLayersUnwindOnlyTheirOwnValues` |
| event | 既有五模式 + value/chain + cancellation | `EventDispatchGapTests.*` |
| fs | Remove/Mkdir/Copy/Move/Range/List + 挂载边界与符号链接防护 | `FsFullConformance` |
| http | `State()`、`RoundTripAsync`、`StreamResponse`、stream lease | `HttpStateStreamCloseConformance` |
| http-socks | `SocksRegistry` + 内置 SOCKS5 CONNECT，注册可逆 | `SocksProxyRegistrationAndUndo` |
| market | `MarketService.Install/Upgrade/Uninstall`，checksum/版本门 | `MarketInstallUpgradeUninstallAndPackageLockDiff` |
| package-manager | `PackageLockfile`、结构化 `DeclarationDiff`、`PackageTransaction` apply/rollback | `MarketInstallUpgradeUninstallAndPackageLockDiff` |
| patch | `PatchFile` 文件级加载 + 原子应用 | `PatchFileAtomicFailureLeavesNoChange` |
| pluginmeta | `PluginManifest.ParseJson`、`PluginManifestChecksum.VerifyFile` | `ChecksumMismatchRejected` |
| server | `HostAcl`、`ServerRouteGuard` 可逆 route-check，deny=404 | `ServerAclProxyWsConformance` |
| timer | `*Owned`、`TimeoutWait`、`Ticks`、`TimeoutContext` | `TimerOwned*` / `Timeout*` |
| watch | `ReversibleWatchService` 递归/glob/Ready/Error/可逆 provider | `WatchRecursiveFilterReadyErrorAndRevoke` |
| HMR | 后端无关 `GenericHmr`：target/binding/usage/generation fence/先验证后替换 | `GenericHmrStaleGenerationCannotReleaseNewTarget`、`GenericHmrReplaceValidatesFirstAndKeepsOldTargetOnFailure` |

## 仍保留的差异

- WASM `WasmHotSwapper` 仍是 warm-validate + 旧 Fiber Gone 后提交，不是零停机原子替换；
  通用 HMR 协议已迁移，WASM 适配层标 Divergent。
- Console 的 Explorer/Inspector/Dashboard 产品化属于批次 5，不在本批宣称完成。
- `cmd/create-cordis` 已由 `Gocordis.Cli create` 实现；Koishi client SDK 已由 `Gocordis.Client`
  作为 .NET 原生替代实现。两者证据见 `final-calibration.md`。
- 跨 effect 并发撤销仍按论文/Go 的严格 LIFO，不采用 JS Cordis 的 `Promise.all` 并发撤销。

## 验证

- `dotnet build Gocordis.sln`：0 warning / 0 error
- `dotnet test Gocordis.sln`：349/349，连续 3 次全绿
- 新增测试：Runtime 113（+7）、Extensions 52（+17）、Packaging 7
