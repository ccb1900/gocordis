# Plugin Package / Install / Declare Lifecycle（批次 2）

Branch: `migration/dotnet-complete`

## 1. 交付物

- `src/Gocordis.Packaging`：独立插件包格式、目录安装、应用声明、统一加载器。
- `tools/Gocordis.Cli`：`plugin pack/install/upgrade/uninstall/list/resolve`、`app validate`。
- `tests/Gocordis.Packaging.Tests`：7 项端到端测试，覆盖 12 步产品闭环。

插件包是普通 ZIP：`manifest.json` + `payload/**`。NuGet.Versioning 只用于版本与范围解析，
不是唯一包格式；远程只读源通过 `IPluginPackageSource` 接入，已有 HTTP 实现。

## 2. Install / Declare / Enable 的边界

| 动作 | 实现 | 持久状态 |
| --- | --- | --- |
| Install | `PluginStore.InstallAsync` / CLI `plugin install` | 包文件 + `installed.json` |
| Declare | `ApplicationManifest.Plugins` | 应用 JSON 声明 |
| Enable / Disable | `ApplicationLoader.ApplyAsync` → `PluginRegistry.ApplyAsync` | 只改声明 `Enabled`，不写 fiber |
| Upgrade | `PluginStore.UpgradeAsync` + 应用版本范围更新 | 新版本目录 + lock diff |
| Uninstall | `PluginStore.UninstallAsync` | 删除包目录与 lock 条目 |

Runtime 仍通过 `PluginRegistry` 和 Core Effect 管理 Fiber，包系统不写 Fiber 状态。

## 3. 事务与错误语义

- 包读取时校验 host 版本范围、依赖、文件 SHA256 与包 checksum；失败不改 store。
- 安装先写入 `.staging/<guid>`，再移动到最终目录；lock 文件在修改前备份，失败恢复。
- 升级返回结构化 diff：`Added` / `Removed` 旧版本；卸载检查被依赖情况。
- 应用声明保存采用临时文件 + rename；加载时 host 版本必须匹配 store。
- Apply 失败走 `PluginRegistry` 现有 Prepare/Commit/Rollback，不登记 Failed fiber 为活插件。

## 4. 产品闭环验证

`PluginPackageLifecycleTests.ProductLoopInstallDeclareEnableModifyDisableEnableUpgradeUninstall` 依次验证：

1. 创建插件源码目录
2. 构建插件包
3. 从本地路径安装
4. 写入应用声明
5. 加载应用并启用插件
6. 修改插件配置
7. 禁用插件
8. 重新启用插件
9. 升级插件包与声明版本范围
10. 卸载前移除声明
11. 卸载插件
12. 验证 Fiber 与 UI 贡献清理

`CliAndSourceTests` 验证 CLI pack/install/list/uninstall 与本地源解析。

## 5. 尚未完成

- `plugin create/doctor/pack|package/sandbox` 全部实现；sandbox 是 declaration-only 加载检查，不是安全沙箱。
- 远程只读插件源已实现：`HttpPluginPackageSource` + `registry serve` + `plugin fetch`；不包含 Koishi 市场服务端
- 包签名已实现：RSA-2048 keygen/sign/doctor --public-key；无签名包保持兼容
- WinForms / Avalonia Host（批次 5）
