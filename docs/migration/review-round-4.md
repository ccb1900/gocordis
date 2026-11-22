# Review-Fix Round 4

本轮关闭剩余可落地产品工具链缺口，不触碰已冻结的 Core 语义。

| 能力 | 实现 | 红测 |
| --- | --- | --- |
| `plugin create` | 生成可独立构建的插件项目（csproj + Plugin.cs + manifest.toml），非空目录拒绝和写入回滚 | `PluginCreateGeneratesBuildableScaffoldWithReversibleEffect`、`PluginCreateRejectsNonEmptyDirectoryAndCleansUpAfterWriteFailure` |
| `plugin doctor` | 包结构/版本/校验和/依赖诊断，JSON diagnostics + 退出码 | `PluginDoctorReportsDiagnosticsForValidIncompatibleAndCorruptPackages`、`PluginDoctorWarnsOnDeclaredDependenciesWithoutFailing` |
| `plugin package` | `pack` 别名，输出一致 | `PluginPackageAliasProducesSameOutputAsPack` |
| `plugin sandbox` | 临时 store + 独立 Runtime 的 declaration-only 加载检查，不执行包代码、不碰用户 store/manifest | `PluginSandboxLoadsPackageInTemporaryStoreAndReportsSnapshot`、`PluginSandboxRejectsPackageWithMissingDependency` |

明确边界：`sandbox` 不是安全沙箱，输出中带 `securityBoundary=false` 和 `NO_HOST_FACTORY` 警告。

本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 392/392
npm test                 → 85 passed
npm run build            → success
```
