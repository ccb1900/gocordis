# UI Product Control Plane

Branch: `migration/dotnet-complete`

本文件记录 UI 从“只显示贡献”扩展为“可操作插件平台”的闭环。
唯一写入路径仍然是：

```text
UI action → PluginControlService → ApplicationManifest.SaveAsync
          → ApplicationLoader → PluginRegistry.ApplyAsync → Core Runtime
```

Web UI 不直接操作 Fiber，不维护第二套插件状态。

## 1. 服务端唯一控制服务

`src/Gocordis.Packaging/PluginControlService.cs`

- 持有 manifest 路径、PluginStore、PluginRegistry、可选 UiContributionRegistry
- 单一 SemaphoreSlim 串行化所有读写和 mutation
- 工厂注册按 type 去重；ProductPluginComponent 贡献 typed key 与 UI Page
- 每次 mutation 先写声明，再调和；失败时恢复旧声明并尽力回滚 store（二次失败仍报告原始错误）
- Revision 单调递增，供 SSE `plugin-state` 使用

公开能力：List / Store / Dependencies / Upload / Install / Upgrade / Uninstall /
SetEnabled / SetConfig / ApplyAll。

## 2. Web API

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/api/plugins` | 插件真实状态、声明、配置、能力、组件 |
| GET | `/api/plugins/store` | 已安装包、版本、依赖、checksum、是否声明/运行 |
| GET | `/api/plugins/{id}/dependencies` | 包依赖、dependents、typed capability、诊断 |
| POST | `/api/plugins/packages` | multipart 上传 `.gocordis-plugin` |
| POST | `/api/plugins/install` | 安装并声明启用 |
| POST | `/api/plugins/upgrade` | 升级并更新声明版本范围 |
| POST | `/api/plugins/uninstall` | 先撤声明再卸载包 |
| POST | `/api/plugins/control` | 持久化 enable/disable |
| GET | `/api/plugins/{id}/config` | 读取当前插件配置 |
| POST | `/api/plugins/{id}/config` | 持久化配置并 reload |
| SSE | `/api/stream` | `plugin-state` + `ui-manifest` 增量刷新 |

错误码：`invalid_request` 400、`not_found` 404、`conflict` 409、
`invalid_package`/`invalid_config` 422、`forbidden` 403、`unavailable` 503。

## 3. 前端闭环

`web/console/src/`

- `api.ts`：修正 `/api/plugins` 信封；新增 store/dependencies/upload/install/upgrade/uninstall/control/config API
- `types.ts`：对齐真实 DTO，移除前端伪造的 control result
- `components/Explorer.tsx`：真实插件列表、安装、启停、升级、卸载
- `components/PluginDependencies.tsx`：包依赖、dependents、typed capability、诊断
- `components/PluginFileButton.tsx`：选择并上传包，返回 uploadKey
- `ConfigEditor` / `SettingsSlot`：显示配置是否持久化，处理 conflict/invalid_config
- `stream.ts` / `projections.ts`：监听 `plugin-state`，触发 plugin/composition/all 域失效
- `MarketCatalog.tsx`：只读本地 store，不内置远程市场

## 4. 端到端验收

`tests/Gocordis.Conformance.Tests/PluginControlPlaneTests.cs`（6 项）覆盖：

1. 上传安装 → 声明落盘 → Fiber Active
2. 配置与启停 → 持久化 → 重建 control 后仍然生效
3. 依赖视图 → 包依赖、dependents、typed capability、诊断
4. 升级 → 版本变化 → 新 Fiber generation → 卸载清理声明/包/Fiber
5. 失败可观察且无破坏：坏包、重复安装、未知插件、非法配置
6. SSE `plugin-state` 事件

前端本地验证：

```text
npm test  → 84 passed
npm run build → success
```

.NET 本地验证：

```text
dotnet build Gocordis.sln → 0 warning / 0 error
dotnet test  Gocordis.sln → 396/396
```

## 5. 明确边界

- 不做远程插件市场服务端；`IPluginPackageSource` 仍是未来接口
- 不做用户/角色 IAM；loopback 是默认安全边界
- `--allow-remote` 且无 AccessPolicy 时写端点返回 403；远程部署必须外部鉴权
- 不让 Web UI 直接 Load/Dispose Fiber
- 不复制 Koishi 聊天业务插件生态
