# Review-Fix Round 2

本轮由三路源码审查驱动：UI 控制面、Runtime/Packaging 可靠性、账本一致性。只修可复现的真实缺口，不做风格重构。

## 已关闭的 P0/P1

| 问题 | 根因修复 | 红测 |
| --- | --- | --- |
| 父 Fiber 撤销因无 activation 的 child 永久挂起 | `StartUnwind` 只门控有 live activation 的 child；Pending/Failed child 走终态退休并从 ChildList 移除 | `DisposeParentWithFailedOrPendingChildDoesNotHang` |
| 第二个 Managed 组件因静态 Key duplicate provider Failed | `ManagedKeys.ForInstance(id)` 每实例 Key；Provide/context.Provide 使用同一对象 | `TwoManagedComponentsCanBeActiveSimultaneously` |
| Upgrade 失败只回滚 manifest，store 分叉 | v2 先新增、Apply 成功后才删 v1；失败时删 v2 + 恢复 manifest + 重新 Apply | `FailedUpgradeRollsBackStoreAlongsideManifest` |
| Install 失败留下未声明 store 包 | store 变更纳入统一 rollback | `FailedInstallApplyLeavesNoUndeclaredStorePackage` |
| 上传 Key 使用客户端文件名且 staging 无限增长 | 服务端生成 Guid Key；成功/失败/重启均清理 staging | `UploadKeyIsServerGeneratedAndUploadFileIsConsumed` |
| Upgrade range 可排除上传版本 | range 必须包含上传包版本 | `FailedUpgradeRollsBackStoreAlongsideManifest` 等 |
| Upgrade 忽略反向依赖 | 基于所有已安装包检查 dependents，允许仍有其他版本满足的范围 | `UpgradeRejectsWhenInstalledDependentRangeWouldBeUnsatisfied` |
| UI 卸载传声明 range 导致包残留 | 前端传 installedVersion；服务端也接受 range | `UiUninstallWithDeclarationVersionRangeActuallyRemovesStorePackage` |
| 未声明孤儿包无法通过 UI 删除 | Uninstall 允许 orphan package | `OrphanStorePackageCanBeUninstalledWithoutDeclaration` |
| protected 未覆盖 upgrade/config | 所有 mutation 统一检查 ProtectedIds | `ProtectedPluginRejectsUpgradeAndConfigWrite` |
| remote 写端点无鉴权 | 新增 AccessPolicy seam；remote 无 policy 时所有写端点 403 | `AllowRemoteWriteEndpointsRequireAccessPolicy` |
| registry-only control/config 只改内存 | 无 control service 时直接 503 | `RegistryOnlyWebUiMutationIsRejected` + Phase6 契约更新 |
| SSE 重连后丢事件 | 前端首次 open 被动，重连 open 触发 plugin/composition/all 失效 | `stream.reconnect.test.ts` |

## 账本修正

- PC gate 从“PC01–PC30 全覆盖”改为“代表性 6/30”。
- 明确 UI 控制面鉴权边界与 remote 行为。
- 当前 HEAD 的 CI 状态不在本轮工作流核验，不做未经验证的声明。
- 测试计数从 358 更新为 369。

## 本地验证

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 385/385，连续 3 次
npm test                 → 85 passed
npm run build            → success
```

## 仍保留的下一轮候选

- GenericHmr Bind/Replace 的 usage-retire 语义
- SOCKS5 IPv6 / IDN / socks5 本地 DNS 语义
- ConfigSchema 嵌套 enum/default 与默认值填充
- WasmHotSwapper ReleaseGeneration/Replace 竞态
- PluginPackage 写入临时文件清理与超限检查顺序
- ReversibleWatch Ready/Change 顺序
- PluginRegistry.Entries 活字典并发读
